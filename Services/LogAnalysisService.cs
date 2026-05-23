using System.Text;
using System.Net.Http;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>
/// 日志分析服务 — 在空闲时自动分析日志，调用大模型生成软件改进建议。
/// 用户在 UI 确认后，触发自动迭代/更新流程。
/// </summary>
public class LogAnalysisService
{
    private readonly LogService _logService;
    private readonly HttpClient _http;
    private DateTime _lastAnalysis = DateTime.MinValue;
    private const int AnalysisIntervalHours = 12; // 最少间隔12小时

    /// <summary>分析完成事件：返回改进建议列表</summary>
    public event Action<List<ImprovementSuggestion>>? OnAnalysisComplete;

    public LogAnalysisService()
    {
        _logService = LogService.Instance;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>是否应该执行分析</summary>
    public bool ShouldAnalyze()
    {
        return (DateTime.Now - _lastAnalysis).TotalHours >= AnalysisIntervalHours;
    }

    /// <summary>
    /// 执行日志分析，返回改进建议列表
    /// </summary>
    /// <param name="apiKey">大模型 API Key（用于 AI 深度分析）</param>
    /// <param name="baseUrl">API Base URL</param>
    /// <param name="model">模型名称</param>
    /// <param name="projectPath">当前项目路径</param>
    public async Task<List<ImprovementSuggestion>> AnalyzeAsync(
        string? apiKey = null,
        string? baseUrl = null,
        string? model = null,
        string? projectPath = null)
    {
        _lastAnalysis = DateTime.Now;
        var suggestions = new List<ImprovementSuggestion>();

        try
        {
            // 1. 本地统计分析
            var (errors, warnings, total) = _logService.GetLogStats();
            var logContent = _logService.ReadAllLogs(3000);

            if (string.IsNullOrEmpty(logContent) || total < 10)
            {
                LogService.Instance.Debug("日志条目不足，跳过分析", "LogAnalysis");
                return suggestions;
            }

            LogService.Instance.Info($"日志分析开始: {total}条, 错误={errors}, 警告={warnings}", "LogAnalysis");

            // 2. 本地启发式分析
            suggestions.AddRange(AnalyzeLocally(logContent, errors, warnings));

            // 3. 如果有 API Key，调用大模型深度分析
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(baseUrl))
            {
                var aiSuggestions = await AnalyzeWithAIAsync(logContent, apiKey, baseUrl, model ?? "deepseek-v4-pro", projectPath);
                if (aiSuggestions.Count > 0)
                    suggestions.AddRange(aiSuggestions);
            }

            if (suggestions.Count > 0)
            {
                LogService.Instance.Info($"日志分析完成: {suggestions.Count} 条改进建议", "LogAnalysis");
                OnAnalysisComplete?.Invoke(suggestions);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"日志分析异常: {ex.Message}", "LogAnalysis");
        }

        return suggestions;
    }

