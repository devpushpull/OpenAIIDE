using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>允许修改项目外文件的确认项（推理对话框内联展示），支持待确认状态</summary>
public class ExternalEditConsentItem : INotifyPropertyChanged
{
    private string _title = "⚠️ 权限请求";
    private string _detail = string.Empty;
    private DateTime _timestamp = DateTime.Now;
    private bool _isPending = true;
    private bool _isRejected;

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
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
    public TaskCompletionSource<bool>? Confirmation { get; set; }

    /// <summary>接受</summary>
    public void Accept()
    {
        IsPending = false;
        Confirmation?.TrySetResult(true);
    }

    /// <summary>拒绝</summary>
    public void Reject()
    {
        IsPending = false;
        IsRejected = true;
        Confirmation?.TrySetResult(false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
