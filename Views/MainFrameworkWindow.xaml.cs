using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIIDEWPF.Services;
using AIIDEWPF.ViewModels;

namespace AIIDEWPF.Views;

public partial class MainFrameworkWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DatabaseService _db;
    private readonly AuthService _auth;
    private bool _isCheckingUpdate; // 防止重复弹窗

    public MainFrameworkWindow(DatabaseService db, AuthService auth)
    {
        InitializeComponent();
        _db = db;
        _auth = auth;

        // 创建 ViewModel 单例
        _viewModel = new MainViewModel(_db, _auth);
        DataContext = _viewModel;

        // 直接显示工作区
        PageHost.Content = new WorkspaceView(_viewModel);

        // 更新头像显示
        RefreshAvatar();
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsLoggedIn) ||
                e.PropertyName == nameof(MainViewModel.CurrentUsername))
                RefreshAvatar();
        };

        // ===== 生命周期事件 =====
        Loaded += async (s, e) =>
        {
            LogService.Instance.Info("主框架窗口已加载", "UI");
            _ = _viewModel.Network.CheckConnectivityAsync();
            await _viewModel.TryRestoreLastProjectAsync();
            await _viewModel.CheckAndOfferRecoveryAsync();
        };
        Closing += (s, e) =>
        {
            _viewModel.SaveProjectState();
            LogService.Instance.Info("应用关闭");
        };
        Activated += (s, e) =>
        {
            _viewModel.OnAppReactivated();
            Title = _viewModel.WindowTitle;
            CheckSessionExpiry();
        };
        Deactivated += (s, e) =>
        {
            _viewModel.OnAppDeactivated();
            if (_viewModel.Chat.IsStreaming || _viewModel.BackgroundTaskCount > 0)
                Title = $"◉ {_viewModel.WindowTitle} (后台运行中)";
        };
    }

    private void RefreshAvatar()
    {
        if (_viewModel.IsLoggedIn)
        {
            var name = _viewModel.CurrentUsername;
            AvatarText.Text = name.Length > 0 ? name[0].ToString().ToUpper() : "👤";
            UserAvatarBtn.ToolTip = $"用户: {name}\n点击管理个人资料";
        }
        else
        {
            AvatarText.Text = "👤";
            UserAvatarBtn.ToolTip = "未登录 - 点击登录";
        }
    }

    // ===== 用户头像菜单 =====
    private void UserAvatarBtn_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d)), Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4)) };

        if (_viewModel.IsLoggedIn)
        {
            var profileItem = new MenuItem { Header = $"👤 {_viewModel.CurrentUsername} (个人资料设置)" };
            profileItem.Click += (s, args) => _viewModel.ShowSettingsCommand.Execute(null);
            menu.Items.Add(profileItem);
            menu.Items.Add(new Separator());
        }

        var aiSettingsItem = new MenuItem { Header = "⚙ AI IDE 设置" };
        aiSettingsItem.Click += (s, args) => _viewModel.ShowSettingsCommand.Execute(null);
        menu.Items.Add(aiSettingsItem);

        var editorSettingsItem = new MenuItem { Header = "📝 编辑器设置" };
        editorSettingsItem.Click += (s, args) => _viewModel.ShowSettingsCommand.Execute(null);
        menu.Items.Add(editorSettingsItem);

        if (_viewModel.IsLoggedIn)
        {
            menu.Items.Add(new Separator());

            var changePwdItem = new MenuItem { Header = "🔑 修改密码" };
            changePwdItem.Click += (s, args) =>
            {
                var dlg = new ChangePasswordDialog(_auth) { Owner = this };
                dlg.ShowDialog();
            };
            menu.Items.Add(changePwdItem);

            if (_viewModel.IsAdmin)
            {
                var userMgmtItem = new MenuItem { Header = "👥 用户管理" };
                userMgmtItem.Click += (s, args) =>
                {
                    var dlg = new UserManagementDialog(_db, _auth) { Owner = this };
                    dlg.ShowDialog();
                };
                menu.Items.Add(userMgmtItem);
            }
        }

        menu.Items.Add(new Separator());

        var helpItem = new MenuItem { Header = "❓ 帮助文档" };
        helpItem.Click += (s, args) => CheckUpdate_Click(s, args);
        menu.Items.Add(helpItem);

        menu.Items.Add(new Separator());

        if (_viewModel.IsLoggedIn)
        {
            var logoutItem = new MenuItem { Header = "🚪 退出登录" };
            logoutItem.Click += (s, args) =>
            {
                _auth.Logout();
                _viewModel.OnPropertyChanged(nameof(MainViewModel.IsLoggedIn));
                _viewModel.OnPropertyChanged(nameof(MainViewModel.CurrentUsername));
                _viewModel.OnPropertyChanged(nameof(MainViewModel.IsAdmin));
                _viewModel.OnPropertyChanged(nameof(MainViewModel.NotLoggedInVisibility));
                RefreshAvatar();
            };
            menu.Items.Add(logoutItem);
        }
        else
        {
            var loginItem = new MenuItem { Header = "🔑 登录" };
            loginItem.Click += (s, args) => _viewModel.LoginCommand.Execute(null);
            menu.Items.Add(loginItem);
        }

        menu.IsOpen = true;
    }

    // ===== 修改密码 =====
    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ChangePasswordDialog(_auth) { Owner = this };
        dlg.ShowDialog();
    }

    // ===== 用户管理（超级管理员专用） =====
    private void UserManagement_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new UserManagementDialog(_db, _auth) { Owner = this };
        dlg.ShowDialog();
    }

    // ===== 快捷键参考 =====
    private void KeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new KeyboardShortcutsDialog { Owner = this };
        dlg.ShowDialog();
    }

    // ===== 检查更新 =====
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdate) return; // 已有检查在进行中，忽略重复点击
        _isCheckingUpdate = true;
        try
        {
            var projectPath = _viewModel.FileTree.ProjectPath ?? Environment.CurrentDirectory;
            var maintenance = new SelfMaintenanceService(projectPath);
            var report = await maintenance.CheckCompetitorUpdatesAsync();
            MessageBox.Show(this, report, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"检查更新失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isCheckingUpdate = false;
        }
    }

    // ===== 运行环境检测 =====
    private bool _isCheckingEnv;
    private async void EnvironmentCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingEnv) return;
        _isCheckingEnv = true;
        try
        {
            // 清除环境检测缓存以获取最新结果
            var cacheField = typeof(EnvironmentCheckService).GetField("_cachedResult",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            cacheField?.SetValue(null, null);

            // 运行启动诊断（完整模式）
            var diagResult = await _viewModel.Network.RunStartupDiagnosticsAsync(quickMode: false);

            // 显示诊断弹窗
            var dlg = new EnvironmentCheckDialog(diagResult) { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"运行环境检测失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isCheckingEnv = false;
        }
    }

    // ===== 重置窗口布局 =====
    private void ResetWindow_Click(object sender, RoutedEventArgs e)
    {
        // 恢复默认窗口大小和位置
        WindowState = WindowState.Normal;
        Width = 1400;
        Height = 800;
        Left = (SystemParameters.PrimaryScreenWidth - 1400) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - 800) / 2;

        // 恢复显示所有面板
        _viewModel.IsAIPanelVisible = true;
        _viewModel.IsLeftSidebarVisible = true;
        _viewModel.ActiveSidebar = "files";
        _viewModel.ActiveBottomTab = "";

        // 重置滚动位置
        if (PageHost.Content is WorkspaceView ws)
        {
            // 侧边栏和AI面板宽度由Grid ColumnDefinition控制，自动恢复
        }
    }

    // ===== 打开 Quest =====
    private void OpenQuest_Click(object sender, RoutedEventArgs e)
    {
        var questWindow = new QuestWindow(_viewModel, _db, _auth)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _viewModel.SetQuestWindow(questWindow);
        questWindow.Show();
    }

    // ===== 工具栏：搜索 =====
    private void ToolbarSearch_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SwitchSidebarCommand.Execute("search");
    }

    // ===== 工具栏：切换主侧栏 =====
    private void ToolbarToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleSidebarCommand.Execute(null);
    }

    // ===== 工具栏：切换底部面板 =====
    private void ToolbarTogglePanel_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.ActiveBottomTab))
            _viewModel.ActiveBottomTab = "terminal";
        else
            _viewModel.ActiveBottomTab = "";
    }

    // ===== 工具栏：切换AI侧栏 =====
    private void ToolbarToggleAI_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleAIPanelCommand.Execute(null);
    }

    // ===== 会话过期检查 =====
    private DateTime _lastSessionCheck = DateTime.MinValue;

    private void CheckSessionExpiry()
    {
        if (!_auth.IsLoggedIn) return;
        // 每小时最多检查一次
        if ((DateTime.Now - _lastSessionCheck).TotalHours < 1) return;
        _lastSessionCheck = DateTime.Now;

        if (_auth.IsSessionExpiringSoon())
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _auth.ClearLoginSession();
                var result = MessageBox.Show(
                    "您的登录会话即将过期，是否现在重新登录以保持登录状态？",
                    "登录会话提醒",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    _viewModel.LoginCommand.Execute(null);
                else
                    LogService.Instance.Info("用户选择暂不重新登录", "Auth");
            }));
        }
    }
}
