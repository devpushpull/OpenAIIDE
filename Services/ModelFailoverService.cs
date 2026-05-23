using System.Collections.Concurrent;
using System.Net.Http;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 大模型故障自动切换服务 —— 监控各模型健康状态，
/// 在网络不佳或模型自身不稳定时自动切换到备用模型。
/// 参考 Qoder/Cursor 的模型自动回退机制实现。
/// </summary>
public class ModelFailoverService
{
    private readonly ModelManager _modelManager;
    private readonly NetworkService _networkService;
    private readonly ConcurrentDictionary<string, ModelHealth> _healthMap = new();

    /// <summary>单模型在时间窗口内允许的最大连续失败次数</summary>
    private const int MaxConsecutiveErrors = 3;

    /// <summary>单模型允许的最大平均延迟 (ms)</summary>
    private const int MaxAvgLatencyMs = 15000;

    /// <summary>健康状态重置时间窗口</summary>
    private static readonly TimeSpan HealthWindow = TimeSpan.FromMinutes(5);

    /// <summary>模型切换时触发，参数: (oldModelId, newModelId, reason)</summary>
    public event Action<string, string, string>? OnFailover;

    public ModelFailoverService(ModelManager modelManager, NetworkService networkService)
    {
        _modelManager = modelManager;
        _networkService = networkService;
    }

    /// <summary>记录一次成功调用</summary>
    public void RecordSuccess(string modelId, int latencyMs)
    {
        var health = _healthMap.GetOrAdd(modelId, _ => new ModelHealth());
        lock (health)
        {
            health.LastSuccess = DateTime.UtcNow;
            health.ConsecutiveErrors = 0;
            health.TotalCalls++;
            health.TotalLatency += latencyMs;
            health.RecentLatencies.Enqueue((DateTime.UtcNow, latencyMs));
            while (health.RecentLatencies.Count > 20)
                health.RecentLatencies.TryDequeue(out _);
        }
    }

    /// <summary>记录一次调用失败</summary>
    public void RecordError(string modelId, string errorMessage)
    {
        var health = _healthMap.GetOrAdd(modelId, _ => new ModelHealth());
        lock (health)
        {
            health.ConsecutiveErrors++;
            health.LastError = DateTime.UtcNow;
            health.LastErrorMessage = errorMessage;
            health.TotalCalls++;
            LogService.Instance.Warn(
                $"模型 {modelId} 调用失败 (连续{health.ConsecutiveErrors}次): {errorMessage}", "Failover");
        }
    }

    /// <summary>判断指定模型是否健康</summary>
    public bool IsModelHealthy(string modelId)
    {
        if (!_healthMap.TryGetValue(modelId, out var health)) return true; // 无记录 = 假定健康

        lock (health)
        {
            // 连续错误过多
            if (health.ConsecutiveErrors >= MaxConsecutiveErrors)
                return false;

            // 最近调用延迟过高
            var avgLatency = GetRecentAverageLatency(health);
            if (avgLatency > MaxAvgLatencyMs && health.TotalCalls > 5)
                return false;

            return true;
        }
    }

    /// <summary>获取模型的健康状态摘要</summary>
    public string GetHealthSummary(string modelId)
    {
        if (!_healthMap.TryGetValue(modelId, out var health))
            return "✅ 无异常记录";

        lock (health)
        {
            if (health.ConsecutiveErrors >= MaxConsecutiveErrors)
                return $"❌ 连续 {health.ConsecutiveErrors} 次失败";
            var avgLatency = GetRecentAverageLatency(health);
            if (avgLatency > MaxAvgLatencyMs)
                return $"⚠️ 平均延迟 {avgLatency}ms (偏高)";
            return $"✅ 正常 (平均 {avgLatency}ms)";
        }
    }

