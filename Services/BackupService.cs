using System.IO;

namespace AIIDEWPF.Services;

/// <summary>
/// 智能备份服务 —— AI 修改文件前自动备份至 .aiide-backups/ 目录
/// 防止代码被大模型改错、破坏或丢失
/// </summary>
public class BackupService
{
    private readonly string? _projectPath;
    private bool _enabled = true;

    public BackupService(string? projectPath = null)
    {
        _projectPath = projectPath;
    }

    public bool Enabled { get => _enabled; set => _enabled = value; }

    /// <summary>备份文件（若文件内容有实质性差异）</summary>
    /// <returns>备份文件路径，若未备份则返回 null</returns>
    public string? BackupBeforeWrite(string filePath)
    {
        if (!_enabled || !File.Exists(filePath))
            return null;

        try
        {
            var backupDir = GetBackupDir();
            Directory.CreateDirectory(backupDir);

            var relativePath = GetRelativePath(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeName = relativePath.Replace(Path.DirectorySeparatorChar, '_')
                                       .Replace(Path.AltDirectorySeparatorChar, '_');
            var backupPath = Path.Combine(backupDir, $"{timestamp}_{safeName}");

            File.Copy(filePath, backupPath, overwrite: false);

            // 清理旧备份：每个文件最多保留 20 个备份
            CleanOldBackups(backupDir, safeName, 20);

            LogService.Instance.Info($"文件已备份: {filePath} -> {Path.GetFileName(backupPath)}");
            return backupPath;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"备份失败: {filePath} - {ex.Message}");
            return null;
        }
    }

    /// <summary>备份文本内容变更（用于 search_replace 操作）</summary>
    public string? BackupContentChange(string filePath, string originalContent)
    {
        if (!_enabled)
            return null;

        try
        {
            var backupDir = GetBackupDir();
            Directory.CreateDirectory(backupDir);

            var relativePath = GetRelativePath(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeName = relativePath.Replace(Path.DirectorySeparatorChar, '_')
                                       .Replace(Path.AltDirectorySeparatorChar, '_');
            var backupPath = Path.Combine(backupDir, $"{timestamp}_{safeName}");

            File.WriteAllText(backupPath, originalContent);

            CleanOldBackups(backupDir, safeName, 20);
            return backupPath;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"内容备份失败: {filePath} - {ex.Message}");
            return null;
        }
    }

    /// <summary>获取备份文件列表</summary>
    public List<string> GetBackups(string filePath)
    {
        var results = new List<string>();
        try
        {
            var backupDir = GetBackupDir();
            if (!Directory.Exists(backupDir)) return results;

            var safeName = GetRelativePath(filePath)
                .Replace(Path.DirectorySeparatorChar, '_')
                .Replace(Path.AltDirectorySeparatorChar, '_');

            foreach (var f in Directory.GetFiles(backupDir, $"*_{safeName}"))
                results.Add(f);
            results.Sort();
            results.Reverse(); // 最新在前
        }
        catch { }
        return results;
    }

    /// <summary>从备份恢复文件</summary>
    public bool Restore(string backupPath, string targetPath)
    {
        try
        {
            if (!File.Exists(backupPath)) return false;
            // 恢复前也备份一下当前内容
            if (File.Exists(targetPath))
                BackupBeforeWrite(targetPath);
            File.Copy(backupPath, targetPath, overwrite: true);
            LogService.Instance.Info($"文件已从备份恢复: {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "恢复备份");
            return false;
        }
    }

    // ==================== 批量变更快照（Checkpoint） ====================

    /// <summary>
    /// 创建批量变更快照：在多文件修改前备份所有受影响文件的原始内容。
    /// 对标 Cursor 的 auto-checkpoint 机制。
    /// </summary>
    /// <returns>checkpointId，失败返回 null</returns>
    public string? CreateCheckpoint(string[] filePaths, string sessionId = "", int toolCount = 0)
    {
        if (!_enabled || filePaths.Length == 0) return null;

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var checkpointDir = GetCheckpointDir(timestamp);
            Directory.CreateDirectory(checkpointDir);

            var manifest = new CheckpointManifest
            {
                Timestamp = timestamp,
                SessionId = sessionId,
                ToolCount = toolCount,
                Files = new List<CheckpointFileEntry>()
            };

            foreach (var fp in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(fp)) continue;

                try
                {
                    var relativePath = GetRelativePath(fp);
                    var safeName = relativePath.Replace(Path.DirectorySeparatorChar, '_')
                                                 .Replace(Path.AltDirectorySeparatorChar, '_');
                    var backupPath = Path.Combine(checkpointDir, safeName);

                    File.Copy(fp, backupPath, overwrite: false);

                    var fileInfo = new FileInfo(fp);
                    manifest.Files.Add(new CheckpointFileEntry
                    {
                        FilePath = fp,
                        BackupPath = backupPath,
                        Size = fileInfo.Length
                    });
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warn($"Checkpoint 备份单个文件失败: {fp} - {ex.Message}");
                }
            }

            // 写入 manifest
            var manifestPath = Path.Combine(checkpointDir, "manifest.json");
            var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);

