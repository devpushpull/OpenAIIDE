using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>压缩策略</summary>
public enum CompressionStrategy
{
    None = 0,    // 不压缩
    Light = 1,   // 轻型文本截断（原有模式）
    LLM = 2      // LLM 智能摘要压缩
}

/// <summary>会话压缩服务 — 当对话历史超过阈值时自动压缩，防止上下文溢出</summary>
public class ConversationCompressor
{
    private const int MaxMessagesBeforeCompress = 16; // 超过此数量触发压缩
    private const int KeepRecentCount = 8; // 保留最近 N 条消息
    private const int TokenThresholdForCompress = 4000; // token 超此阈值触发压缩
    private TokenCounterService.ModelFamily _modelFamily = TokenCounterService.ModelFamily.Generic;
    private readonly HttpClient _http = new();
    private ConfigService? _config;
    private CompressionStrategy _strategy = CompressionStrategy.LLM;

    /// <summary>压缩完成事件（消息数, 压缩后消息数, 压缩前token数, 压缩后token数）</summary>
    public event Action<int, int, int, int>? OnCompressed;

    public CompressionStrategy Strategy
    {
        get => _strategy;
        set => _strategy = value;
    }

    /// <summary>估算 token 数（模型感知）</summary>
    public static int EstimateTokens(List<object> history)
    {
        return TokenCounterService.CountTokens(history);
    }

    /// <summary>设置当前模型族以优化 token 估算</summary>
    public void SetModelFamily(string modelName)
    {
        _modelFamily = TokenCounterService.DetectFamily(modelName);
    }

    /// <summary>设置配置和服务，启用 LLM 压缩</summary>
    public void SetConfig(ConfigService config, HttpClient http)
    {
        _config = config;
    }

