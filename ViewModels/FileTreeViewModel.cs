using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using AIIDEWPF.Models;

namespace AIIDEWPF.ViewModels;

public class FileTreeViewModel : INotifyPropertyChanged
{
    private ObservableCollection<FileItem> _rootItems = new();
    private string _projectName = "无项目";
    private string _projectPath = string.Empty;
    private int _fileCount;

    public ObservableCollection<FileItem> RootItems { get => _rootItems; set { _rootItems = value; OnPropertyChanged(); } }
    public string ProjectName { get => _projectName; set { _projectName = value; OnPropertyChanged(); } }
    public string ProjectPath { get => _projectPath; set { _projectPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsProjectOpen)); } }
    public int FileCount { get => _fileCount; set { _fileCount = value; OnPropertyChanged(); } }
    public bool IsProjectOpen => !string.IsNullOrEmpty(_projectPath);

    public event Action<string>? FileSelected;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void BuildTree(IEnumerable<FileInfoModel> files)
    {
        // 创建新的集合实例以确保 WPF 绑定检测到变更并刷新 TreeView
        var newItems = new ObservableCollection<FileItem>();
        var tree = new Dictionary<string, FileItem>();
        foreach (var f in files)
        {
            var parts = f.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = tree;
            FileItem? parent = null;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == parts.Length - 1)
                {
                    var item = new FileItem { Name = parts[i], FullPath = f.FullPath, RelativePath = f.RelativePath, IsDirectory = f.IsDirectory };
                    if (parent != null) parent.Children.Add(item);
                    else newItems.Add(item);
                }
                else
                {
                    if (!current.TryGetValue(parts[i], out var dir))
                    {
                        current[parts[i]] = dir = new FileItem { Name = parts[i], IsDirectory = true };
                    }
                    parent = dir;
                    current = dir.Children.ToDictionary(c => c.Name);
                }
            }
        }
        // 用新集合替换旧集合，触发 CollectionChanged → TreeView 完全重建
        RootItems = newItems;
    }
}

public class FileInfoModel
{
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
}
