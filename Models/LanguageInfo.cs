namespace AIIDEWPF.Models;

/// <summary>模板支持等级</summary>
public enum TemplateLevel { Full, Basic, None }

/// <summary>编程语言信息与模板</summary>
public class LanguageInfo
{
    public string Name { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public TemplateLevel Level { get; init; }
    /// <summary>代码文件模板内容</summary>
    public string FileTemplate { get; init; } = string.Empty;
    /// <summary>项目结构模板（逗号分隔的目录, 用 | 分隔多个路径）</summary>
    public string ProjectTemplate { get; init; } = string.Empty;

    /// <summary>是否完整支持（含项目结构模板）</summary>
    public bool HasFullSupport => Level == TemplateLevel.Full;
    /// <summary>是否基础支持（仅文件模板）</summary>
    public bool HasBasicSupport => Level == TemplateLevel.Basic;
    /// <summary>未支持时的提示信息</summary>
    public string UnsupportedHint => Level == TemplateLevel.None
        ? $"暂不支持 {Name} 语言模板，将以纯文本创建。"
        : "";
}
