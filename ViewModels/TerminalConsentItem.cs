using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>
/// 终端/沙箱命令确认项（推理对话框内联三段式UI）
/// 左侧：不再询问/每次询问  中间：命令摘要  右侧：沙箱运行/终端
/// </summary>
public class TerminalConsentItem : INotifyPropertyChanged
{
    private string _command = string.Empty;
    private string _summary = string.Empty;
    private DateTime _timestamp = DateTime.Now;
    private bool _isPending = true;
    private bool _isRejected;

    /// <summary>完整命令文本</summary>
    public string Command { get => _command; set { _command = value; OnPropertyChanged(); } }

    /// <summary>命令摘要（中间显示）</summary>
    public string Summary { get => _summary; set { _summary = value; OnPropertyChanged(); } }

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); } }

    /// <summary>是否等待用户确认</summary>
    public bool IsPending { get => _isPending; set { _isPending = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotPending)); OnPropertyChanged(nameof(StatusDisplay)); } }
    public bool IsNotPending => !_isPending;

    /// <summary>是否已被用户拒绝</summary>
    public bool IsRejected { get => _isRejected; set { _isRejected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } }

    public string StatusDisplay => IsRejected ? "已拒绝" : (IsPending ? "等待确认..." : "已执行");

    // ===== 左侧：审批偏好 =====

    private string _rememberPreference = "remember_per_project";
    /// <summary>"remember_per_project" = 不再询问, "always_ask" = 每次询问</summary>
    public string RememberPreference
    {
        get => _rememberPreference;
        set { _rememberPreference = value; OnPropertyChanged(); OnPropertyChanged(nameof(RememberDisplay)); }
    }
    public string RememberDisplay => _rememberPreference == "remember_per_project" ? "不再询问" : "每次询问";

    // ===== 右侧：执行模式 =====

    private string _executionMode = "sandbox";
    /// <summary>"sandbox" = 沙箱运行, "terminal" = 终端</summary>
    public string ExecutionMode
    {
        get => _executionMode;
        set { _executionMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExecutionDisplay)); }
    }
    public string ExecutionDisplay => _executionMode == "sandbox" ? "沙箱运行" : "终端";

    // ===== 确认回调 =====

    /// <summary>
    /// 确认回调 TCS
    /// 返回: (accepted, executionMode, rememberPreference)
    /// accepted: true=执行, false=拒绝
    /// executionMode: "sandbox" / "terminal"
    /// rememberPreference: "remember_per_project" / "always_ask"
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TaskCompletionSource<(bool accepted, string executionMode, string rememberPreference)>? Confirmation { get; set; }

    /// <summary>切换审批偏好</summary>
    public void ToggleRememberPreference()
    {
        RememberPreference = _rememberPreference == "remember_per_project" ? "always_ask" : "remember_per_project";
    }

    /// <summary>切换执行模式</summary>
    public void ToggleExecutionMode()
    {
        ExecutionMode = _executionMode == "sandbox" ? "terminal" : "sandbox";
    }

    /// <summary>接受执行</summary>
    public void Accept()
    {
        IsPending = false;
        Confirmation?.TrySetResult((true, _executionMode, _rememberPreference));
    }

    /// <summary>拒绝执行</summary>
    public void Reject()
    {
        IsPending = false;
        IsRejected = true;
        Confirmation?.TrySetResult((false, _executionMode, _rememberPreference));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
