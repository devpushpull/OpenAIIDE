using System.Windows;
using System.Windows.Controls;
using AIIDEWPF.ViewModels;

namespace AIIDEWPF.Views;

/// <summary>
/// 设置窗口 —— 对标 Qoder / 通义灵码 设置面板
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.RequestClose += () => DialogResult = true;

        // 作用域筛选按钮
        ScopeFilterAll_Click(null!, null!); // 初始化高亮

        // 导航列表选中事件 —— 切换页面
        NavListBox.SelectionChanged += NavListBox_SelectionChanged;

        // 编辑自定义模型时，若 API 密钥发生变化则同步到 PasswordBox
        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.EditProviderApiKey))
                EditProviderApiKeyBox.Password = _vm.EditProviderApiKey ?? "";
        };
    }

    private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavListBox.SelectedItem is NavItem nav)
        {
            _vm.ActivePage = nav.PageKey;

            // 切换页面可见性
            PageGeneral.Visibility = _vm.IsGeneralPage ? Visibility.Visible : Visibility.Collapsed;
            PageAI.Visibility = _vm.IsAIPage ? Visibility.Visible : Visibility.Collapsed;
            PageEditor.Visibility = _vm.IsEditorPage ? Visibility.Visible : Visibility.Collapsed;
            PageAppearance.Visibility = _vm.IsAppearancePage ? Visibility.Visible : Visibility.Collapsed;
            PageTerminal.Visibility = _vm.IsTerminalPage ? Visibility.Visible : Visibility.Collapsed;
            PageFileExclude.Visibility = _vm.IsFileExcludePage ? Visibility.Visible : Visibility.Collapsed;
            PageMemory.Visibility = _vm.IsMemoryPage ? Visibility.Visible : Visibility.Collapsed;
            PageRules.Visibility = _vm.IsRulesPage ? Visibility.Visible : Visibility.Collapsed;
            PageCommands.Visibility = _vm.IsCommandsPage ? Visibility.Visible : Visibility.Collapsed;
            PageKeymap.Visibility = _vm.IsKeymapPage ? Visibility.Visible : Visibility.Collapsed;
            PagePrivacy.Visibility = _vm.IsPrivacyPage ? Visibility.Visible : Visibility.Collapsed;
            PageProxy.Visibility = _vm.IsProxyPage ? Visibility.Visible : Visibility.Collapsed;
            PageNetwork.Visibility = _vm.IsNetworkPage ? Visibility.Visible : Visibility.Collapsed;
            PageMCP.Visibility = _vm.IsMCPPage ? Visibility.Visible : Visibility.Collapsed;
            PageLearning.Visibility = _vm.IsLearningPage ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = _vm.IsAboutPage ? Visibility.Visible : Visibility.Collapsed;

            // 切换到 AI 页面时，将 ViewModel 中的密钥同步到 PasswordBox
            if (_vm.IsAIPage)
            {
                DefaultApiKeyBox.Password = _vm.ApiKey ?? "";
                EditProviderApiKeyBox.Password = _vm.EditProviderApiKey ?? "";
            }

            // 切换到 MCP 页时加载数据
            if (_vm.IsMCPPage)
            {
                _vm.LoadMCPServers();
                _ = _vm.LoadMCPMarketplaceAsync();
            }

            // 切换到关于页时加载环境+网络状态
            if (_vm.IsAboutPage)
                _ = _vm.LoadEnvInfoAsync();

            // 切换到学习统计页时刷新数据
            if (_vm.IsLearningPage)
                _vm.RefreshLearningStats();

            // 切换到代理页时加载密码
            if (_vm.IsProxyPage)
                ProxyPasswordBox.Password = _vm.ProxyPassword ?? "";
        }
    }

    // ===== API 密钥 PasswordBox 双向同步 =====
    private void DefaultApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.ApiKey = DefaultApiKeyBox.Password;
    }

    private void EditProviderApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.EditProviderApiKey = EditProviderApiKeyBox.Password;
    }

    private void ProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.ProxyPassword = ProxyPasswordBox.Password;
    }

    // ===== 作用域筛选按钮 =====

    private void ScopeFilterAll_Click(object sender, RoutedEventArgs e)
    {
        _vm.MemoryVM.ScopeFilter = "all";
        UpdateScopeFilterButtonStyles();
    }

    private void ScopeFilterGlobal_Click(object sender, RoutedEventArgs e)
    {
        _vm.MemoryVM.ScopeFilter = "global";
        UpdateScopeFilterButtonStyles();
    }

    private void ScopeFilterProject_Click(object sender, RoutedEventArgs e)
    {
        _vm.MemoryVM.ScopeFilter = "project";
        UpdateScopeFilterButtonStyles();
    }

    private void UpdateScopeFilterButtonStyles()
    {
        var activeBg = "#007acc";
        var activeFg = "White";
        var inactiveBg = "#555";
        var inactiveFg = "#d4d4d4";

        var filter = _vm.MemoryVM.ScopeFilter;
        ScopeFilterAllBtn.Background = ParseColor(filter == "all" ? activeBg : inactiveBg);
        ScopeFilterAllBtn.Foreground = ParseColor(filter == "all" ? activeFg : inactiveFg);
        ScopeFilterGlobalBtn.Background = ParseColor(filter == "global" ? activeBg : inactiveBg);
        ScopeFilterGlobalBtn.Foreground = ParseColor(filter == "global" ? activeFg : inactiveFg);
        ScopeFilterProjectBtn.Background = ParseColor(filter == "project" ? activeBg : inactiveBg);
        ScopeFilterProjectBtn.Foreground = ParseColor(filter == "project" ? activeFg : inactiveFg);
    }

    // ===== 学习统计刷新 =====

    private void LearningRefresh_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshLearningStats();
    }

    private static System.Windows.Media.Brush ParseColor(string hex) =>
        new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
}
