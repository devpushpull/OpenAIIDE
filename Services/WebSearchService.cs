using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>
/// 联网搜索服务 —— 支持 DuckDuckGo / Google / Bing 多引擎搜索
/// 参考 Qoder 和通义灵码的搜索 Agent 设计
/// </summary>
public class WebSearchService
{
    private readonly HttpClient _http;
    private readonly int _maxResults;
    private readonly TimeSpan _timeout;
    private const int MaxRetries = 2;

    /// <summary>搜索引擎类型</summary>
    public enum SearchEngine { DuckDuckGo, Google, Bing }

    /// <summary>当前搜索引擎（默认 DuckDuckGo 免费）</summary>
    public SearchEngine Engine { get; set; } = SearchEngine.DuckDuckGo;

    /// <summary>Google API Key（使用 Google 时需要）</summary>
    public string? GoogleApiKey { get; set; }

    /// <summary>Google Custom Search Engine ID（使用 Google 时需要）</summary>
    public string? GoogleCx { get; set; }

    /// <summary>Bing API Key（使用 Bing 时需要）</summary>
    public string? BingApiKey { get; set; }

    public WebSearchService(HttpClient http, int maxResults = 5, int timeoutSeconds = 15)
    {
        _http = http;
        _maxResults = Math.Clamp(maxResults, 3, 10);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    /// <summary>执行联网搜索，带重试和超时</summary>
    public async Task<string> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "{\"success\":false,\"error\":\"empty query\"}";

        var lastError = "";
        for (int retry = 0; retry <= MaxRetries; retry++)
        {
            try
            {
                using var cts = new CancellationTokenSource(_timeout);
                return Engine switch
                {
                    SearchEngine.DuckDuckGo => await DuckDuckGoSearchAsync(query, cts.Token),
                    SearchEngine.Google => await GoogleSearchAsync(query, cts.Token),
                    SearchEngine.Bing => await BingSearchAsync(query, cts.Token),
                    _ => await DuckDuckGoSearchAsync(query, cts.Token)
                };
            }
            catch (OperationCanceledException)
            {
                lastError = "搜索超时";
                LogService.Instance.Warn($"联网搜索超时 ({retry + 1}/{MaxRetries + 1}): {Truncate(query, 60)}", "WebSearch");
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                LogService.Instance.Warn($"联网搜索失败 ({retry + 1}/{MaxRetries + 1}): {ex.Message}", "WebSearch");
                if (retry < MaxRetries)
                    await Task.Delay(500 * (retry + 1));
            }

            // 重试时尝试降级到 DuckDuckGo
            if (retry == 0 && Engine != SearchEngine.DuckDuckGo)
            {
                LogService.Instance.Info("搜索降级到 DuckDuckGo", "WebSearch");
                try
                {
                    using var cts = new CancellationTokenSource(_timeout);
                    return await DuckDuckGoSearchAsync(query, cts.Token);
                }
                catch { }
            }
        }

        return $"{{\"success\":false,\"error\":\"{Escape(lastError)}\"}}";
    }

