using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>
/// 自动迭代修复服务 — 定期扫描项目发现问题，提示用户确认后执行修复。
/// 低风险操作自动修复，高风险操作仅建议。
/// </summary>
public class SelfMaintenanceService
{
    private readonly string _projectPath;
    private readonly HttpClient _http;
    private DateTime _lastScan = DateTime.MinValue;
    private const int ScanIntervalMinutes = 30;
    private const int MaxAutoFixFiles = 5;

    public SelfMaintenanceService(string projectPath)
    {
        _projectPath = projectPath;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public bool ShouldScan()
    {
        if (string.IsNullOrEmpty(_projectPath)) return false;
        return (DateTime.Now - _lastScan).TotalMinutes >= ScanIntervalMinutes;
    }

    /// <summary>
    /// 执行扫描，返回发现的问题列表。
    /// 返回 null 表示无需扫描或无问题。
    /// </summary>
    public async Task<List<MaintenanceIssue>?> ScanAsync()
    {
        if (!ShouldScan()) return null;
        _lastScan = DateTime.Now;

        var issues = new List<MaintenanceIssue>();

        try
        {
            // 1. 扫描项目源代码问题
            var codeIssues = ScanProjectCode();
            issues.AddRange(codeIssues);

            // 2. 联网检查最新动态（仅空闲时做一次，不阻塞）
            try
            {
                var newsIssues = await CheckForUpdatesAsync();
                issues.AddRange(newsIssues);
            }
            catch (Exception ex) { LogService.Instance.Debug($"联网检查异常: {ex.Message}", "Maintenance"); }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "Maintenance");
        }

        return issues.Count > 0 ? issues : null;
    }

    /// <summary>执行自动修复（仅限低风险操作）</summary>
    public async Task<List<MaintenanceIssue>> ApplyAutoFixesAsync(List<MaintenanceIssue> issues)
    {
        var applied = new List<MaintenanceIssue>();
        int fixedCount = 0;

        foreach (var issue in issues.Where(i => i.CanAutoFix && fixedCount < MaxAutoFixFiles))
        {
            try
            {
                if (string.IsNullOrEmpty(issue.FilePath) || !File.Exists(issue.FilePath))
                    continue;

                var content = await File.ReadAllTextAsync(issue.FilePath);
                var newContent = ApplyFix(content, issue);
                if (newContent != content)
                {
                    await File.WriteAllTextAsync(issue.FilePath, newContent);
                    issue.Fixed = true;
                    applied.Add(issue);
                    fixedCount++;
                    LogService.Instance.Info($"自动修复: {issue.FilePath} — {issue.Title}", "Maintenance");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex, "Maintenance");
            }
        }

        return applied;
    }

    /// <summary>备份项目到临时目录</summary>
    public string? BackupProject()
    {
        try
        {
            var backupDir = Path.Combine(
                Path.GetTempPath(),
                $"AIIDE_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(backupDir);

            foreach (var file in Directory.GetFiles(_projectPath, "*.*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(_projectPath, file);
                if (relPath.StartsWith(".aiide") || relPath.StartsWith("bin") || relPath.StartsWith("obj"))
                    continue;

                var dest = Path.Combine(backupDir, relPath);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                File.Copy(file, dest, true);
            }

            LogService.Instance.Info($"项目已备份到: {backupDir}", "Maintenance");
            return backupDir;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "Maintenance");
            return null;
        }
    }

    // ===== 代码扫描 =====

    private List<MaintenanceIssue> ScanProjectCode()
    {
        var issues = new List<MaintenanceIssue>();

        try
        {
            var srcFiles = Directory.GetFiles(_projectPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\.aiide\\"))
                .Take(20);

            foreach (var file in srcFiles)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var lines = content.Split('\n');
                    var fileName = Path.GetFileName(file);

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        var lineNum = i + 1;

                        // 空 catch 块
                        if (line == "catch { }" || line == "catch{ }" || line == "catch {")
                        {
                            issues.Add(new MaintenanceIssue
                            {
                                FilePath = file,
                                LineNumber = lineNum,
                                Title = "空 catch 块",
                                Description = "空的 catch 块吞掉了异常，建议至少记录日志",
                                Type = IssueType.CodeSmell,
                                CanAutoFix = true,
                                FixDescription = "添加异常日志记录"
                            });
                        }
                        // TODO 注释
                        else if (Regex.IsMatch(line, @"//\s*TODO", RegexOptions.IgnoreCase))
                        {
                            issues.Add(new MaintenanceIssue
                            {
                                FilePath = file,
                                LineNumber = lineNum,
                                Title = "TODO 标记",
                                Description = $"未完成的 TODO: {line}",
                                Type = IssueType.Todo,
                                CanAutoFix = false,
                                FixDescription = "需手动处理"
                            });
                        }
                        // 过长的行（> 200 字符）
                        else if (line.Length > 200 && !line.StartsWith("//") && !line.StartsWith("using "))
                        {
                            issues.Add(new MaintenanceIssue
                            {
                                FilePath = file,
                                LineNumber = lineNum,
                                Title = "代码行过长",
                                Description = $"行长度 {line.Length} 超过 200 字符，建议拆分",
                                Type = IssueType.Style,
                                CanAutoFix = false,
                                FixDescription = "建议手动拆分"
                            });
                        }
                    }
                }
                catch (Exception ex) { LogService.Instance.Debug($"扫描文件异常: {ex.Message}", "Maintenance"); }
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"项目代码扫描异常: {ex.Message}", "Maintenance"); }

