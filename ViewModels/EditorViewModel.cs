using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

public class EditorViewModel : INotifyPropertyChanged
{
    private string _currentFile = string.Empty;
    private string _fileType = "纯文本";
    private string _cursorPosition = "行 1, 列 1";
    private bool _isDirty;
    private string _currentContent = string.Empty;
    private ObservableCollection<EditorTab> _tabs;
    private EditorTab? _activeTab;

    public EditorViewModel()
    {
        _tabs = new ObservableCollection<EditorTab>();
        _tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTabs));
            OnPropertyChanged(nameof(HasMultipleTabs));
            UpdateActiveTab();
        };
    }

    public string CurrentFile { get => _currentFile; set { _currentFile = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentFileName)); OnPropertyChanged(nameof(HasOpenFile)); UpdateActiveTab(); } }
    public string CurrentFileName => string.IsNullOrEmpty(_currentFile) ? "未命名" : System.IO.Path.GetFileName(_currentFile);
    public bool HasOpenFile => !string.IsNullOrEmpty(_currentFile);
    public string FileType { get => _fileType; set { _fileType = value; OnPropertyChanged(); } }
    public string CursorPosition { get => _cursorPosition; set { _cursorPosition = value; OnPropertyChanged(); } }
    public int CursorLine => 1;
    public int CursorColumn => 1;
    public string StatusFileType => string.IsNullOrEmpty(_fileType) ? "纯文本" : _fileType;
    public bool IsDirty { get => _isDirty; set { _isDirty = value; OnPropertyChanged(); } }
    public string CurrentContent { get => _currentContent; set { _currentContent = value; OnPropertyChanged(); } }
    public ObservableCollection<EditorTab> Tabs { get => _tabs; set { _tabs = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTabs)); OnPropertyChanged(nameof(HasMultipleTabs)); UpdateActiveTab(); } }
    public bool HasTabs => _tabs.Count > 0;
    public bool HasMultipleTabs => _tabs.Count > 1;

    /// <summary>当前激活的标签页</summary>
    public EditorTab? ActiveTab { get => _activeTab; private set { _activeTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasActiveTab)); } }
    public bool HasActiveTab => _activeTab != null;

    private void UpdateActiveTab()
    {
        ActiveTab = Tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, _currentFile, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>导航到指定行号（设置后触发滚动）</summary>
    private int _navigateToLine = -1;
    public int NavigateToLine { get => _navigateToLine; set { _navigateToLine = value; OnPropertyChanged(); } }

    /// <summary>调试器当前停在的行号（-1 表示不在调试中）</summary>
    private int _debugLine = -1;
    public int DebugLine { get => _debugLine; set { _debugLine = value; OnPropertyChanged(); } }

    /// <summary>调试器当前停的文件路径</summary>
    private string _debugFile = "";
    public string DebugFile { get => _debugFile; set { _debugFile = value; OnPropertyChanged(); } }

    public event Action<string>? FileOpened;
    public event Action? FileSaved;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class EditorTab : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private bool _isDirty;
    private bool _isActive;

    public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(); } }
    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    public bool IsDirty { get => _isDirty; set { _isDirty = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    public string DisplayName => IsDirty ? $"* {FileName}" : FileName;
    public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveBackground)); OnPropertyChanged(nameof(ActiveForeground)); } }

    public string ActiveBackground => IsActive ? "#1e1e1e" : "#2d2d2d";
    public string ActiveForeground => IsActive ? "#ffffff" : "#cccccc";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
