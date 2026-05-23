using System.IO;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>代码片段检测器——检测粘贴的代码是否存在于项目中</summary>
public class CodeClipDetector
{
    private readonly SearchService _searchService;
    private string _projectPath = string.Empty;

    public CodeClipDetector(SearchService searchService)
    {
        _searchService = searchService;
    }

    public void SetProjectPath(string path) => _projectPath = path;

    /// <summary>检测粘贴文本是否匹配项目中的代码</summary>
    public List<CodeMatchResult> Detect(string pastedText)
    {
        var results = new List<CodeMatchResult>();
        if (string.IsNullOrEmpty(_projectPath) || string.IsNullOrWhiteSpace(pastedText))
            return results;

        // 提取关键代码行（去除空行和纯注释行）
        var lines = pastedText.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 3 && !l.StartsWith("//") && !l.StartsWith("#") && !l.StartsWith("--"))
            .Take(8)
            .ToList();

        if (lines.Count == 0) return results;

        // 用最长的非平凡行作为搜索关键词
        var query = lines.OrderByDescending(l => l.Length).First();
        // 清理特殊字符用于正则搜索
        var safeQuery = Regex.Escape(query[..Math.Min(query.Length, 60)].Trim());
        if (safeQuery.Length < 8) return results;

        try
        {
            var matches = _searchService.Grep(_projectPath, safeQuery);
            foreach (var m in matches.Take(5))
            {
                // 计算相似度
                var similarity = ComputeSimilarity(pastedText, m.Content);
                if (similarity > 0.3)
                {
                    results.Add(new CodeMatchResult
                    {
                        FilePath = m.File,
                        Line = m.Line,
                        MatchedContent = m.Content.Trim(),
                        Similarity = similarity,
                        RelativePath = Path.GetRelativePath(_projectPath, m.File)
                    });
                }
            }
        }
        catch { }

        return results.OrderByDescending(r => r.Similarity).Take(3).ToList();
    }

    /// <summary>简单相似度：共同行数 / 总行数</summary>
    private static double ComputeSimilarity(string pasted, string matched)
    {
        var pastedLines = pasted.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 2).ToHashSet();
        var matchedLines = matched.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 2).ToHashSet();
        if (pastedLines.Count == 0) return 0;

        var common = pastedLines.Count(l => matchedLines.Contains(l));
        return (double)common / pastedLines.Count;
    }

    /// <summary>判断文本是否像代码（含缩进、括号、分号等特征）</summary>
    public static bool LooksLikeCode(string text)
    {
        if (text.Length < 20) return false;
        // 用字符串数组避免隐式类型混淆
        string[] codeIndicators = ["{", "}", ";", "public", "class", "function", "def ", "import ", "var ", "let ", "const ",
                                    "return", "if (", "for (", "while (", "async ", "await ", "=>", "namespace"];
        var lower = text.ToLowerInvariant();
        return codeIndicators.Count(c => lower.Contains(c.ToLowerInvariant())) >= 2;
    }
}

/// <summary>代码匹配结果</summary>
public class CodeMatchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public string MatchedContent { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string Summary => $"{FileName}:L{Line} ({Similarity:P0} 匹配)";
}
