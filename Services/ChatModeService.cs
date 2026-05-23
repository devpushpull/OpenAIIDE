using System.ComponentModel;
using System.Runtime.CompilerServices;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 聊天模式管理服务 —— 管理智能体/问答/规划等多种交互模式。
/// 从 ModelManager 中独立抽取，支持未来扩展新模式。
/// </summary>
public class ChatModeService : INotifyPropertyChanged
{
    private readonly ModelManager _modelManager;
    private string _activeModeId = "agent";

    /// <summary>所有已注册的模式定义</summary>
    private readonly Dictionary<string, ChatModeDef> _modes = new();

    /// <summary>当前活跃模式ID</summary>
    public string ActiveModeId
    {
        get => _activeModeId;
        set
        {
            if (_activeModeId == value) return;
            if (!_modes.ContainsKey(value)) return;
            _activeModeId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAgentMode));
            OnPropertyChanged(nameof(IsQAMode));
            OnPropertyChanged(nameof(IsPlanningMode));
            OnPropertyChanged(nameof(ActiveMode));
            OnModeChanged?.Invoke(value);
        }
    }

    /// <summary>当前活跃模式</summary>
    public ChatModeDef? ActiveMode => _modes.TryGetValue(_activeModeId, out var mode) ? mode : null;

    /// <summary>是否为智能体模式</summary>
    public bool IsAgentMode => _activeModeId == "agent";
    /// <summary>是否为问答模式</summary>
    public bool IsQAMode => _activeModeId == "qa";
    /// <summary>是否为规划模式</summary>
    public bool IsPlanningMode => _activeModeId == "planning";

    /// <summary>模式变更事件</summary>
    public event Action<string>? OnModeChanged;

    public ChatModeService(ModelManager modelManager)
    {
        _modelManager = modelManager;
        RegisterBuiltInModes();
    }

    /// <summary>注册内置模式</summary>
    private void RegisterBuiltInModes()
    {
        // 智能体模式
        _modes["agent"] = new ChatModeDef
        {
            Id = "agent",
            Name = "智能体",
            Description = "可读写、修改项目文件，执行终端命令，端到端完成任务",
            Icon = "🤖",
            AllowsFileWrite = true,
            AllowsTerminal = true,
            AllowsWebSearch = true,
            RequiresApproval = false,
            SystemPromptSuffix = "你处于【智能体模式】，拥有完整的文件读写、终端执行、联网搜索能力。请主动完成任务，不要频繁询问用户。"
        };

        // 问答模式
        _modes["qa"] = new ChatModeDef
        {
            Id = "qa",
            Name = "问答",
            Description = "仅扫描阅读项目代码回答问题，不修改任何文件",
            Icon = "💬",
            AllowsFileWrite = false,
            AllowsTerminal = false,
            AllowsWebSearch = true,
            RequiresApproval = false,
            SystemPromptSuffix = "你处于【问答模式】，只能阅读/搜索项目代码来回答问题，不能修改、创建、删除任何文件或运行终端命令。"
        };

        // 规划模式（参考Qoder/Lingma的Spec-driven开发）
        _modes["planning"] = new ChatModeDef
        {
            Id = "planning",
            Name = "规划",
            Description = "先制定详细执行计划，获得用户确认后再分步执行",
            Icon = "📋",
            AllowsFileWrite = false, // 规划阶段不写文件
            AllowsTerminal = false,
            AllowsWebSearch = true,
            RequiresApproval = true,
            SystemPromptSuffix = "你处于【规划模式】。你的任务是：\n" +
                "1. 分析用户需求，制定详细的执行计划（使用 todo_write）\n" +
                "2. 列出每个步骤的具体操作和预期产出\n" +
                "3. 等待用户确认计划后再开始执行\n" +
                "4. 不要自行修改任何文件，先做好规划！"
        };
    }

    /// <summary>获取当前模型支持的模式列表</summary>
    public ChatModeDef[] GetAvailableModes()
    {
        var activeModel = _modelManager.ActiveModel;
        if (activeModel == null)
            return _modes.Values.ToArray();

        var available = new List<ChatModeDef>();
        // agent 模式需要模型支持 AgentMode
        if (activeModel.AgentMode) available.Add(_modes["agent"]);
        // qa 模式需要模型支持 QAMode
        if (activeModel.QAMode) available.Add(_modes["qa"]);
        // planning 模式在 agent 模式下可用
        if (activeModel.AgentMode) available.Add(_modes["planning"]);
        return available.ToArray();
    }

    /// <summary>注册自定义模式（扩展用）</summary>
    public void RegisterMode(ChatModeDef mode)
    {
        _modes[mode.Id] = mode;
    }

    /// <summary>获取模式定义</summary>
    public ChatModeDef? GetMode(string modeId) =>
        _modes.TryGetValue(modeId, out var mode) ? mode : null;

    /// <summary>获取所有已注册的模式</summary>
    public IEnumerable<ChatModeDef> GetAllModes() => _modes.Values;

    /// <summary>获取当前有效工具列表（根据模式过滤）</summary>
    public string[] GetDisabledTools()
    {
        return _activeModeId switch
        {
            "qa" => new[] {
                "search_replace", "create_file", "delete_file", "delete_dir",
                "create_dir", "rename_file", "move_file", "copy_file",
                "run_in_terminal", "build_project"
            },
            "planning" => new[] {
                "search_replace", "create_file", "delete_file", "delete_dir",
                "create_dir", "rename_file", "move_file", "copy_file",
                "run_in_terminal", "build_project"
            },
            _ => Array.Empty<string>()
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
