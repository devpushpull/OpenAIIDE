using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AIIDEWPF.Services;

namespace AIIDEWPF.ViewModels;

public class LoginViewModel : INotifyPropertyChanged
{
    private readonly AuthService _auth;
    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoggingIn;
    private bool _rememberMe = true;

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;
        LoginCommand = new RelayCommand(_ => DoLogin());
    }

    public string Username { get => _username; set { _username = value; OnPropertyChanged(); ErrorMessage = string.Empty; } }
    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }
    public bool IsLoggingIn { get => _isLoggingIn; set { _isLoggingIn = value; OnPropertyChanged(); } }
    public bool RememberMe { get => _rememberMe; set { _rememberMe = value; OnPropertyChanged(); } }

    public ICommand LoginCommand { get; }

    public event Action? LoginSucceeded;

    private void DoLogin()
    {
        if (IsLoggingIn) return;
        ErrorMessage = string.Empty;

        var password = PasswordFromWindow ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = "请输入用户名和密码";
            return;
        }

        IsLoggingIn = true;
        try
        {
            // 先检查用户是否存在
            if (!_auth.UserExists(Username.Trim()))
            {
                ErrorMessage = "用户不存在，请联系超级管理员（开发者）开通账号";
                return;
            }

            var user = _auth.Login(Username.Trim(), password, RememberMe);

            if (user == null)
            {
                // 优先使用 AuthService 提供的详细错误信息（如管理员密钥缺失）
                ErrorMessage = _auth.LoginErrorMessage ?? "密码错误";
                return;
            }

            LoginSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"登录失败: {ex.Message}";
            LogService.Instance.Error($"登录异常: {ex}", "Auth");
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    /// <summary>由 code-behind 在点击登录时设置当前密码</summary>
    public string? PasswordFromWindow { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
