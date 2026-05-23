using System.IO;

namespace AIIDEWPF.Services;

// ==================== 抽象接口与模型 ====================

/// <summary>
/// 快照来源接口 —— 供未来扩展（用户手动备份、Git 历史、远程快照等）。
/// 实现此接口即可被 FileCompareService 统一调用。
/// </summary>
public interface ISnapshotSource
{
    /// <summary>快照来源标识（如 "AutoBackup", "Checkpoint", "Git", "UserBackup"）</summary>
    string SourceType { get; }
    /// <summary>获取指定文件的所有可用快照</summary>
    List<SnapshotInfo> GetSnapshots(string filePath);
    /// <summary>读取指定快照的文本内容</summary>
    string ReadSnapshotContent(SnapshotInfo snapshot);
}

/// <summary>单个快照元信息</summary>
public class SnapshotInfo
{
    /// <summary>快照对应的源文件路径</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>快照文件的物理路径</summary>
    public string SnapshotPath { get; init; } = string.Empty;
    /// <summary>快照创建时间</summary>
    public DateTime Timestamp { get; init; }
    /// <summary>快照来源类型（AutoBackup / Checkpoint / Git / UserBackup 等）</summary>
    public string SourceType { get; init; } = string.Empty;
    /// <summary>可读标签（如 "自动备份 20260518_120000"）</summary>
    public string Label { get; init; } = string.Empty;
    /// <summary>快照文件大小（字节）</summary>
    public long Size { get; init; }

    public override string ToString() => $"[{SourceType}] {Label}";
}

/// <summary>比较结果 —— 包含 DiffResult + 快照元信息</summary>
public class CompareResult
{
    /// <summary>源快照</summary>
    public SnapshotInfo? SourceSnapshot { get; init; }
    /// <summary>目标快照（或 null = 当前文件）</summary>
    public SnapshotInfo? TargetSnapshot { get; init; }
    /// <summary>源路径标签（用于 Diff 标题）</summary>
    public string SourceLabel => SourceSnapshot?.Label ?? "源";
    /// <summary>目标路径标签（用于 Diff 标题）</summary>
    public string TargetLabel => TargetSnapshot?.Label ?? "目标";
    /// <summary>Diff 计算结果</summary>
    public DiffResult Diff { get; init; } = new();
    /// <summary>是否有变化</summary>
    public bool HasChanges => Diff.HasChanges;
    /// <summary>变更统计摘要</summary>
    public string Summary => Diff.HasChanges
        ? $"+{Diff.AddedLines} 行新增 / -{Diff.RemovedLines} 行删除"
        : "无变化";
}

/// <summary>快照扫描结果（批量对比）</summary>
public class ScanResult
{
    /// <summary>扫描的文件路径</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>当前文件是否存在</summary>
    public bool FileExists { get; init; }
    /// <summary>可用快照列表</summary>
    public List<SnapshotInfo> Snapshots { get; init; } = new();
    /// <summary>与最新快照的差异摘要（null = 无快照或无差异）</summary>
    public CompareResult? LatestDiff { get; init; }
    /// <summary>快照数量</summary>
    public int SnapshotCount => Snapshots.Count;
}

// ==================== 内置快照来源实现 ====================

/// <summary>自动备份快照来源（通过 BackupService）</summary>
public class AutoBackupSnapshotSource : ISnapshotSource
{
    private readonly BackupService _backup;
    public string SourceType => "AutoBackup";

    public AutoBackupSnapshotSource(BackupService backup)
    {
        _backup = backup;
    }

    public List<SnapshotInfo> GetSnapshots(string filePath)
    {
        var result = new List<SnapshotInfo>();
        try
        {
            var backupPaths = _backup.GetBackups(filePath);
            foreach (var bp in backupPaths)
            {
                var fi = new FileInfo(bp);
                var timestampStr = Path.GetFileNameWithoutExtension(bp).Split('_')[0];
                DateTime.TryParseExact(timestampStr, "yyyyMMdd_HHmmss", null,
                    System.Globalization.DateTimeStyles.None, out var ts);
                if (ts == default)
                    ts = fi.LastWriteTime;

                result.Add(new SnapshotInfo
                {
                    FilePath = filePath,
                    SnapshotPath = bp,
                    Timestamp = ts,
                    SourceType = SourceType,
                    Label = $"自动备份 {ts:yyyy-MM-dd HH:mm:ss}",
                    Size = fi.Length
                });
            }
        }
        catch { }
        return result;
    }

