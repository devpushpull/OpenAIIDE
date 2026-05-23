using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using AIIDEWPF.Services;

namespace AIIDEWPF.ViewModels;

public class UserManagementViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _db;
    private readonly AuthService _auth;
    private ObservableCollection<UserDisplay> _users = new();
    private string _newUsername = string.Empty;
    private string _newPassword = string.Empty;
    private string _errorMessage = string.Empty;

    public UserManagementViewModel(DatabaseService db, AuthService auth)
    {
        _db = db;
        _auth = auth;
        LoadUsers();
        AddUserCommand = new RelayCommand(_ => AddUser());
        DeleteUserCommand = new RelayCommand(param => DeleteUser(param));
        ResetPasswordCommand = new RelayCommand(param => ResetPassword(param));
        ChangeRoleCommand = new RelayCommand(param => ToggleRole(param));
    }

    public ObservableCollection<UserDisplay> Users { get => _users; set { _users = value; OnPropertyChanged(); } }
    public string NewUsername { get => _newUsername; set { _newUsername = value; OnPropertyChanged(); ErrorMessage = string.Empty; } }
    public string NewPassword { get => _newPassword; set { _newPassword = value; OnPropertyChanged(); ErrorMessage = string.Empty; } }
    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

    public ICommand AddUserCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand ChangeRoleCommand { get; }

    public void LoadUsers()
    {
        _users.Clear();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, Role, CreatedAt, LastLoginAt FROM Users ORDER BY Id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _users.Add(new UserDisplay
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Role = reader.GetString(2),
                CreatedAt = reader.GetString(3),
                LastLoginAt = reader.IsDBNull(4) ? "从未登录" : reader.GetString(4)
            });
        }
    }

    private void AddUser()
    {
        var username = NewUsername.Trim();
        var password = NewPassword.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = "用户名和密码不能为空";
            return;
        }
        if (password.Length < 3)
        {
            ErrorMessage = "密码至少 3 位";
            return;
        }

        try
        {
            var conn = _db.GetConnection();
            var hash = DatabaseService.HashPassword(password);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Users (Username, PasswordHash, Role) VALUES (@u, @h, 'user')";
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.ExecuteNonQuery();

            NewUsername = string.Empty;
            NewPassword = string.Empty;
            ErrorMessage = string.Empty;
            LoadUsers();
            LogService.Instance.Info($"管理员创建了新用户: {username}");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ErrorMessage = "用户名已存在";
        }
    }

    private void DeleteUser(object? param)
    {
        if (param is not UserDisplay user) return;
        if (user.Username == "admin")
        {
            ErrorMessage = "不能删除超级管理员";
            return;
        }

        var result = MessageBox.Show($"确定删除用户「{user.Username}」？", "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Users WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", user.Id);
        cmd.ExecuteNonQuery();

        LoadUsers();
        LogService.Instance.Info($"管理员删除了用户: {user.Username}");
    }

    private void ResetPassword(object? param)
    {
        if (param is not UserDisplay user) return;
        var newPwd = $"123456";
        if (_auth.ResetUserPassword(user.Id, newPwd))
        {
            MessageBox.Show($"用户「{user.Username}」的密码已重置为: {newPwd}\n请告知用户及时修改密码。",
                "密码已重置", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadUsers();
        }
    }

    private void ToggleRole(object? param)
    {
        if (param is not UserDisplay user) return;
        if (user.Username == "admin")
        {
            ErrorMessage = "不能修改超级管理员的角色";
            return;
        }

        var newRole = user.Role == "super_admin" ? "user" : "super_admin";
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET Role = @r WHERE Id = @id";
        cmd.Parameters.AddWithValue("@r", newRole);
        cmd.Parameters.AddWithValue("@id", user.Id);
        cmd.ExecuteNonQuery();

        LoadUsers();
        LogService.Instance.Info($"管理员修改了用户 {user.Username} 的角色为: {newRole}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class UserDisplay
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string LastLoginAt { get; set; } = string.Empty;
    public string RoleDisplay => Role == "super_admin" ? "👑 超级管理员" : "👤 普通用户";
    public string IsAdmin => Role == "super_admin" ? "是" : "否";
}