    /// <summary>
    /// LLM 智能压缩：将历史消息发送给 LLM 生成结构化摘要。
    /// </summary>
    public async Task<string?> CompressWithLLMAsync(List<object> history)
    {
        if (_config == null || history.Count < 8)
            return null;

        try
        {
            var aiConfig = _config.GetAIConfig();
            if (string.IsNullOrEmpty(aiConfig.ApiKey))
                return null;

            var compressPrompt = BuildCompressPrompt(history);
            var requestBody = new
            {
                model = aiConfig.Model,
                messages = new[]
                {
                    new { role = "system", content = "你是一个对话压缩助手。你的任务是将长时间的编程对话总结为结构化摘要，保留关键技术决策、代码变更和上下文。输出纯 JSON，不要有任何解释前缀。" },
                    new { role = "user", content = compressPrompt }
                },
                temperature = 0.3,
                stream = false
            };

            var baseUrl = "https://api.deepseek.com/v1";
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions") { Content = content };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiConfig.ApiKey);

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                LogService.Instance.Warn($"LLM压缩请求失败: {response.StatusCode}", "Compressor");
                return null;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            var respJson = JsonDocument.Parse(respBody).RootElement;
            var llmContent = respJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            // 尝试解析 JSON 输出，如果不是 JSON 则直接作为文本摘要
            return ParseCompressResult(llmContent);
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"LLM压缩异常: {ex.Message}", "Compressor");
            return null;
        }
    }

    /// <summary>构建压缩 prompt</summary>
    private static string BuildCompressPrompt(List<object> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("请将以下编程对话历史压缩为 JSON 格式的结构化摘要。要求：");
        sb.AppendLine("1. summary: 300字以内的对话内容总结，保留核心技术意图");
        sb.AppendLine("2. key_decisions: 用户或AI做出的关键技术决策列表（最多5条）");
        sb.AppendLine("3. file_changes: 涉及的文件变更列表（文件名+操作类型）");
        sb.AppendLine("4. errors_fixed: 修复过的错误列表（最多5条）");
        sb.AppendLine("5. context_hints: 继续对话需要知道的上下文提示（最多5条）");
        sb.AppendLine("\n--- 对话历史 ---");

        foreach (var msg in messages)
        {
            try
            {
                var json = JsonSerializer.Serialize(msg);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var role = root.TryGetProperty("role", out var r) ? r.GetString() ?? "?" : "?";
                var text = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(text) && root.TryGetProperty("tool_calls", out _))
                    text = "[工具调用]";
                if (string.IsNullOrEmpty(text)) continue;

                // 截断过长内容
                if (text.Length > 500) text = text[..500];
                sb.AppendLine($"[{role}] {text}");
            }
            catch { }
        }

        sb.AppendLine("\n请输出纯 JSON（不要有 markdown 代码块标记），格式如: {\"summary\":\"...\",\"key_decisions\":[],\"file_changes\":[],\"errors_fixed\":[],\"context_hints\":[]}");
        return sb.ToString();
    }

    /// <summary>解析 LLM 压缩结果</summary>
    private static string ParseCompressResult(string llmContent)
    {
        try
        {
            // 尝试提取 JSON（兼容 markdown 代码块包裹）
            var jsonStart = llmContent.IndexOf('{');
            var jsonEnd = llmContent.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = llmContent[jsonStart..(jsonEnd + 1)];
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                var result = new StringBuilder();
                if (root.TryGetProperty("summary", out var summary))
                    result.AppendLine($"[压缩摘要] {summary.GetString()}");
                if (root.TryGetProperty("key_decisions", out var decisions) && decisions.GetArrayLength() > 0)
                {
                    result.AppendLine("\n关键技术决策:");
                    foreach (var d in decisions.EnumerateArray())
                        result.AppendLine($"  - {d.GetString()}");
                }
                if (root.TryGetProperty("file_changes", out var changes) && changes.GetArrayLength() > 0)
                {
                    result.AppendLine("\n文件变更:");
                    foreach (var fc in changes.EnumerateArray())
                        result.AppendLine($"  - {fc.GetString()}");
                }
                if (root.TryGetProperty("errors_fixed", out var errors) && errors.GetArrayLength() > 0)
                {
                    result.AppendLine("\n已修复错误:");
                    foreach (var e in errors.EnumerateArray())
                        result.AppendLine($"  - {e.GetString()}");
                }
                if (root.TryGetProperty("context_hints", out var hints) && hints.GetArrayLength() > 0)
                {
                    result.AppendLine("\n上下文提示:");
                    foreach (var h in hints.EnumerateArray())
                        result.AppendLine($"  - {h.GetString()}");
                }
                return result.ToString();
            }
        }
        catch { }

        // 回退：直接作为文本摘要
        return llmContent.Length > 1500 ? llmContent[..1500] : llmContent;
    }

    /// <summary>
    /// 检查是否需要压缩，需要则执行压缩（同步轻量版本，LLM压缩需异步调用）。
    /// 返回压缩后的历史列表（可能原地修改 _history）。
    /// </summary>
    public bool TryCompress(List<object> history, List<object>? extraMessages = null)
    {
        if (_strategy == CompressionStrategy.None)
            return false;

        var totalCount = history.Count + (extraMessages?.Count ?? 0);
        if (totalCount <= MaxMessagesBeforeCompress)
            return false;

        var estimatedTokens = EstimateTokens(history);
        if (estimatedTokens < TokenThresholdForCompress && totalCount <= MaxMessagesBeforeCompress + 4)
            return false;

        int oldCount = history.Count;
        int beforeTokens = EstimateTokens(history);

        // 提取要被压缩的消息
        var messagesToSummarize = history.Take(history.Count - KeepRecentCount).ToList();
        if (messagesToSummarize.Count == 0)
            return false;

        // 轻量模式：使用原有文本截断
        var summary = BuildSummary(messagesToSummarize);

        // 移除旧消息，保留最近 KeepRecentCount 条
        var recent = history.Skip(history.Count - KeepRecentCount).ToList();
        history.Clear();

        // 插入压缩摘要作为第一条消息
        history.Add(new
        {
            role = "system",
            content = $"[对话已压缩: {oldCount}条消息 -> {KeepRecentCount + 1}条 (含本摘要)]\n\n历史对话要点:\n{summary}\n\n---\n请基于以上历史摘要继续对话，如有遗漏细节请询问用户。"
        });

        // 恢复最近的消息
        history.AddRange(recent);

        int afterTokens = EstimateTokens(history);
        OnCompressed?.Invoke(oldCount, history.Count, beforeTokens, afterTokens);
        return true;
    }

    /// <summary>从消息列表构建摘要</summary>
    private static string BuildSummary(List<object> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(msg);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                var role = root.TryGetProperty("role", out var r) ? r.GetString() ?? "?" : "?";
                var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(content))
                {
                    if (root.TryGetProperty("tool_calls", out _))
                        content = "[工具调用]";
                    else if (root.TryGetProperty("tool_call_id", out _))
                        content = "[工具结果]";
                }

                var summary = TruncateByWords(content, 60);
                if (!string.IsNullOrEmpty(summary))
                    sb.AppendLine($"- [{role}] {summary}");
            }
            catch (Exception ex) { LogService.Instance.Debug($"对话压缩提取文本异常: {ex.Message}", "Compressor"); }
        }
        return sb.Length == 0 ? "(无文本内容)" : sb.ToString();
    }

    private static string TruncateByWords(string text, int maxChars) => CommonUtils.TruncateByWords(text, maxChars);
}
