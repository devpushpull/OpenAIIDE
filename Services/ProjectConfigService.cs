using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIIDEWPF.Services;

/// <summary>
/// 管理项目级配置文件 .aiide/project.json，跟踪项目状态
/// </summary>
public class ProjectConfigService
{
    private const string ConfigDir = ".aiide";
    private const string ConfigFile = "project.json";

    public string ProjectPath { get; private set; } = string.Empty;
    public bool IsLoaded => !string.IsNullOrEmpty(ProjectPath);

    private ProjectConfig? _data;
    public ProjectConfig Data => _data ??= new();

    public void LoadOrCreate(string projectRoot)
    {
        ProjectPath = projectRoot;
        var dir = Path.Combine(projectRoot, ConfigDir);
        var file = Path.Combine(dir, ConfigFile);

        if (File.Exists(file))
        {
            try
            {
                _data = JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                LogService.Instance.Warn($"项目配置解析失败: {ex.Message}", "ProjectConfig");
                _data = null;
            }
        }

        if (_data == null)
        {
            _data = new ProjectConfig
            {
                ProjectName = Path.GetFileName(projectRoot),
                CreatedAt = DateTime.Now
            };
            Save();
        }
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;
        var dir = Path.Combine(ProjectPath, ConfigDir);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        // 写入 .gitkeep 确保 .aiide 目录在 Windows 资源管理器中可见
        var gitkeep = Path.Combine(dir, ".gitkeep");
        if (!File.Exists(gitkeep))
            File.WriteAllText(gitkeep, "");
        var file = Path.Combine(dir, ConfigFile);
        File.WriteAllText(file, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void RecordFileChange(string relativePath, string changeType)
    {
        if (_data == null) return;
        _data.LastModifiedAt = DateTime.Now;
        var existing = _data.RecentChanges.Find(c =>
            c.RelativePath == relativePath && c.ChangeType == changeType);
        if (existing != null)
            existing.Timestamp = DateTime.Now;
        else
        {
            _data.RecentChanges.Add(new FileChangeEntry
            {
                RelativePath = relativePath,
                ChangeType = changeType,
                Timestamp = DateTime.Now
            });
        }
        // 只保留最近 50 条
        if (_data.RecentChanges.Count > 50)
            _data.RecentChanges = _data.RecentChanges.OrderByDescending(x => x.Timestamp).Take(50).ToList();
        Save();
    }

    public void SetOpenFiles(List<string> filePaths)
    {
        if (_data == null) return;
        _data.OpenFiles = filePaths.ConvertAll(f =>
        {
            try { return Path.GetRelativePath(ProjectPath, f); }
            catch (Exception ex) { LogService.Instance.Debug($"路径转换失败 '{f}': {ex.Message}", "ProjectConfig"); return f; }
        });
        Save();
    }

    // ===== AGENTS.md 支持 =====

    /// <summary>读取项目根目录的 AGENTS.md 文件内容</summary>
    public string? GetAgentsMd()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return null;
        var path = Path.Combine(ProjectPath, "AGENTS.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    // ===== .aiideignore 支持 =====

    /// <summary>读取 .aiideignore 中的忽略模式列表</summary>
    public List<string> GetAiideIgnorePatterns()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return new();
        var path = Path.Combine(ProjectPath, ".aiideignore");
        if (!File.Exists(path)) return new();
        return File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
            .ToList();
    }

    // ===== Rules 规则文件支持 (.aiide/rules/) =====

    /// <summary>获取规则目录路径</summary>
    private string RulesDir => Path.Combine(ProjectPath, ConfigDir, "rules");

    /// <summary>扫描 .aiide/rules/ 目录下的所有 .md 规则文件</summary>
    public List<ProjectRule> GetRules()
    {
        var rules = new List<ProjectRule>();
        if (string.IsNullOrEmpty(ProjectPath)) return rules;
        var dir = RulesDir;
        if (!Directory.Exists(dir)) return rules;

        foreach (var file in Directory.GetFiles(dir, "*.md"))
        {
            var content = File.ReadAllText(file);
            var (description, globs, alwaysApply) = ParseRuleFrontmatter(content);
            rules.Add(new ProjectRule
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Content = StripFrontmatter(content),
                Description = description,
                Globs = globs,
                AlwaysApply = alwaysApply,
                FilePath = file
            });
        }
        return rules;
    }

    /// <summary>解析 .md 规则文件的 YAML frontmatter</summary>
    private static (string description, string[] globs, bool alwaysApply) ParseRuleFrontmatter(string content)
    {
        var description = "";
        var globs = Array.Empty<string>();
        var alwaysApply = false;

        if (content.StartsWith("---"))
        {
            var endIdx = content.IndexOf("---", 3);
            if (endIdx > 0)
            {
                var fm = content[3..endIdx];
                foreach (var line in fm.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("description:"))
                        description = trimmed["description:".Length..].Trim().Trim('"');
                    else if (trimmed.StartsWith("globs:"))
                        globs = ParseYamlList(trimmed, fm, line);
                    else if (trimmed.StartsWith("alwaysApply:"))
                        bool.TryParse(trimmed["alwaysApply:".Length..].Trim(), out alwaysApply);
                }
            }
        }
        return (description, globs, alwaysApply);
    }