    /// <summary>
    /// 获取最佳备用模型。
    /// 优先在同提供商内查找，无则跨提供商查找已配置 API Key 的健康模型。
    /// </summary>
    public (string providerId, ModelDef model)? GetFallbackModel(
        string failedModelId,
        string currentProviderId)
    {
        // 1. 收集所有可用候选
        var candidates = new List<(string providerId, ModelDef model)>();

        foreach (var provider in _modelManager.GetProviders())
        {
            var apiKey = _modelManager.GetEffectiveApiKey(provider.Id);
            if (string.IsNullOrEmpty(apiKey)) continue;

            foreach (var model in provider.Models)
            {
                // 跳过失败的模型自身
                if (model.Id == failedModelId) continue;
                // 必须支持智能体模式
                if (!model.AgentMode) continue;
                // 必须健康
                if (!IsModelHealthy(model.Id)) continue;

                candidates.Add((provider.Id, model));
            }
        }

        if (candidates.Count == 0)
        {
            // 放宽限制：允许不那么健康的模型（至少没有连续失败）
            foreach (var provider in _modelManager.GetProviders())
            {
                var apiKey = _modelManager.GetEffectiveApiKey(provider.Id);
                if (string.IsNullOrEmpty(apiKey)) continue;

                foreach (var model in provider.Models)
                {
                    if (model.Id == failedModelId || !model.AgentMode) continue;
                    if (_healthMap.TryGetValue(model.Id, out var h) && h.ConsecutiveErrors >= MaxConsecutiveErrors * 2)
                        continue;
                    candidates.Add((provider.Id, model));
                }
            }
        }

        if (candidates.Count == 0) return null;

        // 2. 优先同类提供商
        var sameProvider = candidates
            .Where(c => c.providerId == currentProviderId)
            .OrderBy(c => c.model.Tier)
            .ToList();

        if (sameProvider.Count > 0)
        {
            // 优先选择 Premium 级别
            var premium = sameProvider.FirstOrDefault(c => c.model.Tier == ModelTier.Premium);
            return premium.model != null ? (premium.providerId, premium.model) : sameProvider[0];
        }

        // 3. 跨提供商选择
        var best = candidates
            .OrderByDescending(c => c.model.Tier)
            .ThenBy(c =>
            {
                _healthMap.TryGetValue(c.model.Id, out var h);
                return h?.ConsecutiveErrors ?? 0;
            })
            .First();

        return (best.providerId, best.model);
    }

    /// <summary>网络是否正常（供切换决策使用）</summary>
    public bool IsNetworkHealthy => _networkService.IsConnected;

    /// <summary>检查网络并触发必要通知</summary>
    public async Task<bool> EnsureNetworkAsync()
    {
        if (!_networkService.IsConnected)
        {
            await _networkService.CheckConnectivityAsync();
        }
        return _networkService.IsConnected;
    }

    /// <summary>重置模型健康状态</summary>
    public void ResetHealth(string? modelId = null)
    {
        if (modelId != null)
        {
            _healthMap.TryRemove(modelId, out _);
        }
        else
        {
            _healthMap.Clear();
        }
    }

    private int GetRecentAverageLatency(ModelHealth health)
    {
        if (health.RecentLatencies.Count == 0) return 0;
        // 仅统计过去 HealthWindow 内的延迟
        var cutoff = DateTime.UtcNow - HealthWindow;
        var recent = health.RecentLatencies
            .Where(r => r.time > cutoff)
            .Select(r => r.latencyMs)
            .ToList();
        return recent.Count > 0 ? (int)recent.Average() : 0;
    }

    /// <summary>
    /// 快速预检：发送轻量API请求验证端点可达性。
    /// 耗时控制在3秒内，不阻塞主流程。
    /// </summary>
    public async Task<bool> PingApiAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        try
        {
            using var pingCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, pingCts.Token);
            using var pingClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            // 发送一个最小的API请求验证可达性（仅获取模型列表）
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await pingClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 主动健康检查：检测所有已配置模型的API可达性。
    /// 建议每30秒调用一次（由 BackgroundScheduler 触发）。
    /// </summary>
    public async Task ProactiveHealthCheckAsync(ModelManager modelManager)
    {
        foreach (var provider in modelManager.GetProviders())
        {
            var apiKey = modelManager.GetEffectiveApiKey(provider.Id);
            if (string.IsNullOrEmpty(apiKey)) continue;

            var baseUrl = provider.BaseUrl;
            if (string.IsNullOrEmpty(baseUrl)) continue;

            var isReachable = await PingApiAsync(baseUrl, apiKey);
            foreach (var model in provider.Models)
            {
                if (isReachable)
                {
                    // 如果之前标记为不健康，现在恢复了
                    if (!IsModelHealthy(model.Id))
                    {
                        ResetHealth(model.Id);
                        LogService.Instance.Info($"模型 {model.Id} 已恢复健康", "Failover");
                    }
                }
            }
        }
    }

    private class ModelHealth
    {
        public int ConsecutiveErrors;
        public DateTime? LastError;
        public DateTime? LastSuccess;
        public string LastErrorMessage = "";
        public int TotalCalls;
        public long TotalLatency;
        public readonly ConcurrentQueue<(DateTime time, int latencyMs)> RecentLatencies = new();
    }
}
