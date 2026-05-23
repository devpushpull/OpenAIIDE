using System.Windows;
using AIIDEWPF.Services;
using AIIDEWPF.ViewModels;

namespace AIIDEWPF.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(AuthService auth)
    {
        InitializeComponent();

        _vm = new LoginViewModel(auth);
        DataContext = _vm;

        // 登录成功 → 关闭窗口
        _vm.LoginSucceeded += () =>
        {
            DialogResult = true;
            Close();
        };

        // 回车键登录
        KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                DoLogin();
        };

        // 用户名框获取焦点
        Loaded += (s, e) => UsernameBox.Focus();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        DoLogin();
    }

    private void DoLogin()
    {
        _vm.PasswordFromWindow = PasswordBox.Password;
        _vm.LoginCommand.Execute(null);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
