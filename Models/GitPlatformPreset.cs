namespace AIIDEWPF.Models;

/// <summary>Git 平台预设 —— 用户可自定义追加</summary>
public class GitPlatformPreset
{
    public string Name { get; set; } = string.Empty;
    public string UrlTemplate { get; set; } = string.Empty;

    public string Display => $"{Name}  →  {UrlTemplate}";
}
