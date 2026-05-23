using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace AIIDEWPF.Services.ProviderUsage;

/// <summary>
/// DeepSeek 平台余额查询服务
/// API: GET https://api.deepseek.com/user/balance
/// </summary>
public class DeepSeekUsageService : IProviderUsageService
{
    public string ProviderId => "deepseek";

    public async Task<ProviderBalanceInfo?> FetchBalanceAsync(HttpClient http, string baseUrl, string apiKey)
    {
        try
        {
            const string balanceUrl = "https://api.deepseek.com/user/balance";

            var request = new HttpRequestMessage(HttpMethod.Get, balanceUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var j = JsonNode.Parse(body);
                var isAvailable = j?["is_available"]?.GetValue<bool>() ?? false;
                var infos = j?["balance_infos"]?.AsArray();

                if (infos != null && infos.Count > 0)
                {
                    var info = infos[0];
                    return new ProviderBalanceInfo
                    {
                        IsAvailable = isAvailable,
                        Currency = info?["currency"]?.GetValue<string>() ?? "CNY",
                        TotalBalance = info?["total_balance"] != null
                            ? decimal.Parse(info["total_balance"]!.GetValue<string>())
                            : 0,
                        GrantedBalance = info?["granted_balance"] != null
                            ? decimal.Parse(info["granted_balance"]!.GetValue<string>())
                            : 0,
                        ToppedUpBalance = info?["topped_up_balance"] != null
                            ? decimal.Parse(info["topped_up_balance"]!.GetValue<string>())
                            : 0,
                        ProviderId = ProviderId,
                        FetchedAt = DateTime.Now
                    };
                }

                return new ProviderBalanceInfo
                {
                    IsAvailable = false,
                    ProviderId = ProviderId,
                    ErrorMessage = "余额信息格式不正确"
                };
            }

            return new ProviderBalanceInfo
            {
                IsAvailable = false,
                ProviderId = ProviderId,
                ErrorMessage = $"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}"
            };
        }
        catch (Exception ex)
        {
            return new ProviderBalanceInfo
            {
                IsAvailable = false,
                ProviderId = ProviderId,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string Truncate(string s, int maxLen) => s.Length <= maxLen ? s : s[..maxLen] + "...";
}
