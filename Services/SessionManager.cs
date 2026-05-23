using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.Services;

/// <summary>
/// 会话运行状态
/// </summary>
public enum SessionStatus
{
    Idle,       // 空闲（无 AI 推理在跑）
    Running,    // AI 正在推理中
    Waiting,    // 等待用户输入或锁释放
    Error       // 推理遇到错误
}

/// <summary>
/// 会话调度管理器 —— 管理多个对话会话，防止并发文件修改冲突。
/// 每个会话拥有独立的对话历史和文件锁管理能力。
/// </summary>
public class SessionManager : IDisposable
{
    private readonly Dictionary<string, ConversationSession> _sessions = new();
    private readonly FileLockService _fileLock;
    private readonly object _lock = new();
    private int _sessionCounter;

    /// <summary>当前活跃会话</summary>
    public ConversationSession? ActiveSession { get; private set; }

    /// <summary>所有会话</summary>
    public IReadOnlyList<ConversationSession> Sessions
    {
        get { lock (_lock) return _sessions.Values.ToList(); }
    }

    /// <summary>文件锁服务（供外部查询锁状态）</summary>
    public FileLockService FileLock => _fileLock;

    /// <summary>会话创建事件</summary>
    public event Action<ConversationSession>? OnSessionCreated;
    /// <summary>会话切换事件</summary>
    public event Action<ConversationSession?, ConversationSession?>? OnSessionSwitched;
    /// <summary>会话关闭事件</summary>
    public event Action<ConversationSession>? OnSessionClosed;
    /// <summary>文件冲突事件（某会话尝试修改已被其他会话锁定的文件）</summary>
    public event Action<string, string, string>? OnFileConflict; // (filePath, lockedBy, attemptedBy)
    /// <summary>会话状态变更事件</summary>
    public event Action<ConversationSession, SessionStatus, SessionStatus>? OnSessionStatusChanged; // (session, oldStatus, newStatus)

    public SessionManager()
    {
        _fileLock = new FileLockService();
        LogService.Instance.Debug("会话管理器已初始化", "Session");
    }

    /// <summary>
    /// 创建新会话。
    /// </summary>
    public ConversationSession CreateSession(string name = "")
    {
        lock (_lock)
        {
            var id = Interlocked.Increment(ref _sessionCounter);
            var session = new ConversationSession
            {
                Id = $"session-{id}",
                Name = string.IsNullOrEmpty(name) ? $"对话 {id}" : name,
                CreatedAt = DateTime.Now,
                FileLock = _fileLock
            };

            _sessions[session.Id] = session;

            // 如果这是第一个会话，自动激活
            if (_sessions.Count == 1)
                SwitchTo(session.Id);

            OnSessionCreated?.Invoke(session);
            LogService.Instance.Info($"会话已创建: {session.Name} ({session.Id})", "Session");
            return session;
        }
    }

