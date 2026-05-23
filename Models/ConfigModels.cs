using System.Text.Json.Serialization;

namespace AIIDEWPF.Models;

/// <summary>终端命令执行偏好</summary>
public enum TerminalExecutionPreference
{
    /// <summary>每次执行危险命令都询问</summary>
    AskEveryTime = 0,
    /// <summary>始终允许（不再询问）</summary>
    AlwaysAllow = 1,
    /// <summary>始终拒绝</summary>
    AlwaysDeny = 2
}

public class AIConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Provider { get; set; } = "deepseek";
    public string Model { get; set; } = "deepseek-v4-pro";
    public int MaxTokens { get; set; } = 0;
    public bool Stream { get; set; } = true;
    public bool AllowExternalFileEdit { get; set; } = false;
    /// <summary>是否自动执行联网搜索（关闭时需要每次手动确认）</summary>
    public bool AutoWebSearch { get; set; } = false;
    /// <summary>终端命令执行偏好：AskEveryTime=每次询问, AlwaysAllow=始终允许, AlwaysDeny=始终拒绝</summary>
    public TerminalExecutionPreference TerminalExecutionPreference { get; set; } = TerminalExecutionPreference.AskEveryTime;
}

public class EditorConfig
{
    public double FontSize { get; set; } = 14;
    public string Theme { get; set; } = "Dark";
    public bool WordWrap { get; set; } = true;
    public bool ShowMinimap { get; set; } = false;
    public int TabSize { get; set; } = 4;
}

public class AppConfig
{
    public AIConfig AI { get; set; } = new();
    public EditorConfig Editor { get; set; } = new();
    public AppearanceConfig Appearance { get; set; } = new();
    public TerminalConfig Terminal { get; set; } = new();
    public FileExcludeConfig FileExclude { get; set; } = new();
    public PrivacyConfig Privacy { get; set; } = new();
    public ProxyConfig Proxy { get; set; } = new();
    public SandboxConfig Sandbox { get; set; } = new();
    public List<string> RecentProjects { get; set; } = new();
    public Dictionary<string, ProviderDef> Providers { get; set; } = new();

    /// <summary>机器/用户指纹（DPAPI 加密）：用于检测换机或换用户，自动清除敏感配置</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineFingerprint { get; set; }
}

/// <summary>外观设置</summary>
public class AppearanceConfig
{
    public string Theme { get; set; } = "Dark";   // Dark / Light
    public string Language { get; set; } = "zh-CN"; // UI 显示语言
    public string ResponseLanguage { get; set; } = "中文"; // AI 响应语言
    public bool AutoCheckUpdates { get; set; } = true;
}

/// <summary>终端设置</summary>
public class TerminalConfig
{
    public string FontFamily { get; set; } = "Consolas";
    public int FontSize { get; set; } = 13;
    public string CursorStyle { get; set; } = "Block";   // Block, Underline, IBeam
    public string ShellPath { get; set; } = "powershell.exe";
    public int ScrollbackLines { get; set; } = 5000;
    public bool EnableColors { get; set; } = true;
    public bool EnableSyntaxHighlighting { get; set; } = true;
}

/// <summary>文件/搜索排除设置</summary>
public class FileExcludeConfig
{
    public string ExcludePatterns { get; set; } = "**/node_modules\n**/.git\n**/bin\n**/obj\n**/.vs\n**/packages";
    public string SearchExcludePatterns { get; set; } = "**/node_modules\n**/.git\n**/*.min.js\n**/*.map";
    public bool UseGitIgnore { get; set; } = true;
    public bool HideDotFiles { get; set; } = false;
}

/// <summary>隐私设置</summary>
public class PrivacyConfig
{
    public bool EnableTelemetry { get; set; } = true;
    public bool EnableCrashReports { get; set; } = true;
    public bool EnableUsageAnalytics { get; set; } = false;
    public bool ShareCodeSnippets { get; set; } = false;
    public bool AutoClearHistoryOnExit { get; set; } = false;
    public int HistoryRetentionDays { get; set; } = 30;
}

/// <summary>网络代理设置</summary>
public class ProxyConfig
{
    public bool EnableProxy { get; set; } = false;
    public string ProxyType { get; set; } = "HTTP";  // HTTP, SOCKS5
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public bool UseAuth { get; set; } = false;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
