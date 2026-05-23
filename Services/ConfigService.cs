using System.IO;
using System.Text.Json;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

public class ConfigService
{
    private readonly string _configDir;
    private readonly string _configPath;
    private AppConfig _config;

    public ConfigService()
    {
        _configDir = AppEnvironment.SettingDir;
        _configPath = Path.Combine(_configDir, "aiide-config.json");
        _config = Load();
    }

    /// <summary>安全重置时触发（换机/换用户导致 API Key 被清除时通知 UI）</summary>
    public event Action? OnSecurityReset;

    /// <summary>清除所有 API Key 配置（换机/换用户时调用）</summary>
    private static void ClearAllApiKeys(AppConfig cfg)
    {
        cfg.AI.ApiKey = string.Empty;
        foreach (var kv in cfg.Providers)
            kv.Value.ApiKey = string.Empty;
    }

    public AppConfig GetConfig() => _config;

    public AIConfig GetAIConfig() => _config.AI;
    public EditorConfig GetEditorConfig() => _config.Editor;

    // ─── AI 配置强类型 setter ───
    public void SetAIProvider(string providerId) { _config.AI.Provider = providerId; Save(); }
    public void SetAIModel(string modelId) { _config.AI.Model = modelId; Save(); }
    public void SetTerminalExecutionPreference(TerminalExecutionPreference val) { _config.AI.TerminalExecutionPreference = val; Save(); }
    public void SetAutoWebSearch(bool enabled) { _config.AI.AutoWebSearch = enabled; Save(); }

    // ─── 编辑器配置强类型 setter ───
    public void SetFontSize(double size) { _config.Editor.FontSize = size; Save(); }
    public void SetEditorTheme(string theme) { _config.Editor.Theme = theme; Save(); }
    public void SetWordWrap(bool enabled) { _config.Editor.WordWrap = enabled; Save(); }
    public void SetShowMinimap(bool enabled) { _config.Editor.ShowMinimap = enabled; Save(); }
    public void SetTabSize(int size) { _config.Editor.TabSize = size; Save(); }

    /// <summary>加密配置中的所有敏感字段（加密后写入磁盘）</summary>
    private static void EncryptSensitiveFields(AppConfig cfg)
    {
        if (!string.IsNullOrEmpty(cfg.AI.ApiKey))
            cfg.AI.ApiKey = SecureConfigHelper.Encrypt(cfg.AI.ApiKey);
        foreach (var kv in cfg.Providers)
        {
            if (!string.IsNullOrEmpty(kv.Value.ApiKey))
                kv.Value.ApiKey = SecureConfigHelper.Encrypt(kv.Value.ApiKey);
        }
    }

    /// <summary>解密配置中的所有敏感字段（加载后恢复明文）</summary>
    private static void DecryptSensitiveFields(AppConfig cfg)
    {
        if (!string.IsNullOrEmpty(cfg.AI.ApiKey))
            cfg.AI.ApiKey = SecureConfigHelper.Decrypt(cfg.AI.ApiKey);
        foreach (var kv in cfg.Providers)
        {
            if (!string.IsNullOrEmpty(kv.Value.ApiKey))
                kv.Value.ApiKey = SecureConfigHelper.Decrypt(kv.Value.ApiKey);
        }
    }

    public void AddRecentProject(string path)
    {
        _config.RecentProjects.Remove(path);
        _config.RecentProjects.Insert(0, path);
        if (_config.RecentProjects.Count > 10)
            _config.RecentProjects = _config.RecentProjects.Take(10).ToList();
        Save();
    }

    private AppConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                // 反序列化后做基础校验
                ValidateConfig(cfg);

                // ═══ 机器/用户指纹检查：检测换机或换用户 ═══
                if (!SecureConfigHelper.ValidateFingerprint(cfg.MachineFingerprint))
                {
                    LogService.Instance.Warn("检测到机器或用户账户变更，为保护 API Key 安全，已自动清除所有 API Key 配置。请重新配置。", "Security");
                    ClearAllApiKeys(cfg);
                    OnSecurityReset?.Invoke(); // 通知 UI 提示用户
                }
                // 更新指纹（每次启动都刷新）
                cfg.MachineFingerprint = SecureConfigHelper.GenerateFingerprint();

                // 检测是否有明文 Key 需要迁移加密
                bool needsMigration = HasPlaintextSensitiveFields(cfg);
                // 解密磁盘上的 API Key（DPAPI 加密 → 明文）
                DecryptSensitiveFields(cfg);
                // 迁移：如果 Key 还是明文，自动加密保存
                if (needsMigration)
                {
                    _config = cfg;
                    LogService.Instance.Info("检测到明文 API Key，正在自动加密...", "Config");
                    Save();
                }
                LogService.Instance.Info($"配置已加载: {_configPath}", "Config");
                return cfg;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"配置加载失败: {ex.Message}", "Config");
        }
        return new AppConfig();
    }

    /// <summary>检测配置中是否存在明文（未加密）的敏感字段</summary>
    private static bool HasPlaintextSensitiveFields(AppConfig cfg)
    {
        if (!string.IsNullOrEmpty(cfg.AI.ApiKey) && !cfg.AI.ApiKey.StartsWith("DPAPI:"))
            return true;
        foreach (var kv in cfg.Providers)
        {
            if (!string.IsNullOrEmpty(kv.Value.ApiKey) && !kv.Value.ApiKey.StartsWith("DPAPI:"))
                return true;
        }
        return false;
    }

    /// <summary>反序列化后的基础合理性校验，修复异常值</summary>
    private static void ValidateConfig(AppConfig cfg)
    {
        // AI 配置校验
        if (cfg.AI == null) cfg.AI = new AIConfig();
        if (string.IsNullOrWhiteSpace(cfg.AI.Provider)) cfg.AI.Provider = "deepseek";
        if (string.IsNullOrWhiteSpace(cfg.AI.Model)) cfg.AI.Model = "deepseek-v4-pro";
        if (cfg.AI.MaxTokens < 0) cfg.AI.MaxTokens = 0;

        // 编辑器配置校验
        if (cfg.Editor == null) cfg.Editor = new EditorConfig();
        if (cfg.Editor.FontSize < 8 || cfg.Editor.FontSize > 72) cfg.Editor.FontSize = 14;
        if (cfg.Editor.TabSize < 1 || cfg.Editor.TabSize > 16) cfg.Editor.TabSize = 4;

        // 其他配置子对象补全
        cfg.Appearance ??= new AppearanceConfig();
        cfg.Terminal ??= new TerminalConfig();
        cfg.FileExclude ??= new FileExcludeConfig();
        cfg.Privacy ??= new PrivacyConfig();
        cfg.Proxy ??= new ProxyConfig();
        cfg.RecentProjects ??= new List<string>();
        cfg.Providers ??= new Dictionary<string, ProviderDef>();

        // 隐私配置校验
        if (cfg.Privacy.HistoryRetentionDays < 1) cfg.Privacy.HistoryRetentionDays = 30;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_configDir);
            // 序列化前临时加密 API Key，保存后恢复明文
            EncryptSensitiveFields(_config);
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            // 恢复内存中的明文，确保运行时正常使用
            DecryptSensitiveFields(_config);
        }
        catch (Exception ex)
        {
            // 即使保存失败也尝试恢复明文
            DecryptSensitiveFields(_config);
            LogService.Instance.Error($"配置保存失败: {ex.Message}", "Config");
        }
    }
}
