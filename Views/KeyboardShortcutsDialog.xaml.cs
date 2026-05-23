using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AIIDEWPF.Models;

namespace AIIDEWPF.Views;

public partial class KeyboardShortcutsDialog : Window
{
    public ObservableCollection<ShortcutRow> Shortcuts { get; } = new();

    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
        DataContext = this;
        LoadShortcuts();
    }

    private void LoadShortcuts()
    {
        var config = KeyboardShortcutsConfig.CreateDefault();
        var groups = config.Shortcuts.GroupBy(s => s.Category);

        foreach (var group in groups)
        {
            // 分类标题
            Shortcuts.Add(new ShortcutRow
            {
                Category = group.Key,
                IsCategoryHeader = true
            });

            // 分类下的快捷键
            foreach (var shortcut in group)
            {
                var keyStr = string.IsNullOrEmpty(shortcut.Modifiers)
                    ? shortcut.Key
                    : $"{shortcut.Modifiers}+{shortcut.Key}";
                Shortcuts.Add(new ShortcutRow
                {
                    Name = shortcut.Name,
                    ShortcutKey = keyStr,
                    IsItem = true
                });
            }
        }

        ShortcutsList.ItemsSource = Shortcuts;
    }
}

/// <summary>快捷键展示行</summary>
public class ShortcutRow : INotifyPropertyChanged
{
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortcutKey { get; set; } = "";
    public bool IsCategoryHeader { get; set; }
    public bool IsItem { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
