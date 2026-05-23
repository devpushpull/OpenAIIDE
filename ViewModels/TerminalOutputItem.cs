using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>终端命令执行输出（聊天区内联展示），支持待确认状态</summary>
public class TerminalOutputItem : INotifyPropertyChanged
{
    private string _command = string.Empty;
    private string _output = string.Empty;
    private int _exitCode;
    private DateTime _timestamp = DateTime.Now;
    private bool _isExpanded = true;
    private bool _isPending;
    private bool _isRejected;

    public string Command { get => _command; set { _command = value; OnPropertyChanged(); OnPropertyChanged(nameof(CommandDisplay)); } }
    public string Output { get => _output; set { _output = value; OnPropertyChanged(); } }
    public int ExitCode { get => _exitCode; set { _exitCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSuccess)); OnPropertyChanged(nameof(ExitDisplay)); } }
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); } }
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    public string CommandDisplay => Command.Length > 60 ? Command[..60] + "..." : Command;
    public bool IsSuccess => ExitCode == 0;
    public string ExitDisplay => IsSuccess ? "✅ 成功" : $"❌ 退出码 {ExitCode}";

    /// <summary>是否等待用户确认（危险命令）</summary>
    public bool IsPending { get => _isPending; set { _isPending = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotPending)); OnPropertyChanged(nameof(StatusDisplay)); } }
    public bool IsNotPending => !_isPending;
    /// <summary>是否已被用户拒绝</summary>
    public bool IsRejected { get => _isRejected; set { _isRejected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } }
    public string StatusDisplay => IsRejected ? "🚫 已拒绝" : (IsPending ? "⏳ 等待确认..." : (IsSuccess ? "✅ 成功" : $"❌ 退出码 {ExitCode}"));

    /// <summary>确认回调 TCS：Accept 时设置 (true, rememberChoice, alwaysAllow)，Reject 时设置 (false, rememberChoice, alwaysAllow)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TaskCompletionSource<(bool accepted, bool rememberChoice, bool alwaysAllow)>? Confirmation { get; set; }

    /// <summary>接受执行</summary>
    public void Accept(bool rememberChoice, bool alwaysAllow)
    {
        IsPending = false;
        Confirmation?.TrySetResult((true, rememberChoice, alwaysAllow));
    }

    /// <summary>拒绝执行</summary>
    public void Reject(bool rememberChoice, bool alwaysAllow)
    {
        IsPending = false;
        IsRejected = true;
        Confirmation?.TrySetResult((false, rememberChoice, alwaysAllow));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
