namespace AIIDEWPF.Models;

/// <summary>技能清单 —— 描述一个可用的 AI 技能（markdown 文件驱动）</summary>
public class SkillManifest
{
    /// <summary>技能唯一标识（从文件名推导，如 "pdf"）</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>技能名称（从 front matter 或文件名推导）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>技能描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>技能版本</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>触发词（如 "/pdf", "处理PDF"）</summary>
    public string[] Triggers { get; set; } = Array.Empty<string>();

    /// <summary>技能分类</summary>
    public string Category { get; set; } = "general";

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>markdown 文件完整路径</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>技能提示词内容（markdown 正文）</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>作用域: global / project</summary>
    public string Scope { get; set; } = "project";
}
