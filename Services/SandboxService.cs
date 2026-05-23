using System.Diagnostics;
using System.Text.RegularExpressions;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 沙箱安全服务 —— 命令校验、执行隔离、审计日志
/// 集成到 AIService.Tools.cs 的 RunInTerminalTool 前置校验
/// </summary>
public class SandboxService
{
    private readonly LogService _log;
    private SandboxConfig _config;

    /// <summary>审计记录列表</summary>
    public List<SandboxAuditEntry> AuditLog { get; } = new();

    /// <summary>当前沙箱配置</summary>
    public SandboxConfig Config => _config;

    /// <summary>触发终端命令内联确认: (command, summary) -> (accepted, executionMode, rememberPreference)</summary>
    public event Func<string, string, Task<(bool accepted, string executionMode, string rememberPreference)>>? OnTerminalConsentRequested;

    /// <summary>命令黑名单正则模式（危险命令）</summary>
    private static readonly Regex[] DangerPatterns = new[]
    {
        new Regex(@"rm\s+-rf\s+/", RegexOptions.IgnoreCase),
        new Regex(@"rd\s+/s\s+/q\s+[A-Z]:\\", RegexOptions.IgnoreCase),
        new Regex(@"format\s+[A-Z]:", RegexOptions.IgnoreCase),
        new Regex(@"del\s+/f\s+/s\s+/q\s+[A-Z]:\\", RegexOptions.IgnoreCase),
        new Regex(@"dd\s+if=", RegexOptions.IgnoreCase),
        new Regex(@"mkfs\.", RegexOptions.IgnoreCase),
        new Regex(@">\s*/dev/sd", RegexOptions.IgnoreCase),
        new Regex(@"chmod\s+777\s+[-/]", RegexOptions.IgnoreCase),
        new Regex(@"curl.*\|.*sh", RegexOptions.IgnoreCase),
        new Regex(@"wget.*\|.*sh", RegexOptions.IgnoreCase),
    };

    public SandboxService(LogService log, SandboxConfig? config = null)
    {
        _log = log;
        _config = config ?? new SandboxConfig();
    }

    /// <summary>更新沙箱配置</summary>
    public void UpdateConfig(SandboxConfig config)
    {
        _config = config;
        _log.Info($"[SandboxService] 沙箱配置已更新, Level={config.Level}");
    }

    /// <summary>
    /// 校验终端命令合法性
    /// 返回: (blocked, reason) — blocked=true 表示命令被拦截
    /// </summary>
    public (bool blocked, string reason) ValidateCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return (false, "");

        var trimmed = command.Trim();

        // 1. 黑名单强制拦截（所有级别）
        foreach (var pattern in DangerPatterns)
        {
            if (pattern.IsMatch(trimmed))
            {
                var entry = new SandboxAuditEntry
                {
                    Category = "command",
                    Detail = trimmed.Length > 100 ? trimmed[..100] + "..." : trimmed,
                    Reason = "危险命令模式匹配",
                    WasBlocked = true
                };
                AuditLog.Add(entry);
                _log.Warn("[SandboxService] 拦截危险命令: {0}", entry.Detail);
                return (true, $"危险命令已拦截: 匹配模式 {pattern}");
            }
        }

        // 2. Strict 模式：仅允许白名单
        if (_config.Level == SandboxLevel.Strict)
        {
            var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
            var isAllowed = _config.CommandWhitelist.Any(w => firstWord.StartsWith(w, StringComparison.OrdinalIgnoreCase));
            if (!isAllowed)
            {
                var entry = new SandboxAuditEntry
                {
                    Category = "command",
                    Detail = trimmed.Length > 100 ? trimmed[..100] + "..." : trimmed,
                    Reason = "严格模式：不在白名单中",
                    WasBlocked = true
                };
                AuditLog.Add(entry);
                _log.Warn("[SandboxService] 严格模式拦截: {0}", entry.Detail);
                return (true, $"严格模式已拦截: 命令 '{firstWord}' 不在白名单中");
            }
        }

