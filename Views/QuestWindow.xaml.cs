using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIIDEWPF.Services;
using AIIDEWPF.ViewModels;

namespace AIIDEWPF.Views;

public partial class QuestWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DatabaseService? _db;
    private readonly AuthService? _auth;
    private readonly ObservableCollection<QuestItem> _quests = new();
    private bool _isAgentMode = true;
    private QuestItem? _activeQuest;  // 当前正在执行的任务

    public string ProjectName => _viewModel.FileTree.ProjectName ?? "未打开项目";

    public QuestWindow(MainViewModel viewModel, DatabaseService? db, AuthService? auth)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _db = db;
        _auth = auth;
        DataContext = this;

        // 初始化示例任务
        _quests.Add(new QuestItem
        {
            Title = "示例：创建用户登录页面",
            Description = "使用 React 创建登录表单并集成后端 API",
            Status = QuestStatus.Ready,
        });
        _quests.Add(new QuestItem
        {
            Title = "示例：优化数据库查询性能",
            Description = "分析慢查询并添加索引优化",
            Status = QuestStatus.Running,
        });
        QuestList.ItemsSource = _quests;
    }

    private void NewQuest_Click(object sender, RoutedEventArgs e)
    {
        var quest = new QuestItem
        {
            Title = "新任务",
            Description = "请输入任务描述...",
            Status = QuestStatus.Ready,
        };
        _quests.Insert(0, quest);
    }

    private void QuestItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is QuestItem quest)
        {
            QuestInputBox.Text = $"{quest.Title}\n\n{quest.Description}";
            QuestInputBox.Focus();
        }
    }

    private void AgentMode_Click(object sender, RoutedEventArgs e)
    {
        _isAgentMode = true;
        AgentModeBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7a, 0xcc));
        AgentModeBtn.Foreground = new SolidColorBrush(Colors.White);
        AgentModeBtn.BorderThickness = new Thickness(0);
        ExpertsModeBtn.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
        ExpertsModeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa));
        ExpertsModeBtn.BorderThickness = new Thickness(1);
    }

    private void ExpertsMode_Click(object sender, RoutedEventArgs e)
    {
        _isAgentMode = false;
        ExpertsModeBtn.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7a, 0xcc));
        ExpertsModeBtn.Foreground = new SolidColorBrush(Colors.White);
        ExpertsModeBtn.BorderThickness = new Thickness(0);
        AgentModeBtn.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
        AgentModeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa));
        AgentModeBtn.BorderThickness = new Thickness(1);
    }

    private void QuestInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            QuestSend_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void QuestSend_Click(object sender, RoutedEventArgs e)
    {
        var input = QuestInputBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        // 发送前余额检查
        if (!CheckBalanceBeforeSend()) return;

        // 创建新任务
        var quest = new QuestItem
        {
            Title = input.Length > 40 ? input[..40] + "..." : input,
            Description = input,
            Status = QuestStatus.Running,
            OriginalInput = input
        };
        _quests.Insert(0, quest);
        _activeQuest = quest;

        // 将输入填入主窗口的聊天输入框并发送
        _viewModel.Chat.InputText = input;
        _viewModel.SendMessageCommand.Execute(null);

        QuestInputBox.Clear();
        QuestInputBox.Focus();
    }

    /// <summary>重试任务：重新发送原始输入</summary>
    private void RetryQuest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is QuestItem quest)
        {
            quest.Status = QuestStatus.Running;
            quest.LastError = "";
            _activeQuest = quest;

            var input = !string.IsNullOrEmpty(quest.OriginalInput) ? quest.OriginalInput : quest.Description;
            _viewModel.Chat.InputText = input;
            _viewModel.SendMessageCommand.Execute(null);

            LogService.Instance.Info($"Quest 重试: {quest.Title}", "Quest");
        }
    }

    /// <summary>继续任务（从中断点继续）</summary>
    private void ContinueQuest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is QuestItem quest)
        {
            quest.Status = QuestStatus.Running;
            quest.LastError = "";
            _activeQuest = quest;

            // 继续时附加中断上下文提示
            var continuePrompt = $"继续执行之前中断的任务:\n{quest.Description}";
            _viewModel.Chat.InputText = continuePrompt;
            _viewModel.SendMessageCommand.Execute(null);

            LogService.Instance.Info($"Quest 继续: {quest.Title}", "Quest");
        }
    }

    /// <summary>任务执行出错时调用（由 MainViewModel 的 OnAIError 触发）</summary>
    public void OnQuestError(string error)
    {
        if (_activeQuest == null) return;

        _activeQuest.Status = QuestStatus.Interrupted;
        _activeQuest.LastError = error;
    }

    /// <summary>任务执行完成时调用（由 MainViewModel 的 OnAIDone 触发）</summary>
    public void OnQuestCompleted()
    {
        if (_activeQuest == null) return;

        _activeQuest.Status = QuestStatus.Completed;
        _activeQuest = null;
    }

    // ===== 余额提醒 =====

    private UsageTrackerService? _usageTracker;

    /// <summary>设置用量追踪器引用（由 MainViewModel 调用）</summary>
    public void SetUsageTracker(UsageTrackerService? tracker)
    {
        if (_usageTracker != null)
        {
            _usageTracker.OnStatusChanged -= OnUsageStatusChanged;
            _usageTracker.OnBalanceUpdated -= OnUsageBalanceUpdated;
            _usageTracker.OnWarning -= OnUsageWarning;
            _usageTracker.OnCritical -= OnUsageCritical;
            _usageTracker.OnQuotaExhausted -= OnUsageQuotaExhausted;
        }

        _usageTracker = tracker;

        if (_usageTracker != null)
        {
            _usageTracker.OnStatusChanged += OnUsageStatusChanged;
            _usageTracker.OnBalanceUpdated += OnUsageBalanceUpdated;
            _usageTracker.OnWarning += OnUsageWarning;
            _usageTracker.OnCritical += OnUsageCritical;
            _usageTracker.OnQuotaExhausted += OnUsageQuotaExhausted;

            // 立即更新一次状态
            Dispatcher.InvokeAsync(UpdateBalanceStatus);
        }
    }

    private void OnUsageStatusChanged() => Dispatcher.InvokeAsync(UpdateBalanceStatus);
    private void OnUsageBalanceUpdated() => Dispatcher.InvokeAsync(UpdateBalanceStatus);

    private void OnUsageWarning(string msg)
    {
        Dispatcher.InvokeAsync(() => ShowLowBalanceWarning(msg, false));
    }

    private void OnUsageCritical(string msg)
    {
        Dispatcher.InvokeAsync(() => ShowLowBalanceWarning(msg, true));
    }

    private void OnUsageQuotaExhausted(string msg)
    {
        Dispatcher.InvokeAsync(() =>
        {
            BalanceWarningText.Text = "❌ 余额已耗尽，无法继续处理";
            BalanceWarningText.Foreground = new SolidColorBrush(Color.FromRgb(0xf4, 0x47, 0x47));

            MessageBox.Show(
                $"{msg}\n\n" +
                "💡 建议操作：\n" +
                "  1. 前往大模型开放平台充值余额\n" +
                "  2. 切换到其他可用模型（设置 → 模型管理）\n" +
                "  3. 更换 API Key\n" +
                "  4. 等待配额周期重置（每小时/每天自动重置）",
                "⚠ 余额已耗尽",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }

    /// <summary>更新余额状态栏</summary>
    private void UpdateBalanceStatus()
    {
        if (_usageTracker == null)
        {
            BalanceStatusBar.Visibility = Visibility.Collapsed;
            return;
        }

        BalanceStatusBar.Visibility = Visibility.Visible;

        // 模型名称
        var modelName = _viewModel.ModelManager?.ActiveModel?.Name 
            ?? _viewModel.ModelManager?.ActiveModel?.Id 
            ?? "当前模型";
        BalanceModelName.Text = modelName;

        // 会话费用
        var cost = _usageTracker.EstimatedCostYuan;
        BalanceSessionCost.Text = $"会话: ¥{cost:F2}";

        // 用量百分比
        var usagePercent = _usageTracker.RateLimitUsagePercent;
        if (usagePercent >= 0)
        {
            BalanceProgressBar.Width = Math.Min(80, usagePercent * 0.8);
            BalancePercentText.Text = $"{usagePercent:F0}%";

            if (usagePercent >= _usageTracker.CriticalThresholdPercent)
            {
                BalanceProgressBar.Background = new SolidColorBrush(Color.FromRgb(0xf4, 0x47, 0x47));
                BalancePercentText.Foreground = new SolidColorBrush(Color.FromRgb(0xf4, 0x47, 0x47));
            }
            else if (usagePercent >= _usageTracker.WarningThresholdPercent)
            {
                BalanceProgressBar.Background = new SolidColorBrush(Color.FromRgb(0xfd, 0x7e, 0x14));
                BalancePercentText.Foreground = new SolidColorBrush(Color.FromRgb(0xfd, 0x7e, 0x14));
            }
            else
            {
                BalanceProgressBar.Background = new SolidColorBrush(Color.FromRgb(0x4e, 0xc9, 0xb0));
                BalancePercentText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }
        }
        else
        {
            BalanceProgressBar.Width = 0;
            BalancePercentText.Text = "";
        }

        // 余额信息
        if (_usageTracker.HasBalance && _usageTracker.ProviderBalance != null)
        {
            var balance = _usageTracker.ProviderBalance;
            BalanceQuotaInfo.Text = $"余额: ¥{balance.TotalBalance:F2}";
        }
        else
        {
            BalanceQuotaInfo.Text = "";
        }

        // 警告消息
        if (!string.IsNullOrEmpty(_usageTracker.WarningMessage))
        {
            BalanceWarningText.Text = _usageTracker.WarningMessage;
        }
    }

    /// <summary>显示低余额/不足警告</summary>
    private void ShowLowBalanceWarning(string msg, bool isCritical)
    {
        BalanceWarningText.Text = msg;
        BalanceWarningText.Foreground = isCritical
            ? new SolidColorBrush(Color.FromRgb(0xf4, 0x47, 0x47))
            : new SolidColorBrush(Color.FromRgb(0xfd, 0x7e, 0x14));

        // 严重不足时弹窗提醒
        if (isCritical)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var result = MessageBox.Show(
                    $"{msg}\n\n建议：\n• 充值大模型余额\n• 切换到更经济的模型\n• 等待配额重置\n\n是否仍然继续发送？",
                    "⚠ 余额严重不足",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    // 用户取消，不清除输入框，让用户可以修改后决定
                }
            });
        }
    }

    /// <summary>发送前检查余额</summary>
    private bool CheckBalanceBeforeSend()
    {
        if (_usageTracker == null) return true;

        // 余额/配额完全耗尽：直接阻止发送
        if (_usageTracker.IsQuotaExhausted)
        {
            MessageBox.Show(
                "❌ API 余额/配额已完全耗尽，无法继续处理任务。\n\n" +
                "请采取以下措施：\n" +
                "• 前往大模型开放平台充值\n" +
                "• 切换到其他可用模型（设置 → 模型管理）\n" +
                "• 更换 API Key\n" +
                "• 等待配额周期重置",
                "余额已耗尽",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        var usagePercent = _usageTracker.RateLimitUsagePercent;

        // 严重不足（>95%）：弹窗警告
        if (usagePercent >= _usageTracker.CriticalThresholdPercent && usagePercent >= 0)
        {
            var result = MessageBox.Show(
                $"⚠ 余额严重不足（已用 {usagePercent:F0}%）\n\n" +
                "建议采取以下措施：\n" +
                "• 充值大模型余额\n" +
                "• 切换到更经济的模型（如 DeepSeek-V3 或 Budget 模式）\n" +
                "• 等待配额重置\n\n" +
                "是否仍然继续发送？",
                "余额严重不足",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        // 接近上限（>80%）：轻量提醒
        if (usagePercent >= _usageTracker.WarningThresholdPercent && usagePercent >= 0)
        {
            var result = MessageBox.Show(
                $"⚠ 余额即将用完（已用 {usagePercent:F0}%）\n\n" +
                "是否继续发送？",
                "余额不足提醒",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            return result == MessageBoxResult.Yes;
        }

        return true;
    }
}

/// <summary>Quest 任务项</summary>
public class QuestItem : INotifyPropertyChanged
{
    private string _title = "";
    private string _description = "";
    private QuestStatus _status;
    private string _originalInput = "";
    private string _lastError = "";

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    /// <summary>发送时的原始输入（用于重试/继续）</summary>
    public string OriginalInput { get => _originalInput; set { _originalInput = value; OnPropertyChanged(); } }
    /// <summary>最后一次错误信息</summary>
    public string LastError { get => _lastError; set { _lastError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(_lastError);

    public QuestStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(ShowRetryActions));
        }
    }

    public string StatusText => _status switch
    {
        QuestStatus.Ready => "就绪",
        QuestStatus.Running => "执行中",
        QuestStatus.ActionRequired => "等待操作",
        QuestStatus.Completed => "已完成",
        QuestStatus.Error => "错误",
        QuestStatus.Interrupted => "中断",
        _ => "未知"
    };

    public string StatusColor => _status switch
    {
        QuestStatus.Ready => "#4ec9b0",
        QuestStatus.Running => "#007acc",
        QuestStatus.ActionRequired => "#c0a060",
        QuestStatus.Completed => "#1a5a1a",
        QuestStatus.Error => "#8b0000",
        QuestStatus.Interrupted => "#fd7e14",  // 橙色 - 表示可重试
        _ => "#555"
    };

    /// <summary>是否可以重试（Error 或 Interrupted 状态下可重试）</summary>
    public bool CanRetry => _status == QuestStatus.Error || _status == QuestStatus.Interrupted;
    /// <summary>是否可以继续（Interrupted 状态下可以尝试继续）</summary>
    public bool CanContinue => _status == QuestStatus.Interrupted;
    /// <summary>是否显示重试操作按钮</summary>
    public bool ShowRetryActions => CanRetry;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum QuestStatus
{
    Ready,
    Running,
    ActionRequired,
    Completed,
    Error,
    Interrupted   // 因网络中断/异常而导致未完成
}
