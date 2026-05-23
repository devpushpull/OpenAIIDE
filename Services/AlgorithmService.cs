using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>算法管理服务 — 统一管理项目中的所有算法</summary>
public class AlgorithmService
{
    private readonly string _storageDir;
    private readonly string _storageFile;
    private List<AlgorithmInfo> _algorithms = new();

    public AlgorithmService(string projectPath)
    {
        _storageDir = Path.Combine(projectPath, ".aiide");
        _storageFile = Path.Combine(_storageDir, "algorithms.json");
        Load();
    }

    public IReadOnlyList<AlgorithmInfo> All => _algorithms.AsReadOnly();

    // ===== CRUD =====

    public AlgorithmInfo Create(string name, string description, string language,
        string code, string category = "", string complexity = "",
        string spaceComplexity = "", List<string>? tags = null, string sourceFile = "")
    {
        var alg = new AlgorithmInfo
        {
            Name = name,
            Description = description,
            Language = language,
            Code = code,
            Category = category,
            Complexity = complexity,
            SpaceComplexity = spaceComplexity,
            Tags = tags ?? new(),
            SourceFile = sourceFile,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _algorithms.Add(alg);
        Save();
        return alg;
    }

    public AlgorithmInfo? Get(string id) => _algorithms.FirstOrDefault(a => a.Id == id);

    public bool Update(string id, Action<AlgorithmInfo> updater)
    {
        var alg = Get(id);
        if (alg == null) return false;
        updater(alg);
        alg.UpdatedAt = DateTime.Now;
        Save();
        return true;
    }

    public bool Delete(string id)
    {
        var alg = Get(id);
        if (alg == null) return false;
        _algorithms.Remove(alg);
        Save();
        return true;
    }

    // ===== Search =====

    public List<AlgorithmInfo> Search(string keyword)
    {
        var kw = keyword.ToLower();
        return _algorithms.Where(a =>
            a.Name.ToLower().Contains(kw) ||
            a.Description.ToLower().Contains(kw) ||
            a.Category.ToLower().Contains(kw) ||
            a.Language.ToLower().Contains(kw) ||
            a.Tags.Any(t => t.ToLower().Contains(kw)) ||
            a.Code.ToLower().Contains(kw)
        ).ToList();
    }

    public List<AlgorithmInfo> Filter(string? category = null, string? language = null,
        string? complexity = null, string? tag = null)
    {
        var q = _algorithms.AsEnumerable();
        if (!string.IsNullOrEmpty(category))
            q = q.Where(a => a.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(language))
            q = q.Where(a => a.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(complexity))
            q = q.Where(a => a.Complexity.Equals(complexity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(tag))
            q = q.Where(a => a.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)));
        return q.ToList();
    }

