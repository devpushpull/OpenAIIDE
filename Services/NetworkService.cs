using System.Net.NetworkInformation;
using System.Net.Http;

namespace AIIDEWPF.Services;

/// <summary>
/// 网络连接管理服务，用于检测网络状态并在断网时提示用户
/// </summary>
public class NetworkService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public bool IsConnected { get; private set; }
    public event Action<bool>? NetworkStatusChanged;

    public NetworkService()
    {
        IsConnected = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        LogService.Instance.Info($"网络状态: {(IsConnected ? "已连接" : "未连接")}", "Network");
    }

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var prev = IsConnected;
        IsConnected = e.IsAvailable;
        if (prev != IsConnected)
        {
            LogService.Instance.Info($"网络状态变更: {(IsConnected ? "已连接" : "已断开")}", "Network");
            NetworkStatusChanged?.Invoke(IsConnected);
        }
    }

    /// <summary>
    /// 联网检测：尝试访问百度首页，返回是否连通
    /// </summary>
    public async Task<bool> CheckConnectivityAsync()
    {
        try
        {
            var response = await _http.GetAsync("https://www.baidu.com");
            IsConnected = response.IsSuccessStatusCode;
        }
        catch
        {
            IsConnected = false;
        }
        LogService.Instance.Info($"联网检测结果: {(IsConnected ? "可达" : "不可达")}", "Network");
        return IsConnected;
    }

    /// <summary>
    /// 确保网络可用，不可用时提示用户并返回 false
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(string operationName = "网络操作")
    {
        if (!IsConnected)
        {
            // 再试一次
            await CheckConnectivityAsync();
        }
        if (!IsConnected)
        {
            LogService.Instance.Warn($"{operationName} 需要网络连接，但当前未联网", "Network");
            return false;
        }
        return true;
    }

    /// <summary>
    /// 带网络保护执行异步操作，断网时自动捕获异常并记录
    /// </summary>
    public async Task<T?> SafeExecuteAsync<T>(Func<Task<T>> action, string operationName = "网络操作")
    {
        try
        {
            if (!await EnsureConnectedAsync(operationName))
                return default;
            return await action();
        }
        catch (HttpRequestException ex)
        {
            LogService.Instance.Warn($"{operationName} 网络请求失败: {ex.Message}", "Network");
            IsConnected = false;
            return default;
        }
        catch (TaskCanceledException)
        {
            LogService.Instance.Warn($"{operationName} 请求超时", "Network");
            IsConnected = false;
            return default;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"{operationName} 异常: {ex.Message}", "Network");
            return default;
        }
    }

    // ===== 网络诊断 =====

    /// <summary>
    /// 运行网络诊断，测试多个目标端点和服务的连通性。
    /// 对标 Qoder 的网络诊断功能。
    /// </summary>
    public async Task<List<NetworkDiagResult>> RunDiagnosticsAsync()
    {
        var results = new List<NetworkDiagResult>();

        // 1. 基础网络检测
        results.Add(await TestConnectivityAsync("互联网连接", "https://www.baidu.com", DiagTargetType.General));
        results.Add(await TestConnectivityAsync("Google", "https://www.google.com", DiagTargetType.General));

        // 2. DNS 解析
        results.Add(await TestDnsAsync("DNS解析 (baidu.com)", "www.baidu.com"));
        results.Add(await TestDnsAsync("DNS解析 (github.com)", "github.com"));

        // 3. AI 服务端点
        results.Add(await TestConnectivityAsync("DeepSeek API", "https://api.deepseek.com/v1/models", DiagTargetType.AIService));
        results.Add(await TestConnectivityAsync("OpenAI API", "https://api.openai.com/v1/models", DiagTargetType.AIService));

        // 4. 延迟测试 (Ping)
        results.Add(await TestPingAsync("延迟 (baidu.com)", "www.baidu.com"));
        results.Add(await TestPingAsync("延迟 (api.deepseek.com)", "api.deepseek.com"));

        return results;
    }

    // ===== 启动诊断统一入口 =====

    /// <summary>
    /// 启动时统一的诊断入口：网络连通性 + 运行环境检测。
    /// 所有调用方（App启动、设置关于页、手动诊断）统一走此方法。
    /// </summary>
    /// <param name="quickMode">快速模式：仅做基础连通检测，跳过完整诊断链</param>
    public async Task<StartupDiagResult> RunStartupDiagnosticsAsync(bool quickMode = false)
    {
        var result = new StartupDiagResult();

        // 1. 网络连通性（始终执行）
        result.IsNetworkAvailable = await CheckConnectivityAsync();

        // 2. 运行环境检测 (.NET / Git)
        result.Environment = await EnvironmentCheckService.CheckAllAsync();

        // 3. 完整网络诊断（仅非快速模式）
        if (!quickMode)
        {
            result.NetworkDiags = await RunDiagnosticsAsync();
        }

        // 4. 生成用户提示语
        result.SummaryMessage = BuildStartupDiagMessage(result);

        return result;
    }

    /// <summary>构建启动诊断的用户提示消息</summary>
    public static string BuildStartupDiagMessage(StartupDiagResult result)
    {
        var lines = new List<string>();

        // 网络状态
        if (!result.IsNetworkAvailable)
        {
            lines.Add("⚠️ 网络连接不可用");
            lines.Add("   → AI 对话、联网搜索等功能将无法使用");
            lines.Add("   → 请检查网络连接后重启应用");
            lines.Add("");
        }

        // 环境状态
        if (result.Environment != null && !result.Environment.IsAllReady)
        {
            lines.Add(EnvironmentCheckService.GetMissingEnvironmentMessage(result.Environment));
        }
        else if (result.IsNetworkAvailable && result.Environment?.IsAllReady == true)
        {
            lines.Add("✅ 启动诊断通过");
            if (result.Environment != null)
            {
                lines.Add($"   网络: 已连接");
                lines.Add($"   .NET: {result.Environment.DotNet.Version}");
                lines.Add($"   Git:  {result.Environment.Git.Version}");
            }
        }

        return string.Join('\n', lines);
    }

    private async Task<NetworkDiagResult> TestConnectivityAsync(string name, string url, DiagTargetType type)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();
            return new NetworkDiagResult
            {
                Name = name,
                TargetType = type,
                Status = response.IsSuccessStatusCode ? DiagStatus.Success : DiagStatus.Warning,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Detail = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            };
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return new NetworkDiagResult { Name = name, TargetType = type, Status = DiagStatus.Failed, LatencyMs = (int)sw.ElapsedMilliseconds, Detail = "连接超时 (8s)" };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return new NetworkDiagResult { Name = name, TargetType = type, Status = DiagStatus.Failed, LatencyMs = (int)sw.ElapsedMilliseconds, Detail = $"请求失败: {ex.Message}" };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NetworkDiagResult { Name = name, TargetType = type, Status = DiagStatus.Failed, LatencyMs = (int)sw.ElapsedMilliseconds, Detail = ex.Message };
        }
    }

    private async Task<NetworkDiagResult> TestDnsAsync(string name, string host)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
            sw.Stop();
            return new NetworkDiagResult
            {
                Name = name,
                TargetType = DiagTargetType.DNS,
                Status = DiagStatus.Success,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Detail = string.Join(", ", addresses.Take(3).Select(a => a.ToString()))
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NetworkDiagResult { Name = name, TargetType = DiagTargetType.DNS, Status = DiagStatus.Failed, LatencyMs = (int)sw.ElapsedMilliseconds, Detail = ex.Message };
        }
    }

    private async Task<NetworkDiagResult> TestPingAsync(string name, string host)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(host, 3000);
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                return new NetworkDiagResult { Name = name, TargetType = DiagTargetType.Latency, Status = DiagStatus.Success, LatencyMs = (int)reply.RoundtripTime, Detail = $"TTL={reply.Options?.Ttl}" };
            else
                return new NetworkDiagResult { Name = name, TargetType = DiagTargetType.Latency, Status = DiagStatus.Failed, LatencyMs = 0, Detail = reply.Status.ToString() };
        }
        catch (Exception ex)
        {
            return new NetworkDiagResult { Name = name, TargetType = DiagTargetType.Latency, Status = DiagStatus.Failed, LatencyMs = 0, Detail = ex.Message };
        }
    }
}

