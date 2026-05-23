using System.IO;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>/ 命令服务——读写 JSON 配置文件，支持增删改查</summary>
public class SlashCommandService
{
    private List<SlashCommandItem> _commands = new();
    private string? _configPath;

    private static readonly List<SlashCommandItem> Defaults = new()
    {
        new() { Id = "file",     Command = "/file ",   Icon = "📄", Label = "引用文件",       Description = "将项目中的文件作为上下文引用",   Category = "上下文" },
        new() { Id = "folder",   Command = "/folder ", Icon = "📁", Label = "引用文件夹",     Description = "将整个文件夹作为上下文引用",       Category = "上下文" },
        new() { Id = "search",   Command = "/search ", Icon = "🔍", Label = "搜索代码",       Description = "在项目中搜索代码或文本",           Category = "工作区" },
        new() { Id = "build",    Command = "/build ",  Icon = "🔨", Label = "编译项目",       Description = "编译当前项目（自动检测语言）",     Category = "工作区" },
        new() { Id = "package",  Command = "/package ",Icon = "📦", Label = "打包项目",       Description = "打包/构建可分发产物",              Category = "工作区" },
        new() { Id = "terminal", Command = "/terminal ",Icon = "💻",Label = "执行终端命令",   Description = "在项目终端中执行任意命令",         Category = "工作区" },
        new() { Id = "clear",    Command = "/clear",   Icon = "🧹", Label = "清空对话",       Description = "清空当前对话历史，开始新会话",     Category = "对话" },
        new() { Id = "plan",     Command = "/plan",    Icon = "📋", Label = "制定计划",       Description = "让AI先制定详细实施计划再编码",   Category = "对话" },
        new() { Id = "explain",  Command = "/explain ",Icon = "💡", Label = "解释代码",       Description = "请AI解释选中代码或文件的功能",   Category = "代码" },
        new() { Id = "fix",      Command = "/fix ",    Icon = "🔧", Label = "修复问题",       Description = "请AI修复代码中的bug或错误",      Category = "代码" },
        new() { Id = "refactor", Command = "/refactor ",Icon = "♻️",Label = "重构代码",       Description = "请AI重构优化指定代码",            Category = "代码" },
        new() { Id = "test",     Command = "/test ",   Icon = "🧪", Label = "生成测试",       Description = "为指定代码生成单元测试",          Category = "代码" },
        new() { Id = "review",   Command = "/review ", Icon = "✅", Label = "代码审查",       Description = "请AI审查代码质量和安全性",       Category = "代码" },
        new() { Id = "new",      Command = "/new ",    Icon = "🆕", Label = "新建文件",       Description = "创建新的代码文件（需指定路径）",     Category = "工作区" },
        new() { Id = "help",     Command = "/help",    Icon = "❓", Label = "帮助",           Description = "显示AI功能和命令帮助",             Category = "其他" },
    };

    public void SetProjectPath(string path)
    {
        _configPath = System.IO.Path.Combine(path, ".codeartsdoer", "slash_commands.json");
        Load();
    }

    /// <summary>从 JSON 加载，不存在则用默认值并保存</summary>
    private void Load()
    {
        if (string.IsNullOrEmpty(_configPath))
        {
            _commands = DeepClone(Defaults);
            return;
        }

        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var loaded = JsonSerializer.Deserialize<List<SlashCommandItem>>(json);
                if (loaded != null && loaded.Count > 0)
                {
                    _commands = loaded;
                    return;
                }
            }
        }
        catch { }

        // 文件不存在或解析失败，使用默认值
        _commands = DeepClone(Defaults);
        Save();
    }

    /// <summary>保存到 JSON 文件</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(_configPath)) return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_commands, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch { }
    }

    /// <summary>重置为默认命令</summary>
    public void ResetToDefaults()
    {
        _commands = DeepClone(Defaults);
        Save();
    }

    /// <summary>根据用户输入搜索匹配的命令</summary>
    public List<SlashCommandItem> Search(string query)
    {
        if (string.IsNullOrEmpty(query))
            return _commands.ToList();

        var q = query.ToLowerInvariant().TrimStart('/');
        return _commands
            .Where(c => c.Command.ToLowerInvariant().Contains(q)
                     || c.Label.ToLowerInvariant().Contains(q)
                     || c.Description.ToLowerInvariant().Contains(q))
            .ToList();
    }

    /// <summary>获取所有命令（可增删改）</summary>
    public IReadOnlyList<SlashCommandItem> All => _commands.AsReadOnly();

    /// <summary>获取可修改的命令列表</summary>
    public List<SlashCommandItem> GetEditableList() => _commands;

    /// <summary>添加命令</summary>
    public void Add(SlashCommandItem item)
    {
        if (string.IsNullOrEmpty(item.Id))
            item.Id = GenerateId();
        _commands.Add(item);
        Save();
    }

    /// <summary>更新命令</summary>
    public void Update(SlashCommandItem item)
    {
        var idx = _commands.FindIndex(c => c.Id == item.Id);
        if (idx >= 0)
        {
            _commands[idx] = item;
            Save();
        }
    }

    /// <summary>删除命令</summary>
    public void Delete(string id)
    {
        _commands.RemoveAll(c => c.Id == id);
        Save();
    }

    private static string GenerateId() => Guid.NewGuid().ToString("N")[..8];

    private static List<SlashCommandItem> DeepClone(List<SlashCommandItem> source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<List<SlashCommandItem>>(json) ?? new();
    }
}

/// <summary>内置斜杠命令项</summary>
public class SlashCommandItem
{
    public string Id { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Display => $"{Icon}  {Command}  — {Description}";
}