    public string ReadSnapshotContent(SnapshotInfo snapshot)
        => File.Exists(snapshot.SnapshotPath) ? File.ReadAllText(snapshot.SnapshotPath) : string.Empty;
}

/// <summary>Checkpoint 快照来源（通过 BackupService）</summary>
public class CheckpointSnapshotSource : ISnapshotSource
{
    private readonly BackupService _backup;
    public string SourceType => "Checkpoint";

    public CheckpointSnapshotSource(BackupService backup)
    {
        _backup = backup;
    }

    public List<SnapshotInfo> GetSnapshots(string filePath)
    {
        var result = new List<SnapshotInfo>();
        try
        {
            var pending = _backup.GetPendingCheckpoints();
            foreach (var manifest in pending)
            {
                foreach (var entry in manifest.Files)
                {
                    // 匹配文件路径
                    if (!string.Equals(entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!File.Exists(entry.BackupPath)) continue;

                    var fi = new FileInfo(entry.BackupPath);
                    DateTime.TryParseExact(manifest.Timestamp, "yyyyMMdd_HHmmss_fff", null,
                        System.Globalization.DateTimeStyles.None, out var ts);
                    if (ts == default) ts = fi.LastWriteTime;

                    result.Add(new SnapshotInfo
                    {
                        FilePath = filePath,
                        SnapshotPath = entry.BackupPath,
                        Timestamp = ts,
                        SourceType = SourceType,
                        Label = $"Checkpoint {manifest.Timestamp}",
                        Size = fi.Length
                    });
                }
            }
        }
        catch { }
        return result;
    }

    public string ReadSnapshotContent(SnapshotInfo snapshot)
        => File.Exists(snapshot.SnapshotPath) ? File.ReadAllText(snapshot.SnapshotPath) : string.Empty;
}

/// <summary>用户手动导入的快照来源</summary>
public class UserBackupSnapshotSource : ISnapshotSource
{
    private readonly string _userBackupDir;
    public string SourceType => "UserBackup";

    public UserBackupSnapshotSource(string userBackupDir)
    {
        _userBackupDir = userBackupDir;
    }

    public List<SnapshotInfo> GetSnapshots(string filePath)
    {
        var result = new List<SnapshotInfo>();
        try
        {
            if (!Directory.Exists(_userBackupDir)) return result;
            var safeName = filePath.Replace(Path.DirectorySeparatorChar, '_')
                                   .Replace(Path.AltDirectorySeparatorChar, '_');
            foreach (var f in Directory.GetFiles(_userBackupDir, $"*_{safeName}"))
            {
                var fi = new FileInfo(f);
                result.Add(new SnapshotInfo
                {
                    FilePath = filePath,
                    SnapshotPath = f,
                    Timestamp = fi.LastWriteTime,
                    SourceType = SourceType,
                    Label = $"用户备份 {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}",
                    Size = fi.Length
                });
            }
            result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        }
        catch { }
        return result;
    }

    public string ReadSnapshotContent(SnapshotInfo snapshot)
        => File.Exists(snapshot.SnapshotPath) ? File.ReadAllText(snapshot.SnapshotPath) : string.Empty;
}

// ==================== 核心比较服务 ====================

/// <summary>
/// 文件快照比较服务 —— 抽象可复用的扫描/对比/恢复能力。
/// 支持多种快照来源（自动备份、Checkpoint、用户备份、未来可扩展 Git 历史等）。
/// 内部使用 DiffService 进行行级差异计算。
/// </summary>
public class FileCompareService
{
    private readonly BackupService _backup;
    private readonly List<ISnapshotSource> _sources = new();

    public FileCompareService(BackupService backup)
    {
        _backup = backup;
        // 默认注册内置来源
        _sources.Add(new AutoBackupSnapshotSource(backup));
        _sources.Add(new CheckpointSnapshotSource(backup));
    }

    /// <summary>注册额外的快照来源（供扩展使用）</summary>
    public void RegisterSource(ISnapshotSource source)
    {
        _sources.Add(source);
    }

