using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>联网搜索同意项（聊天区内联展示），支持待确认状态</summary>
public class WebSearchConsentItem : INotifyPropertyChanged
{
    private string _query = string.Empty;
    private string _toolName = string.Empty;
    private string _label = string.Empty;
    private string _detail = string.Empty;
    private DateTime _timestamp = DateTime.Now;
    private bool _isPending = true;
    private bool _isRejected;

    public string Query { get => _query; set { _query = value; OnPropertyChanged(); } }
    public string ToolName { get => _toolName; set { _toolName = value; OnPropertyChanged(); } }
    public string Label { get => _label; set { _label = value; OnPropertyChanged(); } }
    public string Detail { get => _detail; set { _detail = value; OnPropertyChanged(); } }
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); } }

    /// <summary>是否等待用户确认</summary>
    public bool IsPending { get => _isPending; set { _isPending = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotPending)); OnPropertyChanged(nameof(StatusDisplay)); } }
    public bool IsNotPending => !_isPending;
    /// <summary>是否已被用户拒绝</summary>
    public bool IsRejected { get => _isRejected; set { _isRejected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } }
    public string StatusDisplay => IsRejected ? "🚫 已拒绝" : (IsPending ? "⏳ 等待确认..." : "✅ 已允许");

    /// <summary>确认回调 TCS</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TaskCompletionSource<(bool accepted, bool rememberChoice)>? Confirmation { get; set; }

    /// <summary>接受搜索</summary>
    public void Accept(bool rememberChoice)
    {
        IsPending = false;
        Confirmation?.TrySetResult((true, rememberChoice));
    }

    /// <summary>拒绝搜索</summary>
    public void Reject(bool rememberChoice)
    {
        IsPending = false;
        IsRejected = true;
        Confirmation?.TrySetResult((false, rememberChoice));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
