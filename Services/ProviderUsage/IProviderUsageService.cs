using System.Net.Http;

namespace AIIDEWPF.Services.ProviderUsage;

/// <summary>
/// 大模型平台用量查询服务接口 —— 各种大模型提供商实现此接口来查询账户余额/用量。
/// 新增提供商时只需实现此接口并在工厂中注册即可，无需修改 UsageTrackerService 核心逻辑。
/// </summary>
public interface IProviderUsageService
{
    /// <summary>提供商ID（如 "deepseek", "openai"）</summary>
    string ProviderId { get; }

    /// <summary>从大模型开放平台拉取余额信息</summary>
    /// <param name="http">共享的 HttpClient</param>
    /// <param name="baseUrl">提供商 API 基地址</param>
    /// <param name="apiKey">API Key</param>
    /// <returns>余额信息，null 表示该提供商暂无余额查询 API / 暂不支持</returns>
    Task<ProviderBalanceInfo?> FetchBalanceAsync(HttpClient http, string baseUrl, string apiKey);
}
