using System.IO;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>
/// 项目代码索引服务 —— 打开项目时异步构建符号/文件索引，
/// AI 对话时自动检索最相关的代码文件注入上下文。
/// 对标 Qoder/Cursor 的自动上下文功能。
/// </summary>
public class CodeIndexService
{
    private readonly Dictionary<string, FileIndexEntry> _index = new();
    private readonly Dictionary<string, List<string>> _symbolMap = new(); // symbol -> file paths
    private bool _isBuilt;
    private CancellationTokenSource? _buildCts;

    public int FileCount => _index.Count;
    public int SymbolCount => _symbolMap.Count;
    public bool IsBuilt => _isBuilt;

    /// <summary>异步构建项目索引</summary>
    public async Task BuildAsync(string projectPath, int maxFiles = 500)
    {
        _buildCts?.Cancel();
        _buildCts = new CancellationTokenSource();
        _isBuilt = false;
        _index.Clear();
        _symbolMap.Clear();

        var ct = _buildCts.Token;

        try
        {
            await Task.Run(() =>
            {
                var codeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".cs", ".java", ".py", ".js", ".ts", ".tsx", ".jsx",
                    ".go", ".rs", ".cpp", ".c", ".h", ".hpp", ".swift",
                    ".kt", ".scala", ".rb", ".php", ".dart", ".lua", ".r",
                    ".fs", ".vb", ".sql", ".sh", ".ps1", ".yaml", ".yml",
                    ".json", ".xml", ".xaml", ".csproj", ".md", ".html", ".css",
                    ".toml", ".cfg", ".ini", ".config"
                };

                var allFiles = new List<string>();
                CollectFiles(projectPath, allFiles, codeExts, maxFiles, ct);

                foreach (var file in allFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    IndexFile(file, projectPath, codeExts);
                }

                _isBuilt = true;
                LogService.Instance.Info(
                    $"代码索引构建完成: {_index.Count} 个文件, {_symbolMap.Count} 个符号", "CodeIndex");
            }, ct);
        }
        catch (OperationCanceledException)
        {
            LogService.Instance.Info("代码索引构建已取消", "CodeIndex");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"代码索引构建失败: {ex.Message}", "CodeIndex");
        }
    }

    /// <summary>
    /// 根据用户查询查找最相关的代码文件。
    /// 返回 (文件路径, 相关性分数) 列表，按分数降序排列。
    /// </summary>
    public List<(string FilePath, int Score)> SearchRelevant(string query, int topK = 5)
    {
        if (!_isBuilt || string.IsNullOrWhiteSpace(query)) return new();

        var keywords = ExtractKeywords(query);
        if (keywords.Count == 0) return new();

        var scores = new Dictionary<string, int>();

        foreach (var kw in keywords)
        {
            // 符号匹配
            if (_symbolMap.TryGetValue(kw, out var files))
            {
                foreach (var f in files)
                {
                    scores.TryGetValue(f, out var s);
                    scores[f] = s + 3; // 符号匹配权重高
                }
            }

            // 文件名匹配
            foreach (var (filePath, entry) in _index)
            {
                if (entry.FileName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    scores.TryGetValue(filePath, out var s);
                    scores[filePath] = s + 2;
                }
            }

            // 内容关键词匹配
            foreach (var (filePath, entry) in _index)
            {
                var contentScore = 0;
                foreach (var ck in entry.ContentKeywords)
                {
                    if (ck.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        contentScore++;
                }
                if (contentScore > 0)
                {
                    scores.TryGetValue(filePath, out var s);
                    scores[filePath] = s + Math.Min(contentScore, 5);
                }
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// 构建 AI 上下文：将最相关的 N 个文件内容作为代码上下文返回。
    /// </summary>
    public string BuildContextForAI(string query, int maxFiles = 5, int maxCharsPerFile = 3000)
    {
        if (!_isBuilt) return string.Empty;

        var relevant = SearchRelevant(query, maxFiles);
        if (relevant.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<project_context>");
        sb.AppendLine("以下是与当前问题最相关的项目代码文件：");
        sb.AppendLine();

        foreach (var (filePath, score) in relevant)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                sb.AppendLine($"### {fileName} (相关度: {score})");
                sb.AppendLine("```");

                var content = File.ReadAllText(filePath);
                if (content.Length > maxCharsPerFile)
                {
                    // 尝试找到与关键词最相关的区域
                    var snippet = ExtractRelevantSnippet(content, query, maxCharsPerFile);
                    sb.AppendLine(snippet);
                    sb.AppendLine($"... (文件共 {content.Length} 字符，已截取最相关片段)");
                }
                else
                {
                    sb.AppendLine(content.TrimEnd());
                }
                sb.AppendLine("```");
                sb.AppendLine();
            }
            catch (Exception ex) { LogService.Instance.Debug($"索引文件读取异常: {ex.Message}", "CodeIndex"); }
        }

        sb.AppendLine("</project_context>");
        return sb.ToString();
    }

    /// <summary>取消正在进行的索引构建</summary>
    public void CancelBuild()
    {
        _buildCts?.Cancel();
    }

    /// <summary>
    /// 生成项目结构摘要（用于注入到 System Prompt）。
    /// 包含：目录结构、关键文件列表、模块依赖关系。
    /// </summary>
    public string GetProjectStructureSummary(string projectPath, int maxDirs = 10, int maxServicesPerDir = 5)
    {
        if (!_isBuilt || string.IsNullOrEmpty(projectPath))
            return "代码索引尚未构建完成。";

        var sb = new System.Text.StringBuilder();

        try
        {
            // 按目录分组
            var byDir = _index.Values
                .GroupBy(e => Path.GetDirectoryName(e.FilePath) ?? "")
                .OrderBy(g => g.Key)
                .ToList();

            sb.AppendLine($"**项目结构** ({_index.Count} 个文件, {_symbolMap.Count} 个符号)");
            sb.AppendLine();

            foreach (var group in byDir.Take(maxDirs))
            {
                var dirName = Path.GetRelativePath(projectPath, group.Key);
                if (string.IsNullOrEmpty(dirName)) dirName = "(根目录)";

                var topFiles = group
                    .OrderByDescending(e => e.Symbols.Count)
                    .Take(maxServicesPerDir)
                    .ToList();

                var summaries = new List<string>();
                foreach (var entry in topFiles)
                {
                    var summary = BuildFileSummary(entry);
                    if (!string.IsNullOrEmpty(summary))
                        summaries.Add($"  - {entry.FileName}: {summary}");
                }

                if (summaries.Count > 0 || group.Count() <= maxServicesPerDir)
                {
                    sb.AppendLine($"📁 {dirName}/ ({group.Count()} 个文件)");
                    foreach (var s in summaries)
                        sb.AppendLine(s);
                    if (group.Count() > maxServicesPerDir)
                        sb.AppendLine($"  ... 等共 {group.Count()} 个文件");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(结构摘要生成异常: {ex.Message})");
        }

        return sb.ToString();
    }

    /// <summary>为单个文件生成一句话摘要</summary>
    private static string BuildFileSummary(FileIndexEntry entry)
    {
        if (entry.Symbols.Count == 0)
            return entry.Extension switch
            {
                ".csproj" => "项目配置文件",
                ".sln" => "解决方案文件",
                ".xaml" => "WPF视图文件",
                ".json" => "JSON配置/数据",
                ".xml" => "XML配置",
                ".config" => "配置文件",
                ".md" => "文档",
                _ => ""
            };

        var mainSymbols = entry.Symbols.Take(3).ToList();
        var symbolList = string.Join(", ", mainSymbols);
        if (entry.Symbols.Count > 3)
            symbolList += $" 等{entry.Symbols.Count}个符号";

        return $"[{symbolList}] (约{entry.LineCount}行)";
    }

    // ===== 内部方法 =====

    private void CollectFiles(string dir, List<string> results, HashSet<string> exts, int maxFiles,
        CancellationToken ct)
    {
        if (results.Count >= maxFiles) return;
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(dir))
            {
                ct.ThrowIfCancellationRequested();
                if (results.Count >= maxFiles) return;

                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || name == "node_modules" || name == "bin" || name == "obj")
                    continue;

                if (Directory.Exists(entry))
                {
                    CollectFiles(entry, results, exts, maxFiles, ct);
                }
                else
                {
                    var ext = Path.GetExtension(name);
                    if (exts.Contains(ext))
                        results.Add(entry);
                }
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"文件收集跳过异常目录: {ex.Message}", "CodeIndex"); }
    }

    private void IndexFile(string filePath, string projectPath, HashSet<string> codeExts)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var ext = Path.GetExtension(fileName).ToLowerInvariant();

            // 代码文件：提取符号
            if (IsCodeFile(ext, codeExts))
            {
                var content = File.ReadAllText(filePath);
                if (content.Length > 50000) content = content[..50000];

                var symbols = ExtractSymbols(content, ext);
                var contentKeywords = ExtractContentKeywords(content, 30);

                var entry = new FileIndexEntry
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Extension = ext,
                    Symbols = symbols,
                    ContentKeywords = contentKeywords,
                    LineCount = content.Split('\n').Length
                };

                _index[filePath] = entry;

                foreach (var sym in symbols)
                {
                    var lowerSym = sym.ToLowerInvariant();
                    if (!_symbolMap.ContainsKey(lowerSym))
                        _symbolMap[lowerSym] = new List<string>();
                    if (!_symbolMap[lowerSym].Contains(filePath))
                        _symbolMap[lowerSym].Add(filePath);
                }
            }
            else
            {
                // 配置文件：只索引文件名和路径
                var content = "";
                try { content = File.ReadAllText(filePath); if (content.Length > 10000) content = content[..10000]; } catch (Exception ex) { LogService.Instance.Debug($"读取配置文件异常: {ex.Message}", "CodeIndex"); }

                _index[filePath] = new FileIndexEntry
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Extension = ext,
                    Symbols = new List<string>(),
                    ContentKeywords = ExtractContentKeywords(content, 10),
                    LineCount = 0
                };
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"索引文件异常: {ex.Message}", "CodeIndex"); }
    }

    private static bool IsCodeFile(string ext, HashSet<string> codeExts)
    {
        return ext switch
        {
            ".cs" or ".java" or ".py" or ".js" or ".ts" or ".tsx" or ".jsx" or
            ".go" or ".rs" or ".cpp" or ".c" or ".h" or ".hpp" or ".swift" or
            ".kt" or ".scala" or ".rb" or ".php" or ".dart" or ".lua" or ".r" or
            ".fs" or ".vb" or ".sql" or ".sh" or ".ps1" => true,
            _ => false
        };
    }

    private static List<string> ExtractSymbols(string content, string extension)
    {
        var symbols = new List<string>();

        // C# / Java / TypeScript: class, interface, method, property, field
        var classPattern = extension switch
        {
            ".cs" => @"\b(?:class|struct|interface|enum|record)\s+(\w+)",
            ".java" => @"\b(?:class|interface|enum)\s+(\w+)",
            ".ts" or ".tsx" => @"\b(?:class|interface|type|enum|function)\s+(\w+)",
            ".py" => @"\b(?:class|def)\s+(\w+)",
            ".go" => @"\b(?:func|type)\s+(?:\([^)]*\)\s*)?(\w+)",
            ".rs" => @"\b(?:fn|struct|impl|trait|enum)\s+(\w+)",
            _ => null
        };

        if (classPattern != null)
        {
            foreach (Match m in Regex.Matches(content, classPattern, RegexOptions.Multiline))
            {
                var sym = m.Groups[1].Value;
                if (sym.Length > 1 && !symbols.Contains(sym))
                    symbols.Add(sym);
            }
        }

        // 方法模式（通用）
        var methodPattern = extension switch
        {
            ".cs" => @"\b(?:public|private|protected|internal|static|async|virtual|override|abstract)?\s+\w+(?:<[^>]+>)?\s+(\w+)\s*\(",
            ".java" => @"\b(?:public|private|protected|static|final|synchronized)?\s+\w+(?:<[^>]+>)?\s+(\w+)\s*\(",
            ".py" => @"\bdef\s+(\w+)\s*\(",
            ".go" => @"\bfunc\s+(?:\([^)]*\)\s+)?(\w+)\s*\(",
            ".rs" => @"\bfn\s+(\w+)\s*\(",
            _ => null
        };

        if (methodPattern != null)
        {
            foreach (Match m in Regex.Matches(content, methodPattern, RegexOptions.Multiline))
            {
                var sym = m.Groups[1].Value;
                if (sym.Length > 1 && !symbols.Contains(sym))
                    symbols.Add(sym);
            }
        }

        return symbols.Take(100).ToList();
    }

    private static List<string> ExtractContentKeywords(string content, int maxCount)
    {
        // 从内容中提取频率最高的有意义单词作为关键词
        var words = Regex.Matches(content.ToLowerInvariant(), @"[a-zA-Z_]\w{2,}")
            .Select(m => m.Value)
            .Where(w => !IsStopWord(w))
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(maxCount)
            .Select(g => g.Key)
            .ToList();

        // 也提取中文字符序列作为关键词
        var chineseWords = Regex.Matches(content, @"[\u4e00-\u9fff]{2,8}")
            .Select(m => m.Value)
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        words.AddRange(chineseWords);
        return words.Distinct().Take(maxCount).ToList();
    }

    private static List<string> ExtractKeywords(string query)
    {
        var keywords = new List<string>();

        // 提取英文关键词
        keywords.AddRange(Regex.Matches(query.ToLowerInvariant(), @"[a-zA-Z_]\w{2,}")
            .Select(m => m.Value)
            .Where(w => !IsStopWord(w))
            .Distinct());

        // 提取中文关键词
        keywords.AddRange(Regex.Matches(query, @"[\u4e00-\u9fff]{2,8}")
            .Select(m => m.Value)
            .Distinct());

        return keywords.Distinct().Take(15).ToList();
    }

    private static string ExtractRelevantSnippet(string content, string query, int maxChars)
    {
        var keywords = ExtractKeywords(query);
        if (keywords.Count == 0)
            return content.Length <= maxChars ? content : content[..maxChars] + "...";

        var lowerContent = content.ToLowerInvariant();
        int bestStart = 0;
        int bestScore = 0;

        // 滑动窗口找关键词最密集的区域
        var windowSize = maxChars / 2;
        for (int i = 0; i < lowerContent.Length - windowSize; i += windowSize / 2)
        {
            var window = lowerContent.Substring(i, Math.Min(windowSize, lowerContent.Length - i));
            var score = keywords.Count(k => window.Contains(k));
            if (score > bestScore)
            {
                bestScore = score;
                bestStart = i;
            }
        }

        var start = Math.Max(0, bestStart - maxChars / 4);
        var len = Math.Min(maxChars, content.Length - start);
        var snippet = content.Substring(start, len);
        if (start > 0) snippet = "...\n" + snippet;
        if (start + len < content.Length) snippet += "\n...";

        return snippet;
    }

    private static bool IsStopWord(string word)
    {
        return word switch
        {
            "the" or "and" or "for" or "this" or "that" or "with" or "from" or
            "have" or "are" or "not" or "but" or "all" or "has" or "been" or
            "will" or "can" or "get" or "set" or "var" or "let" or "const" or
            "new" or "out" or "ref" or "int" or "string" or "void" or "bool" or
            "var" or "null" or "true" or "false" or "public" or "private" or
            "static" or "class" or "return" or "using" or "import" or "include" => true,
            _ => false
        };
    }

    private class FileIndexEntry
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Extension { get; set; } = "";
        public List<string> Symbols { get; set; } = new();
        public List<string> ContentKeywords { get; set; } = new();
        public int LineCount { get; set; }
    }
}
