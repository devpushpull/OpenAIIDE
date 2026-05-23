using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>待确认的文件变更</summary>
public class PendingFileChange : INotifyPropertyChanged
{
    private string _toolName = string.Empty;
    private string _filePath = string.Empty;
    private string _changeType = string.Empty; // search_replace, create_file, delete_file
    private int _countdown = 3;
    private bool _isPending = true;
    private string _originalContent = string.Empty;
    private string _newContent = string.Empty;
    private bool _isDiffExpanded;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ToolName { get => _toolName; set { _toolName = value; OnPropertyChanged(); } }
    public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileName)); } }
    public string ChangeType { get => _changeType; set { _changeType = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChangeLabel)); } }
    public string FileName => System.IO.Path.GetFileName(_filePath);
    public string ChangeLabel => ChangeType switch
    {
        "search_replace" => "修改",
        "create_file" => "新建",
        "delete_file" => "删除",
        _ => "变更"
    };
    public int Countdown { get => _countdown; set { _countdown = value; OnPropertyChanged(); OnPropertyChanged(nameof(CountdownText)); } }
    public string CountdownText => Countdown > 0 ? $"{Countdown}s 后自动接受" : "";
    public bool IsPending { get => _isPending; set { _isPending = value; OnPropertyChanged(); } }

    /// <summary>原始文件内容（用于 Diff 展示）</summary>
    public string OriginalContent { get => _originalContent; set { _originalContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiffSummary)); OnPropertyChanged(nameof(HasDiffContent)); } }
    /// <summary>变更后内容（用于 Diff 展示）</summary>
    public string NewContent { get => _newContent; set { _newContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiffSummary)); OnPropertyChanged(nameof(HasDiffContent)); } }
    /// <summary>Diff 行列表（UI 绑定）</summary>
    public ObservableCollection<DiffLineDisplay> DiffLines { get; } = new();
    /// <summary>是否展开 Diff 预览</summary>
    public bool IsDiffExpanded { get => _isDiffExpanded; set { _isDiffExpanded = value; OnPropertyChanged(); } }
    /// <summary>是否有 Diff 内容</summary>
    public bool HasDiffContent => !string.IsNullOrEmpty(_originalContent) || !string.IsNullOrEmpty(_newContent);
    /// <summary>Diff 统计摘要</summary>
    public string DiffSummary
    {
        get
        {
            if (string.IsNullOrEmpty(_originalContent) && string.IsNullOrEmpty(_newContent)) return "";
            var diff = Services.DiffService.ComputeDiff(_originalContent, _newContent);
            if (!diff.HasChanges) return "(无变化)";
            return $"(+{diff.AddedLines}/-{diff.RemovedLines})";
        }
    }

    /// <summary>关联的 TaskCompletionSource，用于通知 AIService 确认结果</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TaskCompletionSource<bool>? Confirmation { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Diff 行展示项（用于 UI 绑定）</summary>
public class DiffLineDisplay
{
    public string Text { get; set; } = "";
    public string BackgroundColor { get; set; } = "Transparent"; // "#3a1a1a" 删除 / "#1a3a1a" 新增
    public string Prefix { get; set; } = " ";
}
