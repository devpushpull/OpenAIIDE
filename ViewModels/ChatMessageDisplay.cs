using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

public class ChatMessageDisplay : INotifyPropertyChanged
{
    private string _role = string.Empty;
    private string _content = string.Empty;
    private string _toolStatus = string.Empty;
    private string _toolResult = string.Empty;
    private DateTime _timestamp = DateTime.Now;
    private string _reasoningFullContent = string.Empty;
    private bool _isReasoningCollapsed = true;
    private string _modelLabel = string.Empty;
    private bool _isStreaming;

    public string Role { get => _role; set { _role = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUser)); OnPropertyChanged(nameof(IsAssistant)); OnPropertyChanged(nameof(IsTool)); OnPropertyChanged(nameof(IsSystem)); OnPropertyChanged(nameof(IsReasoning)); } }
    public string Content { get => _content; set { _content = value; OnPropertyChanged(); } }
    public string ToolStatus { get => _toolStatus; set { _toolStatus = value; OnPropertyChanged(); } }
    public string ToolResult { get => _toolResult; set { _toolResult = value; OnPropertyChanged(); } }
    public DateTime Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(); } }
    /// <summary>模型标签（对比模式下显示模型名）</summary>
    public string ModelLabel { get => _modelLabel; set { _modelLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasModelLabel)); } }
    public bool HasModelLabel => !string.IsNullOrEmpty(_modelLabel);
    /// <summary>推理消息是否正在流式输出中</summary>
    public bool IsStreaming { get => _isStreaming; set { _isStreaming = value; OnPropertyChanged(); } }

    /// <summary>推理过程的完整内容（折叠后保留）</summary>
    public string ReasoningFullContent
    {
        get => _reasoningFullContent;
        set { _reasoningFullContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningToggleText)); }
    }

    /// <summary>推理过程是否处于折叠状态</summary>
    public bool IsReasoningCollapsed
    {
        get => _isReasoningCollapsed;
        set { _isReasoningCollapsed = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReasoningToggleText)); }
    }

    /// <summary>折叠/展开按钮文字</summary>
    public string ReasoningToggleText => IsReasoningCollapsed
        ? "💭 推理过程（点击展开）"
        : "💭 推理过程（点击折叠）";

    public bool IsUser => Role == AiConstants.RoleUser;
    public bool IsAssistant => Role == AiConstants.RoleAssistant;
    public bool IsTool => Role == AiConstants.RoleTool;
    public bool IsSystem => Role == AiConstants.RoleSystem;
    public bool IsReasoning => Role == "reasoning";
    public bool IsToolRunning => ToolStatus == "running";
    public bool IsToolDone => ToolStatus == "done";

    /// <summary>切换推理过程的折叠/展开状态</summary>
    public void ToggleReasoning()
    {
        if (!IsReasoning || string.IsNullOrEmpty(ReasoningFullContent)) return;
        IsReasoningCollapsed = !IsReasoningCollapsed;
        Content = IsReasoningCollapsed ? ReasoningToggleText : ReasoningFullContent;
    }

    /// <summary>将推理内容折叠为摘要</summary>
    public void CollapseReasoning()
    {
        if (!IsReasoning || string.IsNullOrEmpty(ReasoningFullContent)) return;
        ReasoningFullContent = Content; // 保存完整内容
        IsReasoningCollapsed = true;
        Content = ReasoningToggleText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
