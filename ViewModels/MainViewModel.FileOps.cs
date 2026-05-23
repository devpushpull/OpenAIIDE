using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using AIIDEWPF.Models;
using AIIDEWPF.Services;
using AIIDEWPF.Views;
using Microsoft.Win32;

namespace AIIDEWPF.ViewModels;

public partial class MainViewModel
{
    public void NewFile()
    {
        // 在项目目录（若有）或当前目录下创建新文件
        var dir = !string.IsNullOrEmpty(FileTree.ProjectPath)
            ? FileTree.ProjectPath
            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var baseName = "新建文件.txt";
        var filePath = System.IO.Path.Combine(dir, baseName);

        // 重名检测
        if (System.IO.File.Exists(filePath) || System.IO.Directory.Exists(filePath))
            filePath = ResolveNameConflict(dir, baseName, false);
        if (string.IsNullOrEmpty(filePath)) return; // 用户取消

        try
        {
            System.IO.File.WriteAllText(filePath, "");
            if (_projectConfig.IsLoaded)
                _projectConfig.RecordFileChange(
                    System.IO.Path.GetRelativePath(FileTree.ProjectPath, filePath), "created");
            if (!string.IsNullOrEmpty(FileTree.ProjectPath))
                RefreshFileTree(FileTree.ProjectPath);
            // 打开新文件
            OpenFile(filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>关闭当前打开的文件（Ctrl+W）</summary>
    public void CloseFile()
    {
        Editor.CurrentFile = null;
        Editor.CurrentContent = string.Empty;
        Editor.IsDirty = false;
    }

    /// <summary>关闭文件夹：恢复文件树默认状态，清除所有标签和编辑器内容</summary>
    public void CloseFolder()
    {
        // 停止文件监视
        StopFileWatcher();

        // 清除所有编辑器标签
        Editor.Tabs.Clear();
        CloseFile();

        // 恢复文件树默认状态
        FileTree.ProjectPath = string.Empty;
        FileTree.ProjectName = "无项目";
        FileTree.FileCount = 0;
        FileTree.RootItems = new ObservableCollection<FileItem>();

        // 清除 AI 状态
        Chat.AIStatus = "AI: 就绪";
        Chat.CurrentFiles.Clear();

        LogService.Instance.Info("已关闭文件夹");
    }

    /// <summary>启动文件系统监视器，自动感知文件变化并刷新文件树</summary>
    private void StartFileWatcher(string root)
    {
        StopFileWatcher();
        try
        {
            _fileWatcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
                InternalBufferSize = 65536
            };
            // 过滤掉临时文件、隐藏文件和 node_modules/bin/obj
            _fileWatcher.Filter = "";

            _fileWatcher.Created += (s, e) => DebounceRefreshTree();
            _fileWatcher.Deleted += (s, e) => DebounceRefreshTree();
            _fileWatcher.Renamed += (s, e) => DebounceRefreshTree();
            _fileWatcher.Changed += (s, e) =>
            {
                // 忽略临时文件、备份文件的变更
                var name = Path.GetFileName(e.FullPath);
                if (name.StartsWith('.') || name.EndsWith(".tmp") || name.EndsWith(".bak")) return;
                DebounceRefreshTree();
            };

            LogService.Instance.Info("Started file watcher");
        }
        catch (Exception ex)
        {
            LogService.Instance.Debug($"File watcher start failed: {ex.Message}");
        }
    }

    private void StopFileWatcher()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    /// <summary>防抖刷新：500ms 内多次文件变化合并为一次刷新</summary>
    private void DebounceRefreshTree()
    {
        if (_isRefreshingTree) return;
        _isRefreshingTree = true;
        System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _isRefreshingTree = false;
                if (!string.IsNullOrEmpty(FileTree.ProjectPath))
                    RefreshFileTree(FileTree.ProjectPath);
            });
        });
    }

    public void NewFolder()
    {
        if (string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            MessageBox.Show("请先打开一个项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Generate folder name with conflict detection
        var baseName = "新建文件夹";
        var folderPath = Path.Combine(FileTree.ProjectPath, baseName);

        // 重名检测
        if (Directory.Exists(folderPath) || System.IO.File.Exists(folderPath))
            folderPath = ResolveNameConflict(FileTree.ProjectPath, baseName, true);
        if (string.IsNullOrEmpty(folderPath)) return; // 用户取消

        try
        {
            Directory.CreateDirectory(folderPath);
            _projectConfig.RecordFileChange(
                Path.GetRelativePath(FileTree.ProjectPath, folderPath), "created");
            RefreshFileTree(FileTree.ProjectPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建文件夹失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>重名检测：弹窗让用户修改，5秒无操作自动加后缀</summary>
    public string ResolveNameConflict(string directory, string baseName, bool isDirectory)
    {
        var dlg = new Views.NameConflictDialog(baseName, directory, isDirectory);
        dlg.ShowDialog();
        if (string.IsNullOrEmpty(dlg.ResultName))
            return string.Empty; // 用户取消
        return System.IO.Path.Combine(directory, dlg.ResultName);
    }

    public void OpenFile(string? path = null)
    {
        if (path == null)
        {
            var dlg = new OpenFileDialog { Title = "打开文件", Filter = "所有文件 (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }

        try
        {
            var content = _fileService.ReadFile(path);
            Editor.CurrentFile = path;
            Editor.FileType = _fileService.GetLanguageFromExtension(path);
            Editor.IsDirty = false;
            Editor.CurrentContent = content;

            // 更新所有标签的 IsActive 状态
            foreach (var t in Editor.Tabs)
                t.IsActive = false;

            // Update tabs: 切换到已有标签或新增
            var existing = Editor.Tabs.FirstOrDefault(t =>
                string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var newTab = new EditorTab { FilePath = path, FileName = System.IO.Path.GetFileName(path), IsActive = true };
                Editor.Tabs.Add(newTab);
            }
            else
            {
                existing.IsActive = true;
            }
            // 触发文件树自动定位
            FileOpenedInTree?.Invoke(path);
            // 记录到项目配置
            if (_projectConfig.IsLoaded)
                _projectConfig.SetOpenFiles(new() { path });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void SaveFile(bool saveAs)
    {
        var content = Editor.CurrentContent;
        var path = Editor.CurrentFile;
        var isNew = string.IsNullOrEmpty(path);

        if (saveAs || isNew)
        {
            var dlg = new SaveFileDialog { Title = "保存文件", Filter = "所有文件 (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }

        try
        {
            _fileService.SaveFile(path, content);
            Editor.CurrentFile = path;
            Editor.IsDirty = false;
            // 记录变更并刷新文件树
            if (_projectConfig.IsLoaded)
                _projectConfig.RecordFileChange(
                    System.IO.Path.GetRelativePath(FileTree.ProjectPath, path),
                    isNew ? "created" : "modified");
            if (!string.IsNullOrEmpty(FileTree.ProjectPath))
                RefreshFileTree(FileTree.ProjectPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenProject()
    {
        var dlg = new OpenFolderDialog { Title = "打开项目目录" };
        if (dlg.ShowDialog() != true) return;

        var root = dlg.FolderName;
        _aiService.SetProjectPath(root);
        _debugService.SetProjectPath(root);
        _atMentionService.SetProjectPath(root);
        _gitService.SetRepoPath(root);
        _slashCommandService.SetProjectPath(root);
        _codeClipDetector?.SetProjectPath(root);
        // 加载持久化的 Git 配置
        _gitConfig = GitConfigStore.Load(root);
        _config.AddRecentProject(root);
        // 检查/创建项目配置文件
        _projectConfig.LoadOrCreate(root);
        RefreshFileTree(root);
        RefreshAlgorithmList();
        BuildLanguage = _aiService.BuildSvc.DetectLanguage();
        RefreshGitStatus();
        _backup = new BackupService(root);
        _aiService.SetBackupService(_backup);
        _fileCompare = new FileCompareService(_backup);

        // 初始化技能和钩子服务（项目级）
        _skillService = new SkillService(root);
        _hooksService = new HooksService(root);
        _aiService.SetHooksService(_hooksService);
        _aiService.FileOps.SetHooksService(_hooksService);
        // 更新 AI 提示词中的技能列表
        var skillsPrompt = _skillService.GetAllSkillsPrompt();
        _promptService.SetSkillsSection(skillsPrompt);

        _maintenanceService = new SelfMaintenanceService(root);
        LogService.Instance.Info($"打开项目: {root}");
        // 启动文件系统监视，自动感知文件变化
        StartFileWatcher(root);

        // 异步构建代码索引
        _codeIndexService = new CodeIndexService();
        _ = _codeIndexService.BuildAsync(root);
    }

    public void RefreshFileTree(string root)
    {
        var items = new List<FileInfoModel>();
        GatherFileSystem(root, root, items);
        FileTree.ProjectName = System.IO.Path.GetFileName(root);
        FileTree.ProjectPath = root;
        FileTree.FileCount = items.Count(i => !i.IsDirectory);
        FileTree.BuildTree(items);

        // 填充 Git 状态
        ApplyGitStatusToTree();
    }

    /// <summary>将 Git 变更状态应用到文件树</summary>
    private void ApplyGitStatusToTree()
    {
        if (!_gitService.IsGitRepo()) return;
        var changedFiles = _gitService.GetChangedFiles();
        if (changedFiles.Count == 0) return;

        var statusMap = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, status) in changedFiles)
        {
            var normalized = file.Replace('/', System.IO.Path.DirectorySeparatorChar);
            // git status 返回两个字符状态，如 " M" 或 "??", 取最后一个非空格字符
            var ch = status.Length > 0 ? status[^1] : ' ';
            statusMap[normalized] = ch;
        }

        SetGitStatusRecursive(FileTree.RootItems, statusMap);
    }

    private static void SetGitStatusRecursive(IEnumerable<FileItem> items, Dictionary<string, char> statusMap)
    {
        foreach (var item in items)
        {
            if (statusMap.TryGetValue(item.RelativePath, out var status))
                item.GitStatus = status;
            if (item.Children.Count > 0)
                SetGitStatusRecursive(item.Children, statusMap);
        }
    }

    /// <summary>递归收集文件和目录，所有目录节点都加入列表以构建完整层级树</summary>
    private void GatherFileSystem(string basePath, string currentDir, List<FileInfoModel> items)
    {
        try
        {
            // 先加入当前目录本身（根目录除外），确保 BuildTree 能构建完整层级
            if (currentDir != basePath)
            {
                var dirRelPath = System.IO.Path.GetRelativePath(basePath, currentDir);
                items.Add(new FileInfoModel { FullPath = currentDir, RelativePath = dirRelPath, IsDirectory = true });
            }

            foreach (var entry in System.IO.Directory.GetFileSystemEntries(currentDir))
            {
                var name = System.IO.Path.GetFileName(entry);
                if (name.StartsWith('.') || name == "node_modules" || name == "bin" || name == "obj") continue;

                if (System.IO.Directory.Exists(entry))
                {
                    GatherFileSystem(basePath, entry, items);
                }
                else
                {
                    var relPath = System.IO.Path.GetRelativePath(basePath, entry);
                    items.Add(new FileInfoModel { FullPath = entry, RelativePath = relPath, Size = new System.IO.FileInfo(entry).Length, IsDirectory = false });
                }
            }
        }
        catch { }
    }

    /// <summary>刷新算法库面板</summary>
    private void RefreshAlgorithmList()
    {
        var algorithms = _aiService.GetAlgorithms();
        Chat.Algorithms.Clear();
        foreach (var alg in algorithms)
        {
            Chat.Algorithms.Add(new AlgorithmDisplayItem
            {
                Id = alg.Id,
                Name = alg.Name,
                Language = alg.Language,
                Category = alg.Category,
                Complexity = alg.Complexity,
                Description = alg.Description,
                LineCount = alg.LineCount
            });
        }
    }
}
