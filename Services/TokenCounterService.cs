using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>模型感知的 Token 精确计数器 —— 区分中英文、代码、不同模型族</summary>
public static class TokenCounterService
{
    // 各模型族的估算比例（chars per token），值越小=token越多
    // DeepSeek/通用中文模型: CJK ~1.5 chars/token, English ~3.5, Code ~3.0
    // OpenAI GPT-4/cl100k: CJK ~1.2, English ~3.0, Code ~2.5
    // Claude: CJK ~1.0, English ~4.5, Code ~3.5
    public enum ModelFamily { DeepSeek, OpenAI, Claude, Generic }

    private static readonly Dictionary<ModelFamily, (double cjkRate, double engRate, double codeRate)> Rates = new()
    {
        [ModelFamily.DeepSeek] = (1.5, 3.5, 3.0),
        [ModelFamily.OpenAI]   = (1.2, 3.0, 2.5),
        [ModelFamily.Claude]   = (1.0, 4.5, 3.5),
        [ModelFamily.Generic]  = (1.5, 4.0, 3.0),
    };

    /// <summary>从模型名推断模型族</summary>
    public static ModelFamily DetectFamily(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return ModelFamily.Generic;
        var lower = modelName.ToLowerInvariant();
        if (lower.Contains("deepseek")) return ModelFamily.DeepSeek;
        if (lower.Contains("gpt-") || lower.Contains("o1-") || lower.Contains("o3-") || lower.Contains("o4-"))
            return ModelFamily.OpenAI;
        if (lower.Contains("claude")) return ModelFamily.Claude;
        return ModelFamily.Generic;
    }

    /// <summary>精确估算纯文本的 token 数</summary>
    public static int CountTokens(string text, ModelFamily family = ModelFamily.Generic)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var (cjkRate, engRate, codeRate) = Rates[family];

        int cjkChars = 0, alphaChars = 0, digitChars = 0, symbolChars = 0, wsChars = 0;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { wsChars++; continue; }
            if (CommonUtils.IsCJK(c)) { cjkChars++; continue; }
            if (char.IsLetter(c)) { alphaChars++; continue; }
            if (char.IsDigit(c)) { digitChars++; continue; }
            symbolChars++;
        }

        // 空白字符约100 chars/token
        double tokens = wsChars / 100.0;
        // CJK 按字符数 / cjkRate
        tokens += cjkChars / cjkRate;
        // 字母按英文词近似：每5字符一个词
        tokens += alphaChars / engRate;
        // 数字和符号按字母比例
        tokens += (digitChars + symbolChars) / engRate;

        return Math.Max(1, (int)Math.Ceiling(tokens));
    }

    /// <summary>估算 JSON 序列化后的消息列表 token 数</summary>
    public static int CountTokens(List<object> messages, ModelFamily family = ModelFamily.Generic)
    {
        int totalChars = 0;
        foreach (var msg in messages)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(msg);
            totalChars += json.Length;
        }
        return CountTokens(totalChars, family);
    }

    /// <summary>估算总字符数（简单版）</summary>
    private static int CountTokens(int totalChars, ModelFamily family)
    {
        // 按混合比例估算: 假定20% CJK, 60% English, 20% code/symbols
        var (cjkRate, engRate, _) = Rates[family];
        var cjkPortion = totalChars * 0.2;
        var engPortion = totalChars * 0.8;
        return Math.Max(1, (int)Math.Ceiling(cjkPortion / cjkRate + engPortion / engRate));
    }
}
