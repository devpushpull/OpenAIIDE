using System.Collections.Concurrent;
using System.IO;

namespace AIIDEWPF.Services;

public enum LogLevel { Debug, Info, Warn, Error }

public class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string LevelText => Level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "???"
    };
    public override string ToString() => $"[{Time:HH:mm:ss.fff}] [{LevelText}] [{Source}] {Message}";
}

public class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    private readonly string _logDir;
    private readonly string _logPath;
    private readonly ConcurrentQueue<LogEntry> _recentEntries = new();
    private readonly object _fileLock = new();
    private const int MaxRecentEntries = 500;

    public event Action<LogEntry>? OnLog;

    private LogService()
    {
        _logDir = AppEnvironment.LogDir;
        _logPath = Path.Combine(_logDir, $"aiide-{DateTime.Now:yyyyMMdd_HHmmss}.log");
        CleanOldLogs();
    }

    /// <summary>删除一周前的旧日志文件</summary>
    private void CleanOldLogs()
    {
        try
        {
            if (!Directory.Exists(_logDir)) return;
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var f in Directory.GetFiles(_logDir, "aiide-*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
            }
        }
        catch { }
    }

    public void Debug(string message, string source = "App")
        => Write(LogLevel.Debug, message, source);

    public void Info(string message, string source = "App")
        => Write(LogLevel.Info, message, source);

    public void Warn(string message, string source = "App")
        => Write(LogLevel.Warn, message, source);

    public void Error(string message, string source = "App")
        => Write(LogLevel.Error, message, source);

    public void Error(Exception ex, string source = "App")
        => Write(LogLevel.Error, $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", source);

    public IEnumerable<LogEntry> GetRecentEntries() => _recentEntries.Reverse();

    /// <summary>获取日志目录路径</summary>
    public string LogDir => _logDir;

    /// <summary>获取当前日志文件路径</summary>
    public string LogPath => _logPath;

    /// <summary>读取所有日志文件内容，返回合并后的文本</summary>
    public string ReadAllLogs(int maxLines = 2000)
    {
        try
        {
            if (!Directory.Exists(_logDir)) return string.Empty;
            var files = Directory.GetFiles(_logDir, "aiide-*.log")
                .OrderByDescending(f => f)
                .Take(3); // 最多读最近3个日志文件
            var lines = new List<string>();
            foreach (var file in files)
            {
                if (lines.Count >= maxLines) break;
                try
                {
                    var fileLines = File.ReadAllLines(file);
                    var toTake = Math.Min(fileLines.Length, maxLines - lines.Count);
                    lines.AddRange(fileLines.TakeLast(toTake));
                }
                catch { }
            }
            return string.Join(Environment.NewLine, lines);
        }
        catch { return string.Empty; }
    }

    /// <summary>统计日志中的错误和警告数量</summary>
    public (int errors, int warnings, int totalEntries) GetLogStats()
    {
        int errors = 0, warnings = 0, total = 0;
        try
        {
            if (!Directory.Exists(_logDir)) return (0, 0, 0);
            var files = Directory.GetFiles(_logDir, "aiide-*.log")
                .OrderByDescending(f => f)
                .Take(3);
            foreach (var file in files)
            {
                try
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        total++;
                        if (line.Contains("[ERROR]")) errors++;
                        else if (line.Contains("[WARN]")) warnings++;
                    }
                }
                catch { }
            }
        }
        catch { }
        return (errors, warnings, total);
    }

    private void Write(LogLevel level, string message, string source)
    {
        // 脱敏：防止 API Key 泄露到日志文件
        message = SecureConfigHelper.MaskApiKey(message);

        var entry = new LogEntry { Level = level, Message = message, Source = source };
        _recentEntries.Enqueue(entry);
        while (_recentEntries.Count > MaxRecentEntries)
            _recentEntries.TryDequeue(out _);

        try
        {
            Directory.CreateDirectory(_logDir);
            lock (_fileLock)
            {
                File.AppendAllText(_logPath, entry.ToString() + Environment.NewLine);
            }
        }
        catch { }

        OnLog?.Invoke(entry);
    }
}
