using System.Windows;
using AIIDEWPF.Services;
using AIIDEWPF.Views;

namespace AIIDEWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private DatabaseService? _db;
    private AuthService? _auth;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 初始化数据库和认证服务
        _db = new DatabaseService();
        _auth = new AuthService(_db);
        LogService.Instance.Info("数据库和认证服务已初始化", "App");

        // 尝试自动登录（从缓存恢复会话）
        if (_auth.TryAutoLogin())
            LogService.Instance.Info("已从缓存恢复登录会话", "App");

        // 直接启动主窗口（不强制登录）
        var mainWindow = new MainFrameworkWindow(_db, _auth);
        mainWindow.Show();
        LogService.Instance.Info("主窗口已启动", "App");

        // 后台异步运行启动诊断（网络 + 环境，统一入口，不阻塞 UI）
        _ = Task.Run(async () =>
        {
            var networkService = new NetworkService();
            var diagResult = await networkService.RunStartupDiagnosticsAsync(quickMode: true);

            if (diagResult.HasIssues)
            {
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new EnvironmentCheckDialog(diagResult)
                    {
                        Owner = mainWindow
                    };
                    dialog.ShowDialog();
                });
            }
        });

        mainWindow.Closed += (s, args) =>
        {
            _auth.Logout();
            _db.Dispose();
            LogService.Instance.Info("应用已退出，资源已释放", "App");
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Instance.Info("应用退出事件触发", "App");
        base.OnExit(e);
    }
}

