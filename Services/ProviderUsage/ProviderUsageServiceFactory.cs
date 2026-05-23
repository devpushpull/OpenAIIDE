namespace AIIDEWPF.Services.ProviderUsage;

/// <summary>
/// 大模型用量查询服务工厂 —— 根据提供商 ID 返回对应的 IProviderUsageService 实现。
/// 
/// 扩展方式：新增提供商时，在此工厂中注册即可，无需修改 UsageTrackerService。
/// 未注册或不支持余额查询的提供商返回 null。
/// </summary>
public static class ProviderUsageServiceFactory
{
    private static readonly Dictionary<string, IProviderUsageService> _services = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deepseek"] = new DeepSeekUsageService(),
        // TODO: 以下提供商暂无公开余额查询 API，后续可补充
        // ["openai"]    = new OpenAIUsageService(),
        // ["anthropic"] = new AnthropicUsageService(),
        // ["google"]    = new GeminiUsageService(),
        // ["xai"]       = new GrokUsageService(),
        // ["mistral"]   = new MistralUsageService(),
        // ["zhipu"]     = new ZhipuUsageService(),
        // ["moonshot"]  = new MoonshotUsageService(),
        // ["tongyi"]    = new TongyiUsageService(),
        // ["baichuan"]  = new BaichuanUsageService(),
    };

    /// <summary>根据提供商 ID 获取用量查询服务，null 表示不支持</summary>
    public static IProviderUsageService? GetService(string providerId)
    {
        _services.TryGetValue(providerId, out var service);
        return service;
    }

    /// <summary>注册自定义用量查询服务（插件/扩展可用）</summary>
    public static void Register(string providerId, IProviderUsageService service)
    {
        _services[providerId] = service;
    }

    /// <summary>获取所有已注册的提供商 ID</summary>
    public static IEnumerable<string> RegisteredProviders => _services.Keys;
}
