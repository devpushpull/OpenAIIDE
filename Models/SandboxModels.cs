using System.Text.Json.Serialization;

namespace AIIDEWPF.Models;

/// <summary>
/// 沙箱安全级别
/// </summary>
public enum SandboxLevel
{
    /// <summary>宽松：仅拦截黑名单命令和域名</summary>
    Relaxed = 0,
    /// <summary>适中：拦截黑名单 + 危险命令模式 + 内网IP（默认）</summary>
    Moderate = 1,
    /// <summary>严格：仅允许白名单，其余全部拦截</summary>
    Strict = 2
}

/// <summary>
/// 沙箱安全配置 —— 持久化到 AppConfig
/// </summary>
public class SandboxConfig
{
    /// <summary>沙箱安全级别</summary>
    public SandboxLevel Level { get; set; } = SandboxLevel.Moderate;

    /// <summary>终端命令是否需要用户审批</summary>
    public bool TerminalRequireApproval { get; set; } = true;

    /// <summary>终端命令审批偏好："always_ask" / "remember_per_project"</summary>
    public string TerminalApprovalMode { get; set; } = "remember_per_project";

    /// <summary>默认执行模式："sandbox" / "terminal"</summary>
    public string DefaultExecutionMode { get; set; } = "sandbox";

    // ===== 命令黑名单（任何级别都拦截）=====

    /// <summary>命令黑名单</summary>
    public List<string> CommandBlacklist { get; set; } = new()
    {
        "rm -rf /",
        "rd /s /q C:\\",
        "format C:",
        "format D:",
        "del /f /s /q C:\\",
        "dd if=",
        "mkfs."
    };

    /// <summary>命令白名单（Strict 模式下仅允许这些）</summary>
    public List<string> CommandWhitelist { get; set; } = new()
    {
        "dotnet", "git", "npm", "npx", "node", "python", "go", "cargo",
        "ls", "dir", "cat", "type", "echo", "pwd", "cd",
        "mkdir", "rmdir", "copy", "move", "ren",
        "ping", "nslookup", "ipconfig", "netstat"
    };

    // ===== 域名黑名单/白名单 =====

    /// <summary>域名黑名单</summary>
    public List<string> DomainBlacklist { get; set; } = new();

    /// <summary>域名白名单（Strict 模式下仅允许这些）</summary>
    public List<string> DomainWhitelist { get; set; } = new()
    {
        "github.com", "api.github.com",
        "nuget.org", "api.nuget.org",
        "npmjs.com", "registry.npmjs.org",
        "pypi.org", "crates.io"
    };
}

/// <summary>
/// 沙箱拦截审计记录
/// </summary>
public class SandboxAuditEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Category { get; set; } = string.Empty;  // "command" / "url" / "file"
    public string Detail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool WasBlocked { get; set; } = true;
}