    public List<string> GetAllCategories() =>
        _algorithms.Select(a => a.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();

    public List<string> GetAllLanguages() =>
        _algorithms.Select(a => a.Language).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();

    /// <summary>从项目源代码中扫描提取算法（查找函数/方法定义）</summary>
    public List<AlgorithmInfo> ExtractFromProject(string projectPath, int maxFiles = 50)
    {
        var extracted = new List<AlgorithmInfo>();
        var srcFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => IsSourceFile(f))
            .Take(maxFiles);

        foreach (var file in srcFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var funcs = ExtractFunctions(content, Path.GetExtension(file));
                foreach (var (name, code) in funcs)
                {
                    // 跳过简单函数（getters/setters/非常短的方法）
                    if (code.Split('\n').Length < 3) continue;

                    var lang = GetLanguageFromExtension(Path.GetExtension(file));
                    var cat = GuessCategory(name);
                    extracted.Add(new AlgorithmInfo
                    {
                        Name = name,
                        Description = $"从 {Path.GetFileName(file)} 中提取",
                        Language = lang,
                        Code = code,
                        Category = cat,
                        SourceFile = Path.GetRelativePath(projectPath, file),
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    });
                }
            }
            catch { }
        }
        return extracted;
    }

    // ===== Persistence =====

    private void Load()
    {
        try
        {
            if (File.Exists(_storageFile))
            {
                var json = File.ReadAllText(_storageFile);
                _algorithms = JsonSerializer.Deserialize<List<AlgorithmInfo>>(json) ?? new();
            }
        }
        catch
        {
            _algorithms = new();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_storageDir);
            var json = JsonSerializer.Serialize(_algorithms, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storageFile, json);
        }
        catch { }
    }

    // ===== Helpers =====

    private static bool IsSourceFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext is ".cs" or ".py" or ".js" or ".ts" or ".java" or ".go" or ".rs"
            or ".cpp" or ".c" or ".h" or ".rb" or ".php" or ".swift" or ".kt" or ".scala";
    }

    private static List<(string name, string code)> ExtractFunctions(string content, string ext)
    {
        var results = new List<(string, string)>();
        var lines = content.Split('\n');

        // 简单的函数提取：查找 function/method 定义行
        string? currentFunc = null;
        int braceDepth = 0;
        bool inFunc = false;
        var funcLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!inFunc)
            {
                // 检测函数定义
                var funcName = DetectFunctionStart(trimmed, ext);
                if (funcName != null)
                {
                    currentFunc = funcName;
                    inFunc = true;
                    funcLines = new List<string> { trimmed };
                    braceDepth = trimmed.Count(c => c == '{') - trimmed.Count(c => c == '}');
                    if (braceDepth <= 0 && trimmed.EndsWith("{"))
                        braceDepth = 1;
                    // Python 风格：缩进块
                    if (trimmed.EndsWith(":") && ext == ".py")
                        braceDepth = 1;
                }
            }
            else
            {
                funcLines.Add(trimmed);
                braceDepth += trimmed.Count(c => c == '{') - trimmed.Count(c => c == '}');

                // Python 风格：检测缩进退出
                if (ext == ".py" && trimmed.Length > 0 && !char.IsWhiteSpace(line[0]) && !trimmed.StartsWith("else") && !trimmed.StartsWith("elif") && !trimmed.StartsWith("except") && !trimmed.StartsWith("finally"))
                {
                    braceDepth = 0;
                }

                if (braceDepth <= 0)
                {
                    if (currentFunc != null)
                        results.Add((currentFunc, string.Join("\n", funcLines)));
                    inFunc = false;
                    currentFunc = null;
                    braceDepth = 0;
                }
            }
        }

        // 处理文件末尾未闭合的函数
        if (inFunc && currentFunc != null && funcLines.Count > 0)
            results.Add((currentFunc, string.Join("\n", funcLines)));

        return results;
    }

    private static string? DetectFunctionStart(string line, string ext)
    {
        // C#/Java/C++ 风格
        if (ext is ".cs" or ".java" or ".cpp" or ".c" or ".h" or ".go" or ".rs" or ".swift" or ".kt" or ".scala")
        {
            // public/private/protected/static 返回类型 函数名(
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"(?:public|private|protected|internal|static|virtual|override|async|unsafe|extern|func|fn|def|fun)\s+(?:[\w<>\[\],\s]+\s+)?(\w+)\s*\([^)]*\)\s*[\{:]?");
            if (match.Success)
                return match.Groups[1].Value;

            // Go: func Name(
            match = System.Text.RegularExpressions.Regex.Match(line, @"func\s+(?:\([^)]*\)\s+)?(\w+)\s*\(");
            if (match.Success) return match.Groups[1].Value;

            // Rust: fn name(
            match = System.Text.RegularExpressions.Regex.Match(line, @"fn\s+(\w+)\s*[<(]");
            if (match.Success) return match.Groups[1].Value;
        }

        // Python
        if (ext == ".py")
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"def\s+(\w+)\s*\(");
            if (match.Success) return match.Groups[1].Value;
        }

        // JS/TS
        if (ext is ".js" or ".ts")
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"(?:function|async\s+function)\s+(\w+)\s*\(|(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s*)?\(|(\w+)\s*=\s*(?:async\s*)?\(|(\w+)\s*\([^)]*\)\s*\{");
            if (match.Success)
                return match.Groups[1].Value ?? match.Groups[2].Value ?? match.Groups[3].Value ?? match.Groups[4].Value;
        }

        return null;
    }

    private static string GetLanguageFromExtension(string ext) => ext.ToLower() switch
    {
        ".cs" => "C#",
        ".py" => "Python",
        ".js" => "JavaScript",
        ".ts" => "TypeScript",
        ".java" => "Java",
        ".go" => "Go",
        ".rs" => "Rust",
        ".cpp" or ".cc" or ".cxx" => "C++",
        ".c" or ".h" => "C",
        ".rb" => "Ruby",
        ".php" => "PHP",
        ".swift" => "Swift",
        ".kt" => "Kotlin",
        ".scala" => "Scala",
        _ => "Other"
    };

    private static string GuessCategory(string name)
    {
        var n = name.ToLower();
        if (n.Contains("sort") || n.Contains("quick") || n.Contains("merge") || n.Contains("bubble") || n.Contains("heap")) return "sort";
        if (n.Contains("search") || n.Contains("find") || n.Contains("binary") || n.Contains("lookup")) return "search";
        if (n.Contains("graph") || n.Contains("bfs") || n.Contains("dfs") || n.Contains("dijkstra") || n.Contains("path")) return "graph";
        if (n.Contains("tree") || n.Contains("trie") || n.Contains("bst") || n.Contains("avl")) return "tree";
        if (n.Contains("dp") || n.Contains("knapsack") || n.Contains("dynamic") || n.Contains("lcs") || n.Contains("lis")) return "dp";
        if (n.Contains("string") || n.Contains("parse") || n.Contains("kmp") || n.Contains("trie") || n.Contains("substr")) return "string";
        if (n.Contains("math") || n.Contains("prime") || n.Contains("gcd") || n.Contains("fib") || n.Contains("factorial")) return "math";
        if (n.Contains("greedy") || n.Contains("huffman")) return "greedy";
        if (n.Contains("backtrack") || n.Contains("permut") || n.Contains("nqueen") || n.Contains("sudoku")) return "backtracking";
        return "general";
    }
}
