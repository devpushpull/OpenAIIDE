using System.Collections.Concurrent;
using System.IO;

namespace AIIDEWPF.Services;

/// <summary>
/// 文件并发锁服务 —— 防止多个会话同时修改同一文件导致代码破坏或丢失。
/// 每个写操作（search_replace/create_file/delete_file）执行前需获取锁，
/// 操作完成后释放锁。其他会话对同一文件的写操作将被阻塞或提示。
/// </summary>
public class FileLockService
{
    private readonly ConcurrentDictionary<string, FileLock> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PendingLockRequest>> _waitQueues = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(30);
    private readonly Timer _cleanupTimer;

    public FileLockService()
    {
        // 每 30 秒清理过期锁
        _cleanupTimer = new Timer(_ => CleanupExpiredLocks(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        LogService.Instance.Debug("文件锁服务已初始化", "FileLock");
    }

    /// <summary>锁获取结果</summary>
    public enum LockResult { Acquired, Waiting, Conflict, NotFound }

    /// <summary>
    /// 尝试获取文件写锁。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="sessionId">请求锁的会话ID</param>
    /// <returns>锁结果和消息</returns>
    public (LockResult Result, string Message) TryAcquireLock(string filePath, string sessionId)
    {
        var normalizedPath = NormalizePath(filePath);
        var newLock = new FileLock
        {
            FilePath = normalizedPath,
            SessionId = sessionId,
            AcquiredAt = DateTime.UtcNow
        };

        var existing = _locks.GetOrAdd(normalizedPath, newLock);

        // 如果是同一个会话重入，直接返回成功
        if (existing.SessionId == sessionId)
        {
            existing.AcquiredAt = DateTime.UtcNow; // 刷新过期时间
            return (LockResult.Acquired, $"✓ 文件锁已持有: {Path.GetFileName(normalizedPath)}");
        }

        // 检查现有锁是否过期
        if (DateTime.UtcNow - existing.AcquiredAt > _lockTimeout)
        {
            // 过期锁，强制获取
            _locks.TryUpdate(normalizedPath, newLock, existing);
            NotifyWaiters(normalizedPath, sessionId);
            return (LockResult.Acquired, $"✓ 文件锁已获取（前锁已过期）: {Path.GetFileName(normalizedPath)}");
        }

        // 其他会话持有锁 → 加入等待队列
        var queue = _waitQueues.GetOrAdd(normalizedPath, _ => new ConcurrentQueue<PendingLockRequest>());
        var tcs = new TaskCompletionSource<bool>();
        queue.Enqueue(new PendingLockRequest { SessionId = sessionId, Tcs = tcs });

        LogService.Instance.Debug($"文件锁等待: {Path.GetFileName(normalizedPath)} (持有者={existing.SessionId}, 请求者={sessionId})", "FileLock");

        // 异步等待
        Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(_waitTimeout);
                // 轮询检查锁是否释放
                while (!cts.Token.IsCancellationRequested)
                {
                    if (_locks.TryGetValue(normalizedPath, out var current) &&
                        (current.SessionId == sessionId || DateTime.UtcNow - current.AcquiredAt > _lockTimeout))
                    {
                        _locks.TryUpdate(normalizedPath, newLock, current);
                        tcs.TrySetResult(true);
                        return;
                    }
                    await Task.Delay(500, cts.Token);
                }
                tcs.TrySetResult(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Debug($"文件锁等待异常: {ex.Message}", "FileLock");
                tcs.TrySetResult(false);
            }
        });

        return (LockResult.Waiting,
            $"⏳ 文件 [{Path.GetFileName(normalizedPath)}] 正被会话 [{existing.SessionId}] 修改中，" +
            $"等待释放（最长 {_waitTimeout.TotalSeconds:F0}s）...");
    }