        return issues;
    }

    /// <summary>联网检查 AI IDE 最新动态</summary>
    private async Task<List<MaintenanceIssue>> CheckForUpdatesAsync()
    {
        var issues = new List<MaintenanceIssue>();
        try
        {
            var response = await _http.GetAsync("https://api-docs.deepseek.com/");
            if (response.IsSuccessStatusCode)
            {
                issues.Add(new MaintenanceIssue
                {
                    Title = "联网检查: DeepSeek API",
                    Description = "DeepSeek API 文档可访问，服务正常",
                    Type = IssueType.Info,
                    CanAutoFix = false
                });
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"联网检查异常: {ex.Message}", "Maintenance"); }

        return issues;
    }

    /// <summary>
    /// 联网检查 Qoder、通义灵码、Cursor 等竞品更新动态。
    /// 返回格式化的更新报告，供"帮助 → 检查更新"菜单调用。
    /// </summary>
    public async Task<string> CheckCompetitorUpdatesAsync()
    {
        var results = new List<string>();
        results.Add($"=== AI IDE 竞品更新检查 ({DateTime.Now:yyyy-MM-dd HH:mm}) ===");
        results.Add("");

        // 并行检查三个竞品
        var competitors = new[]
        {
            (Name: "Qoder", Url: "https://qoder.com", SearchQuery: "Qoder AI IDE latest release 2025 2026"),
            (Name: "通义灵码", Url: "https://tongyi.aliyun.com/lingma", SearchQuery: "通义灵码 最新版本 更新 2025 2026"),
            (Name: "Cursor", Url: "https://cursor.com", SearchQuery: "Cursor AI IDE latest release changelog 2025 2026")
        };

        foreach (var comp in competitors)
        {
            try
            {
                var status = await CheckSiteStatusAsync(comp.Url);
                var info = await SearchUpdateInfoAsync(comp.SearchQuery);
                results.Add($"【{comp.Name}】");
                results.Add($"  站点状态: {(status ? "可访问" : "无法访问")}");
                if (!string.IsNullOrEmpty(info))
                    results.Add($"  最新动态: {info}");
                else
                    results.Add($"  最新动态: 未能获取更新信息");
                results.Add("");
            }
            catch (Exception ex)
            {
                results.Add($"【{comp.Name}】检查失败: {ex.Message}");
                results.Add("");
            }
        }

        results.Add("=== 本工具功能建议 ===");
        results.Add("如需对齐竞品最新功能，可在对话中描述需求，AI 将自动实现。");
        results.Add("");
        results.Add($"当前项目版本: {GetCurrentVersion()}");

        return string.Join(Environment.NewLine, results);
    }

    /// <summary>检查站点是否可访问</summary>
    private async Task<bool> CheckSiteStatusAsync(string url)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>搜索竞品更新信息</summary>
    private async Task<string> SearchUpdateInfoAsync(string query)
    {
        try
        {
            var encoded = Uri.EscapeDataString(query);
            var url = $"https://html.duckduckgo.com/html/?q={encoded}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return "";

            var html = await response.Content.ReadAsStringAsync();
            // 提取搜索结果标题
            var titleMatches = Regex.Matches(html,
                @"class=""result__title"">.*?<a[^>]*?>(.*?)</a>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var titles = new List<string>();
            for (int i = 0; i < Math.Min(titleMatches.Count, 3); i++)
            {
                var title = Regex.Replace(titleMatches[i].Groups[1].Value, "<[^>]+>", " ");
                title = Regex.Replace(title, @"\s+", " ").Trim();
                if (!string.IsNullOrEmpty(title))
                    titles.Add(title);
            }

            return titles.Count > 0 ? string.Join(" | ", titles) : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>获取当前项目版本号</summary>
    public static string GetCurrentVersion()
    {
        try
        {
            var csprojPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "AIIDEWPF.csproj");
            if (File.Exists(csprojPath))
            {
                var csproj = File.ReadAllText(csprojPath);
                var versionMatch = Regex.Match(csproj, @"<Version>(.*?)</Version>");
                if (versionMatch.Success)
                    return $"v{versionMatch.Groups[1].Value}";
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"获取版本号异常: {ex.Message}", "Maintenance"); }
        return "开发版";
    }

    // ===== 自动修复实现 =====

    private static string ApplyFix(string content, MaintenanceIssue issue)
    {
        return issue.Type switch
        {
            IssueType.CodeSmell when issue.Title.Contains("空 catch") =>
                FixEmptyCatch(content),
            _ => content
        };
    }

    private static string FixEmptyCatch(string content)
    {
        // 将 catch { } 替换为带日志记录的 catch
        return Regex.Replace(content,
            @"catch\s*\{\s*\}",
            "catch (Exception ex) { LogService.Instance.Error(ex, \"AutoFix\"); }");
    }
}

/// <summary>维护问题项</summary>
public class MaintenanceIssue
{
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueType Type { get; set; }
    public bool CanAutoFix { get; set; }
    public string FixDescription { get; set; } = string.Empty;
    public bool Fixed { get; set; }

    public string DisplayText => Type switch
    {
        IssueType.CodeSmell => $"⚠ {Title} (第{LineNumber}行)",
        IssueType.Todo => $"📝 {Title} (第{LineNumber}行)",
        IssueType.Style => $"💡 {Title} (第{LineNumber}行)",
        IssueType.Info => $"ℹ {Title}",
        _ => Title
    };
}

public enum IssueType
{
    CodeSmell,
    Todo,
    Style,
    Info
}
