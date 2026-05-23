using Microsoft.Data.Sqlite;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AIIDEWPF.Services;

/// <summary>
/// SQLite 数据库服务 —— 管理用户/记忆/规则表，初始化默认超级管理员
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public DatabaseService()
    {
        // 数据库存放在应用 setting 目录下，与应用同目录携带，方便多机共享
        var settingDir = AppEnvironment.SettingDir;
        Directory.CreateDirectory(settingDir);
        _dbPath = Path.Combine(settingDir, "users.db");

        // 从旧位置（%LOCALAPPDATA%\AIIDE\users.db）迁移到新位置
        MigrateFromLegacyLocation();

        Initialize();
    }

    /// <summary>
    /// 将旧位置（%LOCALAPPDATA%\AIIDE\users.db）的数据库迁移到新位置（setting\users.db）
    /// </summary>
    private void MigrateFromLegacyLocation()
    {
        try
        {
            var legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIIDE");
            var legacyDbPath = Path.Combine(legacyDir, "users.db");

            // 新位置已有数据库，无需迁移
            if (File.Exists(_dbPath))
                return;

            // 旧位置有数据库，迁移到新位置
            if (File.Exists(legacyDbPath))
            {
                File.Copy(legacyDbPath, _dbPath, overwrite: false);
                LogService.Instance.Info($"数据库已从旧位置迁移: {legacyDbPath} → {_dbPath}", "DB");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"数据库迁移失败（将使用新位置）: {ex.Message}", "DB");
        }
    }

    public string DbPath => _dbPath;

    private void Initialize()
    {
        try
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Users (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username    TEXT    NOT NULL UNIQUE,
                    PasswordHash TEXT   NOT NULL,
                    Role        TEXT    NOT NULL DEFAULT 'user',
                    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                    LastLoginAt TEXT
                );
                """;
            cmd.ExecuteNonQuery();

            LogService.Instance.Info($"数据库已初始化: {_dbPath}", "DB");

            // 记忆库表
            using var memCmd = _connection.CreateCommand();
            memCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Memories (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title         TEXT    NOT NULL,
                    Content       TEXT    NOT NULL,
                    Category      TEXT    NOT NULL DEFAULT 'user_preferences',
                    Scope         TEXT    NOT NULL DEFAULT 'global',
                    WorkspacePath TEXT,
                    Keywords      TEXT    NOT NULL DEFAULT '',
                    CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                    UpdatedAt     TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                );
                """;
            memCmd.ExecuteNonQuery();

            // 规则表
            using var ruleCmd = _connection.CreateCommand();
            ruleCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Rules (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name        TEXT    NOT NULL,
                    Content     TEXT    NOT NULL,
                    RuleType    TEXT    NOT NULL DEFAULT 'always',
                    GlobPattern TEXT,
                    Description TEXT,
                    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                );
                """;
            ruleCmd.ExecuteNonQuery();

            // 提示词库表
            using var promptCmd = _connection.CreateCommand();
            promptCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS PromptLibrary (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title         TEXT    NOT NULL,
                    Content       TEXT    NOT NULL,
                    Category      TEXT    NOT NULL DEFAULT 'general',
                    Scope         TEXT    NOT NULL DEFAULT 'global',
                    WorkspacePath TEXT,
                    Tags          TEXT    NOT NULL DEFAULT '',
                    UsageCount    INTEGER NOT NULL DEFAULT 0,
                    IsActive      INTEGER NOT NULL DEFAULT 1,
                    CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                    UpdatedAt     TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                );
                """;
            promptCmd.ExecuteNonQuery();

            // 学习经验表 —— 自我学习进化系统
            using var learnCmd = _connection.CreateCommand();
            learnCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS LearningExperiences (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title         TEXT    NOT NULL,
                    Content       TEXT    NOT NULL,
                    Category      TEXT    NOT NULL DEFAULT 'general',
                    Source        TEXT    NOT NULL DEFAULT 'auto_detected',
                    Confidence    REAL    NOT NULL DEFAULT 0.5,
                    IsVerified    INTEGER NOT NULL DEFAULT 0,
                    RelatedFiles  TEXT    NOT NULL DEFAULT '',
                    WorkspacePath TEXT,
                    CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                    UpdatedAt     TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                );
                """;
            learnCmd.ExecuteNonQuery();

            // 学习经验改进索引（加速按置信度排序查询）
            using var learnIdxCmd = _connection.CreateCommand();
            learnIdxCmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_learning_confidence
                ON LearningExperiences(Confidence DESC, IsVerified DESC);
                """;
            learnIdxCmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"数据库初始化失败: {ex.Message}", "DB");
            throw;
        }
    }

    public SqliteConnection GetConnection()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();
        }
        return _connection;
    }

    /// <summary>SHA256 密码哈希</summary>
    public static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>验证密码</summary>
    public static bool VerifyPassword(string password, string hash)
        => string.Equals(HashPassword(password), hash, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
    }
}