/// <summary>网络诊断结果</summary>
public class NetworkDiagResult
{
    public string Name { get; init; } = string.Empty;
    public DiagTargetType TargetType { get; init; }
    public DiagStatus Status { get; init; }
    public int LatencyMs { get; init; }
    public string Detail { get; init; } = string.Empty;

    public string StatusIcon => Status switch
    {
        DiagStatus.Success => "\u2705",
        DiagStatus.Warning => "\u26A0\uFE0F",
        DiagStatus.Failed => "\u274C",
        DiagStatus.Running => "\u23F3",
        _ => "\u2753"
    };

    public string LatencyDisplay => Status == DiagStatus.Running ? "..." : LatencyMs > 0 ? $"{LatencyMs}ms" : "-";
}

public enum DiagStatus { Running, Success, Warning, Failed }

public enum DiagTargetType { General, DNS, AIService, Latency }

/// <summary>启动诊断统一结果 —— 合并网络状态 + 运行环境</summary>
public class StartupDiagResult
{
    /// <summary>网络是否可用</summary>
    public bool IsNetworkAvailable { get; set; }

    /// <summary>运行环境检测结果 (.NET / Git)</summary>
    public EnvironmentCheckResult? Environment { get; set; }

    /// <summary>完整网络诊断明细（快速模式为 null）</summary>
    public List<NetworkDiagResult>? NetworkDiags { get; set; }

    /// <summary>用户提示消息</summary>
    public string SummaryMessage { get; set; } = string.Empty;

    /// <summary>是否需要显示警告提示</summary>
    public bool HasIssues => !IsNetworkAvailable || (Environment?.IsAllReady == false);

    /// <summary>一切正常</summary>
    public bool IsAllGood => IsNetworkAvailable && (Environment?.IsAllReady == true);
}
