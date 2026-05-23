using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AIIDEWPF.Models;
using AIIDEWPF.Services;

namespace AIIDEWPF.ViewModels;

/// <summary>
/// 记忆管理 ViewModel —— 记忆库的查看/搜索/新增/编辑/删除
/// </summary>
public class MemoryViewModel : INotifyPropertyChanged
{
    private readonly MemoryService _memoryService;
    private readonly string? _workspacePath;

    public MemoryViewModel(MemoryService memoryService, string? workspacePath = null)
    {
        _memoryService = memoryService;
        _workspacePath = workspacePath;

        AddCommand = new RelayCommand(_ => StartAdd());
        EditCommand = new RelayCommand(Edit, _ => SelectedMemory != null);
        DeleteCommand = new RelayCommand(Delete, _ => SelectedMemory != null);
        SaveCommand = new RelayCommand(_ => Save());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
        SearchCommand = new RelayCommand(_ => LoadMemories());

        LoadMemories();
    }

    // ===== 绑定属性 =====

    private ObservableCollection<MemoryItem> _memories = new();
    public ObservableCollection<MemoryItem> Memories
    {
        get => _memories;
        set { _memories = value; OnPropertyChanged(); }
    }

    private MemoryItem? _selectedMemory;
    public MemoryItem? SelectedMemory
    {
        get => _selectedMemory;
        set { _selectedMemory = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEditing)); }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); }
    }

    private string _editTitle = string.Empty;
    public string EditTitle
    {
        get => _editTitle;
        set { _editTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); }
    }

    private string _editContent = string.Empty;
    public string EditContent
    {
        get => _editContent;
        set { _editContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); }
    }

    private string _editCategory = "user_preferences";
    public string EditCategory
    {
        get => _editCategory;
        set { _editCategory = value; OnPropertyChanged(); }
    }

    private string _editKeywords = string.Empty;
    public string EditKeywords
    {
        get => _editKeywords;
        set { _editKeywords = value; OnPropertyChanged(); }
    }

    private string _editScope = "global";
    public string EditScope
    {
        get => _editScope;
        set { _editScope = value; OnPropertyChanged(); }
    }

    private string _scopeFilter = "all";
    public string ScopeFilter
    {
        get => _scopeFilter;
        set { _scopeFilter = value; OnPropertyChanged(); LoadMemories(); }
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotEditing)); }
    }

    public bool IsNotEditing => !_isEditing;
    public bool CanSave => !string.IsNullOrWhiteSpace(EditTitle) && !string.IsNullOrWhiteSpace(EditContent);

    // 类别列表（供 ComboBox 使用）
    public static List<string> Categories { get; } = new()
    {
        "user_preferences", "project_info", "development_standards", "lessons_learned"
    };

    public static Dictionary<string, string> CategoryNames { get; } = new()
    {
        ["user_preferences"] = "用户偏好",
        ["project_info"] = "项目信息",
        ["development_standards"] = "开发规范",
        ["lessons_learned"] = "经验教训"
    };

    // 作用域列表（供 ComboBox 使用）
    public static List<string> Scopes { get; } = new() { "global", "project", "session" };

    public static Dictionary<string, string> ScopeNames { get; } = new()
    {
        ["global"] = "全局",
        ["project"] = "项目",
        ["session"] = "会话"
    };

    // ===== 命令 =====

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand SearchCommand { get; }

    // ===== 操作方法 =====

    public void LoadMemories()
    {
        var list = string.IsNullOrWhiteSpace(SearchText)
            ? _memoryService.GetAll(_workspacePath)
            : _memoryService.Search(SearchText, _workspacePath);
        // 按作用域过滤
        if (_scopeFilter != "all")
            list = list.Where(m => m.Scope == _scopeFilter).ToList();
        Memories = new ObservableCollection<MemoryItem>(list);
    }

    private void StartAdd()
    {
        SelectedMemory = null;
        EditTitle = string.Empty;
        EditContent = string.Empty;
        EditCategory = "user_preferences";
        EditKeywords = string.Empty;
        // 有项目时默认项目级，否则全局
        EditScope = string.IsNullOrEmpty(_workspacePath) ? "global" : "project";
        IsEditing = true;
    }

    private void Edit(object? param)
    {
        if (SelectedMemory == null) return;
        EditTitle = SelectedMemory.Title;
        EditContent = SelectedMemory.Content;
        EditCategory = SelectedMemory.Category;
        EditKeywords = SelectedMemory.Keywords;
        EditScope = SelectedMemory.Scope;
        IsEditing = true;
    }

    private void Save()
    {
        if (!CanSave) return;

        if (SelectedMemory != null && SelectedMemory.Id > 0)
        {
            // 更新现有记忆
            _memoryService.Update(SelectedMemory.Id, EditTitle, EditContent, EditCategory, EditKeywords);
        }
        else
        {
            // 新增记忆（使用用户选择的作用域）
            _memoryService.Add(EditTitle, EditContent, EditCategory, EditScope, _workspacePath, EditKeywords);
        }
        IsEditing = false;
        LoadMemories();
    }

    private void CancelEdit()
    {
        IsEditing = false;
    }

    private void Delete(object? param)
    {
        if (SelectedMemory == null || SelectedMemory.Id <= 0) return;
        _memoryService.Delete(SelectedMemory.Id);
        LoadMemories();
        SelectedMemory = null;
    }

    // ===== INotifyPropertyChanged =====

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
