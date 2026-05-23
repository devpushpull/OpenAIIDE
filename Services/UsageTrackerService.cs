using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIIDEWPF.Services.ProviderUsage;

namespace AIIDEWPF.Services;

/// <summary>
/// 单次 API 调用记录
/// </summary>
public class ApiCallRecord
{
    public DateTime Time { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;
    public string ModelName { get; init; } = "";
    public long ResponseTimeMs { get; init; }
    public bool IsSuccess { get; init; } = true;
}

/// <summary>
/// 平台余额信息（从大模型开放平台拉取）
/// </summary>
public class ProviderBalanceInfo
{
    /// <summary>是否可用</summary>
    public bool IsAvailable { get; init; }
    /// <summary>币种</summary>
    public string Currency { get; init; } = "CNY";
    /// <summary>总余额</summary>
    public decimal TotalBalance { get; init; }
    /// <summary>赠送余额</summary>
    public decimal GrantedBalance { get; init; }
    /// <summary>充值余额</summary>
    public decimal ToppedUpBalance { get; init; }
    /// <summary>提供商ID</summary>
    public string ProviderId { get; init; } = "";
    /// <summary>拉取时间</summary>
    public DateTime FetchedAt { get; init; } = DateTime.Now;
    /// <summary>错误信息（null=成功）</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Token 用量追踪服务 —— 估算每次请求的 token 消耗、解析 API 响应头中的速率限制、
/// 检测 API 错误中的配额耗尽提示，并在用量接近阈值时向用户发出警告。
/// </summary>
public class UsageTrackerService : IDisposable
{
    // ========== 配置 ==========
    /// <summary>警告阈值（占已知限额百分比）</summary>
    public double WarningThresholdPercent { get; set; } = 80;

    /// <summary>严重警告阈值</summary>
    public double CriticalThresholdPercent { get; set; } = 95;

    /// <summary>单次会话 token 使用警告上限（超过此值强烈建议分批对话）</summary>
    public int SessionTokenWarningLimit { get; set; } = 100_000;

    // ========== 状态 ==========
    /// <summary>本次会话输入 token 累计</summary>
    public long SessionInputTokens { get; private set; }
    /// <summary>本次会话输出 token 累计</summary>
    public long SessionOutputTokens { get; private set; }
    /// <summary>本次会话总 token</summary>
    public long SessionTotalTokens => SessionInputTokens + SessionOutputTokens;

    /// <summary>本次会话 API 调用次数</summary>
    public int ApiCallCount { get; private set; }

    /// <summary>本次会话成功的 API 调用次数</summary>
    public int SuccessfulCallCount { get; private set; }

    /// <summary>最近 API 调用历史（最多保留 50 条）</summary>
    public List<ApiCallRecord> CallHistory { get; } = new();
    private const int MaxCallHistory = 50;

    /// <summary>API 报告的剩余请求数（-1 表示未知）</summary>
    public int RateLimitRemainingRequests { get; private set; } = -1;
    /// <summary>API 报告的剩余 token 数（-1 表示未知）</summary>
    public int RateLimitRemainingTokens { get; private set; } = -1;
    /// <summary>API 报告的总请求限额</summary>
    public int RateLimitTotalRequests { get; private set; } = -1;
    /// <summary>API 报告的总 token 限额</summary>
    public int RateLimitTotalTokens { get; private set; } = -1;
    /// <summary>上次 API 响应时间</summary>
    public DateTime LastApiResponseTime { get; private set; } = DateTime.MinValue;

    /// <summary>是否有速率限制信息</summary>
    public bool HasRateLimitInfo => RateLimitTotalTokens > 0 || RateLimitTotalRequests > 0;

    /// <summary>平台余额信息（从大模型开放平台拉取）</summary>
    public ProviderBalanceInfo? ProviderBalance { get; private set; }

    /// <summary>是否已拉取到余额</summary>
    public bool HasBalance => ProviderBalance != null && ProviderBalance.IsAvailable;

    /// <summary>余额拉取时间</summary>
    public DateTime? BalanceFetchedAt => ProviderBalance?.FetchedAt;

    /// <summary>速率限制使用百分比（0-100），无信息返回 -1</summary>
    public double RateLimitUsagePercent
    {
        get
        {
            if (RateLimitTotalTokens <= 0) return -1;
            return 100.0 - (RateLimitRemainingTokens * 100.0 / RateLimitTotalTokens);
        }
    }

    /// <summary>估算费用（基于 DeepSeek 标准定价：输入 ¥1/百万token，输出 ¥2/百万token）</summary>
    public double EstimatedCostYuan => SessionInputTokens * 0.000001 + SessionOutputTokens * 0.000002;