        return (false, "");
    }

    /// <summary>
    /// 校验 URL 合法性（用于 web_search / fetch_content）
    /// </summary>
    public (bool blocked, string reason) ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (false, "");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (true, "无效的URL格式");

        var host = uri.Host.ToLowerInvariant();

        // 检查域名黑名单
        if (_config.DomainBlacklist.Any(d => host == d || host.EndsWith("." + d)))
        {
            var entry = new SandboxAuditEntry
            {
                Category = "url",
                Detail = url,
                Reason = "域名在黑名单中",
                WasBlocked = true
            };
            AuditLog.Add(entry);
            _log.Warn("[SandboxService] 拦截黑名单URL: {0}", url);
            return (true, $"URL已拦截: 域名 {host} 在黑名单中");
        }

        // Strict 模式：仅允许白名单
        if (_config.Level == SandboxLevel.Strict)
        {
            var isAllowed = _config.DomainWhitelist.Any(d => host == d || host.EndsWith("." + d));
            if (!isAllowed)
            {
                var entry = new SandboxAuditEntry
                {
                    Category = "url",
                    Detail = url,
                    Reason = "严格模式：域名不在白名单中",
                    WasBlocked = true
                };
                AuditLog.Add(entry);
                _log.Warn("[SandboxService] 严格模式拦截URL: {0}", url);
                return (true, $"严格模式已拦截: 域名 {host} 不在白名单中");
            }
        }

        // 内网IP拦截（Moderate 和 Strict）
        if (_config.Level >= SandboxLevel.Moderate && IsPrivateIP(host))
        {
            var entry = new SandboxAuditEntry
            {
                Category = "url",
                Detail = url,
                Reason = "内网IP地址拦截",
                WasBlocked = true
            };
            AuditLog.Add(entry);
            _log.Warn("[SandboxService] 拦截内网IP URL: {0}", url);
            return (true, $"内网地址已拦截: {host}");
        }

        return (false, "");
    }

    /// <summary>
    /// 请求用户内联确认终端命令执行
    /// 流程: 校验命令 → 如果需要确认 → 触发 OnTerminalConsentRequested → 等待用户决策
    /// </summary>
    public async Task<(bool accepted, string executionMode, string rememberPreference)> RequestConsentAsync(string command)
    {
        // 先校验命令安全性
        var (blocked, reason) = ValidateCommand(command);
        if (blocked)
        {
            _log.Warn("[SandboxService] 命令被沙箱拦截: {0}", reason);
            return (false, "sandbox", "always_ask");
        }

        // 不需要审批则直接通过
        if (!_config.TerminalRequireApproval)
        {
            _log.Info("[SandboxService] 无需审批，直接执行");
            return (true, _config.DefaultExecutionMode, "remember_per_project");
        }

        // 触发内联确认
        var summary = GenerateSummary(command);
        if (OnTerminalConsentRequested == null)
        {
            _log.Warn("[SandboxService] 无确认回调，默认允许沙箱执行");
            return (true, "sandbox", "remember_per_project");
        }

        _log.Info($"[SandboxService] 请求用户确认命令: {summary}");
        return await OnTerminalConsentRequested(command, summary);
    }

    /// <summary>
    /// 执行命令（沙箱模式：restricted shell / 终端模式：完整权限）
    /// 当前实现：沙箱模式下对命令做 final check，终端模式下直接执行
    /// </summary>
    public async Task<(string output, int exitCode)> ExecuteAsync(string command, string executionMode, string projectPath)
    {
        if (executionMode == "sandbox")
        {
            // 沙箱模式：再次校验 + 可能限制路径
            var (blocked, reason) = ValidateCommand(command);
            if (blocked)
                return (reason, 1);

            _log.Info($"[SandboxService] 沙箱模式执行: {GenerateSummary(command)}");
        }
        else
        {
            _log.Info($"[SandboxService] 终端模式执行: {GenerateSummary(command)}");
        }

        // 实际通过 TerminalService 执行
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = projectPath
                };

                using var proc = Process.Start(psi);
                if (proc == null) return ("无法启动进程", 1);

                var output = proc.StandardOutput.ReadToEnd();
                var error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30000);

                var result = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
                return (result, proc.ExitCode);
            }
            catch (Exception ex)
            {
                return ($"执行异常: {ex.Message}", 1);
            }
        });
    }

    /// <summary>检查是否是内网IP</summary>
    private static bool IsPrivateIP(string host)
    {
        if (host == "localhost" || host == "127.0.0.1" || host == "::1")
            return true;
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                return bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168);
            }
        }
        return false;
    }

    /// <summary>生成命令摘要（截取前60字符）</summary>
    private static string GenerateSummary(string command)
        => command.Length > 60 ? command[..57] + "..." : command;

    /// <summary>清除审计日志</summary>
    public void ClearAuditLog() => AuditLog.Clear();
}
