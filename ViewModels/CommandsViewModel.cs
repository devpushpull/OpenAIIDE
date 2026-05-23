using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AIIDEWPF.Services;

namespace AIIDEWPF.ViewModels;

/// <summary>/ 命令管理 ViewModel —— 设置页内嵌</summary>
public class CommandsViewModel : INotifyPropertyChanged
{
    private readonly SlashCommandService _service;

    public CommandsViewModel(SlashCommandService service)
    {
        _service = service;
        Commands = new ObservableCollection<SlashCommandItem>(service.GetEditableList());

        AddCommand = new RelayCommand(_ => StartAdd());
        EditCmdCommand = new RelayCommand(StartEdit, _ => SelectedCommand != null);
        DeleteCommand = new RelayCommand(Delete, _ => SelectedCommand != null);
        SaveCommand = new RelayCommand(_ => Save());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
        ResetCommand = new RelayCommand(_ => ResetToDefaults());
    }

    // ===== 列表 =====
    private ObservableCollection<SlashCommandItem> _commands = new();
    public ObservableCollection<SlashCommandItem> Commands
    {
        get => _commands;
        set { _commands = value; OnPropertyChanged(); }
    }

    private SlashCommandItem? _selectedCommand;
    public SlashCommandItem? SelectedCommand
    {
        get => _selectedCommand;
        set { _selectedCommand = value; OnPropertyChanged(); }
    }

    // ===== 编辑状态 =====
    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotEditing)); }
    }
    public bool IsNotEditing => !_isEditing;

    private string _editCommand = string.Empty;
    public string EditCommand { get => _editCommand; set { _editCommand = value; OnPropertyChanged(); } }

    private string _editLabel = string.Empty;
    public string EditLabel { get => _editLabel; set { _editLabel = value; OnPropertyChanged(); } }

    private string _editIcon = "⚡";
    public string EditIcon { get => _editIcon; set { _editIcon = value; OnPropertyChanged(); } }

    private string _editCategory = "自定义";
    public string EditCategory { get => _editCategory; set { _editCategory = value; OnPropertyChanged(); } }

    private string _editDescription = string.Empty;
    public string EditDescription { get => _editDescription; set { _editDescription = value; OnPropertyChanged(); } }

    private string? _editingId;

    // ===== 命令 =====
    public ICommand AddCommand { get; }
    public ICommand EditCmdCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand ResetCommand { get; }

    private void RefreshList()
    {
        Commands = new ObservableCollection<SlashCommandItem>(_service.GetEditableList());
    }

    private void StartAdd()
    {
        _editingId = null;
        EditCommand = string.Empty;
        EditLabel = string.Empty;
        EditIcon = "⚡";
        EditCategory = "自定义";
        EditDescription = string.Empty;
        IsEditing = true;
    }

    private void StartEdit(object? param)
    {
        if (SelectedCommand == null) return;
        var item = SelectedCommand;
        _editingId = item.Id;
        EditCommand = item.Command;
        EditLabel = item.Label;
        EditIcon = item.Icon;
        EditCategory = item.Category;
        EditDescription = item.Description;
        IsEditing = true;
    }

    private void Save()
    {
        var cmd = EditCommand.Trim();
        if (string.IsNullOrEmpty(cmd) || !cmd.StartsWith("/"))
        {
            System.Windows.MessageBox.Show("命令必须以 / 开头", "格式错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var item = new SlashCommandItem
        {
            Id = _editingId ?? string.Empty,
            Command = cmd,
            Label = EditLabel.Trim(),
            Icon = EditIcon.Trim(),
            Category = EditCategory.Trim(),
            Description = EditDescription.Trim()
        };

        if (string.IsNullOrEmpty(_editingId))
            _service.Add(item);
        else
            _service.Update(item);

        IsEditing = false;
        RefreshList();
    }

    private void CancelEdit()
    {
        IsEditing = false;
    }

    private void Delete(object? param)
    {
        if (SelectedCommand == null) return;
        if (System.Windows.MessageBox.Show($"确定删除命令 \"{SelectedCommand.Command}\"？", "确认删除",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
            == System.Windows.MessageBoxResult.Yes)
        {
            _service.Delete(SelectedCommand.Id);
            RefreshList();
        }
    }

    private void ResetToDefaults()
    {
        if (System.Windows.MessageBox.Show("将恢复为默认命令列表，自定义命令会丢失。确定？", "恢复默认",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
            == System.Windows.MessageBoxResult.Yes)
        {
            _service.ResetToDefaults();
            RefreshList();
        }
    }

    // ===== INotifyPropertyChanged =====
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