    // ========== UI 状态属性 ==========
    /// <summary>状态栏用量文本</summary>
    public string StatusText
    {
        get
        {
            if (HasRateLimitInfo)
                return $"📊 Token: {FormatTokens(SessionTotalTokens)} | 剩余: {FormatTokens(RateLimitRemainingTokens)} / {FormatTokens(RateLimitTotalTokens)}";
            return $"📊 Token: {FormatTokens(SessionTotalTokens)} 本次会话";
        }
    }

    /// <summary>警告消息（空表示无警告）</summary>
    public string? WarningMessage { get; private set; }

    // ========== 事件 ==========
    /// <summary>用量警告触发（warning 级别）</summary>
    public event Action<string>? OnWarning;
    /// <summary>严重用量警告触发</summary>
    public event Action<string>? OnCritical;
    /// <summary>状态栏文本变化</summary>
    public event Action? OnStatusChanged;
    /// <summary>用量数据更新（供看板刷新）</summary>
    public event Action? OnDataUpdated;
    /// <summary>平台余额更新（供看板刷新）</summary>
    public event Action? OnBalanceUpdated;
    /// <summary>余额/配额彻底耗尽触发（剩余为0或100%已使用）</summary>
    public event Action<string>? OnQuotaExhausted;

    /// <summary>余额/配额是否已彻底耗尽（剩余 token 为 0 且已知限额信息）</summary>
    public bool IsQuotaExhausted => HasRateLimitInfo && RateLimitRemainingTokens == 0;

    // ========== 公共方法 ==========

    /// <summary>记录一次 API 调用的 token 消耗（基于文本估算）</summary>
    public void RecordUsage(string inputText, string outputText,
        int? apiReportedInputTokens = null, int? apiReportedOutputTokens = null,
        long responseTimeMs = 0, string modelName = "", bool isSuccess = true)
    {
        var inputTokens = apiReportedInputTokens ?? EstimateTokens(inputText);
        var outputTokens = apiReportedOutputTokens ?? EstimateTokens(outputText);

        SessionInputTokens += inputTokens;
        SessionOutputTokens += outputTokens;

        ApiCallCount++;
        if (isSuccess) SuccessfulCallCount++;

        // 记录调用历史
        CallHistory.Insert(0, new ApiCallRecord
        {
            Time = DateTime.Now,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelName = modelName,
            ResponseTimeMs = responseTimeMs,
            IsSuccess = isSuccess
        });
        while (CallHistory.Count > MaxCallHistory)
            CallHistory.RemoveAt(CallHistory.Count - 1);

        LastApiResponseTime = DateTime.Now;

        // 检查会话用量警告
        CheckSessionLimits();

        // 检查 API 限额警告
        CheckRateLimits();

        OnStatusChanged?.Invoke();
        OnDataUpdated?.Invoke();
    }

    /// <summary>解析 API 响应的速率限制头</summary>
    public void ParseRateLimitHeaders(Func<string, string?> headerGetter)
    {
        // OpenAI 标准: x-ratelimit-*
        RateLimitRemainingRequests = TryParseInt(headerGetter("x-ratelimit-remaining-requests")) ?? RateLimitRemainingRequests;
        RateLimitRemainingTokens = TryParseInt(headerGetter("x-ratelimit-remaining-tokens")) ?? RateLimitRemainingTokens;
        RateLimitTotalRequests = TryParseInt(headerGetter("x-ratelimit-limit-requests")) ?? RateLimitTotalRequests;
        RateLimitTotalTokens = TryParseInt(headerGetter("x-ratelimit-limit-tokens")) ?? RateLimitTotalTokens;

        // 阿里云百炼: x-dashscope-*
        if (RateLimitTotalTokens <= 0 && RateLimitTotalRequests <= 0)
        {
            RateLimitRemainingTokens = TryParseInt(headerGetter("x-dashscope-ratelimit-remaining")) ?? RateLimitRemainingTokens;
            RateLimitTotalTokens = TryParseInt(headerGetter("x-dashscope-ratelimit-limit")) ?? RateLimitTotalTokens;
        }

        // 通用: retry-after (秒)
        var retryAfter = headerGetter("retry-after");
        if (!string.IsNullOrEmpty(retryAfter) && int.TryParse(retryAfter, out var secs) && secs > 0)
        {
            OnWarning?.Invoke($"⏳ API 速率限制，请等待 {secs} 秒后重试。");
        }

        OnStatusChanged?.Invoke();
    }

