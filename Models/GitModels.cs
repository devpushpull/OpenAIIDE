namespace AIIDEWPF.Models;

/// <summary>Git Blame 行信息</summary>
public class GitBlameLine
{
    public int LineNumber { get; set; }
    public string CommitHash { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string LineContent { get; set; } = string.Empty;
}

/// <summary>Git 提交信息</summary>
public class GitCommitInfo
{
    public string Hash { get; set; } = string.Empty;
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
    public string Author { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Message { get; set; } = string.Empty;
}