    /// <summary>DuckDuckGo 搜索（免费，无需 API Key）</summary>
    private async Task<string> DuckDuckGoSearchAsync(string query, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query);
        var url = $"https://html.duckduckgo.com/html/?q={encoded}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var results = ExtractDuckDuckGoResults(html, _maxResults);
        LogService.Instance.Info($"DuckDuckGo 搜索完成: [{Truncate(query, 40)}] 返回 {results.Count} 条", "WebSearch");
        return JsonSerializer.Serialize(new { success = true, engine = "duckduckgo", results });
    }

    /// <summary>Google Custom Search API</summary>
    private async Task<string> GoogleSearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(GoogleApiKey) || string.IsNullOrEmpty(GoogleCx))
            throw new InvalidOperationException("Google 搜索需要配置 API Key 和 CX");

        var encoded = Uri.EscapeDataString(query);
        var url = $"https://www.googleapis.com/customsearch/v1?key={GoogleApiKey}&cx={GoogleCx}&q={encoded}&num={_maxResults}";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = new List<object>();
        var items = doc.RootElement.TryGetProperty("items", out var itemsEl) ? itemsEl.EnumerateArray() : default;
        foreach (var item in items)
        {
            results.Add(new
            {
                title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                url = item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "",
                snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : ""
            });
        }

        LogService.Instance.Info($"Google 搜索完成: [{Truncate(query, 40)}] 返回 {results.Count} 条", "WebSearch");
        return JsonSerializer.Serialize(new { success = true, engine = "google", results });
    }

    /// <summary>Bing Web Search API</summary>
    private async Task<string> BingSearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(BingApiKey))
            throw new InvalidOperationException("Bing 搜索需要配置 API Key");

        var encoded = Uri.EscapeDataString(query);
        var url = $"https://api.bing.microsoft.com/v7.0/search?q={encoded}&count={_maxResults}&mkt=zh-CN";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", BingApiKey);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = new List<object>();
        var webPages = doc.RootElement.TryGetProperty("webPages", out var wp)
            && wp.TryGetProperty("value", out var val) ? val.EnumerateArray() : default;
        foreach (var page in webPages)
        {
            results.Add(new
            {
                title = page.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                url = page.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                snippet = page.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : ""
            });
        }

        LogService.Instance.Info($"Bing 搜索完成: [{Truncate(query, 40)}] 返回 {results.Count} 条", "WebSearch");
        return JsonSerializer.Serialize(new { success = true, engine = "bing", results });
    }

    /// <summary>抓取网页内容（增强版：支持超时、重试、编码检测）</summary>
    public async Task<string> FetchContentAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "{\"success\":false,\"error\":\"empty url\"}";

        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            // 检测编码
            var rawBytes = await response.Content.ReadAsByteArrayAsync();
            var encoding = DetectEncoding(response.Content.Headers.ContentType?.CharSet, rawBytes);
            var html = encoding.GetString(rawBytes);

            var text = StripHtml(html);
            if (text.Length > 8000) text = text[..8000] + "\n...(内容已截断)";

            LogService.Instance.Info($"网页抓取完成: [{Truncate(url, 60)}] {rawBytes.Length} bytes", "WebSearch");
            return JsonSerializer.Serialize(new { success = true, url, content = text });
        }
        catch (OperationCanceledException)
        {
            LogService.Instance.Warn($"网页抓取超时: {Truncate(url, 60)}", "WebSearch");
            return $"{{\"success\":false,\"error\":\"网页抓取超时: {Escape(Truncate(url, 60))}\"}}";
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"网页抓取失败: {Truncate(url, 60)} - {ex.Message}", "WebSearch");
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>增强版 DuckDuckGo 结果提取</summary>
    private static List<object> ExtractDuckDuckGoResults(string html, int maxResults)
    {
        var results = new List<object>();

        // 方法1: 标准 DuckDuckGo HTML 结果页
        var titleMatches = Regex.Matches(html,
            @"class=""result__title"">.*?<a[^>]*?href=""([^""]*)"".*?>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var snippetMatches = Regex.Matches(html,
            @"class=""result__snippet"">(.*?)</(?:a|td|div)>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        for (int i = 0; i < Math.Min(titleMatches.Count, maxResults); i++)
        {
            var url = titleMatches[i].Groups[1].Value.Trim();
            var title = StripHtml(titleMatches[i].Groups[2].Value).Trim();
            var snippet = i < snippetMatches.Count ? StripHtml(snippetMatches[i].Groups[1].Value).Trim() : "";
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                results.Add(new { title, url, snippet, source = "DuckDuckGo" });
        }

        // 方法2: 后备——提取所有 <a> 链接描述
        if (results.Count == 0)
        {
            var linkMatches = Regex.Matches(html,
                @"<a[^>]*?href=""(https?://[^""]+)""[^>]*?>(.*?)</a>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var seen = new HashSet<string>();
            foreach (Match m in linkMatches)
            {
                var url = m.Groups[1].Value.Trim();
                var text = StripHtml(m.Groups[2].Value).Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length > 10 && !seen.Contains(url))
                {
                    seen.Add(url);
                    results.Add(new { title = text, url, snippet = "", source = "DuckDuckGo" });
                    if (results.Count >= maxResults) break;
                }
            }
        }

        if (results.Count == 0)
            results.Add(new { title = "未找到结果", url = "", snippet = "请尝试其他关键词或检查网络连接", source = "none" });

        return results;
    }

    /// <summary>HTML 标签剥离</summary>
    private static string StripHtml(string html)
    {
        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = Regex.Replace(text, @"&[a-z]+;", " ");
        text = Regex.Replace(text, @"&#\d+;", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return System.Net.WebUtility.HtmlDecode(text);
    }

    /// <summary>编码检测</summary>
    private static Encoding DetectEncoding(string? charset, byte[] data)
    {
        if (!string.IsNullOrEmpty(charset))
        {
            try { return Encoding.GetEncoding(charset); }
            catch { }
        }
        // 简单 UTF-8 BOM 检测
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8;
        return Encoding.UTF8;
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\\", "\\\\");
    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";
}
