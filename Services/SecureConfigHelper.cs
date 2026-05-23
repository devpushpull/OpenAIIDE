using System.Security.Cryptography;
using System.Text;

namespace AIIDEWPF.Services;

/// <summary>
/// 配置敏感信息加密帮助类 —— 使用 Windows DPAPI 加密/解密 API Key 等敏感字段。
/// 加密后的内容仅限当前 Windows 用户账户在当前机器上解密。
/// </summary>
public static class SecureConfigHelper
{
    private const string DpapiPrefix = "DPAPI:";
    private const string FingerprintPrefix = "FP:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("");
    private static readonly byte[] FingerprintEntropy = Encoding.UTF8.GetBytes("");

    /// <summary>加密明文，返回 "DPAPI:base64" 格式的密文</summary>
    public static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (plaintext.StartsWith(DpapiPrefix)) return plaintext; // already encrypted

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>解密密文，返回明文。若非加密格式则原样返回。
    /// 解密失败时返回空字符串并触发 OnDecryptFailed 事件。</summary>
    public static string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;
        if (!ciphertext.StartsWith(DpapiPrefix)) return ciphertext; // plaintext (backward compat)

        try
        {
            var base64 = ciphertext[DpapiPrefix.Length..];
            var encryptedBytes = Convert.FromBase64String(base64);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"API Key 解密失败: {ex.Message}. 可能需要重新配置。", "SecureConfig");
            OnDecryptFailed?.Invoke(ex.Message);
            return string.Empty; // decryption failed, return empty to avoid using garbage
        }
    }

    /// <summary>解密失败时触发（UI 可订阅此事件提示用户）</summary>
    public static event Action<string>? OnDecryptFailed;

    // ==================== 机器/用户指纹 ====================

    /// <summary>生成当前机器的唯一指纹（MachineName + UserName + User SID 的哈希），DPAPI 加密后返回</summary>
    public static string GenerateFingerprint()
    {
        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "";
            var raw = $"{Environment.MachineName}|{Environment.UserName}|{sid}";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            var encrypted = ProtectedData.Protect(hashBytes, FingerprintEntropy, DataProtectionScope.CurrentUser);
            return FingerprintPrefix + Convert.ToBase64String(encrypted);
        }
        catch
        {
            return FingerprintPrefix + "ERROR";
        }
    }

    /// <summary>验证指纹是否与当前机器/用户匹配。false = 换了机器或换了用户</summary>
    public static bool ValidateFingerprint(string? storedFingerprint)
    {
        if (string.IsNullOrEmpty(storedFingerprint)) return true; // 无指纹 = 首次启动，视为匹配
        if (!storedFingerprint.StartsWith(FingerprintPrefix)) return true; // 旧格式，兼容

        try
        {
            var base64 = storedFingerprint[FingerprintPrefix.Length..];
            var encryptedBytes = Convert.FromBase64String(base64);
            var storedHash = ProtectedData.Unprotect(encryptedBytes, FingerprintEntropy, DataProtectionScope.CurrentUser);

            // 生成当前指纹的哈希并比较
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "";
            var raw = $"{Environment.MachineName}|{Environment.UserName}|{sid}";
            var currentHash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));

            return storedHash.SequenceEqual(currentHash);
        }
        catch
        {
            // DPAPI 解密失败 = 换了机器或用户
            return false;
        }
    }

    /// <summary>对日志/错误消息中的 API Key 进行脱敏处理</summary>
    public static string MaskApiKey(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 匹配 sk- 开头的常见 API Key 格式（OpenAI / DeepSeek 等）
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"(sk-[a-zA-Z0-9]{8,})", m => m.Value[..8] + "****");

        // 匹配 DPAPI: 开头的加密 Key（防止加密密文泄露）
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"(DPAPI:[A-Za-z0-9+/=]{20,})", "DPAPI:****");

        // 匹配 "ApiKey":"..." 模式中的值
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"""ApiKey""s*:s*""[^""]+""", "\"ApiKey\":\"****\"");

        return text;
    }
}
