using System.IO;
using System.Timers;

namespace AIIDEWPF.Services;

/// <summary>
/// 后台调度服务 — 定时执行内存回收、日志清理、状态保存、看门狗检测。
/// 合并了任务四（自动调度）和任务五（看门狗）功能。
/// </summary>
public class BackgroundScheduler : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly Func<string> _getProjectPath;
    private readonly Action _saveProjectState;
    private DateTime _lastUserInput = DateTime.Now;
    private DateTime _lastGc = DateTime.MinValue;
    private DateTime _lastLogClean = DateTime.MinValue;
    private DateTime _lastAutoSave = DateTime.MinValue;
    private DateTime _lastHeartbeat = DateTime.Now;
    private DateTime _lastLogAnalysis = DateTime.Now; // 启动后等一段时间再首次分析
    private bool _isStreaming;
    private bool _disposed;
    private LogAnalysisService? _logAnalysis;
    private Func<string?>? _getApiKey;
    private Func<string?>? _getBaseUrl;
    private Func<string?>? _getModelName;

    /// <summary>日志分析完成事件</summary>
    public event Action<List<ImprovementSuggestion>>? OnLogAnalysisComplete;
    /// <summary>登录会话即将过期事件</summary>
    public event Action? OnSessionExpiring;

    /// <summary>空闲检测间隔（秒）</summary>
    private const int IdleThresholdSec = 10;
    /// <summary>GC 最小间隔（分钟）</summary>
    private const int GcIntervalMin = 5;
    /// <summary>日志清理间隔（小时）</summary>
    private const int LogCleanIntervalHours = 1;
    /// <summary>自动保存间隔（分钟）</summary>
    private const int AutoSaveIntervalMin = 2;
    /// <summary>看门狗心跳阈值（秒），超过则告警</summary>
    private const int WatchdogThresholdSec = 5;
    /// <summary>日志保留天数</summary>
    private const int LogRetentionDays = 7;
    /// <summary>单文件日志最大大小（MB）</summary>
    private const int LogMaxSizeMb = 10;

    public BackgroundScheduler(Func<string> getProjectPath, Action saveProjectState)
    {
        _getProjectPath = getProjectPath;
        _saveProjectState = saveProjectState;
        _timer = new System.Timers.Timer(30000); // 每 30 秒检查
        _timer.Elapsed += OnTimerTick;
        _timer.AutoReset = true;
        _timer.Start();
        LogService.Instance.Info("后台调度服务已启动 (30s间隔)", "Scheduler");
    }

    /// <summary>通知有用户输入活动</summary>
    public void NotifyUserActivity()
    {
        _lastUserInput = DateTime.Now;
    }

    /// <summary>通知 AI 流式响应状态</summary>
    public void SetStreaming(bool streaming)
    {
        _isStreaming = streaming;
    }

    /// <summary>配置日志分析功能（可选，设置后空闲时自动分析）</summary>
    public void EnableLogAnalysis(LogAnalysisService service, Func<string?> getApiKey, Func<string?> getBaseUrl, Func<string?> getModelName)
    {
        _logAnalysis = service;
        _getApiKey = getApiKey;
        _getBaseUrl = getBaseUrl;
        _getModelName = getModelName;
        _lastLogAnalysis = DateTime.Now; // 延迟首次分析
        LogService.Instance.Info("日志分析功能已启用", "Scheduler");
    }

    /// <summary>看门狗心跳 — 由 UI 线程定期调用</summary>
    public void Heartbeat()
    {
        _lastHeartbeat = DateTime.Now;
    }

    public bool IsIdle =>
        !_isStreaming &&
        (DateTime.Now - _lastUserInput).TotalSeconds >= IdleThresholdSec;

    private void OnTimerTick(object? sender, ElapsedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            // 看门狗：检查 UI 线程心跳
            var sinceHeartbeat = (DateTime.Now - _lastHeartbeat).TotalSeconds;
            if (sinceHeartbeat > WatchdogThresholdSec)
            {
                LogService.Instance.Warn(
                    $"看门狗告警: UI 线程 {sinceHeartbeat:F0}s 无响应 (最后心跳: {_lastHeartbeat:HH:mm:ss})",
                    "Watchdog");
            }

            // 仅在空闲时执行重任务
            if (!IsIdle) return;

            var now = DateTime.Now;

            // 1. 内存回收
            if ((now - _lastGc).TotalMinutes >= GcIntervalMin)
            {
                _lastGc = now;
                PerformGarbageCollection();
            }

            // 2. 日志清理
            if ((now - _lastLogClean).TotalHours >= LogCleanIntervalHours)
            {
                _lastLogClean = now;
                CleanLogs();
            }

            // 3. 项目状态自动保存
            if ((now - _lastAutoSave).TotalMinutes >= AutoSaveIntervalMin)
            {
                _lastAutoSave = now;
                _saveProjectState();
            }

            // 4. 日志分析（空闲时每2小时检查一次）
            if (_logAnalysis != null && (now - _lastLogAnalysis).TotalHours >= 2 && _logAnalysis.ShouldAnalyze())
            {
                _lastLogAnalysis = now;
                _ = RunLogAnalysisAsync();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "Scheduler");
        }
    }

    private static void PerformGarbageCollection()
    {
        try
        {
            var memBefore = GC.GetTotalMemory(false) / 1024 / 1024;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var memAfter = GC.GetTotalMemory(false) / 1024 / 1024;
            if (memBefore - memAfter > 1) // 回收超过 1MB 才记录
                LogService.Instance.Debug($"内存回收: {memBefore}MB → {memAfter}MB (释放 {memBefore - memAfter}MB)", "Scheduler");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "Scheduler");
        }
    }

    private void CleanLogs()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIIDEWPF", "Logs");

            if (!Directory.Exists(logDir)) return;

            var cutoff = DateTime.Now.AddDays(-LogRetentionDays);
            int deleted = 0;

            foreach (var file in Directory.GetFiles(logDir, "*.log"))
            {
                try
                {
                    var fi = new FileInfo(file);
                    // 删除过期日志
                    if (fi.LastWriteTime < cutoff)
                    {
                        fi.Delete();
                        deleted++;
                    }
                    // 截断超大日志文件
                    else if (fi.Length > LogMaxSizeMb * 1024 * 1024)
                    {
                        TruncateLogFile(file, LogMaxSizeMb);
                    }
                }
                catch (Exception ex) { LogService.Instance.Debug($"日志清理异常: {ex.Message}", "Scheduler"); }
            }

            if (deleted > 0)
                LogService.Instance.Debug($"日志清理: 删除 {deleted} 个过期文件", "Scheduler");
        }
        catch (Exception ex) { LogService.Instance.Debug($"CleanLogs异常: {ex.Message}", "Scheduler"); }
    }

    private static void TruncateLogFile(string filePath, int maxMb)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 100) return;
            // 保留后半部分
            var keep = lines.Skip(lines.Length / 2).ToArray();
            File.WriteAllText(filePath, $"[日志已截断 — 原始 {lines.Length} 行]\n");
            File.AppendAllLines(filePath, keep);
        }
        catch (Exception ex) { LogService.Instance.Debug($"截断日志文件异常: {ex.Message}", "Scheduler"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        LogService.Instance.Info("后台调度服务已停止", "Scheduler");
    }

    /// <summary>
    /// 执行日志分析（后台异步），调用大模型生成改进建议。
    /// 分析完成后触发 OnLogAnalysisComplete 事件。
    /// </summary>
    private async Task RunLogAnalysisAsync()
    {
        if (_logAnalysis == null) return;

        try
        {
            LogService.Instance.Debug("后台日志分析开始", "Scheduler");

            var apiKey = _getApiKey?.Invoke();
            var baseUrl = _getBaseUrl?.Invoke();
            var model = _getModelName?.Invoke();
            var projectPath = _getProjectPath();

            var suggestions = await _logAnalysis.AnalyzeAsync(apiKey, baseUrl, model, projectPath);

            if (suggestions.Count > 0)
            {
                LogService.Instance.Info($"后台日志分析完成: {suggestions.Count} 条改进建议", "Scheduler");
                OnLogAnalysisComplete?.Invoke(suggestions);
            }
            else
            {
                LogService.Instance.Debug("后台日志分析未发现改进建议", "Scheduler");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"后台日志分析异常: {ex.Message}", "Scheduler");
        }
    }
}
