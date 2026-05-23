using System.Windows;
using AIIDEWPF.Services;

namespace AIIDEWPF.Views;

public partial class ChangePasswordDialog : Window
{
    private readonly AuthService _auth;

    public ChangePasswordDialog(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
        Loaded += (s, e) => OldPasswordBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var oldPwd = OldPasswordBox.Password;
        var newPwd = NewPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(oldPwd) || string.IsNullOrWhiteSpace(newPwd))
        {
            ErrorText.Text = "请填写所有密码字段";
            return;
        }

        if (newPwd.Length < 3)
        {
            ErrorText.Text = "新密码至少 3 位";
            return;
        }

        if (_auth.ChangePassword(oldPwd, newPwd))
        {
            MessageBox.Show("密码修改成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        else
        {
            ErrorText.Text = "旧密码错误或不符合要求";
        }
    }
}