    /// <summary>检测 API 错误消息是否为配额耗尽</summary>
    public string? DetectQuotaExceeded(string errorBody, int httpStatusCode)
    {
        if (httpStatusCode != 429 && httpStatusCode != 402 && httpStatusCode != 403)
            return null;

        var lower = errorBody.ToLowerInvariant();

        // 各种配额耗尽关键词
        if (lower.Contains("insufficient_quota") || lower.Contains("insufficient quota"))
            return "❌ API 配额已耗尽：当前账户余额/免费额度不足，请充值或更换 API Key。";
        if (lower.Contains("rate_limit") || lower.Contains("rate limit") || lower.Contains("too many requests"))
            return "⏳ API 请求频率超限，请稍后重试。";
        if (lower.Contains("exceeded your current quota") || lower.Contains("quota exceeded"))
            return "❌ API 配额已耗尽：已超过当前配额限制。";
        if ((lower.Contains("billing") || lower.Contains("bill")) && lower.Contains("not"))
            return "❌ API 计费未激活，请在对应平台开通付费。";
        if (lower.Contains("invalid") && lower.Contains("api key"))
            return "🔑 API Key 无效或已过期，请检查设置。";
        if (lower.Contains("context length") || lower.Contains("maximum context"))
            return "⚠️ 上下文长度超出模型限制，请缩短输入内容或开启新对话。";

        // 尝试提取 token 限制信息
        var tokenMatch = Regex.Match(lower, @"(\d+)\s*tokens?", RegexOptions.IgnoreCase);
        var limitMatch = Regex.Match(lower, @"maximum.*?(\d+)", RegexOptions.IgnoreCase);
        if (tokenMatch.Success && limitMatch.Success)
            return $"⚠️ Token 超限：请求使用了 {tokenMatch.Groups[1].Value} tokens，模型上限为 {limitMatch.Groups[1].Value} tokens。";

        return null;
    }

    /// <summary>从大模型开放平台拉取余额信息</summary>
    public async Task FetchProviderBalanceAsync(HttpClient http, string baseUrl, string apiKey, string providerId)
    {
        var service = ProviderUsageServiceFactory.GetService(providerId);
        if (service == null) return; // 该提供商暂无余额查询 API

        ProviderBalance = await service.FetchBalanceAsync(http, baseUrl, apiKey);
        OnBalanceUpdated?.Invoke();
    }

    /// <summary>重置会话用量</summary>
    public void ResetSession()
    {
        SessionInputTokens = 0;
        SessionOutputTokens = 0;
        ApiCallCount = 0;
        SuccessfulCallCount = 0;
        CallHistory.Clear();
        WarningMessage = null;
        OnStatusChanged?.Invoke();
        OnDataUpdated?.Invoke();
    }

    public void Dispose()
    {
        OnWarning = null;
        OnCritical = null;
        OnStatusChanged = null;
        OnDataUpdated = null;
        OnBalanceUpdated = null;
        OnQuotaExhausted = null;
    }

    // ========== 私有方法 ==========

    /// <summary>估算文本的 token 数（模型感知，混合中英文）</summary>
    private static int EstimateTokens(string text)
    {
        return TokenCounterService.CountTokens(text);
    }

    private void CheckSessionLimits()
    {
        if (SessionTotalTokens > SessionTokenWarningLimit)
        {
            var msg = $"💡 本次会话已使用约 {FormatTokens(SessionTotalTokens)} tokens，" +
                      $"建议开启新对话以保持大模型响应质量。";
            WarningMessage = msg;
            OnWarning?.Invoke(msg);
        }
    }

    private void CheckRateLimits()
    {
        if (!HasRateLimitInfo) return;

        var usage = RateLimitUsagePercent;

        // 100% 耗尽：阻止继续使用
        if (RateLimitRemainingTokens == 0)
        {
            var msg = "❌ API 余额/配额已完全耗尽，无法继续处理请求。\n\n" +
                      "请采取以下措施：\n" +
                      "• 前往大模型开放平台充值\n" +
                      "• 切换到其他可用模型（设置 → 模型管理）\n" +
                      "• 更换 API Key";
            WarningMessage = msg;
            OnQuotaExhausted?.Invoke(msg);
        }
        else if (usage >= CriticalThresholdPercent)
        {
            var msg = $"🚨 API Token 配额严重不足：已使用 {usage:F0}%，" +
                      $"剩余 {FormatTokens(RateLimitRemainingTokens)}。请尽快充值或更换 Key！";
            WarningMessage = msg;
            OnCritical?.Invoke(msg);
        }
        else if (usage >= WarningThresholdPercent)
        {
            var msg = $"⚠️ API Token 配额使用 {usage:F0}%，" +
                      $"剩余 {FormatTokens(RateLimitRemainingTokens)}。建议关注用量。";
            WarningMessage = msg;
            OnWarning?.Invoke(msg);
        }
    }

    private static string FormatTokens(long tokens) => CommonUtils.FormatTokens(tokens);

    private static int? TryParseInt(string? value) => CommonUtils.TryParseInt(value);
}
