using System.Linq;
using System.Text;
using System.Windows;
using AIIDEWPF.Services;

namespace AIIDEWPF.Views;

/// <summary>
/// 自动维护建议弹窗 —— 自包含窗口，内置扫描、展示、修复全流程。
/// 只需要传入 SelfMaintenanceService + 回调，不依赖 MainViewModel 的任何业务逻辑。
/// </summary>
public partial class MaintenanceDialog : Window
{
    private readonly SelfMaintenanceService _maintenanceService;
    private readonly Action<string> _onSystemMessage;
    private readonly Action? _onRefreshRequired;
    private List<MaintenanceIssue>? _issues;
    private bool _scanComplete;

    public MaintenanceDialog(SelfMaintenanceService maintenanceService,
        Action<string> onSystemMessage,
        Action? onRefreshRequired = null)
    {
        InitializeComponent();
        _maintenanceService = maintenanceService;
        _onSystemMessage = onSystemMessage;
        _onRefreshRequired = onRefreshRequired;

        // 右上角 X 按钮 → 不触发任何操作
        Closing += (s, e) =>
        {
            if (!DialogResult.HasValue)
                DialogResult = null;
        };

        // 窗口加载后自动开始扫描
        Loaded += async (s, e) => await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        try
        {
            SubtitleLabel.Text = "正在扫描项目...";
            ContentText.Text = "🔍 正在扫描项目中可能存在的问题，请稍候...";
            FixBtn.IsEnabled = false;

            _issues = await _maintenanceService.ScanAsync();

            if (_issues == null || _issues.Count == 0)
            {
                SubtitleLabel.Text = "扫描完成";
                ContentText.Text = "✅ 未发现需要关注的问题，项目状态良好。";
                _scanComplete = true;
                return;
            }

            var autoFixable = _issues.Where(i => i.CanAutoFix).ToList();
            var manualOnly = _issues.Where(i => !i.CanAutoFix).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"自动维护扫描发现 {_issues.Count} 个问题：\n");

            if (autoFixable.Count > 0)
            {
                sb.AppendLine($"【可自动修复】{autoFixable.Count} 项：");
                foreach (var i in autoFixable)
                    sb.AppendLine($"  - {i.DisplayText}: {i.Description}");
                sb.AppendLine();
            }
            if (manualOnly.Count > 0)
            {
                sb.AppendLine($"【需手动处理】{manualOnly.Count} 项：");
                foreach (var i in manualOnly)
                    sb.AppendLine($"  - {i.DisplayText}: {i.Description}");
            }
            sb.AppendLine("\n请点击下方按钮选择操作。");

            SubtitleLabel.Text = $"发现 {_issues.Count} 个问题（可自动修复 {autoFixable.Count} 项，需手动处理 {manualOnly.Count} 项）";
            ContentText.Text = sb.ToString();
            FixBtn.IsEnabled = autoFixable.Count > 0;
            _scanComplete = true;
        }
        catch (Exception ex)
        {
            SubtitleLabel.Text = "扫描出错";
            ContentText.Text = $"❌ 扫描过程中发生错误:\n{ex.Message}";
            LogService.Instance.Error(ex, "Maintenance");
            _scanComplete = true;
        }
    }

    /// <summary>备份并修复</summary>
    private async void FixBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_issues == null || !_scanComplete) return;

        FixBtn.IsEnabled = false;
        ViewOnlyBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;

        try
        {
            ContentText.Text += "\n\n⏳ 正在备份项目...";
            _maintenanceService.BackupProject();

            ContentText.Text += "\n🔧 正在执行自动修复...";
            var applied = await _maintenanceService.ApplyAutoFixesAsync(_issues);

            if (applied.Count > 0)
            {
                ContentText.Text += $"\n\n✅ 修复完成: {applied.Count} 项";
                foreach (var a in applied)
                    ContentText.Text += $"\n   ✓ {a.DisplayText}";

                _onSystemMessage(
                    $"🔧 自动维护完成: 已修复 {applied.Count} 项\n{string.Join("\n", applied.Select(a => "  - " + a.DisplayText))}");
                _onRefreshRequired?.Invoke();
            }
            else
            {
                ContentText.Text += "\n\n⚠ 未能自动修复任何项目，请手动处理。";
            }
        }
        catch (Exception ex)
        {
            ContentText.Text += $"\n\n❌ 修复失败: {ex.Message}";
            LogService.Instance.Error(ex, "Maintenance");
        }
        finally
        {
            CancelBtn.IsEnabled = true;
            CancelBtn.Content = "关闭";
        }
    }

    /// <summary>仅查看（不修复）</summary>
    private void ViewOnlyBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>关闭弹窗</summary>
    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = null;
        Close();
    }
}
