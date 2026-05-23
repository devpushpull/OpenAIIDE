namespace AIIDEWPF.Models;

/// <summary>Git 远程仓库配置</summary>
public class GitConfig
{
    public string RemoteName { get; set; } = "origin";
    public string RemoteUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string CommitMessage { get; set; } = "update";
}
