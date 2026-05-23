using Microsoft.Data.Sqlite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>
/// 认证服务 —— 登录、改密、当前用户会话管理
/// </summary>
public class AuthService
{
    private readonly DatabaseService _db;

    // ===== 管理员密钥文件 =====
    private const string AdminKeyFileName = ".adminkey";
    // 密钥种子（与机器无关的固定值，用于生成/验证密钥文件内容）
    private const string AdminKeySeed = "AIIDE-Admin-2025-SecureKey!@#";

    /// <summary>密钥文件完整路径</summary>
    public static string AdminKeyPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AdminKeyFileName);

    // ===== 登录会话缓存 =====
    private const string SessionFileName = ".aiide_session";
    private const int SessionExpiryDays = 7; // 会话有效期7天
    private static readonly string SessionFilePath =
        Path.Combine(AppEnvironment.AppDataDir, SessionFileName);

    public AuthService(DatabaseService db)
    {
        _db = db;
    }

    public UserInfo? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser != null;
    public bool IsAdmin => CurrentUser?.Role == "super_admin";

    /// <summary>最近一次登录失败的具体原因（供 UI 展示）</summary>
    public string? LoginErrorMessage { get; private set; }

    // ===== 管理员密钥文件验证 =====

    /// <summary>计算预期密钥哈希（不绑定机器名，支持U盘/多机移植）</summary>
    private static string ComputeAdminKeyHash()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(AdminKeySeed));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// 计算旧版本机器绑定的密钥哈希（用于向后兼容迁移）
    /// </summary>
    private static string ComputeLegacyAdminKeyHash()
    {
        var input = AdminKeySeed + "_" + Environment.MachineName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>检查管理员密钥文件是否存在且有效</summary>
    public bool ValidateAdminKey()
    {
        try
        {
            if (!File.Exists(AdminKeyPath))
                return false;
            var content = File.ReadAllText(AdminKeyPath).Trim();

            // 优先匹配新版可移植哈希
            if (content == ComputeAdminKeyHash())
                return true;

            // 向后兼容：如果是旧版机器绑定哈希，自动迁移为可移植格式
            if (content == ComputeLegacyAdminKeyHash())
            {
                var portableHash = ComputeAdminKeyHash();
                File.WriteAllText(AdminKeyPath, portableHash, Encoding.UTF8);
                LogService.Instance.Info("管理员密钥已从旧版（机器绑定）自动迁移为可移植格式", "Auth");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>生成管理员密钥文件（开发人员使用）</summary>
    public static bool GenerateAdminKey()
    {
        try
        {
            var hash = ComputeAdminKeyHash();
            File.WriteAllText(AdminKeyPath, hash, Encoding.UTF8);
            // 设置隐藏属性，降低普通用户发现概率
            File.SetAttributes(AdminKeyPath, File.GetAttributes(AdminKeyPath) | FileAttributes.Hidden);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>登录验证，成功返回用户信息，失败返回 null</summary>
    public UserInfo? Login(string username, string password, bool rememberMe = false)
    {
        LoginErrorMessage = null;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash, Role FROM Users WHERE Username = @u";
        cmd.Parameters.AddWithValue("@u", username.Trim());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var hash = reader.GetString(2);
        if (!DatabaseService.VerifyPassword(password, hash)) return null;

        var user = new UserInfo
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            Role = reader.GetString(3)
        };

        // 超级管理员：密钥文件存在时必须有效，不存在时首次放行
        if (user.IsAdmin && File.Exists(AdminKeyPath) && !ValidateAdminKey())
        {
            LoginErrorMessage = "管理员密钥文件已损坏，\n请在用户管理中重新生成密钥。";
            LogService.Instance.Warn($"管理员 {user.Username} 登录被拒：密钥文件无效");
            return null;
        }

        CurrentUser = user;

        // 更新最后登录时间
        UpdateLastLogin(user.Id);

        // 保存登录会话（如果选择了记住我）
        if (rememberMe)
            SaveLoginSession(user.Username);

        LogService.Instance.Info($"用户登录: {user.Username} ({user.Role})");
        return user;
    }

    /// <summary>修改当前用户密码</summary>
    public bool ChangePassword(string oldPassword, string newPassword)
    {
        if (CurrentUser == null) return false;
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 3)
            return false;

        // 验证旧密码
        var conn = _db.GetConnection();
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT PasswordHash FROM Users WHERE Id = @id";
        checkCmd.Parameters.AddWithValue("@id", CurrentUser.Id);
        var oldHash = (string?)checkCmd.ExecuteScalar();
        if (oldHash == null || !DatabaseService.VerifyPassword(oldPassword, oldHash))
            return false;

        var newHash = DatabaseService.HashPassword(newPassword);
        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE Id = @id";
        updateCmd.Parameters.AddWithValue("@hash", newHash);
        updateCmd.Parameters.AddWithValue("@id", CurrentUser.Id);
        updateCmd.ExecuteNonQuery();

        LogService.Instance.Info($"密码已修改: {CurrentUser.Username}");
        return true;
    }

    /// <summary>管理员重置指定用户密码</summary>
    public bool ResetUserPassword(int userId, string newPassword)
    {
        if (!IsAdmin) return false;
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 3)
            return false;

        var conn = _db.GetConnection();
        var newHash = DatabaseService.HashPassword(newPassword);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE Id = @id";
        cmd.Parameters.AddWithValue("@hash", newHash);
        cmd.Parameters.AddWithValue("@id", userId);
        var rows = cmd.ExecuteNonQuery();

        if (rows > 0)
            LogService.Instance.Info($"管理员重置了用户 ID={userId} 的密码");
        return rows > 0;
    }

    /// <summary>检查用户名是否存在</summary>
    public bool UserExists(string username)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = @u";
        cmd.Parameters.AddWithValue("@u", username.Trim());
        return (long)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>登出</summary>
    public void Logout()
    {
        if (CurrentUser != null)
            LogService.Instance.Info($"用户登出: {CurrentUser.Username}");
        CurrentUser = null;
        ClearLoginSession();
    }

    // ===== 登录会话持久化 =====

    /// <summary>保存登录会话到加密本地文件（记住我）</summary>
    public void SaveLoginSession(string username)
    {
        try
        {
            var session = new LoginSessionData
            {
                Username = username,
                LoginTime = DateTime.Now,
                ExpiryTime = DateTime.Now.AddDays(SessionExpiryDays),
                MachineName = Environment.MachineName
            };
            var json = JsonSerializer.Serialize(session);
            // 简单混淆存储（非安全加密，仅防止明文暴露）
            var encrypted = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(AppEnvironment.AppDataDir);
            File.WriteAllText(SessionFilePath, encrypted, Encoding.UTF8);
            LogService.Instance.Info($"登录会话已保存 (有效期{SessionExpiryDays}天)", "Auth");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"保存登录会话失败: {ex.Message}", "Auth");
        }
    }

    /// <summary>尝试自动登录（从缓存恢复会话）</summary>
    /// <returns>是否成功自动登录</returns>
    public bool TryAutoLogin()
    {
        try
        {
            if (!File.Exists(SessionFilePath))
                return false;

            var encrypted = File.ReadAllText(SessionFilePath, Encoding.UTF8);
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
            var session = JsonSerializer.Deserialize<LoginSessionData>(json);

            if (session == null || string.IsNullOrEmpty(session.Username))
                return false;

            // 检查是否过期
            if (DateTime.Now > session.ExpiryTime)
            {
                LogService.Instance.Info($"登录会话已过期 (过期时间: {session.ExpiryTime:yyyy-MM-dd HH:mm})", "Auth");
                ClearLoginSession();
                return false;
            }

            // 检查机器是否匹配（不同机器/用户账户的DPAPI保护数据不同，天然隔离）
            if (session.MachineName != Environment.MachineName)
            {
                LogService.Instance.Warn("登录会话机器名不匹配，已清除", "Auth");
                ClearLoginSession();
                return false;
            }

            // 用户必须仍然存在于数据库中
            if (!UserExists(session.Username))
            {
                LogService.Instance.Warn($"自动登录失败: 用户 {session.Username} 不存在", "Auth");
                ClearLoginSession();
                return false;
            }

            // 创建一个简单的 UserInfo 实例（不需要密码验证，因为会话已验证）
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username, Role FROM Users WHERE Username = @u";
            cmd.Parameters.AddWithValue("@u", session.Username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return false;

            CurrentUser = new UserInfo
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                Role = reader.GetString(2)
            };

            // 管理员密钥仍需验证
            if (CurrentUser.IsAdmin && File.Exists(AdminKeyPath) && !ValidateAdminKey())
            {
                LogService.Instance.Warn($"自动登录失败: 管理员密钥无效", "Auth");
                CurrentUser = null;
                ClearLoginSession();
                return false;
            }

            UpdateLastLogin(CurrentUser.Id);
            LogService.Instance.Info($"自动登录成功: {CurrentUser.Username} ({CurrentUser.Role})", "Auth");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"自动登录失败: {ex.Message}", "Auth");
            ClearLoginSession();
            return false;
        }
    }

    /// <summary>清除登录会话缓存</summary>
    public void ClearLoginSession()
    {
        try
        {
            if (File.Exists(SessionFilePath))
                File.Delete(SessionFilePath);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"清除登录会话失败: {ex.Message}", "Auth");
        }
    }

    /// <summary>检查会话是否即将过期（还剩不到1天）</summary>
    public bool IsSessionExpiringSoon()
    {
        try
        {
            if (!File.Exists(SessionFilePath))
                return false;
            var encrypted = File.ReadAllText(SessionFilePath, Encoding.UTF8);
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
            var session = JsonSerializer.Deserialize<LoginSessionData>(json);
            if (session == null) return false;
            return (session.ExpiryTime - DateTime.Now).TotalDays < 1;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateLastLogin(int userId)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET LastLoginAt = datetime('now','localtime') WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>用户信息模型</summary>
public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public bool IsAdmin => Role == "super_admin";
}

/// <summary>登录会话缓存数据（序列化到本地文件）</summary>
public class LoginSessionData
{
    public string Username { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; }
    public DateTime ExpiryTime { get; set; }
    public string MachineName { get; set; } = string.Empty;
}