    /// <summary>
    /// 切换到指定会话。切换前释放当前会话的文件锁。
    /// </summary>
    public ConversationSession? SwitchTo(string sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return null;

            var previous = ActiveSession;

            // 取消旧会话活跃标记
            if (previous != null)
                previous.IsActive = false;

            ActiveSession = session;
            session.IsActive = true;
            session.LastActiveAt = DateTime.Now;

            OnSessionSwitched?.Invoke(previous, session);
            LogService.Instance.Debug($"会话切换: {(previous?.Name ?? "无")} → {session.Name} ({session.Id})", "Session");
            return session;
        }
    }

    /// <summary>关闭指定会话。如果关闭的是活跃会话，自动切换到下一个。</summary>
    public bool CloseSession(string sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return false;

            // 释放该会话所有文件锁
            _fileLock.ReleaseAllSessionLocks(sessionId);

            _sessions.Remove(sessionId);

            // 如果关闭的是活跃会话，切换到其他会话
            if (ActiveSession?.Id == sessionId)
            {
                session.IsActive = false;
                var next = _sessions.Values.FirstOrDefault();
                ActiveSession = next;
                if (next != null)
                {
                    next.IsActive = true;
                    next.LastActiveAt = DateTime.Now;
                }
                OnSessionSwitched?.Invoke(session, next);
            }

            OnSessionClosed?.Invoke(session);
            LogService.Instance.Info($"会话已关闭: {session.Name} ({session.Id})", "Session");
            return true;
        }
    }

    /// <summary>查找指定会话（线程安全）</summary>
    public ConversationSession? FindSession(string sessionId)
    {
        lock (_lock)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }
    }

    /// <summary>
    /// 设置指定会话的运行状态。
    /// 自动处理状态转换：新会话不会覆盖 Running 状态的会话。
    /// </summary>
    public void SetSessionStatus(string sessionId, SessionStatus newStatus)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            var oldStatus = session.Status;
            if (oldStatus == newStatus) return;

            session.Status = newStatus;
            OnSessionStatusChanged?.Invoke(session, oldStatus, newStatus);
            LogService.Instance.Debug($"会话状态变更: {session.Name} ({session.Id}) {oldStatus} → {newStatus}", "Session");
        }
    }

    /// <summary>
    /// 尝试获取文件锁（在写操作前调用）。
    /// 如果文件被其他会话锁定，触发冲突通知并返回等待状态。
    /// </summary>
    public FileLockService.LockResult TryAcquireFileLock(string filePath, string sessionId, out string message)
    {
        var result = _fileLock.TryAcquireLock(filePath, sessionId);
        message = result.Message;

        if (result.Result == FileLockService.LockResult.Conflict ||
            result.Result == FileLockService.LockResult.Waiting)
        {
            var owner = _fileLock.GetLockOwner(filePath);
            if (owner != null && owner != sessionId)
            {
                OnFileConflict?.Invoke(filePath, owner, sessionId);
                LogService.Instance.Warn($"文件锁冲突: {filePath} (持有者={owner}, 请求者={sessionId})", "Session");
            }
        }

        return result.Result;
    }

    /// <summary>
    /// 释放文件锁（在写操作完成后调用）。
    /// </summary>
    public void ReleaseFileLock(string filePath, string sessionId)
    {
        _fileLock.ReleaseLock(filePath, sessionId);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var session in _sessions.Values)
                _fileLock.ReleaseAllSessionLocks(session.Id);
            _sessions.Clear();
            ActiveSession = null;
        }
        _fileLock.GetType().GetMethod("Dispose")?.Invoke(_fileLock, null);
    }
}

/// <summary>
/// 单个对话会话 —— 拥有独立的对话历史和上下文。
/// </summary>
public class ConversationSession : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private SessionStatus _status = SessionStatus.Idle;
    private bool _isActive;

    /// <summary>会话唯一ID</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>会话名称</summary>
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>上次活跃时间</summary>
    public DateTime LastActiveAt { get; set; }
    /// <summary>会话的对话历史（区分不同会话）</summary>
    public List<object> History { get; } = new();
    /// <summary>关联的文件锁服务</summary>
    public FileLockService? FileLock { get; set; }
    /// <summary>会话级别的消息计数</summary>
    public int MessageCount => History.Count(h => h is Dictionary<string, object?> d
        && d.TryGetValue("role", out var r) && r is string rs && (rs == AiConstants.RoleUser || rs == AiConstants.RoleAssistant));

    /// <summary>会话运行状态</summary>
    public SessionStatus Status { get => _status; set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); OnPropertyChanged(nameof(StatusIcon)); OnPropertyChanged(nameof(StatusColor)); } }
    /// <summary>是否为当前活跃会话</summary>
    public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(TabBackground)); OnPropertyChanged(nameof(TabBorder)); } }
    /// <summary>状态图标 + 中文文本</summary>
    public string StatusDisplay => _status switch
    {
        SessionStatus.Idle => "○ 空闲",
        SessionStatus.Running => "◉ 推理中",
        SessionStatus.Waiting => "⏳ 等待中",
        SessionStatus.Error => "⚠ 错误",
        _ => "○ 空闲"
    };
    /// <summary>状态图标（纯图标）</summary>
    public string StatusIcon => _status switch
    {
        SessionStatus.Idle => "○",
        SessionStatus.Running => "◉",
        SessionStatus.Waiting => "⏳",
        SessionStatus.Error => "⚠",
        _ => "○"
    };
    /// <summary>状态颜色</summary>
    public string StatusColor => _status switch
    {
        SessionStatus.Idle => "#888",
        SessionStatus.Running => "#60c0ff",
        SessionStatus.Waiting => "#f0c040",
        SessionStatus.Error => "#f06060",
        _ => "#888"
    };
    /// <summary>Tab 背景色</summary>
    public string TabBackground => _isActive ? "#1e3a5f" : "#3a3a3a";
    /// <summary>Tab 边框刷</summary>
    public string TabBorder => _isActive ? "#569cd6" : "#4a4a4a";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => Name;
}
