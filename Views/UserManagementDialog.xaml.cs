using System.IO;
using System.Windows;
using System.Windows.Media;
using AIIDEWPF.Services;
using AIIDEWPF.ViewModels;

namespace AIIDEWPF.Views;

public partial class UserManagementDialog : Window
{
    private readonly UserManagementViewModel _vm;

    public UserManagementDialog(DatabaseService db, AuthService auth)
    {
        InitializeComponent();
        _vm = new UserManagementViewModel(db, auth);
        DataContext = _vm;

        // 确保有 Owner（调用方可能已设置，此处做兜底）
        if (Owner == null)
            Owner = Application.Current.MainWindow;

        // 加载时显示密钥状态
        Loaded += (s, e) => RefreshKeyStatus();
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var pwd = NewUserPasswordBox.Password;
        if (!string.IsNullOrWhiteSpace(pwd))
        {
            _vm.NewPassword = pwd;
            NewUserPasswordBox.Password = string.Empty;
        }
        _vm.AddUserCommand.Execute(null);
    }

    private void GenerateKey_Click(object sender, RoutedEventArgs e)
    {
        var keyPath = AuthService.AdminKeyPath;
        if (File.Exists(keyPath))
        {
            var result = MessageBox.Show(
                $"密钥文件已存在，是否覆盖重新生成？\n\n路径: {keyPath}",
                "确认覆盖", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        if (AuthService.GenerateAdminKey())
        {
            MessageBox.Show($"管理员密钥已生成！\n\n路径: {keyPath}\n\n⚠ 请妥善保管，分发普通用户版本时请移除此文件。",
                "生成成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"密钥生成失败，请检查目录权限。\n\n路径: {keyPath}",
                "生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshKeyStatus();
    }

    private void RefreshKeyStatus()
    {
        var keyPath = AuthService.AdminKeyPath;
        if (File.Exists(keyPath))
        {
            KeyStatusText.Text = $"✅ 密钥已就绪 | 路径: {keyPath}";
            KeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xc0, 0x60));
        }
        else
        {
            KeyStatusText.Text = $"⚠ 密钥不存在 | 管理员将无法登录";
            KeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xf0, 0xa0, 0x60));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