    private static string[] ParseYamlList(string firstLine, string fullFrontmatter, string startLine)
    {
        var values = new List<string>();
        var val = firstLine["globs:".Length..].Trim().Trim('[', ']', '"');
        if (!string.IsNullOrEmpty(val))
            values.Add(val);
        return values.ToArray();
    }

    private static string StripFrontmatter(string content)
    {
        if (content.StartsWith("---"))
        {
            var endIdx = content.IndexOf("---", 3);
            if (endIdx > 0)
                return content[(endIdx + 3)..].TrimStart('\n', '\r');
        }
        return content;
    }

    // ===== Plans 计划存储支持 (.aiide/plans/) =====

    /// <summary>获取计划目录路径</summary>
    private string PlansDir => Path.Combine(ProjectPath, ConfigDir, "plans");

    /// <summary>保存计划 Markdown 文件</summary>
    public void SavePlan(string planName, string markdownContent)
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;
        var dir = PlansDir;
        Directory.CreateDirectory(dir);
        var safeName = string.Join("_", planName.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(dir, $"{safeName}.md");
        File.WriteAllText(filePath, markdownContent);
    }

    /// <summary>列出所有已保存的计划文件</summary>
    public List<string> GetPlans()
    {
        var plans = new List<string>();
        if (string.IsNullOrEmpty(ProjectPath)) return plans;
        var dir = PlansDir;
        if (!Directory.Exists(dir)) return plans;
        return Directory.GetFiles(dir, "*.md").Select(Path.GetFileNameWithoutExtension).ToList()!;
    }

    /// <summary>获取指定计划的内容</summary>
    public string? GetPlanContent(string planName)
    {
        if (string.IsNullOrEmpty(ProjectPath)) return null;
        var safeName = string.Join("_", planName.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(PlansDir, $"{safeName}.md");
        return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
    }
}

public class ProjectConfig
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastModifiedAt")]
    public DateTime LastModifiedAt { get; set; }

    [JsonPropertyName("openFiles")]
    public List<string> OpenFiles { get; set; } = new();

    [JsonPropertyName("recentChanges")]
    public List<FileChangeEntry> RecentChanges { get; set; } = new();

    // ===== 1.0 扩展：技术栈与构建信息 =====

    [JsonPropertyName("techStack")]
    public ProjectTechStack? TechStack { get; set; }

    [JsonPropertyName("buildCommand")]
    public string BuildCommand { get; set; } = string.Empty;

    [JsonPropertyName("testCommand")]
    public string TestCommand { get; set; } = string.Empty;

    [JsonPropertyName("codeStyle")]
    public ProjectCodeStyle? CodeStyle { get; set; }

    [JsonPropertyName("securityRules")]
    public List<string> SecurityRules { get; set; } = new();
}

public class ProjectTechStack
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("framework")]
    public string Framework { get; set; } = string.Empty;

    [JsonPropertyName("buildTool")]
    public string BuildTool { get; set; } = string.Empty;

    [JsonPropertyName("packageManager")]
    public string PackageManager { get; set; } = string.Empty;
}

public class ProjectCodeStyle
{
    [JsonPropertyName("indentStyle")]
    public string IndentStyle { get; set; } = "spaces"; // spaces / tabs

    [JsonPropertyName("indentSize")]
    public int IndentSize { get; set; } = 4;

    [JsonPropertyName("namingConvention")]
    public string NamingConvention { get; set; } = string.Empty; // PascalCase / camelCase / snake_case
}

public class FileChangeEntry
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

/// <summary>项目规则文件信息（来自 .aiide/rules/*.md）</summary>
public class ProjectRule
{
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Globs { get; set; } = Array.Empty<string>();
    public bool AlwaysApply { get; set; }
    public string FilePath { get; set; } = string.Empty;
}