            LogService.Instance.Info($"Checkpoint 已创建: {timestamp}, {manifest.Files.Count} 个文件");
            CleanupOldCheckpoints(10); // 保留最近 10 个 checkpoint
            return timestamp;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "创建 Checkpoint");
            return null;
        }
    }

    /// <summary>
    /// 完成 checkpoint：正常结束时调用，删除 checkpoint 目录。
    /// </summary>
    public void CompleteCheckpoint(string checkpointId)
    {
        try
        {
            var dir = GetCheckpointDir(checkpointId);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
                LogService.Instance.Debug($"Checkpoint 已完成并清理: {checkpointId}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"清理 Checkpoint 失败: {checkpointId} - {ex.Message}");
        }
    }

    /// <summary>获取所有残留的 checkpoint（上次异常退出遗留）</summary>
    public List<CheckpointManifest> GetPendingCheckpoints()
    {
        var result = new List<CheckpointManifest>();
        try
        {
            var baseDir = GetCheckpointBaseDir();
            if (!Directory.Exists(baseDir)) return result;

            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = System.Text.Json.JsonSerializer.Deserialize<CheckpointManifest>(json);
                    if (manifest != null)
                        result.Add(manifest);
                }
                catch { }
            }
            result.Sort((a, b) => string.Compare(b.Timestamp, a.Timestamp, StringComparison.Ordinal)); // 最新在前
        }
        catch { }
        return result;
    }

    /// <summary>从 checkpoint 恢复所有文件</summary>
    public bool RestoreCheckpoint(string checkpointId)
    {
        try
        {
            var dir = GetCheckpointDir(checkpointId);
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) return false;

            var json = File.ReadAllText(manifestPath);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<CheckpointManifest>(json);
            if (manifest == null) return false;

            int restored = 0;
            foreach (var entry in manifest.Files)
            {
                try
                {
                    if (File.Exists(entry.BackupPath))
                    {
                        var targetDir = Path.GetDirectoryName(entry.FilePath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        File.Copy(entry.BackupPath, entry.FilePath, overwrite: true);
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warn($"恢复文件失败: {entry.FilePath} - {ex.Message}");
                }
            }

            LogService.Instance.Info($"Checkpoint 已恢复: {checkpointId}, {restored}/{manifest.Files.Count} 个文件");

            // 恢复后删除 checkpoint
            CompleteCheckpoint(checkpointId);
            return restored > 0;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "恢复 Checkpoint");
            return false;
        }
    }

    /// <summary>放弃 checkpoint：直接删除</summary>
    public void DiscardCheckpoint(string checkpointId)
    {
        CompleteCheckpoint(checkpointId);
    }

    /// <summary>清理旧 checkpoint，保留最近 N 个</summary>
    public void CleanupOldCheckpoints(int maxKeep)
    {
        try
        {
            var baseDir = GetCheckpointBaseDir();
            if (!Directory.Exists(baseDir)) return;

            var dirs = Directory.GetDirectories(baseDir)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.Name)
                .ToList();

            for (int i = maxKeep; i < dirs.Count; i++)
            {
                try { dirs[i].Delete(recursive: true); } catch { }
            }
        }
        catch { }
    }

    // ==================== Git 快照保护 ====================

    /// <summary>
    /// 在大量代码修改前创建 Git 快照（stash）。
    /// 使用 git stash 保存当前工作区状态，即使程序崩溃也可通过 git stash pop 恢复。
    /// 这是最强的保护层级：即使所有 AIIDE 备份都丢失，Git 历史仍可恢复。
    /// </summary>
    /// <returns>stash 引用名，失败返回 null</returns>
    public string? SafeGitSnapshot(string reason = "")
    {
        if (!_enabled || string.IsNullOrEmpty(_projectPath)) return null;

        try
        {
            // 检查是否是 git 仓库
            var gitDir = Path.Combine(_projectPath, ".git");
            if (!Directory.Exists(gitDir)) return null;

            // 检查是否有未暂存变更需要保存
            var statusPsi = new System.Diagnostics.ProcessStartInfo("git", "status --porcelain")
            {
                WorkingDirectory = _projectPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var statusP = System.Diagnostics.Process.Start(statusPsi);
            statusP?.WaitForExit(5000);
            var hasChanges = statusP?.ExitCode == 0 && !string.IsNullOrEmpty(statusP.StandardOutput.ReadToEnd().Trim());

            // 即使没有变更也创建空 stash 作为标记点
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var stashMsg = string.IsNullOrEmpty(reason)
                ? $"AIIDE-auto-snapshot-{timestamp}"
                : $"AIIDE: {reason} ({timestamp})";

            var psi = new System.Diagnostics.ProcessStartInfo("git", $"stash push --include-untracked -m {EscapeArg(stashMsg)}")
            {
                WorkingDirectory = _projectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return null;
            p.WaitForExit(10000);

            if (p.ExitCode == 0)
            {
                LogService.Instance.Info($"Git 快照已创建: {stashMsg}");
                return stashMsg;
            }

            // 如果 "No local changes to save"，不算错误
            var err = p.StandardError.ReadToEnd();
            if (err.Contains("No local changes"))
            {
                LogService.Instance.Debug($"Git 快照跳过（无变更）");
                return null;
            }

            LogService.Instance.Warn($"Git 快照失败: {err}");
            return null;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Git 快照异常: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 恢复最近的 Git 快照（stash pop）。
    /// 用于从大量修改造成的错误中快速恢复。
    /// </summary>
    public bool RestoreGitSnapshot()
    {
        if (string.IsNullOrEmpty(_projectPath)) return false;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "stash pop")
            {
                WorkingDirectory = _projectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(10000);

            var output = p.StandardOutput.ReadToEnd();
            LogService.Instance.Info($"Git 快照已恢复: {output.Trim()}");
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Git 快照恢复失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 检查磁盘空间是否充足（至少需要 minMb MB）。
    /// 在大量修改前检查，避免因磁盘满导致写一半损坏。
    /// </summary>
    public bool HasSufficientDiskSpace(int minMb = 100)
    {
        try
        {
            var dir = !string.IsNullOrEmpty(_projectPath) ? _projectPath : GetBackupDir();
            var driveInfo = new DriveInfo(Path.GetPathRoot(dir) ?? "C:\\");
            var availableMb = driveInfo.AvailableFreeSpace / (1024 * 1024);
            return availableMb >= minMb;
        }
        catch
        {
            return true; // 无法检测时不阻塞操作
        }
    }

    private static string EscapeArg(string arg) => "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private string GetCheckpointBaseDir()
    {
        if (!string.IsNullOrEmpty(_projectPath))
            return Path.Combine(_projectPath, ".aiide", "checkpoints");
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIIDE", "checkpoints");
        return dataDir;
    }

    private string GetCheckpointDir(string timestamp)
        => Path.Combine(GetCheckpointBaseDir(), timestamp);

    private string GetBackupDir()
    {
        if (!string.IsNullOrEmpty(_projectPath))
            return Path.Combine(_projectPath, ".aiide-backups");
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIIDE", "backups");
        return dataDir;
    }

    private string GetRelativePath(string filePath)
    {
        if (!string.IsNullOrEmpty(_projectPath) && filePath.StartsWith(_projectPath, StringComparison.OrdinalIgnoreCase))
            return filePath[_projectPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(filePath);
    }

    private static void CleanOldBackups(string backupDir, string safeName, int maxKeep)
    {
        try
        {
            var files = Directory.GetFiles(backupDir, $"*_{safeName}");
            if (files.Length <= maxKeep) return;

            Array.Sort(files);
            // 删除最旧的
            for (int i = 0; i < files.Length - maxKeep; i++)
                File.Delete(files[i]);
        }
        catch { }
    }
}

/// <summary>Checkpoint 清单（存储在 manifest.json 中）</summary>
public class CheckpointManifest
{
    public string Timestamp { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int ToolCount { get; set; }
    public List<CheckpointFileEntry> Files { get; set; } = new();
}

/// <summary>Checkpoint 中的单个文件条目</summary>
public class CheckpointFileEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public long Size { get; set; }
}
