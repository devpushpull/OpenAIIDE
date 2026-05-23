using System.Diagnostics;
using System.IO;

namespace AIIDEWPF.Services;

public class TerminalService
{
    private Process? _process;
    private StreamWriter? _stdin;

    public event Action<string, string>? OnDataReceived;
    public event Action<string, int>? OnExited;

    public string Id { get; private set; } = string.Empty;

    /// <summary>检测系统默认 Shell（优先 PowerShell，降级 cmd）</summary>
    public static (string FileName, string Args) DetectShell()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command Write-Host 'pwsh'")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            if (p?.ExitCode == 0)
                return ("powershell.exe", "-NoExit -Command Set-Location '");
        }
        catch (Exception ex) { LogService.Instance.Debug($"检测Shell类型异常: {ex.Message}", "Terminal"); }
        return ("cmd.exe", "/k cd /d \"");
    }

    public bool CreateTerminal(string? cwd = null)
    {
        try
        {
            Id = Guid.NewGuid().ToString("N")[..8];
            var targetPath = cwd ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var (shell, prefix) = DetectShell();
            var isPowerShell = shell.Contains("powershell", StringComparison.OrdinalIgnoreCase);
            var args = isPowerShell
                ? $"{prefix}{targetPath}'"
                : $"{prefix}{targetPath}\"";
            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = args,
                WorkingDirectory = targetPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    OnDataReceived?.Invoke(Id, e.Data + "\n");
            };
            _process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    OnDataReceived?.Invoke(Id, e.Data + "\n");
            };
            _process.Exited += (s, e) =>
            {
                var exitCode = _process?.ExitCode ?? -1;
                LogService.Instance.Info($"终端进程已退出 [{Id}] exit={exitCode}", "Terminal");
                OnExited?.Invoke(Id, exitCode);
            };

            _process.Start();
            _stdin = _process.StandardInput;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            LogService.Instance.Info($"终端已创建 [{Id}] shell={shell}", "Terminal");
            OnDataReceived?.Invoke(Id, $"Terminal started [{Id}]\r\n{psi.WorkingDirectory}>");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"终端创建失败: {ex.Message}", "Terminal");
            OnDataReceived?.Invoke(Id, $"Error: {ex.Message}\r\n");
            return false;
        }
    }

    public void WriteInput(string data)
    {
        if (_stdin != null && !_process?.HasExited == true)
        {
            _stdin.Write(data);
            _stdin.Flush();
        }
    }

    public void Kill()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
                _process.Dispose();
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"停止进程异常: {ex.Message}", "Terminal"); }
        finally { _process = null; }
    }

    public bool IsRunning
    {
        get
        {
            try { return _process != null && !_process.HasExited; }
            catch (Exception ex) { LogService.Instance.Debug($"检查进程状态异常: {ex.Message}", "Terminal"); return false; }
        }
    }
}
