using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>AI 行间代码补全服务 —— 支持 FIM (Fill-in-the-Middle)、智能上下文提取、注释跳过</summary>
public class CodeCompletionService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource? _cts;
    private ConfigService? _config;
    private ModelManager? _modelManager;

    // 回调
    public event Action<string>? OnCompletionReady; // 收到补全建议
    public event Action? OnCompletionCancelled;

    public bool Enabled { get; set; } = true;
    /// <summary>防抖毫秒数（自适应：补全越快越短）</summary>
    public int DebounceMs { get; set; } = 350;
    public int MaxLines { get; set; } = 6;
    /// <summary>光标前上下文最大字符数</summary>
    public int MaxPrefixChars { get; set; } = 3000;
    /// <summary>光标后上下文最大字符数（FIM）</summary>
    public int MaxSuffixChars { get; set; } = 1000;

    // ===== 补全结果缓存 =====
    /// <summary>缓存条目数上限</summary>
    public int MaxCacheEntries { get; set; } = 50;
    /// <summary>缓存过期时间</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(3);

    private readonly Dictionary<string, CacheEntry> _cache = new();
    private int _cacheHits;
    private int _cacheMisses;

    /// <summary>缓存命中次数</summary>
    public int CacheHits => _cacheHits;
    /// <summary>缓存未命中次数</summary>
    public int CacheMisses => _cacheMisses;
    /// <summary>缓存命中率</summary>
    public double CacheHitRate => (_cacheHits + _cacheMisses) == 0 ? 0 : (double)_cacheHits / (_cacheHits + _cacheMisses);

    private readonly struct CacheEntry
    {
        public readonly string Suggestion;
        public readonly DateTime Timestamp;
        public CacheEntry(string suggestion, DateTime timestamp)
        {
            Suggestion = suggestion;
            Timestamp = timestamp;
        }
    }

    public void Initialize(ConfigService config, ModelManager modelManager)
    {
        _config = config;
        _modelManager = modelManager;
    }

    /// <summary>提取项目级上下文（依赖信息、框架版本等）</summary>
    public static string ExtractProjectContext(string projectPath, string filePath)
    {
        try
        {
            var sb = new StringBuilder();
            var csproj = FindProjectFile(projectPath, filePath, "*.csproj");
            if (csproj != null)
            {
                sb.AppendLine("## Project Dependencies (C#)");
                sb.AppendLine("```");
                foreach (var line in File.ReadAllLines(csproj).Take(60))
                {
                    var t = line.Trim();
                    if (t.Contains("PackageReference") || t.Contains("TargetFramework")
                        || t.Contains("ProjectReference") || t.Contains("OutputType"))
                        sb.AppendLine(t);
                }
                sb.AppendLine("```");
            }
            var pkgJson = Path.Combine(projectPath, "package.json");
            if (File.Exists(pkgJson))
            {
                sb.AppendLine("## package.json dependencies");
                sb.AppendLine("```json");
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(pkgJson));
                var deps = json?["dependencies"];
                if (deps != null) sb.AppendLine(deps.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                sb.AppendLine("```");
            }
            return sb.ToString();
        }
        catch (Exception ex) { LogService.Instance.Debug($"代码补全上下文构建异常: {ex.Message}", "CodeComplete"); return string.Empty; }
    }

    private static string? FindProjectFile(string projectPath, string filePath, string pattern)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (dir != null && dir.StartsWith(projectPath))
        {
            var files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length > 0) return files[0];
            dir = Path.GetDirectoryName(dir);
        }
        var rootFiles = Directory.GetFiles(projectPath, pattern, SearchOption.TopDirectoryOnly);
        return rootFiles.Length > 0 ? rootFiles[0] : null;
    }

    /// <summary>根据上下文计算缓存键</summary>
    private string ComputeCacheKey(string codeBefore, string? codeAfter, string filePath, string language)
    {
        var combined = $"{filePath}|{language}|{codeBefore}|{codeAfter ?? ""}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>尝试从缓存获取补全结果</summary>
    public bool TryGetCached(string codeBefore, string? codeAfter, string filePath, string language, out string? suggestion)
    {
        suggestion = null;
        var key = ComputeCacheKey(codeBefore, codeAfter, filePath, language);
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow - entry.Timestamp < CacheTtl)
            {
                suggestion = entry.Suggestion;
                Interlocked.Increment(ref _cacheHits);
                return true;
            }
            _cache.Remove(key);
        }
        Interlocked.Increment(ref _cacheMisses);
        return false;
    }

    /// <summary>将补全结果写入缓存</summary>
    private void CacheResult(string codeBefore, string? codeAfter, string filePath, string language, string suggestion)
    {
        var key = ComputeCacheKey(codeBefore, codeAfter, filePath, language);
        _cache[key] = new CacheEntry(suggestion, DateTime.UtcNow);

        // LRU 驱逐：超过上限时移除最旧的条目
        if (_cache.Count > MaxCacheEntries)
        {
            var oldest = _cache.OrderBy(kvp => kvp.Value.Timestamp).First();
            _cache.Remove(oldest.Key);
        }
    }

    /// <summary>清空缓存</summary>
    public void ClearCache()
    {
        _cache.Clear();
        _cacheHits = 0;
        _cacheMisses = 0;
    }

    /// <summary>请求代码补全（支持 FIM）</summary>
    /// <param name="codeBeforeCursor">光标之前的代码</param>
    /// <param name="codeAfterCursor">光标之后的代码（FIM suffix，可为空）</param>
    /// <param name="filePath">当前文件路径</param>
    /// <param name="language">语言</param>
    /// <param name="importsHeader">文件头部的 imports/usings（可为空）</param>
    /// <param name="projectPath">项目根路径（用于提取依赖上下文）</param>
    public async Task RequestCompletionAsync(string codeBeforeCursor, string? codeAfterCursor,
        string filePath, string language, string? importsHeader = null, string? projectPath = null)
    {
        if (_config == null || _modelManager == null || !Enabled) return;
        if (string.IsNullOrWhiteSpace(codeBeforeCursor)) return;

        // 取消之前的请求
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var apiKey = _modelManager.GetEffectiveApiKey(_modelManager.ActiveProvider?.Id ?? "");
            if (string.IsNullOrEmpty(apiKey)) return;

            var provider = _modelManager.ActiveProvider;
            var baseUrl = provider?.BaseUrl ?? "https://api.deepseek.com/v1";
            var model = _modelManager.ActiveModel?.Id ?? "deepseek-v4-pro";

            // 提取项目级上下文
            var projectCtx = projectPath != null ? ExtractProjectContext(projectPath, filePath) : null;
            var prompt = BuildCompletionPrompt(codeBeforeCursor, codeAfterCursor, filePath, language, importsHeader, projectCtx);
            var messages = new List<object>
            {
                new { role = "user", content = prompt }
            };

            var reqDict = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = 0.2,
                ["stream"] = true,
                ["max_tokens"] = Math.Min(MaxLines * 80, 500)
            };

            var json = JsonSerializer.Serialize(reqDict);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var fullContent = "";

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
                var data = line["data: ".Length..].Trim();
                if (data == "[DONE]") break;
                try
                {
                    var j = System.Text.Json.Nodes.JsonNode.Parse(data);
                    var content = j?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
                    if (content != null) fullContent += content;
                }
                catch (Exception ex) { LogService.Instance.Debug($"代码补全流式解析异常: {ex.Message}", "CodeComplete"); }
            }

            if (!string.IsNullOrWhiteSpace(fullContent))
            {
                fullContent = CleanCompletion(fullContent, codeBeforeCursor);
                if (!string.IsNullOrWhiteSpace(fullContent))
                {
                    CacheResult(codeBeforeCursor, codeAfterCursor, filePath, language, fullContent);
                    OnCompletionReady?.Invoke(fullContent);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"代码补全失败: {ex.Message}", "Completion");
        }
    }

    /// <summary>无 FIM 的便捷重载</summary>
    public Task RequestCompletionAsync(string codeBeforeCursor, string filePath, string language)
        => RequestCompletionAsync(codeBeforeCursor, null, filePath, language, null);

    /// <summary>提取代码上下文：函数签名 + 局部变量声明</summary>
    public static string ExtractCodeContext(string codeBeforeCursor, string language)
    {
        if (string.IsNullOrEmpty(codeBeforeCursor)) return string.Empty;

        var funcSig = ExtractFunctionSignature(codeBeforeCursor, language);
        var locals = ExtractNearbyDeclarations(codeBeforeCursor, language);

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(funcSig))
        {
            sb.AppendLine("## 当前函数上下文");
            sb.AppendLine("```");
            sb.AppendLine(funcSig);
            sb.AppendLine("```");
        }
        if (!string.IsNullOrEmpty(locals))
        {
            sb.AppendLine("## 附近的变量声明");
            sb.AppendLine("```");
            sb.AppendLine(TruncateLines(locals, 15));
            sb.AppendLine("```");
        }
        return sb.ToString();
    }

    /// <summary>提取当前所在的函数/方法签名</summary>
    public static string ExtractFunctionSignature(string code, string language)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        var lines = code.Split('\n');

        // 从后向前找最近的函数/方法/类声明
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // 检测函数签名行（根据语言）
            if (IsFunctionSignature(trimmed, language))
            {
                // 返回从签名行到光标位置的代码（去掉前导空格）
                var sigStart = Math.Max(0, i - 3); // 包含签名前的注释/装饰器
                return string.Join('\n', lines.Skip(sigStart).Take(lines.Length - sigStart));
            }

            // C# class 声明
            if (language == "C#" && (trimmed.Contains("class ") || trimmed.Contains("struct ")
                || trimmed.Contains("interface ") || trimmed.Contains("enum ")))
            {
                return lines[i];
            }
        }

        return string.Empty;
    }

    /// <summary>提取光标附近最近的变量/字段声明</summary>
    public static string ExtractNearbyDeclarations(string code, string language)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        var lines = code.Split('\n');
        var declarations = new List<string>();
        var searchLimit = Math.Min(lines.Length, 60); // 只搜索最近60行

        for (int i = lines.Length - 1; i >= Math.Max(0, lines.Length - searchLimit); i--)
        {
            var trimmed = lines[i].TrimStart();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith("//") || trimmed.StartsWith("#") || trimmed.StartsWith("*")) continue;

            // 各种语言的变量声明模式
            bool isDecl = language switch
            {
                "C#" => trimmed.StartsWith("var ") || trimmed.StartsWith("int ") || trimmed.StartsWith("string ")
                    || trimmed.StartsWith("bool ") || trimmed.StartsWith("double ") || trimmed.StartsWith("float ")
                    || trimmed.StartsWith("List<") || trimmed.StartsWith("Dictionary<") || trimmed.StartsWith("DateTime ")
                    || trimmed.StartsWith("private ") || trimmed.StartsWith("public ") || trimmed.StartsWith("protected ")
                    || trimmed.StartsWith("readonly ") || trimmed.StartsWith("static ") || trimmed.StartsWith("const "),
                "Python" => trimmed.Contains('=') && !trimmed.StartsWith("if ") && !trimmed.StartsWith("for ")
                    && !trimmed.StartsWith("while ") && !trimmed.StartsWith("def ") && !trimmed.StartsWith("class "),
                "JavaScript" or "TypeScript" => trimmed.StartsWith("let ") || trimmed.StartsWith("const ")
                    || trimmed.StartsWith("var ") || trimmed.StartsWith("function "),
                "Java" => trimmed.Contains("int ") || trimmed.Contains("String ") || trimmed.Contains("boolean ")
                    || trimmed.Contains("List<") || trimmed.Contains("Map<") || trimmed.Contains("private ")
                    || trimmed.Contains("public ") || trimmed.Contains("protected "),
                "PHP" => trimmed.StartsWith("$") || trimmed.StartsWith("public ") || trimmed.StartsWith("private ")
                    || trimmed.StartsWith("protected ") || trimmed.StartsWith("static "),
                "Swift" => trimmed.StartsWith("var ") || trimmed.StartsWith("let ") || trimmed.StartsWith("lazy var "),
                "Kotlin" => trimmed.StartsWith("val ") || trimmed.StartsWith("var ") || trimmed.StartsWith("lateinit ")
                    || trimmed.StartsWith("private ") || trimmed.StartsWith("internal "),
                "Ruby" => (trimmed.StartsWith("@") || trimmed.Contains(" = ")) && !trimmed.StartsWith("def "),
                "Dart" => trimmed.StartsWith("var ") || trimmed.StartsWith("final ") || trimmed.StartsWith("const ")
                    || trimmed.StartsWith("late ") || trimmed.StartsWith("static "),
                "C++" or "C" => (trimmed.StartsWith("int ") || trimmed.StartsWith("char ") || trimmed.StartsWith("float ")
                    || trimmed.StartsWith("double ") || trimmed.StartsWith("bool ") || trimmed.StartsWith("auto ")
                    || trimmed.StartsWith("string ") || trimmed.StartsWith("vector<") || trimmed.StartsWith("std::"))
                    && !trimmed.Contains("("),
                _ => false
            };

            if (isDecl && declarations.Count < 10)
            {
                declarations.Insert(0, lines[i]);
            }
        }

        return declarations.Count > 0 ? string.Join('\n', declarations) : string.Empty;
    }

    private static bool IsFunctionSignature(string line, string language)
    {
        // 去掉前导空格
        var t = line.Trim();

        return language switch
        {
            "C#" => (t.Contains(" void ") || t.Contains(" int ") || t.Contains(" string ")
                || t.Contains(" bool ") || t.Contains(" Task<") || t.Contains(" async ")
                || t.Contains(" IEnumerator") || t.Contains(" ActionResult"))
                && (t.Contains("(") && t.Contains(")")),
            "Python" => t.StartsWith("def ") || t.StartsWith("async def "),
            "JavaScript" or "TypeScript" => (t.StartsWith("function ") || t.Contains("=>")
                || t.Contains("function(") || t.Contains(" = (")) && !t.StartsWith("//"),
            "Java" => (t.Contains("public ") || t.Contains("private ") || t.Contains("protected "))
                && t.Contains("(") && t.Contains(")") && !t.Contains("="),
            "Go" => t.StartsWith("func "),
            "Rust" => t.StartsWith("fn ") || t.StartsWith("pub fn "),
            "PHP" => t.StartsWith("function ") || (t.Contains("function ") && t.Contains("(")),
            "Swift" => t.StartsWith("func ") || (t.Contains(" func ") && t.Contains("(")),
            "Kotlin" => (t.StartsWith("fun ") || t.Contains(" fun ")) && t.Contains("("),
            "Ruby" => t.StartsWith("def ") && t.Contains("(") || t.Contains(" end"),
            "Dart" => (t.Contains("void ") || t.Contains("Future<") || t.Contains("Widget ")) && t.Contains("(") && t.Contains(")"),
            "Lua" => t.StartsWith("function ") || t.Contains(" = function("),
            "Shell" => t.StartsWith("function ") || (t.Contains("()") && t.Contains("{")),
            "SQL" => t.StartsWith("CREATE ") || t.StartsWith("ALTER ") || t.StartsWith("DROP "),
            "Scala" => t.StartsWith("def ") && t.Contains("("),
            "R" => (t.StartsWith("function(") || t.Contains(" <- function(")) && t.Contains(")"),
            "Elixir" => t.StartsWith("def ") || t.StartsWith("defp "),
            _ => t.Contains("(") && t.Contains(")") && !t.Contains("=") && !t.StartsWith("//"),
        };
    }

    public void Cancel()
    {
        _cts?.Cancel();
        OnCompletionCancelled?.Invoke();
    }

    private string BuildCompletionPrompt(string code, string? codeAfter, string filePath, string language, string? importsHeader, string? projectCtx)
    {
        var fileName = Path.GetFileName(filePath);
        var hasSuffix = !string.IsNullOrEmpty(codeAfter);

        // 提取上下文（函数签名 + 局部变量）
        var codeContext = ExtractCodeContext(code, language);

        var sb = new StringBuilder();
        sb.AppendLine("你是世界顶级的代码补全引擎。请严格遵循以下规则补全代码：");
        sb.AppendLine();
        sb.AppendLine("## 规则");
        sb.AppendLine("- 只输出需要补全的代码片段，不要任何解释、注释说明或markdown标记");
        sb.AppendLine("- 不要重复光标前已有的代码");
        sb.AppendLine($"- 最多输出 {MaxLines} 行代码");
        sb.AppendLine("- 保持与上下文一致的缩进风格和命名规范");
        sb.AppendLine($"- 使用 {fileName} 中已有的变量和类型");
        sb.AppendLine();
        sb.AppendLine($"## 文件信息");
        sb.AppendLine($"- 文件名: {fileName}");
        sb.AppendLine($"- 语言: {language}");

        // 注入项目级依赖上下文
        if (!string.IsNullOrEmpty(projectCtx))
        {
            sb.AppendLine();
            sb.Append(projectCtx);
        }

        // 包含 imports/usings 头部信息
        if (!string.IsNullOrEmpty(importsHeader))
        {
            sb.AppendLine();
            sb.AppendLine("## 文件头部引用");
            sb.AppendLine("```");
            sb.AppendLine(TruncateLines(importsHeader, 30));
            sb.AppendLine("```");
        }

        // 包含函数上下文 + 变量声明
        if (!string.IsNullOrEmpty(codeContext))
        {
            sb.AppendLine();
            sb.Append(codeContext);
        }

        if (hasSuffix)
        {
            // === FIM 模式：注入前后 ===
            sb.AppendLine();
            sb.AppendLine("## <|cursor_before|> 光标前的代码");
            sb.AppendLine("```");
            sb.AppendLine(code);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## <|cursor_after|> 光标后的代码（请确保补全能自然衔接）");
            sb.AppendLine("```");
            sb.AppendLine(codeAfter);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("请在 <|cursor|> 位置输出补全代码。");
        }
        else
        {
            // === 仅前缀模式 ===
            sb.AppendLine();
            sb.AppendLine("## 光标前的代码");
            sb.AppendLine("```");
            sb.AppendLine(code);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## 补全");
        }

        return sb.ToString();
    }

    /// <summary>提取文件头部的 imports/usings</summary>
    public static string ExtractImportsHeader(string fullFileContent, string language)
    {
        if (string.IsNullOrEmpty(fullFileContent)) return string.Empty;

        var lines = fullFileContent.Split('\n');
        var headerLines = new List<string>();
        var importKeywords = language switch
        {
            "C#" => new[] { "using ", "namespace " },
            "Python" => new[] { "import ", "from " },
            "JavaScript" or "TypeScript" => new[] { "import ", "const ", "require(" },
            "Java" => new[] { "import ", "package " },
            "Go" => new[] { "import ", "package " },
            "Rust" => new[] { "use ", "mod ", "extern crate " },
            "PHP" => new[] { "use ", "require", "include", "namespace " },
            "Swift" => new[] { "import " },
            "Kotlin" => new[] { "import ", "package " },
            "Ruby" => new[] { "require ", "include ", "load " },
            "Dart" => new[] { "import ", "export ", "part " },
            "Lua" => new[] { "require ", "local " },
            "C++" or "C" => new[] { "#include", "#define", "#pragma", "using namespace " },
            _ => Array.Empty<string>()
        };

        if (importKeywords.Length == 0) return string.Empty;

        foreach (var line in lines.Take(50)) // 最多检查前50行
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) { headerLines.Add(""); continue; }
            if (importKeywords.Any(k => trimmed.StartsWith(k)))
                headerLines.Add(line);
            else if (headerLines.Count > 0 && !trimmed.StartsWith("//") && !trimmed.StartsWith("#"))
                break; // 遇到非 import 代码行且不是注释，停止
        }

        return string.Join('\n', headerLines).Trim();
    }

    /// <summary>检测光标是否在注释或字符串中</summary>
    public static bool IsCursorInCommentOrString(string codeBeforeCursor, string language)
    {
        if (string.IsNullOrEmpty(codeBeforeCursor)) return false;

        var lastLine = codeBeforeCursor.Split('\n').LastOrDefault() ?? "";
        var trimmed = lastLine.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        // 单行注释
        if (trimmed.StartsWith("//") || trimmed.StartsWith("#")) return true;

        // 多行注释内（未闭合的 /*）
        var lastBlockCommentStart = codeBeforeCursor.LastIndexOf("/*");
        var lastBlockCommentEnd = codeBeforeCursor.LastIndexOf("*/");
        if (lastBlockCommentStart > lastBlockCommentEnd) return true;

        // 字符串内（简单检测：奇数个未转义引号）
        var quoteCount = lastLine.Count(c => c == '"') - lastLine.Count(c => c == '\\' && lastLine.IndexOf(c) < lastLine.LastIndexOf('"'));
        // 简化的引号检测：最后一个非转义引号后还有内容
        var lastQuote = -1;
        for (int i = 0; i < lastLine.Length; i++)
        {
            if (lastLine[i] == '"' && (i == 0 || lastLine[i - 1] != '\\'))
                lastQuote = i;
        }
        if (lastQuote >= 0)
        {
            // 检查引号后面是否只有空格或正常代码（不是字符串）
            var afterQuote = lastLine[(lastQuote + 1)..].TrimEnd();
            if (afterQuote.Length > 0 && !afterQuote.EndsWith(';') && !afterQuote.EndsWith(')'))
                return true; // 可能在字符串中间
        }

        return false;
    }

    private static string TruncateLines(string text, int maxLines)
    {
        var lines = text.Split('\n');
        if (lines.Length <= maxLines) return text;
        return string.Join('\n', lines.Take(maxLines)) + $"\n// ... (省略 {lines.Length - maxLines} 行)";
    }

    private string CleanCompletion(string raw, string codeBefore)
    {
        var result = raw.Trim();

        // 去掉 markdown 代码块标记
        if (result.StartsWith("```"))
        {
            var idx = result.IndexOf('\n');
            if (idx > 0) result = result[(idx + 1)..];
            if (result.EndsWith("```"))
                result = result[..^3].TrimEnd();
        }

        // 去掉与已有代码尾部重复的行
        var existingLines = codeBefore.Split('\n');
        var compLines = result.Split('\n');

        if (compLines.Length > 0 && existingLines.Length > 0)
        {
            var lastExisting = existingLines[^1].Trim();
            var firstComp = compLines[0].Trim();
            if (lastExisting.Length > 0 && firstComp == lastExisting)
            {
                result = string.Join('\n', compLines.Skip(1));
            }
        }

        return result;
    }
}
