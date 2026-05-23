namespace AIIDEWPF.ViewModels;

/// <summary>搜索面板结果项（用于左侧搜索面板展示）</summary>
public class GrepMatchDisplay
{
    public string File { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Content { get; set; } = string.Empty;
    public string DisplayText => $"{File}:{Line}";
    public string Preview => Content?.Length > 60 ? Content[..60] + "..." : Content ?? "";
}
