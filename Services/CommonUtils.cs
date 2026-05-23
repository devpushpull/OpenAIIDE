using System.Text;

namespace AIIDEWPF.Services;

/// <summary>
/// 全局公共工具方法 —— 消除项目中分散的重复代码
/// 包含：字符串截断、CJK检测、路径转义、Token格式化等
/// </summary>
public static class CommonUtils
{
    // ==================== 字符串截断 ====================

    /// <summary>按最大长度截断字符串</summary>
    public static string Truncate(string text, int maxLen) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLen ? text : text[..maxLen];

    /// <summary>按单词边界截断（用于摘要）</summary>
    public static string TruncateByWords(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (text.Length <= maxChars) return text;
        return text[..maxChars] + "...";
    }

    // ==================== CJK 字符检测 ====================

    /// <summary>判断字符是否为 CJK（中日韩统一表意文字）</summary>
    public static bool IsCJK(char c) =>
        c >= 0x4E00 && c <= 0x9FFF ||   // CJK Unified
        c >= 0x3400 && c <= 0x4DBF ||   // CJK Ext-A
        c >= 0x3000 && c <= 0x303F ||   // CJK Symbols
        c >= 0xFF00 && c <= 0xFFEF ||   // Half/Full-width
        c >= 0xF900 && c <= 0xFAFF;     // CJK Compat

    /// <summary>判断字符是否为中文标点</summary>
    public static bool IsCNPunctuation(char c) =>
        c == '，' || c == '。' || c == '！' || c == '？' || c == '；' || c == '：';

    // ==================== 路径/JSON 转义 ====================

    /// <summary>转义字符串用于 JSON 输出（转义反斜杠和双引号）</summary>
    public static string EscapeForJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ==================== Token 格式化 ====================

    /// <summary>格式化 token 数量显示（如 1.5K, 2.3M）</summary>
    public static string FormatTokens(long tokens)
    {
        if (tokens < 0) return "—";
        if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:F1}M";
        if (tokens >= 1_000) return $"{tokens / 1000.0:F1}K";
        return tokens.ToString();
    }

    // ==================== 安全解析 ====================

    /// <summary>安全解析整数，失败返回 null</summary>
    public static int? TryParseInt(string? value)
    {
        if (!string.IsNullOrEmpty(value) && int.TryParse(value, out var v))
            return v;
        return null;
    }

    // ==================== 命令行参数转义 ====================

    /// <summary>转义命令行参数（Windows 兼容），包裹双引号</summary>
    public static string EscapeCmdArg(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        var escaped = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    // ==================== 脱敏处理 ====================

    /// <summary>脱敏处理：保留首尾2个字符，中间用星号替换</summary>
    public static string MaskSensitive(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= 6) return text[..1] + "***" + text[^1..];
        return text[..2] + "****" + text[^2..];
    }

    // ==================== 消息构建 ====================

    /// <summary>构建多行消息</summary>
    public static string BuildMessage(params string[] lines) =>
        string.Join('\n', lines);

    /// <summary>使用 StringBuilder 高效拼接字符串</summary>
    public static string BuildString(Action<StringBuilder> build)
    {
        var sb = new StringBuilder();
        build(sb);
        return sb.ToString();
    }
}