    /// <summary>获取所有已注册的快照来源</summary>
    public IReadOnlyList<ISnapshotSource> Sources => _sources;

    // ---- 快照查询 ----

    /// <summary>获取文件的所有快照（来自所有来源）</summary>
    public List<SnapshotInfo> GetAllSnapshots(string filePath)
    {
        var all = new List<SnapshotInfo>();
        foreach (var source in _sources)
        {
            try { all.AddRange(source.GetSnapshots(filePath)); }
            catch { }
        }
        // 按时间降序排列（最新在前）
        all.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return all;
    }

    /// <summary>获取指定来源类型的快照</summary>
    public List<SnapshotInfo> GetSnapshotsFromSource(string filePath, string sourceType)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == sourceType);
        return source?.GetSnapshots(filePath) ?? new List<SnapshotInfo>();
    }

    /// <summary>获取文件的自动备份快照</summary>
    public List<SnapshotInfo> GetBackupSnapshots(string filePath)
        => GetSnapshotsFromSource(filePath, "AutoBackup");

    /// <summary>获取文件的 Checkpoint 快照</summary>
    public List<SnapshotInfo> GetCheckpointSnapshots(string filePath)
        => GetSnapshotsFromSource(filePath, "Checkpoint");

    // ---- 对比 ----

    /// <summary>将当前文件与指定快照进行对比</summary>
    public CompareResult CompareWithSnapshot(string filePath, SnapshotInfo snapshot)
    {
        var currentContent = File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        var source = _sources.FirstOrDefault(s => s.SourceType == snapshot.SourceType);
        var snapshotContent = source?.ReadSnapshotContent(snapshot) ?? string.Empty;

        var diff = DiffService.ComputeDiff(snapshotContent, currentContent);

        return new CompareResult
        {
            SourceSnapshot = snapshot,
            TargetSnapshot = new SnapshotInfo
            {
                FilePath = filePath,
                SnapshotPath = filePath,
                Timestamp = DateTime.Now,
                SourceType = "Current",
                Label = "当前文件",
                Size = File.Exists(filePath) ? new FileInfo(filePath).Length : 0
            },
            Diff = diff
        };
    }

    /// <summary>对比两个快照</summary>
    public CompareResult CompareSnapshots(SnapshotInfo snapshot1, SnapshotInfo snapshot2)
    {
        var source1 = _sources.FirstOrDefault(s => s.SourceType == snapshot1.SourceType);
        var source2 = _sources.FirstOrDefault(s => s.SourceType == snapshot2.SourceType);

        var content1 = source1?.ReadSnapshotContent(snapshot1) ?? string.Empty;
        var content2 = source2?.ReadSnapshotContent(snapshot2) ?? string.Empty;

        var diff = DiffService.ComputeDiff(content1, content2);

        return new CompareResult
        {
            SourceSnapshot = snapshot1,
            TargetSnapshot = snapshot2,
            Diff = diff
        };
    }

    /// <summary>对比两段任意文本内容</summary>
    public static DiffResult CompareContent(string oldContent, string newContent)
        => DiffService.ComputeDiff(oldContent, newContent);

    // ---- 扫描 ----

    /// <summary>扫描文件的所有快照，返回概览（含与最新快照的差异摘要）</summary>
    public ScanResult ScanAndCompare(string filePath)
    {
        var snapshots = GetAllSnapshots(filePath);
        CompareResult? latestDiff = null;

        if (snapshots.Count > 0 && File.Exists(filePath))
        {
            var latest = snapshots[0]; // 最新快照
            latestDiff = CompareWithSnapshot(filePath, latest);
        }

        return new ScanResult
        {
            FilePath = filePath,
            FileExists = File.Exists(filePath),
            Snapshots = snapshots,
            LatestDiff = latestDiff
        };
    }

    /// <summary>生成 Unified Diff 格式的对比文本</summary>
    public static string ToUnifiedDiff(CompareResult result)
    {
        var srcLabel = result.SourceSnapshot?.Label ?? "原始版本";
        var tgtLabel = result.TargetSnapshot?.Label ?? "当前版本";
        return DiffService.ToUnifiedDiff(result.Diff, $"a/{srcLabel}", $"b/{tgtLabel}");
    }
}
