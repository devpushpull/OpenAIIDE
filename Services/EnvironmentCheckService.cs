using System.Diagnostics;

namespace AIIDEWPF.Services;

/// <summary>
/// 运行环境检测服务 —— 确保软件能正常运行所需的环境已安装
/// 检测项：.NET SDK/Runtime、Git、常用开发工具
/// </summary>
public class EnvironmentCheckService
{
    /// <summary>单次检测结果缓存</summary>
    private static EnvironmentCheckResult? _cachedResult;

    /// <summary>检测所有必要环境，返回检测结果</summary>
    public static async Task<EnvironmentCheckResult> CheckAllAsync()
    {
        if (_cachedResult != null)
            return _cachedResult;

        var result = new EnvironmentCheckResult();

        // 并行检测各项（各命令独立，互不依赖）
        var tasks = new[]
        {
            CheckDotNetAsync(),
            CheckGitAsync(),
        };

        await Task.WhenAll(tasks);

        result.DotNet = tasks[0].Result;
        result.Git = tasks[1].Result;

        result.IsAllReady = result.DotNet.IsInstalled && result.Git.IsInstalled;
        _cachedResult = result;

        // 记录检测结果
        if (!result.IsAllReady)
        {
            var missing = new List<string>();
            if (!result.DotNet.IsInstalled) missing.Add(".NET SDK/Runtime");
            if (!result.Git.IsInstalled) missing.Add("Git");
            LogService.Instance.Warn($"环境检测: 缺失 {string.Join(", ", missing)}", "EnvCheck");
        }
        else
        {
            LogService.Instance.Info($"环境检测通过: .NET {result.DotNet.Version}, Git {result.Git.Version}", "EnvCheck");
        }

        return result;
    }

    /// <summary>快速检测 .NET 是否可用（同步版本，用于启动时快速判断）</summary>
    public static bool IsDotNetAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch (Exception ex) { LogService.Instance.Debug($"环境检查异常: {ex.Message}", "EnvCheck"); return false; }
    }

    /// <summary>生成用户友好的缺失环境提示消息</summary>
    public static string GetMissingEnvironmentMessage(EnvironmentCheckResult result)
    {
        var lines = new List<string>();

        if (!result.IsAllReady)
        {
            lines.Add("⚠️ 检测到以下运行环境缺失，可能影响部分功能：");
            lines.Add("");

            if (!result.DotNet.IsInstalled)
            {
                lines.Add("  ❌ .NET SDK / Runtime 未安装");
                lines.Add("     → 编译/运行 C#/F#/VB.NET 项目功能不可用");
                lines.Add("     → 下载地址: https://dotnet.microsoft.com/download");
                lines.Add("");
            }

            if (!result.Git.IsInstalled)
            {
                lines.Add("  ❌ Git 未安装");
                lines.Add("     → Git 版本控制功能不可用");
                lines.Add("     → 下载地址: https://git-scm.com/downloads");
                lines.Add("");
            }

            lines.Add("💡 安装后重启 AI IDE 即可正常使用全部功能。");
        }
        else
        {
            lines.Add("✅ 运行环境检测通过");
            lines.Add($"   .NET: {result.DotNet.Version}");
            lines.Add($"   Git:  {result.Git.Version}");
        }

        return string.Join('\n', lines);
    }

    // ===== 私有检测方法 =====

    private static async Task<EnvCheckItem> CheckDotNetAsync()
    {
        var item = new EnvCheckItem { Name = ".NET SDK/Runtime", DownloadUrl = "https://dotnet.microsoft.com/download" };
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                item.Error = "无法启动 dotnet 进程";
                return item;
            }

            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            if (p.ExitCode == 0)
            {
                item.IsInstalled = true;
                item.Version = output.Trim();
            }
            else
            {
                item.Error = (await p.StandardError.ReadToEndAsync()).Trim();
                if (string.IsNullOrEmpty(item.Error))
                    item.Error = "dotnet 命令返回非零退出码";
            }
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
        }
        return item;
    }

    private static async Task<EnvCheckItem> CheckGitAsync()
    {
        var item = new EnvCheckItem { Name = "Git", DownloadUrl = "https://git-scm.com/downloads" };
        try
        {
            // 复用 GitService 的检测逻辑
            if (GitService.IsGitAvailable())
            {
                item.IsInstalled = true;
                // 获取版本号
                var psi = new ProcessStartInfo("git", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    var output = await p.StandardOutput.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    item.Version = output.Trim().Replace("git version ", "");
                }
            }
            else
            {
                item.Error = "Git 未安装或不在 PATH 中";
            }
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
        }
        return item;
    }
}

/// <summary>环境检测结果</summary>
public class EnvironmentCheckResult
{
    public EnvCheckItem DotNet { get; set; } = new() { Name = ".NET SDK/Runtime" };
    public EnvCheckItem Git { get; set; } = new() { Name = "Git" };
    public bool IsAllReady { get; set; }

    /// <summary>所有检测项</summary>
    public IEnumerable<EnvCheckItem> AllItems => new[] { DotNet, Git };
}

/// <summary>单项环境检测结果</summary>
public class EnvCheckItem
{
    public string Name { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public string Version { get; set; } = "未安装";
    public string Error { get; set; } = string.Empty;
    /// <summary>下载地址（缺失时使用）</summary>
    public string DownloadUrl { get; set; } = string.Empty;
    public string StatusIcon => IsInstalled ? "✅" : "❌";
    public string DisplayText => IsInstalled
        ? $"{Name}: {Version}"
        : $"{Name}: 未安装";
}
