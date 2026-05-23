using System.IO;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>代码片段系统 —— 输入缩写 + Tab 展开为完整代码</summary>
public class SnippetService
{
    private readonly Dictionary<string, Snippet> _snippets = new();

    /// <summary>已加载的片段数量</summary>
    public int Count => _snippets.Count;

    /// <summary>从 JSON 文件加载片段</summary>
    public void LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var json = File.ReadAllText(filePath);
            var snippets = JsonSerializer.Deserialize<List<Snippet>>(json);
            if (snippets != null)
            {
                _snippets.Clear();
                foreach (var s in snippets)
                    if (!string.IsNullOrEmpty(s.Prefix))
                        _snippets[s.Prefix] = s;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"加载代码片段失败: {ex.Message}");
        }
    }

    /// <summary>保存片段到 JSON 文件</summary>
    public void SaveToFile(string filePath)
    {
        var list = _snippets.Values.ToList();
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    /// <summary>初始化默认片段</summary>
    public void InitDefaults()
    {
        var defaults = new List<Snippet>
        {
            new("for", "for (int i = 0; i < $1; i++)\n{\n    $0\n}", "for 循环", "通用"),
            new("foreach", "foreach (var item in $1)\n{\n    $0\n}", "foreach 循环", "C#"),
            new("if", "if ($1)\n{\n    $0\n}", "if 语句", "通用"),
            new("ifn", "if ($1 == null)\n{\n    $0\n}", "空判断", "C#"),
            new("class", "public class $1\n{\n    $0\n}", "类定义", "C#"),
            new("prop", "public $1 $2 { get; set; }$0", "自动属性", "C#"),
            new("propfull", "private $1 _$2;\npublic $1 $3\n{\n    get => _$2;\n    set => _$2 = value;\n}\n$0", "完整属性", "C#"),
            new("ctor", "public $1($2)\n{\n    $0\n}", "构造函数", "C#"),
            new("method", "public $1 $2($3)\n{\n    $0\n}", "方法", "C#"),
            new("try", "try\n{\n    $0\n}\ncatch (Exception ex)\n{\n    // $1\n}", "try-catch", "通用"),
            new("func", "function $1($2) {\n    $0\n}", "函数", "JavaScript"),
            new("afunc", "const $1 = async ($2) => {\n    $0\n};", "异步箭头函数", "JavaScript"),
            new("def", "def $1($2):\n    $0", "函数定义", "Python"),
            new("clog", "console.log($1);$0", "Console.Log", "JavaScript"),
            new("cw", "Console.WriteLine($1);$0", "Console.WriteLine", "C#"),
        };

        foreach (var s in defaults)
            if (!_snippets.ContainsKey(s.Prefix))
                _snippets[s.Prefix] = s;
    }

    /// <summary>根据前缀查找匹配的片段</summary>
    public Snippet? FindByPrefix(string prefix)
    {
        _snippets.TryGetValue(prefix, out var snippet);
        return snippet;
    }

    /// <summary>获取当前光标前可能的前缀</summary>
    public (string prefix, Snippet snippet)? MatchPrefix(string textBeforeCursor)
    {
        if (string.IsNullOrEmpty(textBeforeCursor)) return null;
        // 从后往前找最近的非标识符字符
        int start = textBeforeCursor.Length - 1;
        while (start >= 0 && (char.IsLetterOrDigit(textBeforeCursor[start]) || textBeforeCursor[start] == '_'))
            start--;
        var prefix = textBeforeCursor[(start + 1)..];
        if (prefix.Length < 2) return null;
        var snippet = FindByPrefix(prefix);
        return snippet != null ? (prefix, snippet) : null;
    }

    /// <summary>展开片段（替换占位符 $0, $1, ...）</summary>
    public static string Expand(Snippet snippet, int indentSpaces = 0)
    {
        var body = snippet.Body;
        if (indentSpaces > 0)
        {
            var indent = new string(' ', indentSpaces);
            body = string.Join('\n', body.Split('\n').Select(l => indent + l));
        }
        return body;
    }

    /// <summary>添加/更新片段</summary>
    public void Add(Snippet snippet)
    {
        _snippets[snippet.Prefix] = snippet;
    }

    /// <summary>删除片段</summary>
    public bool Remove(string prefix) => _snippets.Remove(prefix);

    /// <summary>所有片段列表</summary>
    public IReadOnlyList<Snippet> All => _snippets.Values.ToList().AsReadOnly();
}

/// <summary>单个代码片段</summary>
public class Snippet
{
    public string Prefix { get; set; } = "";
    public string Body { get; set; } = "";
    public string Description { get; set; } = "";
    public string Language { get; set; } = "通用";

    public Snippet() { }
    public Snippet(string prefix, string body, string description, string language)
    {
        Prefix = prefix;
        Body = body;
        Description = description;
        Language = language;
    }
}
