using System.Diagnostics;
using System.IO;
using System.Text;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>Git 操作服务——通过 CLI 执行 Git 命令</summary>
public class GitService
{
    private string _repoPath = string.Empty;

    public void SetRepoPath(string path) => _repoPath = path;

    /// <summary>检查 git 是否可用</summary>
    public static bool IsGitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch (Exception ex) { LogService.Instance.Debug($"检查Git是否可用异常: {ex.Message}", "Git"); return false; }
    }

    /// <summary>检查目录是否是 git 仓库</summary>
    public bool IsGitRepo()
    {
        if (string.IsNullOrEmpty(_repoPath)) return false;
        return Directory.Exists(Path.Combine(_repoPath, ".git"));
    }

    /// <summary>初始化 git 仓库</summary>
    public OperationResult Init()
    {
        return RunGit("init");
    }

    /// <summary>获取 git 状态（简短）</summary>
    public OperationResult Status()
    {
        return RunGit("status --short");
    }

    /// <summary>获取当前分支名</summary>
    public string GetCurrentBranch()
    {
        var r = RunGit("branch --show-current");
        return r.Success ? r.Output.Trim() : "";
    }

    /// <summary>获取所有远程仓库</summary>
    public List<GitRemote> GetRemotes()
    {
        var result = new List<GitRemote>();
        var r = RunGit("remote -v");
        if (!r.Success) return result;

        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            var name = parts[0];
            var urlAndType = parts[1].Split(' ', StringSplitOptions.TrimEntries);
            if (urlAndType.Length < 2) continue;
            result.Add(new GitRemote { Name = name, Url = urlAndType[0], Type = urlAndType[1] });
        }
        return result;
    }

    /// <summary>添加或更新远程仓库</summary>
    public OperationResult SetRemote(string name, string url)
    {
        // 先检查是否已存在
        var remotes = GetRemotes();
        if (remotes.Any(r => r.Name == name))
            return RunGit($"remote set-url {EscapeArg(name)} {EscapeArg(url)}");
        else
            return RunGit($"remote add {EscapeArg(name)} {EscapeArg(url)}");
    }

    /// <summary>添加所有文件到暂存区</summary>
    public OperationResult AddAll()
    {
        return RunGit("add .");
    }

    /// <summary>提交</summary>
    public OperationResult Commit(string message)
    {
        return RunGit($"commit -m {EscapeArg(message)}");
    }

    /// <summary>推送到远程仓库（使用内存凭证，不留痕）</summary>
    public OperationResult Push(GitConfig config)
    {
        if (string.IsNullOrEmpty(config.RemoteUrl))
            return new OperationResult { Success = false, Error = "未配置远程仓库地址" };

        // 先设置远程
        var remoteResult = SetRemote(config.RemoteName, config.RemoteUrl);
        if (!remoteResult.Success)
            return new OperationResult { Success = false, Error = $"设置远程仓库失败: {remoteResult.Error}" };

        // 构建内联凭证脚本（Windows 兼容）
        string credentialScript;
        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            // 转义特殊字符防止注入
            var escapedUser = config.Username.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var escapedPass = config.Password.Replace("\\", "\\\\").Replace("\"", "\\\"");
            credentialScript = $"!f() {{ echo \"username={escapedUser}\"; echo \"password={escapedPass}\"; }}; f";
        }
        else
        {
            credentialScript = "!f() { cat > /dev/null; echo; }; f";
        }

        var args = $"-c credential.helper=\"{credentialScript}\" push {EscapeArg(config.RemoteName)} {EscapeArg(config.Branch)}";
        return RunGit(args, timeoutMs: 60000);
    }

    /// <summary>拉取远程代码</summary>
    public OperationResult Pull(GitConfig config)
    {
        if (string.IsNullOrEmpty(config.RemoteUrl))
            return new OperationResult { Success = false, Error = "未配置远程仓库地址" };

        var branch = string.IsNullOrEmpty(config.Branch) ? "main" : config.Branch;
        string credentialScript;
        if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
        {
            var escapedUser = config.Username.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var escapedPass = config.Password.Replace("\\", "\\\\").Replace("\"", "\\\"");
            credentialScript = $"!f() {{ echo \"username={escapedUser}\"; echo \"password={escapedPass}\"; }}; f";
        }
        else
        {
            credentialScript = "!f() { cat > /dev/null; echo; }; f";
        }

        var args = $"-c credential.helper=\"{credentialScript}\" pull {EscapeArg(config.RemoteName)} {EscapeArg(branch)}";
        return RunGit(args, timeoutMs: 60000);
    }

    /// <summary>获取提交日志</summary>
    public OperationResult Log(int maxCount = 20, bool oneline = true)
    {
        var format = oneline ? "--oneline --decorate" : "--stat";
        return RunGit($"log -{maxCount} {format}");
    }

    /// <summary>列出所有分支</summary>
    public OperationResult ListBranches()
    {
        return RunGit("branch -a -v");
    }

    /// <summary>查看文件逐行作者信息</summary>
    public OperationResult Blame(string filePath)
    {
        if (!File.Exists(Path.Combine(_repoPath, filePath)))
            return new OperationResult { Success = false, Error = $"文件不存在: {filePath}" };
        return RunGit($"blame {EscapeArg(filePath)}");
    }

    /// <summary>查看工作区或暂存区差异</summary>
    public OperationResult Diff(string? filePath = null, bool staged = false)
    {
        var args = "diff";
        if (staged) args += " --staged";
        if (!string.IsNullOrEmpty(filePath)) args += $" -- {EscapeArg(filePath)}";
        return RunGit(args);
    }

    /// <summary>查看两个分支/提交之间的差异</summary>
    public OperationResult DiffBetween(string left, string right, string? filePath = null)
    {
        var args = $"diff {EscapeArg(left)}..{EscapeArg(right)}";
        if (!string.IsNullOrEmpty(filePath)) args += $" -- {EscapeArg(filePath)}";
        return RunGit(args);
    }

    /// <summary>获取未暂存变更的文件列表（含状态）</summary>
    public List<(string File, string Status)> GetChangedFiles()
    {
        var result = new List<(string, string)>();
        var r = Status();
        if (!r.Success) return result;
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var status = line.Length >= 2 ? line[..2].Trim() : "";
            var file = line.Length >= 3 ? line[3..].Trim() : "";
            if (!string.IsNullOrEmpty(file))
                result.Add((file, status));
        }
        return result;
    }

    /// <summary>一键提交并推送</summary>
    public OperationResult CommitAndPush(GitConfig config)
    {
        // 检查是否有变更
        var status = Status();
        if (!status.Success) return status;
        if (string.IsNullOrWhiteSpace(status.Output))
            return new OperationResult { Success = true, Output = "没有需要提交的变更" };

        // add
        var add = AddAll();
        if (!add.Success) return add;

        // commit
        var commit = Commit(config.CommitMessage);
        if (!commit.Success && !commit.Output.Contains("nothing to commit"))
            return commit;

        // push
        return Push(config);
    }

    // ===== 内部方法 =====

    private OperationResult RunGit(string args, int timeoutMs = 30000)
    {
        var result = new OperationResult();
        try
        {
            LogService.Instance.Debug($"git {args}", "Git");
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = _repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                result.Success = false;
                result.Error = "无法启动 git 进程";
                LogService.Instance.Error("Git 进程启动失败", "Git");
                return result;
            }

            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit(timeoutMs);

            if (!p.HasExited)
            {
                p.Kill();
                result.Success = false;
                result.Error = "Git 操作超时";
                LogService.Instance.Warn($"Git 操作超时 ({timeoutMs}ms): git {args}", "Git");
                return result;
            }

            result.Success = p.ExitCode == 0;
            result.Output = output.Trim();
            result.Error = error.Trim();

            if (!result.Success)
                LogService.Instance.Warn($"Git 失败 (exit={p.ExitCode}): git {args} | {result.Error}", "Git");

            // git 有时把信息输出到 stderr
            if (!result.Success && string.IsNullOrEmpty(result.Error) && !string.IsNullOrEmpty(output))
                result.Error = output.Trim();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"Git 操作异常: {ex.Message}";
            LogService.Instance.Error($"Git 异常: git {args} | {ex.Message}", "Git");
        }
        return result;
    }

    private static string EscapeArg(string arg) => CommonUtils.EscapeCmdArg(arg);

    // ===== Git Blame / 历史 =====

    /// <summary>获取文件的 Git Blame 信息</summary>
    public List<GitBlameLine> GetBlame(string filePath)
    {
        var result = new List<GitBlameLine>();
        if (!IsGitRepo()) return result;

        try
        {
            var relativePath = Path.GetRelativePath(_repoPath, filePath);
            var output = RunGit($"blame --date=short -s -l {EscapeArg(relativePath)}");
            if (!output.Success || string.IsNullOrEmpty(output.Output)) return result;

            foreach (var line in output.Output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // blame 格式: commit_hash author_name date line_number) content
                var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                result.Add(new GitBlameLine
                {
                    CommitHash = parts[0][..Math.Min(8, parts[0].Length)],
                    Author = parts[1],
                    Date = parts[2],
                    Content = parts.Length > 3 ? parts[3].TrimStart() : ""
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Git Blame 失败: {ex.Message}");
        }
        return result;
    }

    /// <summary>获取文件历史（最近N次提交）</summary>
    public List<GitCommitInfo> GetFileHistory(string filePath, int maxCommits = 20)
    {
        var result = new List<GitCommitInfo>();
        if (!IsGitRepo()) return result;

        try
        {
            var relativePath = Path.GetRelativePath(_repoPath, filePath);
            var format = "%H|%an|%ad|%s";
            var output = RunGit($"log --date=short --format={format} -n {maxCommits} -- {EscapeArg(relativePath)}");
            if (!output.Success || string.IsNullOrEmpty(output.Output)) return result;

            foreach (var line in output.Output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('|', 4);
                if (parts.Length < 4) continue;
                result.Add(new GitCommitInfo
                {
                    Hash = parts[0][..Math.Min(8, parts[0].Length)],
                    Author = parts[1],
                    Date = parts[2],
                    Message = parts[3]
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Git 历史失败: {ex.Message}");
        }
        return result;
    }

    /// <summary>获取当前仓库的变更统计</summary>
    public (int staged, int unstaged, int untracked) GetChangeCount()
    {
        if (!IsGitRepo()) return (0, 0, 0);
        try
        {
            var status = RunGit("status --porcelain");
            if (!status.Success) return (0, 0, 0);
            int staged = 0, unstaged = 0, untracked = 0;
            foreach (var line in status.Output.Split('\n'))
            {
                if (line.Length < 2) continue;
                var code = line[..2];
                if (code.Contains('?')) untracked++;
                else if (code[0] != ' ') staged++;
                if (code[1] != ' ') unstaged++;
            }
            return (staged, unstaged, untracked);
        }
        catch (Exception ex) { LogService.Instance.Debug($"Git变更统计失败: {ex.Message}", "Git"); return (0, 0, 0); }
    }
}

/// <summary>Git 操作结果</summary>
public class OperationResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string Summary => Success ? Output : Error;
}

/// <summary>Git 远程仓库信息</summary>
public class GitRemote
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // fetch / push
}

/// <summary>Git Blame 行信息</summary>
public class GitBlameLine
{
    public string CommitHash { get; set; } = "";
    public string Author { get; set; } = "";
    public string Date { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>Git 提交信息</summary>
public class GitCommitInfo
{
    public string Hash { get; set; } = "";
    public string Author { get; set; } = "";
    public string Date { get; set; } = "";
    public string Message { get; set; } = "";
}
