using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 技能服务 —— 加载和管理 markdown 技能文件（对标 Cursor/Claude Code Skills）
/// 技能文件存放路径:
///   全局: %AppData%/AIIDE/skills/
///   项目: {project}/.aiide/skills/
/// 文件格式: .md 文件，前置 YAML front matter（用 --- 包裹）
/// </summary>
public class SkillService
{
    private readonly string? _projectPath;
    private readonly string _globalSkillsDir;

    private List<SkillManifest> _skills = new();

    // front matter 正则: 匹配 ---\nkey: value\n...\n---
    private static readonly Regex FrontMatterRegex = new(
        @"^---\s*\n(.*?)\n---\s*\n",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public SkillService(string? projectPath = null)
    {
        _projectPath = projectPath;
        _globalSkillsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIIDE", "skills");
        Refresh();
    }

    /// <summary>重新扫描所有技能目录</summary>
    public void Refresh()
    {
        _skills.Clear();
        ScanDirectory(_globalSkillsDir, "global");
        if (!string.IsNullOrEmpty(_projectPath))
        {
            var projectSkillsDir = Path.Combine(_projectPath, ".aiide", "skills");
            ScanDirectory(projectSkillsDir, "project");
        }
        LogService.Instance.Info($"技能扫描完成: {_skills.Count} 个可用", "Skill");
    }

    /// <summary>列出所有可用技能</summary>
    public IReadOnlyList<SkillManifest> ListSkills() => _skills.AsReadOnly();

    /// <summary>按关键词搜索技能（名称 + 描述 + 触发词）</summary>
    public List<SkillManifest> SearchSkills(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _skills.ToList();

        var q = query.ToLowerInvariant();
        return _skills
            .Where(s => s.Name.ToLowerInvariant().Contains(q)
                     || s.Description.ToLowerInvariant().Contains(q)
                     || s.Triggers.Any(t => t.ToLowerInvariant().Contains(q))
                     || s.Id.ToLowerInvariant() == q)
            .ToList();
    }

    /// <summary>按名称获取技能（不区分大小写）</summary>
    public SkillManifest? GetSkill(string name)
    {
        return _skills.FirstOrDefault(s =>
            string.Equals(s.Id, name, StringComparison.OrdinalIgnoreCase)
         || string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>获取技能提示词文本（供 AI 上下文注入）</summary>
    public string? GetSkillPrompt(string name)
    {
        var skill = GetSkill(name);
        if (skill == null || !skill.Enabled)
            return null;

        return FormatSkillPrompt(skill);
    }

    /// <summary>获取所有已启用技能的提示词汇总</summary>
    public string GetAllSkillsPrompt()
    {
        var enabled = _skills.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "## 可用技能 (Skills)",
            "你可以使用 /skill <技能名> 来激活以下技能：",
            ""
        };

        foreach (var s in enabled)
        {
            var scopeTag = s.Scope == "global" ? "[全局]" : "[项目]";
            var triggers = s.Triggers.Length > 0
                ? $"触发词: {string.Join(", ", s.Triggers)}"
                : "";
            lines.Add($"- **{s.Name}** ({s.Id}) {scopeTag}: {s.Description} {triggers}");
        }
        lines.Add("");
        return string.Join('\n', lines);
    }

    /// <summary>格式化单个技能的提示词</summary>
    private static string FormatSkillPrompt(SkillManifest skill)
    {
        return $"""
## 已激活技能: {skill.Name}

{skill.Content}

---
当前技能 ({skill.Id}) 已激活。请严格遵循上述技能指引执行任务。
""";
    }

    /// <summary>扫描指定目录中的 .md 技能文件</summary>
    private void ScanDirectory(string dir, string scope)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var skill = ParseSkillFile(file, scope);
                if (skill != null)
                    _skills.Add(skill);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warn($"技能文件解析失败: {file} | {ex.Message}", "Skill");
            }
        }
    }

    /// <summary>解析单个 .md 技能文件</summary>
    private static SkillManifest? ParseSkillFile(string filePath, string scope)
    {
        var content = File.ReadAllText(filePath);
        var id = Path.GetFileNameWithoutExtension(filePath);

        var skill = new SkillManifest
        {
            Id = id,
            Name = id,
            FilePath = filePath,
            Scope = scope,
            Content = content
        };

        // 尝试解析 YAML-like front matter
        var fmMatch = FrontMatterRegex.Match(content);
        if (fmMatch.Success)
        {
            var fm = fmMatch.Groups[1].Value;
            // 切除 front matter 得到正文
            skill.Content = content[fmMatch.Length..].Trim();
            ParseFrontMatter(fm, skill);
        }
        else
        {
            // 没有 front matter，尝试从内容中提取标题
            skill.Name = id;
            skill.Description = $"技能: {id}";
        }

        return skill;
    }

    /// <summary>简易 YAML front matter 解析（key: value）</summary>
    private static void ParseFrontMatter(string fm, SkillManifest skill)
    {
        var lines = fm.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = trimmed[..colonIdx].Trim().ToLowerInvariant();
            var value = trimmed[(colonIdx + 1)..].Trim();

            switch (key)
            {
                case "name":
                    skill.Name = value;
                    break;
                case "description":
                    skill.Description = value;
                    break;
                case "version":
                    skill.Version = value;
                    break;
                case "triggers":
                    skill.Triggers = value.Split(',', StringSplitOptions.TrimEntries)
                        .Where(t => !string.IsNullOrEmpty(t)).ToArray();
                    break;
                case "category":
                    skill.Category = value.ToLowerInvariant();
                    break;
                case "enabled":
                    skill.Enabled = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
    }
}
