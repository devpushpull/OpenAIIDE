using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIIDEWPF.Services;
using AIIDEWPF.ViewModels;

namespace AIIDEWPF.Views;

public partial class UsageDashboardView : UserControl
{
    private readonly MainViewModel _viewModel;
    private readonly UsageTrackerService? _tracker;
    private readonly DispatcherTimer _refreshTimer;

    public UsageDashboardView(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _tracker = _viewModel.UsageTracker;

        // 订阅数据更新事件
        if (_tracker != null)
        {
            _tracker.OnDataUpdated += OnDataUpdated;
            _tracker.OnBalanceUpdated += OnBalanceUpdated;
        }

        // 每 2 秒自动刷新
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (s, e) => RefreshDashboard();
        _refreshTimer.Start();

        // 初始加载
        Loaded += (s, e) =>
        {
            PopulateModelFilter();
            RefreshDashboard();
        };
        Unloaded += (s, e) =>
        {
            _refreshTimer.Stop();
            if (_tracker != null)
            {
                _tracker.OnDataUpdated -= OnDataUpdated;
                _tracker.OnBalanceUpdated -= OnBalanceUpdated;
            }
        };
    }

    private void OnDataUpdated() => Dispatcher.Invoke(RefreshDashboard);

    private void OnBalanceUpdated() => Dispatcher.Invoke(RefreshBalance);

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDashboard();

    private void ModelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDashboard();

    private void PopulateModelFilter()
    {
        ModelFilterBox.SelectionChanged -= ModelFilter_SelectionChanged;
        ModelFilterBox.Items.Clear();
        ModelFilterBox.Items.Add("全部模型");

        // 加载所有已配置的模型（从 ModelManager）
        var allModels = _viewModel.ModelManager.GetProviders()
            .SelectMany(p => p.Models)
            .Select(m => m.Id)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        foreach (var m in allModels)
            ModelFilterBox.Items.Add(m);

        ModelFilterBox.SelectedIndex = 0;
        ModelFilterBox.SelectionChanged += ModelFilter_SelectionChanged;
    }

    private void RefreshDashboard()
    {
        if (_tracker == null) return;

        // 获取筛选后的调用历史
        var selectedModel = ModelFilterBox.SelectedItem as string;
        var filteredHistory = string.IsNullOrEmpty(selectedModel) || selectedModel == "全部模型"
            ? _tracker.CallHistory.ToList()
            : _tracker.CallHistory.Where(c => c.ModelName == selectedModel).ToList();

        var inputTokens = filteredHistory.Sum(c => (long)c.InputTokens);
        var outputTokens = filteredHistory.Sum(c => (long)c.OutputTokens);
        var total = inputTokens + outputTokens;

        // === 概览卡片 ===
        TotalTokensText.Text = CommonUtils.FormatTokens(total);
        ApiCallsText.Text = filteredHistory.Count.ToString();
        var successCount = filteredHistory.Count(c => c.IsSuccess);
        SuccessRateText.Text = filteredHistory.Count > 0
            ? $"成功率 {successCount * 100 / filteredHistory.Count}%"
            : "—";
        // 估算费用（DeepSeek定价基准）
        CostText.Text = $"¥{(inputTokens * 0.000001 + outputTokens * 0.000002):F4}";
        ActiveModelText.Text = string.IsNullOrEmpty(selectedModel) || selectedModel == "全部模型"
            ? $"{_tracker.CallHistory.Select(c => c.ModelName).Distinct().Count()} 个模型"
            : selectedModel;
        ProviderText.Text = selectedModel ?? "";

        // === Token 分布 ===
        if (total > 0)
        {
            InputBar.Visibility = Visibility.Visible;
            OutputBar.Visibility = Visibility.Visible;
            var inputRatio = (double)inputTokens / total;
            InputBarCol.Width = new GridLength(inputRatio, GridUnitType.Star);
            OutputBarCol.Width = new GridLength(1 - inputRatio, GridUnitType.Star);
        }
        else
        {
            InputBar.Visibility = Visibility.Collapsed;
            OutputBar.Visibility = Visibility.Collapsed;
            InputBarCol.Width = new GridLength(1, GridUnitType.Star);
            OutputBarCol.Width = new GridLength(1, GridUnitType.Star);
        }
        DistributionLabel.Text = $"{CommonUtils.FormatTokens(inputTokens)} / {CommonUtils.FormatTokens(outputTokens)}";
        InputDetailText.Text = $"{inputTokens:N0} tokens";
        OutputDetailText.Text = $"{outputTokens:N0} tokens";

        // === API 配额状态 ===
        if (_tracker.HasRateLimitInfo)
        {
            QuotaInfoGrid.Visibility = Visibility.Visible;
            NoQuotaInfoText.Visibility = Visibility.Collapsed;

            // Token 配额
            var tokenUsage = _tracker.RateLimitUsagePercent;
            var tokenUsed = _tracker.RateLimitTotalTokens - _tracker.RateLimitRemainingTokens;
            UpdateQuotaBar(TokenQuotaBar, TokenQuotaPercentText, TokenQuotaDetailText,
                tokenUsage, tokenUsed, _tracker.RateLimitTotalTokens,
                _tracker.RateLimitRemainingTokens, "Token");

            // 请求配额
            var reqUsage = _tracker.RateLimitTotalRequests > 0
                ? 100.0 - (_tracker.RateLimitRemainingRequests * 100.0 / _tracker.RateLimitTotalRequests)
                : -1;
            var reqUsed = _tracker.RateLimitTotalRequests - _tracker.RateLimitRemainingRequests;
            UpdateQuotaBar(RequestQuotaBar, RequestQuotaPercentText, RequestQuotaDetailText,
                reqUsage, reqUsed, _tracker.RateLimitTotalRequests,
                _tracker.RateLimitRemainingRequests, "请求");

            // 配额警告
            if (!string.IsNullOrEmpty(_tracker.WarningMessage))
            {
                QuotaWarningBorder.Visibility = Visibility.Visible;
                QuotaWarningText.Text = _tracker.WarningMessage;
            }
            else
            {
                QuotaWarningBorder.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            QuotaInfoGrid.Visibility = Visibility.Collapsed;
            NoQuotaInfoText.Visibility = Visibility.Visible;
        }

        // === 会话用量警戒 ===
        var sessionLimit = _tracker.SessionTokenWarningLimit;
        var sessionRatio = sessionLimit > 0 ? Math.Min(100, total * 100.0 / sessionLimit) : 0;
        SessionUsageBar.Width = sessionRatio;
        SessionUsagePercentText.Text = $"{sessionRatio:F1}%";
        SessionUsageDetailText.Text = $"{CommonUtils.FormatTokens(total)} / {CommonUtils.FormatTokens(sessionLimit)} (建议上限)";
        SessionUsageBar.Background = sessionRatio >= 95
            ? new SolidColorBrush(Color.FromRgb(0xd1, 0x5a, 0x3a))
            : sessionRatio >= 80
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0xa8, 0x43))
                : new SolidColorBrush(Color.FromRgb(0x56, 0x9c, 0xd6));

