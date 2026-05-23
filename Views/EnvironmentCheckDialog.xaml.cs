using System.Diagnostics;
using System.Windows;
using AIIDEWPF.Services;

namespace AIIDEWPF.Views;

/// <summary>
/// 启动诊断弹窗 —— 合并网络状态 + 运行环境检测，单次弹窗展示全部诊断结果。
/// 提供下载链接、一键安装和重新检测功能。
/// </summary>
public partial class EnvironmentCheckDialog : Window
{
    private StartupDiagResult _diagResult;
    private List<EnvCheckItem> _missingItems = new();
    private readonly NetworkService _networkService;

    public EnvironmentCheckDialog(StartupDiagResult diagResult)
    {
        InitializeComponent();
        _diagResult = diagResult;
        _networkService = new NetworkService();
        LoadResult();
    }

    private void LoadResult()
    {
        var env = _diagResult.Environment;
        _missingItems = env?.AllItems.Where(i => !i.IsInstalled).ToList() ?? new();

        // ---- 网络状态 ----
        if (_diagResult.IsNetworkAvailable)
        {
            NetworkIcon.Text = "✅";
            NetworkStatusLabel.Text = "网络连接正常";
            NetworkStatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x80, 0xd0, 0x80));
        }
        else
        {
            NetworkIcon.Text = "⚠️";
            NetworkStatusLabel.Text = "网络连接不可用 — AI 对话、联网搜索等功能将无法使用";
            NetworkStatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xf0, 0xa0, 0x60));
        }

        // 网络详细诊断（非快速模式）
        if (_diagResult.NetworkDiags != null && _diagResult.NetworkDiags.Count > 0)
        {
            NetworkDiagsControl.ItemsSource = _diagResult.NetworkDiags;
            NetworkDiagsControl.Visibility = Visibility.Visible;
        }

        // ---- 环境检测 ----
        if (_missingItems.Count == 0 && _diagResult.IsNetworkAvailable)
        {
            // 一切正常
            AllPassedPanel.Visibility = Visibility.Visible;
            StatusLabel.Text = "";
            MissingItemsControl.Visibility = Visibility.Collapsed;
            RecheckBtn.Visibility = Visibility.Collapsed;
            IgnoreBtn.Content = "关闭";
        }
        else
        {
            var parts = new List<string>();
            if (!_diagResult.IsNetworkAvailable)
                parts.Add("⚠️ 网络连接不可用");
            if (_missingItems.Count > 0)
                parts.Add($"检测到 {_missingItems.Count} 项运行环境缺失");
            StatusLabel.Text = string.Join("；", parts) + "，可能影响部分功能：";

            if (_missingItems.Count > 0)
                MissingItemsControl.ItemsSource = _missingItems;
            else
                MissingItemsControl.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>打开下载页面</summary>
    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is EnvCheckItem item)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开浏览器：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>一键安装（通过 winget）</summary>
    private async void OneClickInstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is EnvCheckItem item)
        {
            var wingetId = GetWingetId(item.Name);
            if (string.IsNullOrEmpty(wingetId))
            {
                OpenUrl_Click(sender, e);
                return;
            }

            var btn = fe as System.Windows.Controls.Button;
            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ 安装中...";
            }

            try
            {
                var psi = new ProcessStartInfo("winget", $"install --id {wingetId} --accept-source-agreements --accept-package-agreements")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);

                await Task.Delay(2000);

                MessageBox.Show(
                    $"已启动 winget 安装 [{item.Name}]。\n\n安装窗口将在单独的终端中运行，请等待安装完成后点击「重新检测」按钮。",
                    "安装已启动", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"winget 安装失败：{ex.Message}\n\n请尝试使用「打开下载页」手动下载安装。",
                    "安装失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    btn.Content = "⚡ 一键安装";
                }
            }
        }
    }

    /// <summary>获取 winget 包 ID</summary>
    private static string? GetWingetId(string envName)
    {
        return envName switch
        {
            ".NET SDK/Runtime" => "Microsoft.DotNet.SDK.8",
            "Git" => "Git.Git",
            _ => null
        };
    }

    /// <summary>重新检测：运行完整启动诊断</summary>
    private async void RecheckBtn_Click(object sender, RoutedEventArgs e)
    {
        RecheckBtn.IsEnabled = false;
        RecheckBtn.Content = "⏳ 检测中...";
        StatusLabel.Text = "正在重新运行启动诊断...";

        try
        {
            // 清除环境检测缓存
            var cacheField = typeof(EnvironmentCheckService).GetField("_cachedResult",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            cacheField?.SetValue(null, null);

            _diagResult = await _networkService.RunStartupDiagnosticsAsync(quickMode: false);
            LoadResult();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"重新检测失败：{ex.Message}";
        }
        finally
        {
            RecheckBtn.IsEnabled = true;
            RecheckBtn.Content = "🔄 重新检测";
        }
    }

    /// <summary>忽略并继续</summary>
    private void IgnoreBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