    /// <summary>
    /// 释放文件锁。
    /// </summary>
    public void ReleaseLock(string filePath, string sessionId)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_locks.TryGetValue(normalizedPath, out var existing) && existing.SessionId == sessionId)
        {
            _locks.TryRemove(normalizedPath, out _);
            NotifyWaiters(normalizedPath, sessionId);
        }
    }

    /// <summary>
    /// 释放指定会话的所有文件锁。
    /// </summary>
    public void ReleaseAllSessionLocks(string sessionId)
    {
        var keys = _locks.Where(kvp => kvp.Value.SessionId == sessionId).Select(kvp => kvp.Key).ToList();
        foreach (var key in keys)
        {
            _locks.TryRemove(key, out _);
            NotifyWaiters(key, sessionId);
        }
        if (keys.Count > 0)
            LogService.Instance.Debug($"释放会话所有锁: {sessionId} ({keys.Count}个)", "FileLock");
    }

    /// <summary>
    /// 获取文件当前被哪个会话锁定（null 表示未锁定）。
    /// </summary>
    public string? GetLockOwner(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        if (_locks.TryGetValue(normalizedPath, out var existing))
        {
            if (DateTime.UtcNow - existing.AcquiredAt > _lockTimeout)
                return null; // 过期锁视为未锁定
            return existing.SessionId;
        }
        return null;
    }

    /// <summary>
    /// 获取当前所有活跃的文件锁信息。
    /// </summary>
    public IReadOnlyList<(string FilePath, string SessionId, DateTime AcquiredAt)> GetActiveLocks()
    {
        return _locks
            .Where(kvp => DateTime.UtcNow - kvp.Value.AcquiredAt <= _lockTimeout)
            .Select(kvp => (kvp.Value.FilePath, kvp.Value.SessionId, kvp.Value.AcquiredAt))
            .ToList();
    }

    /// <summary>
    /// 写操作前自动备份文件。
    /// 备份到 .aiide/file_backups/ 目录，格式：{filename}.{sessionId}.{timestamp}.bak。
    /// 最大保留 20 个备份，超过自动清理最旧的。
    /// 备份失败不抛异常，仅记录日志。
    /// </summary>
    /// <returns>备份文件路径，失败返回 null</returns>
    public string? BackupBeforeWrite(string filePath, string sessionId)
    {
        try
        {
            if (!File.Exists(filePath))
                return null; // 新文件不需要备份

            var fileDir = Path.GetDirectoryName(filePath) ?? "";
            var backupBaseDir = Path.Combine(fileDir, ".aiide", "file_backups");
            Directory.CreateDirectory(backupBaseDir);

            var fileName = Path.GetFileName(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var shortSessionId = sessionId.Length > 8 ? sessionId[..8] : sessionId;
            var backupName = $"{fileName}.{shortSessionId}.{timestamp}.bak";
            var backupPath = Path.Combine(backupBaseDir, backupName);

            File.Copy(filePath, backupPath, overwrite: true);

            // 清理旧备份：超过 20 个时删除最旧的
            CleanOldBackups(backupBaseDir, 20);

            LogService.Instance.Debug($"文件已备份: {fileName} → {backupName}", "FileLock");
            return backupPath;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"文件备份失败（不阻塞写操作）: {filePath}, {ex.Message}", "FileLock");
            return null;
        }
    }

    private static void CleanOldBackups(string backupDir, int maxBackups)
    {
        try
        {
            var backups = Directory.GetFiles(backupDir, "*.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            for (int i = maxBackups; i < backups.Count; i++)
            {
                try { backups[i].Delete(); } catch { }
            }
        }
        catch { }
    }

    // ========== 私有方法 ==========

    private static string NormalizePath(string path)
        => System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

    private void CleanupExpiredLocks()
    {
        var cutoff = DateTime.UtcNow - _lockTimeout;
        var expired = _locks.Where(kvp => kvp.Value.AcquiredAt < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
        {
            if (_locks.TryRemove(key, out var removed))
                NotifyWaiters(key, removed.SessionId);
        }
        if (expired.Count > 0)
            LogService.Instance.Debug($"清理过期文件锁: {expired.Count}个", "FileLock");
    }

    private void NotifyWaiters(string filePath, string releasedBySessionId)
    {
        if (_waitQueues.TryGetValue(filePath, out var queue))
        {
            // 通知队列中第一个等待者
            while (queue.TryDequeue(out var pending))
            {
                if (pending.SessionId == releasedBySessionId) continue; // 跳过自己
                var newLock = new FileLock
                {
                    FilePath = filePath,
                    SessionId = pending.SessionId,
                    AcquiredAt = DateTime.UtcNow
                };
                _locks.TryUpdate(filePath, newLock, _locks.GetValueOrDefault(filePath, newLock));
                pending.Tcs.TrySetResult(true);
                break;
            }
            // 如果队列已空，清理
            if (queue.IsEmpty)
                _waitQueues.TryRemove(filePath, out _);
        }
    }

    private class FileLock
    {
        public string FilePath { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;
        public DateTime AcquiredAt { get; set; }
    }

    private class PendingLockRequest
    {
        public string SessionId { get; init; } = string.Empty;
        public TaskCompletionSource<bool> Tcs { get; init; } = null!;
    }
}
