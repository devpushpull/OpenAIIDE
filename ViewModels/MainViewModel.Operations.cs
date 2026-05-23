using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AIIDEWPF.Models;
using AIIDEWPF.Services;
using AIIDEWPF.Views;
using Microsoft.Win32;

namespace AIIDEWPF.ViewModels;

public partial class MainViewModel
{
    /// <summary>尝试从 checkpoint 恢复单个文件的变更（公开方法，供 UI 侧调用）</summary>
    public bool TryRestoreFromCheckpoint(string checkpointId)
    {
        try
        {
            if (_backup == null)
            {
                LogService.Instance.Warn("BackupService 未初始化，无法恢复", "Restore");
                return false;
            }
            return _backup.RestoreCheckpoint(checkpointId);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, $"从 checkpoint {checkpointId} 恢复");
            return false;
        }
    }

    /// <summary>更新上下文用量显示</summary>
    private void UpdateContextUsage()
    {
        var history = _aiService.GetHistory();
        var tokens = ConversationCompressor.EstimateTokens(history);

        // 根据当前模型确定最大 context size
        var modelFamily = TokenCounterService.DetectFamily(_modelManager.ActiveModel?.Name ?? "");
        int maxTokens = modelFamily switch
        {
            TokenCounterService.ModelFamily.DeepSeek => 200000, // DeepSeek 支持 128K/200K
            TokenCounterService.ModelFamily.OpenAI => 128000,
            TokenCounterService.ModelFamily.Claude => 200000,
            _ => 200000
        };

        Chat.UpdateContextUsage(tokens, maxTokens);
    }

    // 注意：CodeEditor 引用由 WorkspaceView 设置
    internal ICSharpCode.AvalonEdit.TextEditor? CodeEditor { get; set; }

    // ========== 面板功能方法 ==========

    /// <summary>刷新 Git 面板的变更文件列表</summary>
    private void RefreshGitChangesPanel()
    {
        _gitChanges.Clear();
        _gitStagedChanges.Clear();
        _gitUntrackedChanges.Clear();

        if (!_gitService.IsGitRepo()) return;

        var statusOutput = _gitService.Status();
        if (!statusOutput.Success || string.IsNullOrEmpty(statusOutput.Output)) return;

        foreach (var line in statusOutput.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;
            var statusCode = line[..2];
            var filePath = line[3..].Trim();
            if (string.IsNullOrEmpty(filePath)) continue;

            var item = new GitChangeDisplayItem
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                Status = statusCode
            };

            // 分类：暂存 / 未暂存 / 未跟踪
            if (statusCode.Contains('?'))
                _gitUntrackedChanges.Add(item);
            else if (statusCode[0] != ' ')
                _gitStagedChanges.Add(item);
            else
                _gitChanges.Add(item);
        }

        OnPropertyChanged(nameof(HasGitChanges));
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(HasUntrackedChanges));
        OnPropertyChanged(nameof(IsGitClean));
    }

    /// <summary>刷新已安装插件列表</summary>
    private void RefreshInstalledPlugins()
    {
        _installedPlugins.Clear();
        _pluginService ??= new PluginService(FileTree.ProjectPath);
        foreach (var p in _pluginService.ScanPlugins())
            _installedPlugins.Add(p);
        OnPropertyChanged(nameof(HasInstalledPlugins));
        OnPropertyChanged(nameof(InstalledPluginSummary));

        // 检查插件更新状态
        CheckPluginUpdateStatus();
    }

    /// <summary>检查插件更新状态（基于本地已下载的更新包）</summary>
    private void CheckPluginUpdateStatus()
    {
        if (_pluginService == null) return;
        var pluginDir = _pluginService.GetPluginDir();
        if (!Directory.Exists(pluginDir)) return;

        // 检查是否有待安装的更新（.update 标记文件）
        int updateCount = 0;
        bool needRestart = false;
        foreach (var dir in Directory.GetDirectories(pluginDir))
        {
            var updateMarker = Path.Combine(dir, ".update_pending");
            if (File.Exists(updateMarker))
            {
                updateCount++;
                needRestart = true;
            }
        }

        PluginUpdateCount = updateCount;
        PluginNeedRestart = needRestart;
        PluginUpdateChecked = true;
    }

    /// <summary>标记插件需要重启生效（由 PluginManager 安装/更新后调用）</summary>
    public void MarkPluginNeedsRestart(string pluginId)
    {
        _pluginService ??= new PluginService(FileTree.ProjectPath);
        var pluginDir = _pluginService.GetPluginDir();
        var dir = Path.Combine(pluginDir, pluginId);
        if (Directory.Exists(dir))
        {
            File.WriteAllText(Path.Combine(dir, ".update_pending"), DateTime.Now.ToString("O"));
            PluginNeedRestart = true;
            PluginUpdateCount = Math.Max(1, PluginUpdateCount + 1);
            PluginUpdateChecked = true;
        }
    }

    /// <summary>清除重启标记（应用重启后调用）</summary>
    public void ClearPluginRestartFlags()
    {
        _pluginService ??= new PluginService(FileTree.ProjectPath);
        var pluginDir = _pluginService.GetPluginDir();
        if (!Directory.Exists(pluginDir)) return;
        foreach (var dir in Directory.GetDirectories(pluginDir))
        {
            var marker = Path.Combine(dir, ".update_pending");
            if (File.Exists(marker)) File.Delete(marker);
        }
        PluginUpdateCount = 0;
        PluginNeedRestart = false;
        PluginUpdateChecked = true;
    }

    /// <summary>刷新 Wiki 记忆列表</summary>
    private void RefreshWikiMemories()
    {
        _wikiMemories.Clear();
        if (MemorySvc == null) return;

        var workspacePath = !string.IsNullOrEmpty(FileTree.ProjectPath) ? FileTree.ProjectPath : null;
        List<MemoryItem> memories;

        if (!string.IsNullOrEmpty(_wikiSearchQuery))
            memories = MemorySvc.Search(_wikiSearchQuery, workspacePath);
        else
            memories = MemorySvc.GetAll(workspacePath);

        // 也加入会话记忆
        memories.AddRange(MemorySvc.GetSessionMemories());

        foreach (var m in memories)
            _wikiMemories.Add(new WikiMemoryDisplay
            {
                Id = m.Id,
                Title = m.Title,
                Content = m.Content,
                Category = m.Category,
                Scope = m.Scope,
                UpdatedAt = m.UpdatedAt
            });

        OnPropertyChanged(nameof(HasWikiMemories));
    }

    /// <summary>添加 Wiki 记忆</summary>
    private void AddWikiMemory()
    {
        if (string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            Chat.AddMessage("system", "💡 Repo Wiki 需要打开项目后使用。\n在对话中让 AI 记住信息即可自动添加。");
            return;
        }

        // 简易输入对话框
        var dialog = new Window
        {
            Title = "Repo Wiki - 添加记忆",
            Width = 400, Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)),
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(16) };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        var titleLabel = new System.Windows.Controls.TextBlock { Text = "记忆标题：", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12, Margin = new System.Windows.Thickness(0, 0, 0, 2) };
        var titleBox = new System.Windows.Controls.TextBox { Height = 28, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)), FontSize = 12, BorderThickness = new System.Windows.Thickness(0), Padding = new System.Windows.Thickness(8, 4, 8, 4) };
        System.Windows.Controls.Grid.SetRow(titleLabel, 0);
        System.Windows.Controls.Grid.SetRow(titleBox, 0);
        var titleStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
        titleStack.Children.Add(titleLabel);
        titleStack.Children.Add(titleBox);
        grid.Children.Add(titleStack);

        var contentLabel = new System.Windows.Controls.TextBlock { Text = "记忆内容：", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12, Margin = new System.Windows.Thickness(0, 8, 0, 2) };
        var contentBox = new System.Windows.Controls.TextBox { Height = 56, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)), FontSize = 12, BorderThickness = new System.Windows.Thickness(0), Padding = new System.Windows.Thickness(8, 4, 8, 4), TextWrapping = System.Windows.TextWrapping.Wrap, AcceptsReturn = true };
        var contentStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        contentStack.Children.Add(contentLabel);
        contentStack.Children.Add(contentBox);
        System.Windows.Controls.Grid.SetRow(contentStack, 1);
        grid.Children.Add(contentStack);

        var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new System.Windows.Thickness(0, 12, 0, 0) };
        var saveBtn = new System.Windows.Controls.Button { Content = "💾 保存", Width = 80, Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x5a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xa0, 0xd0, 0xa0)), BorderThickness = new System.Windows.Thickness(0), FontSize = 12 };
        var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Width = 60, Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4a, 0x4a, 0x4a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xaa, 0xaa, 0xaa)), BorderThickness = new System.Windows.Thickness(0), FontSize = 12, Margin = new System.Windows.Thickness(8, 0, 0, 0) };
        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        System.Windows.Controls.Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        saveBtn.Click += (_, _) =>
        {
            var title = titleBox.Text.Trim();
            var content = contentBox.Text.Trim();
            if (!string.IsNullOrEmpty(title))
            {
                MemorySvc?.Add(title, string.IsNullOrEmpty(content) ? title : content, "user_preferences", "project", FileTree.ProjectPath);
                Chat.AddMessage("system", $"✅ 已添加记忆：{title}");
                RefreshWikiMemories();
            }
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.Content = grid;
        dialog.ShowDialog();
    }

    /// <summary>清除会话记忆</summary>
    private void ClearWikiSession()
    {
        MemorySvc?.ClearSession();
        Chat.AddMessage("system", "🗑 会话记忆已清除");
        RefreshWikiMemories();
    }

    /// <summary>显示添加远程连接对话框</summary>
    private void ShowAddRemoteConnectionDialog()
    {
        var dialog = new Window
        {
            Title = "➕ 新建远程连接",
            Width = 420, Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)),
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(16) };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        // Type selector
        var typePanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 0, 0, 12) };
        var typeLabel = new System.Windows.Controls.TextBlock { Text = "连接类型：", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new System.Windows.Thickness(0, 0, 8, 0) };
        var typeCombo = new System.Windows.Controls.ComboBox { Width = 180, Height = 28, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)), FontSize = 12 };
        typeCombo.Items.Add("🖥 SSH 远程主机");
        typeCombo.Items.Add("🐧 WSL 本地发行版");
        typeCombo.Items.Add("📦 Dev Container");
        typeCombo.SelectedIndex = 0;
        typePanel.Children.Add(typeLabel);
        typePanel.Children.Add(typeCombo);
        System.Windows.Controls.Grid.SetRow(typePanel, 0);
        grid.Children.Add(typePanel);

        // Display name
        var nameLabel = new System.Windows.Controls.TextBlock { Text = "显示名称：", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12, Margin = new System.Windows.Thickness(0, 0, 0, 2) };
        var nameBox = new System.Windows.Controls.TextBox { Height = 28, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)), FontSize = 12, BorderThickness = new System.Windows.Thickness(0), Padding = new System.Windows.Thickness(8, 4, 8, 4), Margin = new System.Windows.Thickness(0, 0, 0, 8) };
        System.Windows.Controls.Grid.SetRow(nameLabel, 1);
        System.Windows.Controls.Grid.SetRow(nameBox, 1);
        var nameStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        nameStack.Children.Add(nameLabel);
        nameStack.Children.Add(nameBox);
        System.Windows.Controls.Grid.SetRow(nameStack, 1);
        grid.Children.Add(nameStack);

        // Host/Port (SSH)
        var hostLabel = new System.Windows.Controls.TextBlock { Text = "主机地址：端口", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12, Margin = new System.Windows.Thickness(0, 0, 0, 2) };
        var hostGrid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
        hostGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        hostGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        var hostBox = new System.Windows.Controls.TextBox { Height = 28, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)), FontSize = 12, BorderThickness = new System.Windows.Thickness(0), Padding = new System.Windows.Thickness(8, 4, 8, 4), Text = "example.com" };
        var portBox = new System.Windows.Controls.TextBox { Width = 60, Height = 28, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)), FontSize = 12, BorderThickness = new System.Windows.Thickness(0), Padding = new System.Windows.Thickness(8, 4, 8, 4), Text = "22", Margin = new System.Windows.Thickness(6, 0, 0, 0) };
        System.Windows.Controls.Grid.SetColumn(hostBox, 0);
        System.Windows.Controls.Grid.SetColumn(portBox, 1);
        hostGrid.Children.Add(hostBox);
        hostGrid.Children.Add(portBox);
        var hostStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        hostStack.Children.Add(hostLabel);
        hostStack.Children.Add(hostGrid);
        System.Windows.Controls.Grid.SetRow(hostStack, 2);
        grid.Children.Add(hostStack);

        // Username
        var userLabel = new System.Windows.Controls.TextBlock { Text = "用户名：", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12, Margin = new System.Windows.Thickness(0, 0, 0, 2) };
        var userBox = new System.Windows.Controls.TextBox { Height = 28, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd4, 0xd4, 0xd4)), FontSize = 12, BorderThickness = new System.Windows.Thickness(0), Padding = new System.Windows.Thickness(8, 4, 8, 4), Text = "root" };
        var userStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        userStack.Children.Add(userLabel);
        userStack.Children.Add(userBox);
        System.Windows.Controls.Grid.SetRow(userStack, 3);
        grid.Children.Add(userStack);

        // Buttons
        var btnPanel2 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new System.Windows.Thickness(0, 16, 0, 0) };
        var saveBtn2 = new System.Windows.Controls.Button { Content = "💾 保存", Width = 80, Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3a, 0x5a, 0x7a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xa0, 0xc0, 0xe0)), BorderThickness = new System.Windows.Thickness(0), FontSize = 12 };
        var cancelBtn2 = new System.Windows.Controls.Button { Content = "取消", Width = 60, Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4a, 0x4a, 0x4a)), Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xaa, 0xaa, 0xaa)), BorderThickness = new System.Windows.Thickness(0), FontSize = 12, Margin = new System.Windows.Thickness(8, 0, 0, 0) };
        btnPanel2.Children.Add(saveBtn2);
        btnPanel2.Children.Add(cancelBtn2);
        System.Windows.Controls.Grid.SetRow(btnPanel2, 5);
        grid.Children.Add(btnPanel2);

        saveBtn2.Click += (_, _) =>
        {
            var connType = typeCombo.SelectedIndex switch { 1 => "wsl", 2 => "devcontainer", _ => "ssh" };
            var displayName = nameBox.Text.Trim();
            var host = hostBox.Text.Trim();
            int.TryParse(portBox.Text.Trim(), out var port);
            if (port <= 0) port = 22;
            var username = userBox.Text.Trim();

            if (string.IsNullOrEmpty(displayName)) displayName = host;

            var item = new RemoteConnectionItem
            {
                Type = connType,
                DisplayName = string.IsNullOrEmpty(displayName) ? host : displayName,
                Host = host,
                Port = port,
                Username = username
            };

            if (connType == "ssh" && !string.IsNullOrEmpty(host))
                _sshConnections.Add(item);
            else if (connType == "wsl")
                _wslConnections.Add(item);
            else if (connType == "devcontainer")
                _devContainerConnections.Add(item);

            RefreshRemoteConnectionBindings();
            dialog.Close();
        };

        cancelBtn2.Click += (_, _) => dialog.Close();

        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private void RefreshRemoteConnectionBindings()
    {
        OnPropertyChanged(nameof(HasSshConnections));
        OnPropertyChanged(nameof(HasWslConnections));
        OnPropertyChanged(nameof(HasDevContainerConnections));
    }

    /// <summary>刷新远程连接（扫描 WSL 和 DevContainer）</summary>
    private async void RefreshRemoteConnections()
    {
        // 扫描 WSL 发行版 —— 在后台执行避免阻塞 UI
        _wslConnections.Clear();
        try
        {
            var wslLines = await Task.Run(() =>
            {
                var lines = new List<string>();
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("wsl", "-l -q")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    if (p == null) return lines;
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    foreach (var distro in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = distro.Trim();
                        if (!string.IsNullOrEmpty(name) && !name.Contains('\0'))
                            lines.Add(name);
                    }
                }
                catch { /* WSL not installed */ }
                return lines;
            });

            foreach (var name in wslLines)
                _wslConnections.Add(new RemoteConnectionItem
                {
                    Type = "wsl",
                    DisplayName = name,
                    Distribution = name
                });
        }
        catch { /* WSL not installed */ }

        // 扫描 .devcontainer 配置
        _devContainerConnections.Clear();
        if (!string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            var devContainerPath = Path.Combine(FileTree.ProjectPath, ".devcontainer", "devcontainer.json");
            if (File.Exists(devContainerPath))
                _devContainerConnections.Add(new RemoteConnectionItem
                {
                    Type = "devcontainer",
                    DisplayName = Path.GetFileName(FileTree.ProjectPath),
                    ConfigPath = devContainerPath
                });

            // also check for .devcontainer in parent directories
            var parentDevContainer = Path.Combine(FileTree.ProjectPath, "..", ".devcontainer", "devcontainer.json");
            if (File.Exists(parentDevContainer))
                _devContainerConnections.Add(new RemoteConnectionItem
                {
                    Type = "devcontainer",
                    DisplayName = Path.GetFileName(Path.GetDirectoryName(parentDevContainer)!),
                    ConfigPath = Path.GetFullPath(parentDevContainer)
                });
        }

        RefreshRemoteConnectionBindings();
    }

    /// <summary>刷新 Docker 容器列表</summary>
    private async void RefreshDockerContainers()
    {
        _runningContainers.Clear();
        _stoppedContainers.Clear();

        try
        {
            var containers = await Task.Run(() =>
            {
                var list = new List<DockerContainerItem>();
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("docker", "ps -a --format \"{{.ID}}|{{.Names}}|{{.Image}}|{{.Status}}|{{.Ports}}\"")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    if (p == null) return list;
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);

                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|', 5);
                        if (parts.Length < 4) continue;
                        list.Add(new DockerContainerItem
                        {
                            ContainerId = parts[0].Trim(),
                            ContainerName = parts[1].Trim(),
                            Image = parts[2].Trim(),
                            Status = parts[3].Trim(),
                            Ports = parts.Length > 4 ? parts[4].Trim() : ""
                        });
                    }
                }
                catch { /* docker not available */ }
                return list;
            });

            foreach (var c in containers)
            {
                if (c.IsRunning)
                    _runningContainers.Add(c);
                else
                    _stoppedContainers.Add(c);
            }
        }
        catch
        {
            // Docker not available - leave empty lists
        }

        OnPropertyChanged(nameof(HasRunningContainers));
        OnPropertyChanged(nameof(HasStoppedContainers));
        OnPropertyChanged(nameof(HasAnyContainers));
    }
}
