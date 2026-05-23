using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIIDEWPF.Models;
using AIIDEWPF.Services;

namespace AIIDEWPF.Views;

public partial class GitSettingsDialog : Window
{
    private readonly GitService _gitService;
    private readonly string _projectPath;
    private List<GitPlatformPreset> _customPresets;

    public GitConfig Config { get; private set; }

    public GitSettingsDialog(GitService gitService, string projectPath)
    {
        InitializeComponent();
        _gitService = gitService;
        _projectPath = projectPath;

        // 默认值
        Config = new GitConfig();

        // 加载自定义平台预设
        _customPresets = GitPlatformStore.Load(_projectPath);

        // 检查是否是 git 仓库
        if (_gitService.IsGitRepo())
        {
            GitStatusLabel.Text = "✅ 当前目录已是 Git 仓库";
            InitBtn.Visibility = Visibility.Collapsed;

            // 尝试读取现有远程配置
            var remotes = _gitService.GetRemotes();
            var origin = remotes.FirstOrDefault(r => r.Name == "origin");
            if (origin != null)
            {
                Config.RemoteName = origin.Name;
                Config.RemoteUrl = origin.Url;
            }

            Config.Branch = _gitService.GetCurrentBranch();
            if (string.IsNullOrEmpty(Config.Branch))
                Config.Branch = BranchBox.Text;
        }
        else
        {
            GitStatusLabel.Text = "⚠ 当前目录不是 Git 仓库，请先初始化";
        }

        // 加载到界面
        RemoteNameBox.Text = Config.RemoteName;
        RemoteUrlBox.Text = Config.RemoteUrl;
        UsernameBox.Text = Config.Username;
        BranchBox.Text = Config.Branch;
        CommitMsgBox.Text = Config.CommitMessage;

        // 渲染自定义平台
        RefreshCustomPresets();
    }

    /// <summary>用已有配置预填表单</summary>
    public void PreFill(GitConfig config)
    {
        RemoteNameBox.Text = config.RemoteName;
        RemoteUrlBox.Text = config.RemoteUrl;
        UsernameBox.Text = config.Username;
        BranchBox.Text = config.Branch;
        CommitMsgBox.Text = config.CommitMessage;
    }

    private void GitHubPreset_Click(object sender, RoutedEventArgs e)
    {
        RemoteUrlBox.Text = "https://github.com/用户名/仓库名.git";
    }

    private void GitLabPreset_Click(object sender, RoutedEventArgs e)
    {
        RemoteUrlBox.Text = "https://gitlab.com/用户名/仓库名.git";
    }

    private void GiteePreset_Click(object sender, RoutedEventArgs e)
    {
        RemoteUrlBox.Text = "https://gitee.com/用户名/仓库名.git";
    }

    // ===== 自定义平台管理 =====

    private void RefreshCustomPresets()
    {
        CustomPlatformsList.ItemsSource = null;
        CustomPlatformsList.ItemsSource = _customPresets;
    }

    private void AddPlatformBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = NewPlatformName.Text.Trim();
        var url = NewPlatformUrl.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("请输入平台名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("请输入平台 URL 模板", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 数量限制（全球主流 Git 平台约 20+，50 已足够覆盖自建实例）
        const int maxPresets = 50;
        if (_customPresets.Count >= maxPresets)
        {
            MessageBox.Show($"最多支持 {maxPresets} 个自定义平台。\n请先删除不常用的平台再添加。",
                "已达上限", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 去重
        if (_customPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"平台 \"{name}\" 已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _customPresets.Add(new GitPlatformPreset { Name = name, UrlTemplate = url });
        GitPlatformStore.Save(_projectPath, _customPresets);

        NewPlatformName.Text = string.Empty;
        NewPlatformUrl.Text = string.Empty;
        RefreshCustomPresets();
    }

    private void CustomPreset_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is GitPlatformPreset preset)
        {
            RemoteUrlBox.Text = preset.UrlTemplate;
        }
    }

    private void CustomPreset_Delete(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // 防止冒泡触发 CustomPreset_Click

        if (sender is TextBlock textBlock)
        {
            // 向上找到 Border 的 DataContext
            var parent = textBlock.Parent as StackPanel;
            var border = parent?.Parent as Border;
            if (border?.Tag is GitPlatformPreset preset)
            {
                _customPresets.Remove(preset);
                GitPlatformStore.Save(_projectPath, _customPresets);
                RefreshCustomPresets();
            }
        }
    }

    // ===== 初始化 / 保存 / 取消 =====

    private void InitBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = _gitService.Init();
        if (result.Success)
        {
            GitStatusLabel.Text = "✅ Git 仓库初始化成功！";
            InitBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            MessageBox.Show($"初始化失败：{result.Error}", "Git 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        Config.RemoteName = RemoteNameBox.Text.Trim();
        Config.RemoteUrl = RemoteUrlBox.Text.Trim();
        Config.Username = UsernameBox.Text.Trim();
        Config.Password = PasswordBox.Password;
        Config.Branch = BranchBox.Text.Trim();
        Config.CommitMessage = CommitMsgBox.Text.Trim();

        if (string.IsNullOrEmpty(Config.RemoteUrl))
        {
            MessageBox.Show("请输入远程仓库 URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
