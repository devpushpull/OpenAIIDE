using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>子代理调度框架 —— 允许 AI 自主调度专项子代理执行任务</summary>
public class SubAgentService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    /// <summary>子代理执行结果回调</summary>
    public event Action<string, string>? OnAgentResult; // agentType, result summary

    public SubAgentService(string apiKey, string baseUrl, string model)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _model = model;
    }

    /// <summary>调度子代理执行任务</summary>
    /// <param name="agentType">子代理类型: browser / codereview</param>
    /// <param name="taskDescription">任务描述</param>
    /// <returns>执行结果 JSON</returns>
    public async Task<string> DispatchAsync(string agentType, string taskDescription)
    {
        return agentType.ToLowerInvariant() switch
        {
            "browser" => await RunBrowserAgentAsync(taskDescription),
            "codereview" => await RunCodeReviewAgentAsync(taskDescription),
            _ => JsonSerializer.Serialize(new { success = false, error = $"未知子代理类型: {agentType}，支持: browser, codereview" })
        };
    }

    /// <summary>浏览器子代理 —— 执行网页搜索与内容获取</summary>
    private async Task<string> RunBrowserAgentAsync(string taskDescription)
    {
        try
        {
            LogService.Instance.Info($"🌐 BrowserAgent 开始: {Truncate(taskDescription, 80)}", "SubAgent");

            // 使用 AI 解析任务，生成搜索查询
            var searchQuery = await GenerateSearchQueryAsync(taskDescription);
            if (string.IsNullOrEmpty(searchQuery))
                return "{\"success\":false,\"error\":\"无法从任务描述中提取搜索查询\"}";

            // 执行搜索
            var searchResults = await SearchWebAsync(searchQuery);

            // 如果任务要求读取某个 URL
            var urls = ExtractUrls(taskDescription);
            var fetchedContents = new List<string>();
            foreach (var url in urls.Take(3))
            {
                try
                {
                    var content = await FetchWebContentAsync(url);
                    fetchedContents.Add($"## {url}\n{Truncate(content, 3000)}");
                }
                catch (Exception ex)
                {
                    fetchedContents.Add($"## {url}\n获取失败: {ex.Message}");
                }
            }

            var summary = searchResults;
            if (fetchedContents.Count > 0)
                summary += "\n\n---\n\n" + string.Join("\n\n", fetchedContents);

            LogService.Instance.Info($"🌐 BrowserAgent 完成: {Truncate(summary, 100)}", "SubAgent");
            OnAgentResult?.Invoke("browser", Truncate(summary, 200));

            return JsonSerializer.Serialize(new { success = true, agent = "browser", summary });
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"🌐 BrowserAgent 失败: {ex.Message}", "SubAgent");
            return $"{{\"success\":false,\"error\":\"{ex.Message}\"}}";
        }
    }

    /// <summary>代码审查子代理 —— 审查指定文件的代码变更</summary>
    private async Task<string> RunCodeReviewAgentAsync(string taskDescription)
    {
        try
        {
            LogService.Instance.Info($"📋 CodeReviewAgent 开始", "SubAgent");

            // 从任务描述中提取文件列表
            var files = ExtractFileList(taskDescription);

            var reviews = new List<string>();
            foreach (var file in files.Take(5))
            {
                if (!File.Exists(file)) continue;

                var code = await File.ReadAllTextAsync(file);
                var review = await GenerateCodeReviewAsync(file, code);
                reviews.Add(review);
            }

            var summary = string.Join("\n\n---\n\n", reviews);
            if (reviews.Count == 0)
                summary = "未找到可审查的代码文件。请在任务描述中包含文件路径。";

            LogService.Instance.Info($"📋 CodeReviewAgent 完成: {reviews.Count} 个文件已审查", "SubAgent");
            OnAgentResult?.Invoke("codereview", Truncate(summary, 200));

            return JsonSerializer.Serialize(new { success = true, agent = "codereview", files_reviewed = reviews.Count, review = summary });
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"📋 CodeReviewAgent 失败: {ex.Message}", "SubAgent");
            return $"{{\"success\":false,\"error\":\"{ex.Message}\"}}";
        }
    }

    /// <summary>AI 驱动的代码审查</summary>
    private async Task<string> GenerateCodeReviewAsync(string filePath, string code)
    {
        var prompt = $$"""
你是一位资深代码审查专家。请审查以下代码，重点关注：
1. 潜在的 Bug 和逻辑错误
2. 安全漏洞
3. 性能问题
4. 代码可读性和可维护性
5. 最佳实践违规

文件: {{Path.GetFileName(filePath)}}

```
{{Truncate(code, 8000)}}
```

请用简洁的中文列出发现的问题（按严重程度排序），每个问题用一行描述。
如果代码没有问题，请回复"✅ 未发现明显问题"。
""";

        var messages = new[] { new { role = "user", content = prompt } };
        var result = await CallAIAsync(messages, 800);
        return $"### 📄 {Path.GetFileName(filePath)}\n{result}";
    }

    /// <summary>AI 驱动的搜索查询生成</summary>
    private async Task<string> GenerateSearchQueryAsync(string task)
    {
        var prompt = $"从以下任务描述中提取最关键的搜索查询词（只返回搜索词，不要解释）:\n\n{task}";
        var messages = new[] { new { role = "user", content = prompt } };
        return await CallAIAsync(messages, 100);
    }

    /// <summary>网页搜索</summary>
    private async Task<string> SearchWebAsync(string query)
    {
        try
        {
            var searchService = new WebSearchService(_http);
            return await searchService.SearchAsync(query);
        }
        catch (Exception ex)
        {
            return $"搜索失败: {ex.Message}";
        }
    }

    /// <summary>获取网页内容</summary>
    private async Task<string> FetchWebContentAsync(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>从任务描述中提取 URL</summary>
    private static List<string> ExtractUrls(string text)
    {
        var urls = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(text,
            @"https?://[^\s,;，；]+");
        foreach (System.Text.RegularExpressions.Match m in matches)
            urls.Add(m.Value.TrimEnd('.', ')', ']'));
        return urls;
    }

    /// <summary>从任务描述中提取文件路径</summary>
    private static List<string> ExtractFileList(string text)
    {
        var files = new List<string>();
        // 匹配常见文件路径模式
        var matches = System.Text.RegularExpressions.Regex.Matches(text,
            @"(?:[\w.-]+/)*[\w.-]+\.\w{1,10}");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var path = m.Value;
            if (File.Exists(path))
                files.Add(Path.GetFullPath(path));
        }
        return files;
    }

    /// <summary>通用 AI 调用</summary>
    private async Task<string> CallAIAsync(object[] messages, int maxTokens)
    {
        var reqDict = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["temperature"] = 0.3,
            ["stream"] = false,
            ["max_tokens"] = maxTokens
        };

        var json = JsonSerializer.Serialize(reqDict);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";
}
