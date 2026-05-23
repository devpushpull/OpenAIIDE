namespace AIIDEWPF.Services;

/// <summary>
/// 隐私校验快捷帮助类 —— 一行代码完成隐私检测 + 阻断/警告处理
/// 封装了 PrivacyService 的常见调用模式，方便在任意位置复用
/// </summary>
public static class PrivacyHelper
{
    private static readonly PrivacyService _privacy = new();

    /// <summary>
    /// 校验文本并返回结果状态
    /// </summary>
    public enum CheckResult
    {
        Safe,       // 安全，可以发送
        Blocked,    // 被阻断（含敏感身份信息）
        Warned,     // 有警告（含凭据类信息），需要用户确认
    }

    /// <summary>
    /// 快速校验：返回结果状态和消息
    /// </summary>
    /// <param name="input">要校验的文本</param>
    /// <returns>(结果状态, 提示消息)</returns>
    public static (CheckResult Status, string Message) Check(string input)
    {
        var result = _privacy.Validate(input);

        if (result.ShouldBlock)
            return (CheckResult.Blocked, _privacy.GetBlockMessage(result));

        if (result.ShouldWarn)
            return (CheckResult.Warned, _privacy.GetWarnMessage(result));

        return (CheckResult.Safe, string.Empty);
    }

    /// <summary>
    /// 仅校验是否安全（不区分阻断/警告）
    /// </summary>
    public static bool IsSafe(string input) => _privacy.Validate(input).IsSafe;

    /// <summary>
    /// 校验是否有阻断级敏感信息
    /// </summary>
    public static bool HasBlockedContent(string input) => _privacy.Validate(input).ShouldBlock;

    /// <summary>
    /// 校验是否有警告级敏感信息
    /// </summary>
    public static bool HasWarnedContent(string input) => _privacy.Validate(input).ShouldWarn;

    /// <summary>
    /// 直接获取底层 PrivacyService 实例（需要更精细控制时使用）
    /// </summary>
    public static PrivacyService Service => _privacy;
}
