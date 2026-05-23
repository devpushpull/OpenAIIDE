using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>等待发送队列项（当 AI 正在处理时，用户可继续输入消息排队）</summary>
public class PendingMessageItem : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private DateTime _timestamp = DateTime.Now;
    private bool _isEditing;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Content { get => _content; set { _content = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewText)); } }
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeText)); } }
    public string PreviewText => Content.Length > 40 ? Content[..40] + "..." : Content;
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// <summary>是否处于内联编辑模式</summary>
    public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsViewMode)); } }
    public bool IsViewMode => !_isEditing;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