    /// <summary>本地启发式日志分析</summary>
    private static List<ImprovementSuggestion> AnalyzeLocally(string logContent, int errorCount, int warningCount)
    {
        var suggestions = new List<ImprovementSuggestion>();
        var lowerLog = logContent.ToLowerInvariant();

        // 模式1: 高频错误
        if (errorCount >= 5)
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Id = "local-high-errors",
                Title = $"日志中存在 {errorCount} 个错误",
                Description = $"最近日志中发现 {errorCount} 个ERROR级别日志，建议排查高频错误原因并增强异常处理。",
                Category = SuggestionCategory.Stability,
                Priority = SuggestionPriority.High,
                CanAutoFix = false
            });
        }

        // 模式2: 空catch块（来自SelfMaintenance扫描）
        if (lowerLog.Contains("catch { }") || lowerLog.Contains("catch{ }"))
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Id = "local-empty-catch",
                Title = "发现空 catch 块",
                Description = "日志或代码中存在空 catch 语句，异常被静默吞掉，建议至少添加日志记录以便排查问题。",
                Category = SuggestionCategory.CodeQuality,
                Priority = SuggestionPriority.Medium,
                CanAutoFix = true
            });
        }

        // 模式3: 网络超时/连接失败
        if (lowerLog.Contains("timeout") || lowerLog.Contains("连接") && lowerLog.Contains("失败"))
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Id = "local-network-issues",
                Title = "检测到网络连接问题",
                Description = "日志中出现网络超时或连接失败的记录，建议优化超时重试策略或检查网络环境。",
                Category = SuggestionCategory.Network,
                Priority = SuggestionPriority.Medium,
                CanAutoFix = false
            });
        }

        // 模式4: 编译失败
        if (lowerLog.Contains("编译失败") || lowerLog.Contains("build failed"))
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Id = "local-build-failures",
                Title = "检测到编译失败记录",
                Description = "日志中记录了编译失败事件，建议检查项目依赖和代码错误，或开启自动构建验证。",
                Category = SuggestionCategory.Build,
                Priority = SuggestionPriority.High,
                CanAutoFix = false
            });
        }

        // 模式5: GC频繁回收（内存压力）
        var gcCount = CountOccurrences(lowerLog, "内存回收");
        if (gcCount >= 10)
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Id = "local-memory-pressure",
                Title = "内存压力：频繁GC回收",
                Description = $"日志中记录了 {gcCount} 次内存回收事件，可能存在内存泄漏或大对象分配，建议优化内存使用。",
                Category = SuggestionCategory.Performance,
                Priority = SuggestionPriority.Medium,
                CanAutoFix = false
            });
        }

        // 模式6: 警告过多
        if (warningCount >= 20)
        {
            suggestions.Add(new ImprovementSuggestion
            {
                Id = "local-many-warnings",
                Title = $"日志中存在 {warningCount} 个警告",
                Description = "大量 WARN 级别日志可能表示潜在问题，建议检查并降低警告频率或解决根本原因。",
                Category = SuggestionCategory.Stability,
                Priority = SuggestionPriority.Low,
                CanAutoFix = false
            });
        }

        return suggestions;
    }

    /// <summary>调用大模型进行深度日志分析</summary>
    private async Task<List<ImprovementSuggestion>> AnalyzeWithAIAsync(
        string logContent, string apiKey, string baseUrl, string model, string? projectPath)
    {
        var suggestions = new List<ImprovementSuggestion>();

        try
        {
            // 截断日志，避免超长
            var truncatedLog = TruncateLog(logContent, 4000);
            if (string.IsNullOrWhiteSpace(truncatedLog)) return suggestions;

            var prompt = $@"你是一个软件质量和改进专家。请分析以下应用程序的运行日志，并提出3-5条具体的软件改进建议。

日志内容（最近条目）：
```
{truncatedLog}
```

项目路径：{projectPath ?? "未知"}

请以JSON数组格式返回建议，每条建议包含以下字段：
- title: 简短标题
- description: 详细描述（1-2句话）
- category: 分类，可选值: Stability/Performance/CodeQuality/UX/Network/Build/Security
- priority: 优先级，可选值: High/Medium/Low
- canAutoFix: 是否可以自动修复，布尔值

只返回JSON数组，不要包含其他文字。";

            var messages = new List<object>
            {
                new { role = "system", content = "你是一个软件质量分析专家。只返回JSON格式的分析结果。" },
                new { role = "user", content = prompt }
            };

            var reqBody = JsonSerializer.Serialize(new
            {
                model,
                messages,
                temperature = 0.3,
                max_tokens = 1500,
                stream = false
            });

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(reqBody, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                LogService.Instance.Warn($"AI日志分析请求失败: {response.StatusCode}", "LogAnalysis");
                return suggestions;
            }

            var respBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respBody);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(content)) return suggestions;

            // 提取JSON数组
            var jsonStart = content.IndexOf('[');
            var jsonEnd = content.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd < 0) return suggestions;

            var jsonContent = content[jsonStart..(jsonEnd + 1)];
            var aiItems = JsonSerializer.Deserialize<List<JsonElement>>(jsonContent);
            if (aiItems == null) return suggestions;

            foreach (var item in aiItems)
            {
                suggestions.Add(new ImprovementSuggestion
                {
                    Id = $"ai-{Guid.NewGuid():N}"[..12],
                    Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Category = ParseCategory(item),
                    Priority = ParsePriority(item),
                    CanAutoFix = item.TryGetProperty("canAutoFix", out var af) && af.GetBoolean()
                });
            }

            LogService.Instance.Info($"AI日志分析生成 {suggestions.Count} 条建议", "LogAnalysis");
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"AI日志分析异常: {ex.Message}", "LogAnalysis");
        }

        return suggestions;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }

    private static string TruncateLog(string log, int maxChars)
    {
        if (log.Length <= maxChars) return log;
        // 保留开头和结尾
        var half = maxChars / 2;
        return log[..half] + "\n... [中间省略] ...\n" + log[^half..];
    }

    private static SuggestionCategory ParseCategory(JsonElement item) =>
        item.TryGetProperty("category", out var c) && Enum.TryParse<SuggestionCategory>(c.GetString(), true, out var cat)
            ? cat : SuggestionCategory.Stability;

    private static SuggestionPriority ParsePriority(JsonElement item) =>
        item.TryGetProperty("priority", out var p) && Enum.TryParse<SuggestionPriority>(p.GetString(), true, out var pri)
            ? pri : SuggestionPriority.Medium;
}

/// <summary>改进建议项</summary>
public class ImprovementSuggestion
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SuggestionCategory Category { get; set; } = SuggestionCategory.Stability;
    public SuggestionPriority Priority { get; set; } = SuggestionPriority.Medium;
    public bool CanAutoFix { get; set; }
    public bool Confirmed { get; set; }
    public bool Applied { get; set; }

    public string CategoryIcon => Category switch
    {
        SuggestionCategory.Stability => "🛡",
        SuggestionCategory.Performance => "⚡",
        SuggestionCategory.CodeQuality => "📝",
        SuggestionCategory.UX => "🎨",
        SuggestionCategory.Network => "🌐",
        SuggestionCategory.Build => "🔨",
        SuggestionCategory.Security => "🔒",
        _ => "💡"
    };

    public string PriorityIcon => Priority switch
    {
        SuggestionPriority.High => "🔴",
        SuggestionPriority.Medium => "🟡",
        SuggestionPriority.Low => "🟢",
        _ => ""
    };

    public string DisplayText => $"{CategoryIcon} {PriorityIcon} {Title}";
    public string FullDisplay => $"{DisplayText}\n   {Description}";
}

public enum SuggestionCategory
{
    Stability,
    Performance,
    CodeQuality,
    UX,
    Network,
    Build,
    Security
}

public enum SuggestionPriority
{
    High,
    Medium,
    Low
}