        // === 调用历史 ===
        CallHistoryList.ItemsSource = _tracker.CallHistory.ToList();
        NoHistoryText.Visibility = _tracker.CallHistory.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        // 刷新余额
        RefreshBalance();
    }

    private void RefreshBalance()
    {
        if (_tracker == null) return;

        var balance = _tracker.ProviderBalance;
        if (balance != null && balance.IsAvailable)
        {
            BalanceSection.Visibility = Visibility.Visible;
            NoBalanceText.Visibility = Visibility.Collapsed;
            BalanceTotalText.Text = $"¥{balance.TotalBalance:F2} {balance.Currency}";
            BalanceGrantedText.Text = $"赠送: ¥{balance.GrantedBalance:F2}";
            BalanceToppedUpText.Text = $"充值: ¥{balance.ToppedUpBalance:F2}";
            BalanceProviderText.Text = $"🤖 {balance.ProviderId}";
            BalanceFetchTimeText.Text = $"更新时间: {balance.FetchedAt:HH:mm:ss}";
        }
        else if (balance != null && !balance.IsAvailable)
        {
            BalanceSection.Visibility = Visibility.Visible;
            NoBalanceText.Visibility = Visibility.Collapsed;
            BalanceTotalText.Text = "获取失败";
            BalanceGrantedText.Text = balance.ErrorMessage ?? "未知错误";
            BalanceToppedUpText.Text = "";
            BalanceProviderText.Text = "";
            BalanceFetchTimeText.Text = "";
        }
        else
        {
            BalanceSection.Visibility = Visibility.Visible;
            NoBalanceText.Visibility = Visibility.Visible;
            BalanceTotalText.Text = "—";
            BalanceGrantedText.Text = "";
            BalanceToppedUpText.Text = "";
            BalanceProviderText.Text = "";
            BalanceFetchTimeText.Text = "";
        }
    }

    private static void UpdateQuotaBar(Border bar, TextBlock percentText, TextBlock detailText,
        double usagePercent, int used, int total, int remaining, string label)
    {
        if (total <= 0)
        {
            bar.Width = 0;
            percentText.Text = "—";
            detailText.Text = "无数据";
            return;
        }

        var ratio = Math.Max(0, Math.Min(100, usagePercent));
        bar.Width = ratio; // 父容器宽度为1单位时用作百分比
        percentText.Text = $"{usagePercent:F1}%";

        // 颜色
        bar.Background = usagePercent >= 95
            ? new SolidColorBrush(Color.FromRgb(0xd1, 0x5a, 0x3a))
            : usagePercent >= 80
                ? new SolidColorBrush(Color.FromRgb(0xd4, 0xa8, 0x43))
                : new SolidColorBrush(Color.FromRgb(0x56, 0x9c, 0xd6));

        detailText.Text = $"已用: {CommonUtils.FormatTokens(used)} / 总计: {CommonUtils.FormatTokens(total)}  |  剩余: {CommonUtils.FormatTokens(remaining)} {label}";
    }
}
