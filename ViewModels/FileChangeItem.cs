using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>待更新文件列表项</summary>
public class FileChangeItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _changeType = "modified"; // created, modified, deleted
    private string _description = string.Empty;
    private string? _checkpointId;

    public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileName)); } }
    public string FileName => System.IO.Path.GetFileName(_filePath);
    public string ChangeType { get => _changeType; set { _changeType = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChangeIcon)); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    /// <summary>关联的 checkpoint ID，可用于恢复到此状态</summary>
    public string? CheckpointId { get => _checkpointId; set { _checkpointId = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCheckpoint)); } }
    public bool HasCheckpoint => !string.IsNullOrEmpty(_checkpointId);
    public string ChangeIcon => ChangeType switch
    {
        "created" => "📄 新建",
        "modified" => "✏️ 修改",
        "deleted" => "🗑️ 删除",
        _ => "📄 变更"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
