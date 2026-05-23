using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AIIDEWPF.Models;
using AIIDEWPF.Services;
using AIIDEWPF.Views;
using Microsoft.Win32;

namespace AIIDEWPF.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly ConfigService _config;
    private readonly ModelManager _modelManager;
    private readonly ChatModeService _chatModeService;
    private readonly FileService _fileService;
    private readonly SearchService _searchService;
    private readonly TerminalService _terminalService;
    private readonly AIService _aiService;
    private readonly SandboxService _sandboxSvc;
    private readonly SessionManager _sessionManager;
    private readonly ProjectConfigService _projectConfig;
    private readonly NetworkService _networkService;
    private readonly BackgroundScheduler _scheduler;
    private readonly System.Windows.Threading.DispatcherTimer _heartbeatTimer;
    private readonly System.Windows.Threading.DispatcherTimer _maintenanceTimer;
    private SelfMaintenanceService? _maintenanceService;
    private FileSystemWatcher? _fileWatcher;
    private bool _isRefreshingTree; // 防重入标记
    private readonly List<TerminalService> _terminals = new();
    private readonly DatabaseService? _db;
    private readonly AuthService? _auth;
    private readonly PrivacyService _privacy;
    private BackupService? _backup;
    private FileCompareService? _fileCompare;
    private PluginService? _pluginService;
    private QuestWindow? _questWindow;
    private MCPService? _mcpService;
    private readonly FileAttachmentService _attachmentService = new();
    private readonly AtMentionService _atMentionService = new();
    private readonly SlashCommandService _slashCommandService = new();
    private readonly GitService _gitService = new();
    private Models.GitConfig? _gitConfig;
    private CodeClipDetector? _codeClipDetector;
    private readonly DebugService _debugService;
    private readonly PromptService _promptService;
    private readonly InputCorrectionService _inputCorrection = new();
    private CodeIndexService? _codeIndexService;
    private SkillService? _skillService;
    private HooksService? _hooksService;

    public ConfigService Config => _config;
    public AppConfig GetConfig() => _config.GetConfig();
    public ProjectConfigService ProjectConfig => _projectConfig;
    public NetworkService Network => _networkService;
    public MemoryService? MemorySvc { get; private set; }
    public PromptLibraryService? PromptLibrarySvc { get; private set; }
    public LearningService? LearningSvc { get; private set; }
    public BackupService? BackupSvc => _backup;
    public FileCompareService? FileCompare => _fileCompare;

    /// <summary>设置 Quest 窗口引用（用于任务中断/完成通知和余额提醒）</summary>
    public void SetQuestWindow(QuestWindow questWindow)
    {
        _questWindow = questWindow;
        _questWindow.SetUsageTracker(_aiService?.UsageTracker);
    }

    public ModelManager ModelManager => _modelManager;
    public ChatModeService ChatModeService => _chatModeService;
    public LanguageTemplateService LanguageTemplates { get; }
    private ObservableCollection<ChatModeDef> _availableModes = new();
    public ObservableCollection<ChatModeDef> AvailableModes { get => _availableModes; set { _availableModes = value; OnPropertyChanged(); } }
    private bool _refreshingModes; // 防重入，避免 RefreshAvailableModes 递归调用导致 StackOverflow

    public EditorViewModel Editor { get; } = new();
    public FileTreeViewModel FileTree { get; } = new();
    public ChatViewModel Chat { get; } = new();
    public TerminalViewModel Terminal { get; } = new();
    public LogViewModel Log { get; } = new();

    public ICommand NewFolderCommand { get; }
    public ICommand NewFileCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand SaveFileCommand { get; }
    public ICommand SaveFileAsCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand RefreshTreeCommand { get; }
    public ICommand CollapseAllCommand { get; }
    public ICommand CloseFolderCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ToggleTerminalCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand ClearChatCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand ToggleAIPanelCommand { get; }
    public ICommand NewTerminalCommand { get; }
    public ICommand ToggleLogCommand { get; }
    public ICommand OpenPreviewCommand { get; }
    public ICommand ConfirmPlanCommand { get; }
    public ICommand CancelPlanCommand { get; }
    public ICommand ReplanCommand { get; }
    public ICommand ShowPlanDetailCommand { get; }
    public ICommand HidePlanDetailCommand { get; }
    public ICommand AcceptPlanSuggestionCommand { get; }
    public ICommand SkipPlanSuggestionCommand { get; }
    public ICommand SwitchSidebarCommand { get; } // 活动栏切换
    public ICommand SearchCommand { get; }
    public ICommand SwitchBottomTabCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand BuildCommand { get; }
    public ICommand PackageCommand { get; }
    public ICommand PluginManagerCommand { get; }
    public ICommand MCPManagerCommand { get; }
    public ICommand AttachFilesCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand SelectAtMentionCommand { get; }
    public ICommand NavigateToCodeCommand { get; }
    public ICommand DismissCodeChipCommand { get; }
    public ICommand SelectSlashCommand { get; }
    public ICommand GitSettingsCommand { get; }
    public ICommand GitPushCommand { get; }
    public ICommand StartDebugCommand { get; }
    public ICommand StopDebugCommand { get; }
    public ICommand ToggleBreakpointCommand { get; }
    public ICommand StepOverCommand { get; }
    public ICommand StepIntoCommand { get; }
    public ICommand StepOutCommand { get; }
    public ICommand ContinueDebugCommand { get; }
    public ICommand RunToCursorCommand { get; }
    public ICommand GoToDefinitionCommand { get; }
    public ICommand FindReferencesCommand { get; }
    public ICommand NewSessionCommand { get; }
    public ICommand SwitchSessionCommand { get; }
    public ICommand CloseSessionCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand GitCommitPushCommand { get; }
    public ICommand GitRefreshCommand { get; }
    public ICommand RefreshPluginsCommand { get; }
    public ICommand WikiSearchCommand { get; }
    public ICommand WikiAddMemoryCommand { get; }
    public ICommand WikiClearSessionCommand { get; }
    public ICommand AddRemoteConnectionCommand { get; }
    public ICommand RefreshRemoteCommand { get; }
    public ICommand RefreshContainersCommand { get; }
    public ICommand CompressChatCommand { get; }  // 压缩对话
    public ICommand NewChatCommand { get; }        // 新建对话
    public ICommand SetTodoFilterCommand { get; }  // 待办筛选
    public ICommand ToggleTodoCommand { get; }     // 待办状态切换
    public ICommand ClearCompletedTodosCommand { get; } // 清除已完成待办
    public ICommand AddTodoCommand { get; }        // 手动添加待办
    public ICommand RestoreFromCrashCommand { get; }     // 从崩溃 restore
    public ICommand DiscardCrashRecoveryCommand { get; } // 放弃崩溃恢复
    public ICommand CompareWithBackupCommand { get; }    // 与备份对比
    public ICommand RestoreFromBackupCommand { get; }    // 从备份恢复

    // ===== Auth 相关 =====
    public bool IsLoggedIn => _auth?.IsLoggedIn ?? false;
    public string CurrentUsername => _auth?.CurrentUser?.Username ?? "";
    public bool IsAdmin => _auth?.IsAdmin ?? false;
    public Visibility NotLoggedInVisibility => IsLoggedIn ? Visibility.Collapsed : Visibility.Visible;

    private string _activeSidebar = "files"; // files / search / git / extensions / wiki / remote / containers / debug
    private bool _isAIPanelVisible = true;
    private bool _isLeftSidebarVisible = true;
    private string _activeBottomTab = ""; // terminal / log / (空=隐藏)
    private string _searchInput = string.Empty;
    // ===== @ 提及弹窗 =====
    private ObservableCollection<AtMentionItem> _atMentionResults = new();
    private bool _isAtMentionPopupOpen;
    private string _atMentionQuery = string.Empty;
    public ObservableCollection<AtMentionItem> AtMentionResults { get => _atMentionResults; set { _atMentionResults = value; OnPropertyChanged(); } }
    public bool IsAtMentionPopupOpen { get => _isAtMentionPopupOpen; set { _isAtMentionPopupOpen = value; OnPropertyChanged(); } }
    public string AtMentionQuery { get => _atMentionQuery; set { _atMentionQuery = value; OnPropertyChanged(); } }

    // ===== / 斜杠命令弹窗 =====
    private ObservableCollection<SlashCommandItem> _slashCommandResults = new();
    private bool _isSlashCommandPopupOpen;
    private string _slashCommandQuery = string.Empty;
    public ObservableCollection<SlashCommandItem> SlashCommandResults { get => _slashCommandResults; set { _slashCommandResults = value; OnPropertyChanged(); } }
    public bool IsSlashCommandPopupOpen { get => _isSlashCommandPopupOpen; set { _isSlashCommandPopupOpen = value; OnPropertyChanged(); } }
    public string SlashCommandQuery { get => _slashCommandQuery; set { _slashCommandQuery = value; OnPropertyChanged(); } }

    // ===== 代码引用标签 =====
    private ObservableCollection<CodeMatchResult> _codeRefChips = new();
    public ObservableCollection<CodeMatchResult> CodeRefChips { get => _codeRefChips; set { _codeRefChips = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCodeRefs)); } }
    public bool HasCodeRefs => _codeRefChips.Count > 0;

    private string _buildLanguage = ""; // 检测到的项目语言
    private ObservableCollection<GrepMatchDisplay> _searchResults = new();
    private bool _isSearching;

    public string ActiveSidebar { get => _activeSidebar; set { _activeSidebar = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFilesSidebar)); OnPropertyChanged(nameof(IsSearchSidebar)); OnPropertyChanged(nameof(IsGitSidebar)); OnPropertyChanged(nameof(IsExtensionsSidebar)); OnPropertyChanged(nameof(IsWikiSidebar)); OnPropertyChanged(nameof(IsRemoteSidebar)); OnPropertyChanged(nameof(IsContainersSidebar)); OnPropertyChanged(nameof(IsDebugSidebar)); } }
    public bool IsFilesSidebar => _activeSidebar == "files";
    public bool IsSearchSidebar => _activeSidebar == "search";
    public bool IsGitSidebar => _activeSidebar == "git";
    public bool IsExtensionsSidebar => _activeSidebar == "extensions";
    public bool IsWikiSidebar => _activeSidebar == "wiki";
    public bool IsRemoteSidebar => _activeSidebar == "remote";
    public bool IsContainersSidebar => _activeSidebar == "containers";
    public bool IsDebugSidebar => _activeSidebar == "debug";
    public bool IsAIPanelVisible { get => _isAIPanelVisible; set { _isAIPanelVisible = value; OnPropertyChanged(); } }
    public bool IsLeftSidebarVisible { get => _isLeftSidebarVisible; set { _isLeftSidebarVisible = value; OnPropertyChanged(); } }
    public string ActiveBottomTab { get => _activeBottomTab; set { _activeBottomTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTerminalTab)); OnPropertyChanged(nameof(IsLogTab)); OnPropertyChanged(nameof(IsPreviewTab)); OnPropertyChanged(nameof(IsBottomVisible)); } }
    public bool IsTerminalTab => _activeBottomTab == "terminal";
    public bool IsLogTab => _activeBottomTab == "log";
    public bool IsPreviewTab => _activeBottomTab == "preview";
    public bool IsBottomVisible => !string.IsNullOrEmpty(_activeBottomTab);
    public string SearchInput { get => _searchInput; set { _searchInput = value; OnPropertyChanged(); } }
    public ObservableCollection<GrepMatchDisplay> SearchResults { get => _searchResults; set { _searchResults = value; OnPropertyChanged(); } }
    public bool IsSearching { get => _isSearching; set { _isSearching = value; OnPropertyChanged(); } }

    // ===== Web预览 =====
    private string _previewUrl = "";
    public string PreviewUrl { get => _previewUrl; set { _previewUrl = value; OnPropertyChanged(); } }

    // ===== 调试器状态 =====
    private bool _isDebugging;
    private string _debugStatus = "调试";
    public bool IsDebugging { get => _isDebugging; set { _isDebugging = value; OnPropertyChanged(); OnPropertyChanged(nameof(DebugToolbarVisible)); } }
    public string DebugStatus { get => _debugStatus; set { _debugStatus = value; OnPropertyChanged(); } }
    public bool DebugToolbarVisible => _isDebugging;
    public DebugService DebugSvc => _debugService;

    // ===== 文件树交互事件 =====
    /// <summary>当 CollapseAll 命令触发时调用，由 View 层订阅</summary>
    public event Action? CollapseAllRequested;
    /// <summary>当文件在树中打开后触发，参数为文件路径，由 View 层订阅</summary>
    public event Action<string>? FileOpenedInTree;

    // ===== Token 用量状态 =====
    /// <summary>用量追踪器（供用量看板绑定）</summary>
    public UsageTrackerService? UsageTracker => _aiService?.UsageTracker;
    /// <summary>沙箱安全服务</summary>
    public SandboxService SandboxSvc => _sandboxSvc;
    public string UsageStatusText => _aiService?.UsageTracker?.StatusText ?? "";

    // ===== 会话调度状态 =====
    /// <summary>会话管理器（供UI绑定会话列表）</summary>
    public SessionManager SessionMgr => _sessionManager;
    /// <summary>当前活跃会话名称</summary>
    public string ActiveSessionName => _sessionManager?.ActiveSession?.Name ?? "";
    /// <summary>所有会话列表</summary>
    public System.Collections.Generic.IReadOnlyList<ConversationSession> Sessions => _sessionManager?.Sessions ?? Array.Empty<ConversationSession>();

    // ===== 后台任务继续执行 =====
    private bool _isAppActive = true;
    private int _backgroundTaskCount;
    private string _backgroundTaskSummary = string.Empty;
    /// <summary>应用窗口是否处于激活状态（用户是否在当前应用）</summary>
    public bool IsAppActive { get => _isAppActive; set { _isAppActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAppInBackground)); } }
    /// <summary>应用是否在后台（用户切到其他应用）</summary>
    public bool IsAppInBackground => !_isAppActive;
    /// <summary>后台任务计数</summary>
    public int BackgroundTaskCount { get => _backgroundTaskCount; set { _backgroundTaskCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBackgroundTasks)); } }
    /// <summary>后台任务摘要</summary>
    public string BackgroundTaskSummary { get => _backgroundTaskSummary; set { _backgroundTaskSummary = value; OnPropertyChanged(); } }
    public bool HasBackgroundTasks => _backgroundTaskCount > 0;

    /// <summary>窗口标题（动态显示项目名和当前文件名）</summary>
    public string WindowTitle
    {
        get
        {
            var project = !string.IsNullOrEmpty(FileTree.ProjectName) ? FileTree.ProjectName : null;
            var file = !string.IsNullOrEmpty(Editor.CurrentFile) ? System.IO.Path.GetFileName(Editor.CurrentFile) : null;
            if (project != null && file != null)
                return $"{file} - {project} - AI IDE";
            if (project != null)
                return $"{project} - AI IDE";
            if (file != null)
                return $"{file} - AI IDE";
            return "AI IDE - 智能代码编辑器";
        }
    }

    /// <summary>检测到的项目编程语言</summary>
    public string BuildLanguage { get => _buildLanguage; set { _buildLanguage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBuildLanguage)); OnPropertyChanged(nameof(BuildLanguageDisplay)); } }
    public bool HasBuildLanguage => !string.IsNullOrEmpty(_buildLanguage) && _buildLanguage != "未知";
    public string BuildLanguageDisplay => HasBuildLanguage ? $"🔧 {_buildLanguage}" : "";

    // ===== Git 状态栏显示 =====
    private string _gitBranchDisplay = "";
    private string _gitChangeDisplay = "";
    public string GitBranchDisplay { get => _gitBranchDisplay; set { _gitBranchDisplay = value; OnPropertyChanged(); } }
    public string GitChangeDisplay { get => _gitChangeDisplay; set { _gitChangeDisplay = value; OnPropertyChanged(); } }

    // ===== Git 面板属性 =====
    private ObservableCollection<GitChangeDisplayItem> _gitChanges = new();
    private ObservableCollection<GitChangeDisplayItem> _gitStagedChanges = new();
    private ObservableCollection<GitChangeDisplayItem> _gitUntrackedChanges = new();
    private string _gitCommitMessage = "update";
    public ObservableCollection<GitChangeDisplayItem> GitChanges { get => _gitChanges; set { _gitChanges = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasGitChanges)); } }
    public ObservableCollection<GitChangeDisplayItem> GitStagedChanges { get => _gitStagedChanges; set { _gitStagedChanges = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStagedChanges)); } }
    public ObservableCollection<GitChangeDisplayItem> GitUntrackedChanges { get => _gitUntrackedChanges; set { _gitUntrackedChanges = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUntrackedChanges)); } }
    public bool HasGitChanges => _gitChanges.Count > 0;
    public bool HasStagedChanges => _gitStagedChanges.Count > 0;
    public bool HasUntrackedChanges => _gitUntrackedChanges.Count > 0;
    public bool IsGitClean => !HasGitChanges && !HasStagedChanges && !HasUntrackedChanges && !string.IsNullOrEmpty(GitBranchDisplay);
    public bool IsNotGitRepo => string.IsNullOrEmpty(GitBranchDisplay) && !string.IsNullOrEmpty(FileTree.ProjectPath);
    public string GitCommitMessage { get => _gitCommitMessage; set { _gitCommitMessage = value; OnPropertyChanged(); } }

    // ===== 扩展面板属性 =====
    private ObservableCollection<PluginManifest> _installedPlugins = new();
    public ObservableCollection<PluginManifest> InstalledPlugins { get => _installedPlugins; set { _installedPlugins = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasInstalledPlugins)); OnPropertyChanged(nameof(InstalledPluginSummary)); } }
    public bool HasInstalledPlugins => _installedPlugins.Count > 0;
    public string InstalledPluginSummary => HasInstalledPlugins ? $"{_installedPlugins.Count} 个插件已安装" : "暂无已安装插件";

    // ===== 插件更新状态 =====
    private int _pluginUpdateCount;
    public int PluginUpdateCount { get => _pluginUpdateCount; set { _pluginUpdateCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPluginUpdate)); OnPropertyChanged(nameof(PluginUpdateBadge)); OnPropertyChanged(nameof(PluginEntryTooltip)); } }
    public bool HasPluginUpdate => _pluginUpdateCount > 0;
    public string PluginUpdateBadge => _pluginUpdateCount > 0 ? _pluginUpdateCount.ToString() : "";
    private bool _pluginNeedRestart;
    public bool PluginNeedRestart { get => _pluginNeedRestart; set { _pluginNeedRestart = value; OnPropertyChanged(); OnPropertyChanged(nameof(PluginEntryTooltip)); } }
    public string PluginEntryTooltip => _pluginNeedRestart && _pluginUpdateCount > 0
        ? $"扩展 ({_pluginUpdateCount} 个已更新，重启后生效)"
        : _pluginUpdateCount > 0
            ? $"扩展 ({_pluginUpdateCount} 个可用更新)"
            : "扩展";
    private bool _pluginUpdateChecked;
    public bool PluginUpdateChecked { get => _pluginUpdateChecked; set { _pluginUpdateChecked = value; OnPropertyChanged(); OnPropertyChanged(nameof(PluginEntryTooltip)); } }

    // ===== Wiki 面板属性 =====
    private ObservableCollection<WikiMemoryDisplay> _wikiMemories = new();
    private string _wikiSearchQuery = string.Empty;
    public ObservableCollection<WikiMemoryDisplay> WikiMemories { get => _wikiMemories; set { _wikiMemories = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasWikiMemories)); } }
    public bool HasWikiMemories => _wikiMemories.Count > 0;
    public string WikiSearchQuery { get => _wikiSearchQuery; set { _wikiSearchQuery = value; OnPropertyChanged(); } }

    // ===== 远程资源管理器属性 =====
    private ObservableCollection<RemoteConnectionItem> _sshConnections = new();
    private ObservableCollection<RemoteConnectionItem> _wslConnections = new();
    private ObservableCollection<RemoteConnectionItem> _devContainerConnections = new();
    public ObservableCollection<RemoteConnectionItem> SshConnections { get => _sshConnections; set { _sshConnections = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSshConnections)); } }
    public ObservableCollection<RemoteConnectionItem> WslConnections { get => _wslConnections; set { _wslConnections = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasWslConnections)); } }
    public ObservableCollection<RemoteConnectionItem> DevContainerConnections { get => _devContainerConnections; set { _devContainerConnections = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDevContainerConnections)); } }
    public bool HasSshConnections => _sshConnections.Count > 0;
    public bool HasWslConnections => _wslConnections.Count > 0;
    public bool HasDevContainerConnections => _devContainerConnections.Count > 0;

    // ===== Containers 面板属性 =====
    private ObservableCollection<DockerContainerItem> _runningContainers = new();
    private ObservableCollection<DockerContainerItem> _stoppedContainers = new();
    public ObservableCollection<DockerContainerItem> RunningContainers { get => _runningContainers; set { _runningContainers = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRunningContainers)); OnPropertyChanged(nameof(HasAnyContainers)); } }
    public ObservableCollection<DockerContainerItem> StoppedContainers { get => _stoppedContainers; set { _stoppedContainers = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStoppedContainers)); OnPropertyChanged(nameof(HasAnyContainers)); } }
    public bool HasRunningContainers => _runningContainers.Count > 0;
    public bool HasStoppedContainers => _stoppedContainers.Count > 0;
    public bool HasAnyContainers => _runningContainers.Count > 0 || _stoppedContainers.Count > 0;

    /// <summary>刷新 Git 状态栏信息</summary>
    public void RefreshGitStatus()
    {
        try
        {
            var branch = _gitService.GetCurrentBranch();
            if (!string.IsNullOrEmpty(branch))
            {
                GitBranchDisplay = $"⎇ {branch}";
                var (staged, unstaged, untracked) = _gitService.GetChangeCount();
                var parts = new List<string>();
                if (staged > 0) parts.Add($"+{staged} 暂存");
                if (unstaged > 0) parts.Add($"*{unstaged} 修改");
                if (untracked > 0) parts.Add($"?{untracked} 未跟踪");
                GitChangeDisplay = parts.Count > 0 ? string.Join(" | ", parts) : "工作区干净";
            }
            else
            {
                GitBranchDisplay = "";
                GitChangeDisplay = "";
            }
        }
        catch
        {
            GitBranchDisplay = "";
            GitChangeDisplay = "";
        }
    }

    /// <summary>是否允许文件编辑工具修改工作区外的文件</summary>
    public bool AllowExternalFileEdit
    {
        get => _aiService.AllowExternalFileEdit;
        set
        {
            if (_aiService.AllowExternalFileEdit != value)
            {
                _aiService.AllowExternalFileEdit = value;
                _config.GetAIConfig().AllowExternalFileEdit = value;
                _config.Save();
                OnPropertyChanged();
            }
        }
    }

    private ChatMessageDisplay? _currentStreamingMsg;
    private ChatMessageDisplay? _thinkingMsg;
    private ChatMessageDisplay? _reasoningMsg;
    private readonly Queue<string> _pendingModelLabels = new();
    private string? _pendingSuggestionMessage; // 等待计划建议的原始消息
    private List<CheckpointManifest>? _pendingCheckpoints; // 待恢复的 checkpoint 列表
    private bool _isCrashRecoveryMode; // 当前是否处于崩溃恢复提示模式
    private string _settingsApiKey = string.Empty;
    private string _settingsModel = "deepseek-v4-pro";
    private int _settingsMaxTokens = 0;
    private bool _isSettingsOpen;

    public string SettingsApiKey { get => _settingsApiKey; set { _settingsApiKey = value; OnPropertyChanged(); } }
    public string SettingsModel { get => _settingsModel; set { _settingsModel = value; OnPropertyChanged(); } }
    public int SettingsMaxTokens { get => _settingsMaxTokens; set { _settingsMaxTokens = value; OnPropertyChanged(); } }
    public bool IsSettingsOpen { get => _isSettingsOpen; set { _isSettingsOpen = value; OnPropertyChanged(); } }

    public MainViewModel(DatabaseService? db = null, AuthService? auth = null)
    {
        _db = db;
        _auth = auth;
        _privacy = new PrivacyService();
        if (_db != null)
        {
            MemorySvc = new MemoryService(_db);
            PromptLibrarySvc = new PromptLibraryService(_db);
            LearningSvc = new LearningService(_db);
        }

        _config = new ConfigService();
        _modelManager = new ModelManager(_config);
        _chatModeService = new ChatModeService(_modelManager);

        // ═══ API Key 安全：换机/换用户自动清除 + 解密失败通知 ═══
        _config.OnSecurityReset += () =>
            Application.Current.Dispatcher.Invoke(() =>
            {
                Chat.AddMessage("system",
                    "🔒 **安全提示**：检测到当前运行环境（计算机或用户账户）已变更。\n" +
                    "为保护您的 API Key 不被他人使用，已自动清除所有 API Key 配置。\n" +
                    "请前往 **设置 → 管理模型** 重新配置 API Key。");
                Chat.AIStatus = "AI: 未配置";
            });
        SecureConfigHelper.OnDecryptFailed += (err) =>
            Application.Current.Dispatcher.Invoke(() =>
            {
                Chat.AddMessage("system",
                    "⚠️ **API Key 解密失败**：当前计算机或用户账户无法解密已保存的 API Key。\n" +
                    "请前往 **设置 → 管理模型** 重新配置 API Key。");
                Chat.AIStatus = "AI: 未配置";
            });
        _fileService = new FileService();
        _searchService = new SearchService();
        _terminalService = new TerminalService();
        _debugService = new DebugService(_terminalService);
        _promptService = new PromptService();
        // 从用户配置加载 AI 回答语言
        var appCfg = _config.GetConfig();
        _promptService.ResponseLanguage = appCfg.Appearance?.ResponseLanguage ?? "中文";
        _networkService = new NetworkService();
        _aiService = new AIService(_config, _modelManager, _chatModeService, _fileService, _searchService, _terminalService, _promptService, networkService: _networkService);
        _projectConfig = new ProjectConfigService();

        // 初始化沙箱安全服务
        _sandboxSvc = new SandboxService(LogService.Instance, appCfg.Sandbox);
        _aiService.SetSandboxService(_sandboxSvc);
        _sandboxSvc.OnTerminalConsentRequested += OnTerminalConsentRequestedAsync;

        // 初始化会话调度管理器（防止多会话文件修改冲突）
        _sessionManager = new SessionManager();
        _aiService.SetSessionManager(_sessionManager);
        LanguageTemplates = new LanguageTemplateService();

        // 订阅网络状态变化，断网时更新状态栏
        _networkService.NetworkStatusChanged += (connected) =>
        {
            if (!connected)
                Chat.AIStatus = "⚠ 网络已断开";
        };

        // Init AI callbacks
        _aiService.OnChunk += OnAIChunk;
        _aiService.OnToolCall += OnAIToolCall;
        _aiService.OnDone += OnAIDone;
        _aiService.OnError += OnAIError;
        _aiService.OnFileChanged += OnAIFileChanged;
        _aiService.OnTodoWrite += OnAITodoWrite;
        _aiService.OnReasoningChunk += OnAIReasoningChunk;
        _aiService.OnPendingFileChange += OnPendingFileChangeAsync;
        _aiService.OnTerminalOutput += OnAITerminalOutput;
        _aiService.OnTerminalCommandConfirm += OnTerminalCommandConfirmAsync;
        _aiService.OnWebSearchConsent += OnWebSearchConsentAsync;
        _aiService.OnAllowExternalEditRequested = OnAllowExternalEditRequestedAsync;
        _aiService.GetTodosForCompression = () =>
            Chat.Todos
                .Where(t => t.Status != "cancelled")
                .Select(t => (t.Content ?? "", t.Status ?? "pending"))
                .ToList();
        Chat.PlanSuggestionAccepted += OnPlanSuggestionAccepted;
        Chat.CompressChatRequested += () => _ = CompressChatAsync();
        Chat.NewChatRequested += RequestNewChat;
        _codeClipDetector = new CodeClipDetector(_searchService);

        // 订阅会话文件冲突通知
        _sessionManager.OnFileConflict += (filePath, lockedBy, attemptedBy) =>
            Application.Current.Dispatcher.Invoke(() =>
                Chat.AddMessage("system", $"⛔ 文件冲突: [{System.IO.Path.GetFileName(filePath)}] 正被其他会话修改中，请等待完成"));
        _sessionManager.OnSessionCreated += (s) =>
            Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(Sessions)));
        _sessionManager.OnSessionClosed += (s) =>
            Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(Sessions)));
        _sessionManager.OnSessionSwitched += (prev, curr) =>
        {
            if (curr != null)
            {
                _aiService.CurrentSessionId = curr.Id;
                Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(ActiveSessionName)));
            }
        };

        // 订阅聊天输入变化以检测 @ 提及
        Chat.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Chat.InputText))
                HandleInputTextChanged(Chat.InputText);
        };

        // 订阅附件服务警告/信息回调
        _attachmentService.OnWarning += (msg) =>
            Application.Current.Dispatcher.Invoke(() => Chat.AddMessage("system", msg));
        _attachmentService.OnInfo += (msg) =>
            Application.Current.Dispatcher.Invoke(() => Chat.AddMessage("system", msg));

        // 订阅用量追踪器警告回调
        _aiService.UsageTracker.OnWarning += (msg) =>
            Application.Current.Dispatcher.Invoke(() => Chat.AddMessage("system", msg));
        _aiService.UsageTracker.OnCritical += (msg) =>
            Application.Current.Dispatcher.Invoke(() => Chat.AddMessage("system", msg));
        _aiService.UsageTracker.OnStatusChanged += () =>
            Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(UsageStatusText)));
        _aiService.UsageTracker.OnQuotaExhausted += (msg) =>
            Application.Current.Dispatcher.Invoke(() => ShowQuotaExhaustedDialog(msg));

        // 订阅文件树和编辑器变化以更新窗口标题
        FileTree.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FileTree.ProjectName))
                OnPropertyChanged(nameof(WindowTitle));
        };
        Editor.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Editor.CurrentFile))
                OnPropertyChanged(nameof(WindowTitle));
        };

        // Init terminal
        _terminalService.OnDataReceived += OnTerminalData;
        _terminalService.OnExited += OnTerminalExit;

        // Commands
        NewFolderCommand = new RelayCommand(_ => NewFolder());
        NewFileCommand = new RelayCommand(_ => NewFile());
        OpenFileCommand = new RelayCommand(_ => OpenFile());
        SaveFileCommand = new RelayCommand(_ => SaveFile(false));
        SaveFileAsCommand = new RelayCommand(_ => SaveFile(true));
        OpenProjectCommand = new RelayCommand(_ => OpenProject());
        RefreshTreeCommand = new RelayCommand(_ => {
            if (!string.IsNullOrEmpty(FileTree.ProjectPath))
                RefreshFileTree(FileTree.ProjectPath);
        });
        CollapseAllCommand = new RelayCommand(_ => CollapseAllRequested?.Invoke());
        CloseFolderCommand = new RelayCommand(_ => CloseFolder(), _ => FileTree.IsProjectOpen);
        CloseCommand = new RelayCommand(_ => {
            _heartbeatTimer.Stop();
            _scheduler.Dispose();
            Application.Current.Shutdown();
        });
        ToggleTerminalCommand = new RelayCommand(_ => ToggleTerminal());
        SendMessageCommand = new RelayCommand(_ => SendMessage());
        ClearChatCommand = new RelayCommand(_ => Chat.Messages.Clear());
        ShowSettingsCommand = new RelayCommand(_ => ShowSettings());
        CloseSettingsCommand = new RelayCommand(_ => CloseSettings());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        StopCommand = new RelayCommand(_ => Stop(), _ => Chat.IsStreaming);
        BuildCommand = new RelayCommand(_ => RunBuild());
        PackageCommand = new RelayCommand(_ => RunPackage());
        PluginManagerCommand = new RelayCommand(_ => OpenPluginManager());
        MCPManagerCommand = new RelayCommand(_ => OpenMCPManager());
        AttachFilesCommand = new RelayCommand(_ => AttachFiles());
        RemoveAttachmentCommand = new RelayCommand(param =>
        {
            if (param is Models.FileAttachment fa)
                RemoveAttachment(fa);
        });
        SelectAtMentionCommand = new RelayCommand(param =>
        {
            if (param is AtMentionItem item)
                SelectAtMention(item);
        });
        NavigateToCodeCommand = new RelayCommand(param =>
        {
            if (param is CodeMatchResult result)
                NavigateToCode(result);
        });
        DismissCodeChipCommand = new RelayCommand(param =>
        {
            if (param is CodeMatchResult result)
                DismissCodeChip(result);
        });
        SelectSlashCommand = new RelayCommand(param =>
        {
            if (param is SlashCommandItem cmd)
                SelectSlashCommandItem(cmd);
        });
        GitSettingsCommand = new RelayCommand(_ => OpenGitSettings());
        GitPushCommand = new RelayCommand(_ => GitPushAsync());
        ToggleSidebarCommand = new RelayCommand(_ => IsLeftSidebarVisible = !IsLeftSidebarVisible);
        ToggleAIPanelCommand = new RelayCommand(_ => IsAIPanelVisible = !IsAIPanelVisible);
        SwitchSidebarCommand = new RelayCommand(param =>
        {
            var panel = param as string ?? "files";
            if (ActiveSidebar == panel && IsLeftSidebarVisible)
                IsLeftSidebarVisible = false; // 再次点击同一图标 → 折叠
            else
            {
                ActiveSidebar = panel;
                IsLeftSidebarVisible = true;
            }
        });
        SearchCommand = new RelayCommand(_ => RunSearch());
        SwitchBottomTabCommand = new RelayCommand(param =>
        {
            var tab = param as string ?? "";
            if (ActiveBottomTab == tab)
                ActiveBottomTab = ""; // 再次点击同一标签 → 隐藏底部
            else
                ActiveBottomTab = tab;
        });
        NewTerminalCommand = new RelayCommand(_ => CreateTerminal());
        ToggleLogCommand = new RelayCommand(_ => ToggleLog());
        OpenPreviewCommand = new RelayCommand(_ => OpenPreview());
        ConfirmPlanCommand = new RelayCommand(_ => ConfirmPlan());
        CancelPlanCommand = new RelayCommand(_ => CancelPlan());
        ReplanCommand = new RelayCommand(_ => Replan());
        ShowPlanDetailCommand = new RelayCommand(_ => ShowPlanDetail());
        HidePlanDetailCommand = new RelayCommand(_ => HidePlanDetail());
        AcceptPlanSuggestionCommand = new RelayCommand(_ => Chat.AcceptPlanSuggestion());
        SkipPlanSuggestionCommand = new RelayCommand(_ => Chat.SkipPlanSuggestion());
        StartDebugCommand = new RelayCommand(_ => StartDebug());
        StopDebugCommand = new RelayCommand(_ => StopDebug());
        ToggleBreakpointCommand = new RelayCommand(param =>
        {
            if (param is string filePath && !string.IsNullOrEmpty(Editor.CurrentFile))
                ToggleBreakpoint(Editor.CurrentFile);
        });
        StepOverCommand = new RelayCommand(_ => StepOver());
        StepIntoCommand = new RelayCommand(_ => StepInto());
        StepOutCommand = new RelayCommand(_ => StepOut());
        ContinueDebugCommand = new RelayCommand(_ => ContinueDebug());
        RunToCursorCommand = new RelayCommand(_ => RunToCursor());
        GoToDefinitionCommand = new RelayCommand(_ => GoToDefinition());
        FindReferencesCommand = new RelayCommand(_ => FindReferences());
        NewSessionCommand = new RelayCommand(_ => NewSession());
        SwitchSessionCommand = new RelayCommand(param =>
        {
            if (param is string sessionId)
                SwitchSession(sessionId);
        });
        CloseSessionCommand = new RelayCommand(param =>
        {
            if (param is string sessionId)
                CloseSession(sessionId);
        });
        LoginCommand = new RelayCommand(_ => ShowLogin());
        GitCommitPushCommand = new RelayCommand(_ => GitPushAsync(), _ => !string.IsNullOrEmpty(FileTree.ProjectPath));
        GitRefreshCommand = new RelayCommand(_ => { RefreshGitStatus(); RefreshGitChangesPanel(); });
        RefreshPluginsCommand = new RelayCommand(_ => RefreshInstalledPlugins());
        WikiSearchCommand = new RelayCommand(_ => RefreshWikiMemories());
        WikiAddMemoryCommand = new RelayCommand(_ => AddWikiMemory());
        WikiClearSessionCommand = new RelayCommand(_ => ClearWikiSession());
        AddRemoteConnectionCommand = new RelayCommand(_ => ShowAddRemoteConnectionDialog());
        RefreshRemoteCommand = new RelayCommand(_ => RefreshRemoteConnections());
        RefreshContainersCommand = new RelayCommand(_ => RefreshDockerContainers());
        CompressChatCommand = new RelayCommand(async _ => await CompressChatAsync());
        NewChatCommand = new RelayCommand(_ => RequestNewChat());
        // 待办筛选命令
        SetTodoFilterCommand = new RelayCommand(param => Chat.SetTodoFilter(param is TodoFilter f ? f : TodoFilter.All));
        ToggleTodoCommand = new RelayCommand(param => { if (param is TodoItem item) Chat.ToggleTodoStatus(item); });
        ClearCompletedTodosCommand = new RelayCommand(_ => Chat.ClearCompletedTodos());
        AddTodoCommand = new RelayCommand(param =>
        {
            if (param is string content)
                Chat.AddTodo(content);
        });
        RestoreFromCrashCommand = new RelayCommand(_ => RestoreFromCrash());
        DiscardCrashRecoveryCommand = new RelayCommand(_ => DiscardCrashRecovery());
        CompareWithBackupCommand = new RelayCommand(param =>
        {
            if (param is string filePath)
                CompareWithBackup(filePath);
        });
        RestoreFromBackupCommand = new RelayCommand(param =>
        {
            if (param is string backupPath)
                RestoreFromBackup(backupPath);
        });

        // 订阅调试器输出
        _debugService.OnOutput += (msg) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Terminal.AppendOutput(msg + "\r\n");
                DebugStatus = msg;
            });
        };

        // 订阅调试器位置变更 → 自动跳转编辑器和行高亮
        _debugService.OnPositionChanged += (filePath, line, func) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(filePath) || line <= 0)
                {
                    // 清除高亮
                    Editor.DebugLine = -1;
                    Editor.DebugFile = "";
                    return;
                }

                // 打开文件（如果还没打开）
                OpenFile(filePath);

                // 导航到目标行
                Editor.NavigateToLine = line;
                Editor.DebugLine = line;
                Editor.DebugFile = filePath;

                // 更新状态栏
                var displayPath = Path.GetFileName(filePath);
                DebugStatus = func != null
                    ? $"📍 停在: {displayPath}:{line} → {func}()"
                    : $"📍 停在: {displayPath}:{line}";

                // 强制刷新装订线（显示当前位置标记）
                CodeEditor?.TextArea?.TextView?.InvalidateLayer(
                    ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
            });
        };

        // Init log service
        LogService.Instance.OnLog += (entry) => Log.Append(entry.ToString());
        LogService.Instance.Info("应用启动");

        // 启动后台调度 + 看门狗
        _scheduler = new BackgroundScheduler(
            () => FileTree.ProjectPath,
            () => SaveProjectState());
        _heartbeatTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            System.Windows.Threading.DispatcherPriority.Background,
            (s, e) => _scheduler.Heartbeat(),
            System.Windows.Application.Current.Dispatcher);
        _heartbeatTimer.Start();

        // 启用后台日志分析（空闲时自动分析并生成改进建议）
        var logAnalysis = new LogAnalysisService();
        _scheduler.EnableLogAnalysis(logAnalysis,
            () => _config.GetAIConfig().ApiKey,
            () => _modelManager.GetBaseUrl(_modelManager.ActiveProvider?.Id ?? "deepseek"),
            () => _config.GetAIConfig().Model);
        _scheduler.OnLogAnalysisComplete += OnLogAnalysisResults;

        // 启动自动维护定时器（每 10 分钟检查一次，仅在空闲时执行）
        _maintenanceTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMinutes(10),
            System.Windows.Threading.DispatcherPriority.Background,
            (s, e) => RunMaintenanceScanAsync(),
            System.Windows.Application.Current.Dispatcher);
        _maintenanceTimer.Start();

        // Load config
        var cfg = _config.GetAIConfig();
        Chat.SelectedModel = cfg.Model;
        Chat.ChatMode = _chatModeService.ActiveModeId;
        Chat.AIStatus = _modelManager.HasAnyApiKeyConfigured() ? "AI: 就绪" : "AI: 未配置";
        _aiService.AllowExternalFileEdit = cfg.AllowExternalFileEdit;

        // 模式变更同步
        _chatModeService.OnModeChanged += modeId =>
        {
            Chat.ChatMode = modeId;
            _modelManager.ActiveMode = modeId; // 保持 ModelManager 同步
        };

        RefreshAvailableModes();
        // 清除旧的重启标记（应用已重启），再刷新插件状态
        ClearPluginRestartFlags();
        // 检测项目语言（若有恢复的项目）
        if (!string.IsNullOrEmpty(FileTree.ProjectPath))
            BuildLanguage = _aiService.BuildSvc.DetectLanguage();
    }

    public void RefreshAvailableModes()
    {
        if (_refreshingModes) return;
        _refreshingModes = true;
        try
        {
            _availableModes.Clear();
            foreach (var m in _chatModeService.GetAvailableModes())
                _availableModes.Add(m);
            // 模型切换可能重置模式（如新模型不支持 agent），同步回 ChatModeService
            if (_chatModeService.ActiveModeId != _modelManager.ActiveMode
                && _chatModeService.GetMode(_modelManager.ActiveMode) != null)
                _chatModeService.ActiveModeId = _modelManager.ActiveMode;
            // 回同步模式到 Chat
            Chat.ChatMode = _chatModeService.ActiveModeId;
        }
        finally
        {
            _refreshingModes = false;
        }
    }

    private void RunMaintenanceScanAsync()
    {
        if (_maintenanceService == null || Chat.IsStreaming) return;

        var dialog = new MaintenanceDialog(
            _maintenanceService,
            msg => Chat.AddMessage("system", msg),
            () =>
            {
                if (!string.IsNullOrEmpty(FileTree.ProjectPath))
                    RefreshFileTree(FileTree.ProjectPath);
            });
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    private void OnLogAnalysisResults(List<ImprovementSuggestion> suggestions)
    {
        if (suggestions.Count == 0) return;

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📊 后台日志分析发现 {suggestions.Count} 条改进建议：\n");

            var groupedByPriority = suggestions
                .GroupBy(s => s.Priority)
                .OrderByDescending(g => g.Key);

            foreach (var group in groupedByPriority)
            {
                var label = group.Key switch
                {
                    SuggestionPriority.High => "🔴 高优先级",
                    SuggestionPriority.Medium => "🟡 中优先级",
                    SuggestionPriority.Low => "🟢 低优先级",
                    _ => ""
                };
                sb.AppendLine($"{label}:");
                foreach (var s in group)
                {
                    sb.AppendLine($"  {s.DisplayText}");
                    sb.AppendLine($"     {s.Description}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("点击 [是] 确认并开始迭代更新，[否] 忽略。");

            var result = MessageBox.Show(sb.ToString(), "AI 改进建议",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                var suggestionText = string.Join("\n", suggestions.Select(s =>
                    $"- [{s.Priority}] {s.Title}: {s.Description} (可自动修复: {(s.CanAutoFix ? "是" : "否")})"));

                Chat.AddMessage("system", $"📋 以下是根据日志分析得出的改进建议，请逐一评估并实现：\n{suggestionText}");

                LogService.Instance.Info($"用户确认 {suggestions.Count} 条改进建议，将开始迭代更新", "Maintenance");
            }
            else
            {
                LogService.Instance.Info($"用户忽略了 {suggestions.Count} 条改进建议", "Maintenance");
            }
        });
    }
}