using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>待办列表项</summary>
public class TodoItem : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isCompleted;
    private string _status = "pending"; // pending, in_progress, completed, cancelled
    private string _id = string.Empty;
    private string _category = string.Empty; // plan/dev/test/fix/review/refactor/docs

    public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public string Content { get => _content; set { _content = value; OnPropertyChanged(); } }
    public bool IsCompleted { get => _isCompleted; set { _isCompleted = value; _status = value ? "completed" : "pending"; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(DisplayStatusText)); } }
    public string Status { get => _status; set { _status = value; _isCompleted = value == "completed"; OnPropertyChanged(); OnPropertyChanged(nameof(IsCompleted)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(DisplayStatusText)); OnPropertyChanged(nameof(StatusColor)); } }
    public string Category { get => _category; set { _category = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCategory)); OnPropertyChanged(nameof(CategoryDisplay)); } }
    public bool HasCategory => !string.IsNullOrEmpty(_category);
    public string CategoryDisplay => _category switch
    {
        "plan" => "规划",
        "dev" => "开发",
        "test" => "测试",
        "fix" => "修复",
        "review" => "审查",
        "refactor" => "重构",
        "docs" => "文档",
        _ => _category
    };
    public string StatusText => Status switch
    {
        "pending" => "待办",
        "in_progress" => "进行中",
        "completed" => "完成",
        "cancelled" => "取消",
        _ => "待办"
    };
    public string DisplayStatusText => $"{StatusIcon} {StatusText}";
    public string StatusIcon => Status switch
    {
        "pending" => "\u23F3",
        "in_progress" => "\uD83D\uDD04",
        "completed" => "\u2705",
        "cancelled" => "\u274C",
        _ => "\u23F3"
    };
    public string StatusColor => Status switch
    {
        "pending" => "#888888",
        "in_progress" => "#60c0ff",
        "completed" => "#28a745",
        "cancelled" => "#dc3545",
        _ => "#888888"
    };

    public string CategoryColor => _category switch
    {
        "plan" => "#60c0ff",
        "dev" => "#28a745",
        "test" => "#fd7e14",
        "fix" => "#dc3545",
        "review" => "#a0a0d0",
        "refactor" => "#d0c0a0",
        "docs" => "#a0d0c0",
        _ => "#888"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>循环切换状态：pending → in_progress → completed → cancelled → pending</summary>
    public void CycleStatus()
    {
        Status = Status switch
        {
            "pending" => "in_progress",
            "in_progress" => "completed",
            "completed" => "cancelled",
            "cancelled" => "pending",
            _ => "pending"
        };
    }
}
