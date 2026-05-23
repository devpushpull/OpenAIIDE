using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>
/// 隐私校验服务 —— 扫描用户发送内容，检测并阻断/警告敏感信息
/// 分类：绝对阻断（身份证/银行卡）+ 警告确认（token/api_key/密码）
/// </summary>
public class PrivacyService
{
    // 绝对阻断：匹配到直接拒绝发送
    private static readonly (string Pattern, string Description)[] BlockPatterns = new[]
    {
        // 中国大陆身份证号（18位 + 末位X）
        (@"\b\d{6}(19|20)\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])\d{3}[0-9Xx]\b", "身份证号码"),
        // 中国大陆身份证号（15位）
        (@"\b\d{6}\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])\d{3}\b", "身份证号码（15位）"),
        // 银行卡号（16-19位数字）
        (@"\b(\d{4}[ -]?){3,4}\d{4}\b", "银行卡号"),
        // 手机号（宽松匹配）
        (@"\b1[3-9]\d{9}\b", "手机号码"),
    };

    // 警告确认：匹配到提示用户确认后发送
    private static readonly (string Pattern, string Description)[] WarnPatterns = new[]
    {
        // API Key 模式: sk-..., ak-..., key=..., apikey=..., token=...
        (@"\b(sk-[A-Za-z0-9]{20,})\b", "OpenAI/类 OpenAI API Key (sk-...)"),
        (@"\b(ak-[A-Za-z0-9]{10,})\b", "API Key (ak-...)"),
        (@"\b(key|apikey|api_key|token|secret|password|passwd)\s*[:=]\s*['\""]?[\w\-_\.]{8,}['\""]?", "密钥/Token/密码明文"),
        // JWT Token
        (@"\beyJ[A-Za-z0-9\-_]+\.eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\b", "JWT Token"),
        // 邮箱（项目开发中可能误发）
        (@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", "邮箱地址"),
        // IP 地址
        (@"\b(?:\d{1,3}\.){3}\d{1,3}\b", "IP 地址"),
        // 密码相关
        (@"\b(password|passwd|pwd)\s*[=:]\s*\S+", "密码赋值"),
    };

    /// <summary>
    /// 校验用户输入，返回校验结果
    /// </summary>
    public PrivacyResult Validate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new PrivacyResult { IsSafe = true };

        var blocks = new List<PrivacyMatch>();
        var warns = new List<PrivacyMatch>();

        // 检查阻断模式
        foreach (var (pattern, desc) in BlockPatterns)
        {
            var matches = Regex.Matches(input, pattern, RegexOptions.IgnoreCase);
            foreach (Match m in matches)
                blocks.Add(new PrivacyMatch { Pattern = desc, MatchedText = MaskSensitive(m.Value), Index = m.Index, Length = m.Length });
        }

        // 检查警告模式
        foreach (var (pattern, desc) in WarnPatterns)
        {
            var matches = Regex.Matches(input, pattern, RegexOptions.IgnoreCase);
            foreach (Match m in matches)
                warns.Add(new PrivacyMatch { Pattern = desc, MatchedText = MaskSensitive(m.Value), Index = m.Index, Length = m.Length });
        }

        return new PrivacyResult
        {
            IsSafe = blocks.Count == 0 && warns.Count == 0,
            BlockMatches = blocks,
            WarnMatches = warns,
            ShouldBlock = blocks.Count > 0,
            ShouldWarn = warns.Count > 0
        };
    }

    /// <summary>生成阻断提示消息</summary>
    public string GetBlockMessage(PrivacyResult result)
    {
        var lines = new List<string>
        {
            "【隐私保护】检测到以下敏感信息，已阻止发送：",
            ""
        };
        foreach (var m in result.BlockMatches)
            lines.Add($"  - {m.Pattern}: {m.MatchedText}");
        lines.Add("");
        lines.Add("请移除以上敏感信息后重新发送。");
        return string.Join('\n', lines);
    }

    /// <summary>生成警告确认消息</summary>
    public string GetWarnMessage(PrivacyResult result)
    {
        var lines = new List<string>
        {
            "【隐私提醒】检测到以下可能敏感的凭据信息：",
            ""
        };
        foreach (var m in result.WarnMatches)
            lines.Add($"  - {m.Pattern}: {m.MatchedText}");
        lines.Add("");
        lines.Add("如这些是项目开发所需的账号/密码/Token，请确认后继续发送。");
        lines.Add("建议：使用环境变量或配置文件管理敏感凭据，避免在对话中明文传输。");
        return string.Join('\n', lines);
    }

    /// <summary>脱敏处理：保留首尾2个字符</summary>
    private static string MaskSensitive(string text) => CommonUtils.MaskSensitive(text);
}

/// <summary>隐私校验结果</summary>
public class PrivacyResult
{
    public bool IsSafe { get; set; }
    public bool ShouldBlock { get; set; }
    public bool ShouldWarn { get; set; }
    public List<PrivacyMatch> BlockMatches { get; set; } = new();
    public List<PrivacyMatch> WarnMatches { get; set; } = new();
}

/// <summary>隐私匹配项</summary>
public class PrivacyMatch
{
    public string Pattern { get; set; } = string.Empty;
    public string MatchedText { get; set; } = string.Empty;
    public int Index { get; set; }
    public int Length { get; set; }
}
