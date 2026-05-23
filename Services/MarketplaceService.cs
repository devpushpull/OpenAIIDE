using System.Net.Http;
using System.Text.Json;

namespace AIIDEWPF.Services;

/// <summary>MCP 工具市场 & 插件发现服务</summary>
public class MarketplaceService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>获取 MCP 工具市场列表（内置精选 + 在线获取）</summary>
    public static async Task<List<MCPMarketItem>> GetMCPMarketplaceAsync()
    {
        var items = new List<MCPMarketItem>(GetBuiltinMCPTools());

        // 尝试从在线注册表获取
        try
        {
            var online = await FetchOnlineRegistryAsync("https://raw.githubusercontent.com/modelcontextprotocol/servers/main/README.md");
            if (online.Count > 0)
                items.AddRange(online);
        }
        catch { /* 离线时使用内置列表 */ }

        return items.DistinctBy(i => i.Id).ToList();
    }

    /// <summary>获取插件发现列表</summary>
    public static async Task<List<PluginDiscoveryItem>> GetPluginDiscoveryAsync()
    {
        return GetBuiltinPlugins();
    }

    // ===== 内置 MCP 工具精选 =====

    private static List<MCPMarketItem> GetBuiltinMCPTools()
    {
        return new List<MCPMarketItem>
        {
            new() { Id = "mcp-filesystem", Name = "Filesystem", Category = "文件系统",
                Description = "安全的文件系统操作，支持读写文件、目录浏览",
                Command = "npx", Args = "-y @modelcontextprotocol/server-filesystem /path/to/allowed",
                InstallNote = "修改 /path/to/allowed 为实际允许访问的目录" },
            new() { Id = "mcp-github", Name = "GitHub", Category = "开发工具",
                Description = "GitHub API 集成：仓库管理、PR、Issue、文件操作",
                Command = "npx", Args = "-y @modelcontextprotocol/server-github",
                EnvNote = "需要设置 GITHUB_PERSONAL_ACCESS_TOKEN 环境变量" },
            new() { Id = "mcp-postgres", Name = "PostgreSQL", Category = "数据库",
                Description = "PostgreSQL 数据库查询和管理",
                Command = "npx", Args = "-y @modelcontextprotocol/server-postgres postgresql://user:pass@localhost/db",
                InstallNote = "修改连接字符串为实际数据库地址" },
            new() { Id = "mcp-sqlite", Name = "SQLite", Category = "数据库",
                Description = "SQLite 数据库查询，支持本地 .db 文件",
                Command = "npx", Args = "-y @modelcontextprotocol/server-sqlite /path/to/database.db" },
            new() { Id = "mcp-brave-search", Name = "Brave Search", Category = "搜索",
                Description = "Brave 搜索引擎集成，支持网页和本地搜索",
                Command = "npx", Args = "-y @modelcontextprotocol/server-brave-search",
                EnvNote = "需要设置 BRAVE_API_KEY 环境变量" },
            new() { Id = "mcp-puppeteer", Name = "Puppeteer", Category = "浏览器自动化",
                Description = "浏览器自动化：截图、页面操作、爬虫",
                Command = "npx", Args = "-y @modelcontextprotocol/server-puppeteer" },
            new() { Id = "mcp-memory", Name = "Memory", Category = "知识管理",
                Description = "持久化记忆系统，跨会话知识存储与检索",
                Command = "npx", Args = "-y @modelcontextprotocol/server-memory" },
            new() { Id = "mcp-fetch", Name = "Fetch", Category = "网络",
                Description = "URL 内容获取，支持 HTML/JSON/文本",
                Command = "npx", Args = "-y @modelcontextprotocol/server-fetch" },
            new() { Id = "mcp-docker", Name = "Docker", Category = "容器",
                Description = "Docker 容器管理：创建、运行、日志查看",
                Command = "npx", Args = "-y @modelcontextprotocol/server-docker" },
            new() { Id = "mcp-git", Name = "Git", Category = "版本控制",
                Description = "Git 操作：提交、分支、日志、差异对比",
                Command = "npx", Args = "-y @modelcontextprotocol/server-git /path/to/repo" },
            new() { Id = "mcp-everart", Name = "EvertArt", Category = "AI 图像",
                Description = "AI 图像生成：支持多种模型和风格",
                Command = "npx", Args = "-y @modelcontextprotocol/server-everart",
                EnvNote = "需要设置 EVERTART_API_KEY 环境变量" },
            new() { Id = "mcp-sequential-thinking", Name = "Sequential Thinking", Category = "推理",
                Description = "分步推理思维链，复杂问题逐步分析",
                Command = "npx", Args = "-y @modelcontextprotocol/server-sequential-thinking" },
        };
    }

    // ===== 内置插件精选 =====

    private static List<PluginDiscoveryItem> GetBuiltinPlugins()
    {
        return new List<PluginDiscoveryItem>
        {
            new() { Id = "plugin-code-formatter", Name = "Code Formatter", Version = "1.0",
                Author = "AIIDE", Category = "格式化",
                Description = "一键代码格式化，支持 C#/Python/JS/Go/Java，基于 Prettier 和 language-specific formatters",
                InstallSize = "2.3 MB", Rating = "★★★★☆" },
            new() { Id = "plugin-live-preview", Name = "Live Preview", Version = "0.9",
                Author = "AIIDE", Category = "预览",
                Description = "HTML/Web 实时预览，支持热重载和内联编辑",
                InstallSize = "1.8 MB", Rating = "★★★★★" },
            new() { Id = "plugin-gitlens", Name = "GitLens", Version = "2.1",
                Author = "AIIDE", Category = "版本控制",
                Description = "Git 增强：行级 blame、历史对比、可视化分支图",
                InstallSize = "4.5 MB", Rating = "★★★★★" },
            new() { Id = "plugin-rest-client", Name = "REST Client", Version = "1.2",
                Author = "AIIDE", Category = "API 工具",
                Description = "HTTP API 调试工具，支持 .http 文件、环境变量、断言",
                InstallSize = "2.1 MB", Rating = "★★★★☆" },
            new() { Id = "plugin-database-explorer", Name = "Database Explorer", Version = "1.0",
                Author = "AIIDE", Category = "数据库",
                Description = "数据库可视化浏览器，支持 MySQL/PostgreSQL/SQLite/MongoDB",
                InstallSize = "5.8 MB", Rating = "★★★☆☆" },
            new() { Id = "plugin-markdown-preview", Name = "Markdown Preview", Version = "1.3",
                Author = "AIIDE", Category = "预览",
                Description = "Markdown 实时预览，支持 Mermaid 图表和数学公式",
                InstallSize = "1.6 MB", Rating = "★★★★☆" },
            new() { Id = "plugin-docker-manager", Name = "Docker Manager", Version = "0.8",
                Author = "AIIDE", Category = "容器",
                Description = "Docker 可视化管理：镜像、容器、Compose 编排",
                InstallSize = "3.2 MB", Rating = "★★★☆☆" },
            new() { Id = "plugin-snippet-manager", Name = "Snippet Manager", Version = "1.1",
                Author = "AIIDE", Category = "生产力",
                Description = "代码片段管理器，分类、标签、快速插入、云端同步",
                InstallSize = "1.4 MB", Rating = "★★★★★" },
            new() { Id = "plugin-theme-designer", Name = "Theme Designer", Version = "1.0",
                Author = "AIIDE", Category = "主题",
                Description = "可视化主题编辑器，创建/编辑/导出 IDE 配色方案",
                InstallSize = "0.9 MB", Rating = "★★★★☆" },
            new() { Id = "plugin-ai-prompt-templates", Name = "AI Prompt Templates", Version = "1.2",
                Author = "AIIDE", Category = "AI 工具",
                Description = "精选 AI 提示词模板库，按场景分类快速调用",
                InstallSize = "0.5 MB", Rating = "★★★★★" },
        };
    }

    /// <summary>从在线注册表获取 MCP 工具（解析 Markdown）</summary>
    private static async Task<List<MCPMarketItem>> FetchOnlineRegistryAsync(string url)
    {
        var resp = await _http.GetStringAsync(url);
        var items = new List<MCPMarketItem>();
        // 简单解析：提取 ## 开头的服务器名称和描述
        var lines = resp.Split('\n');
        string? currentName = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ") && !line.Contains("Model Context Protocol"))
            {
                currentName = line[3..].Trim();
            }
            else if (currentName != null && (line.Contains("npm install") || line.Contains("npx")))
            {
                var cmd = line.Trim().TrimStart('-').Trim();
                items.Add(new MCPMarketItem
                {
                    Id = "online-" + currentName.ToLower().Replace(" ", "-"),
                    Name = currentName,
                    Category = "在线注册表",
                    Description = "从 MCP 官方注册表获取",
                    Command = cmd.Contains("npx") ? "npx" : "npm",
                    Args = cmd,
                });
                currentName = null;
            }
        }
        return items;
    }
}

// ===== 市场/发现数据模型 =====

/// <summary>MCP 工具市场项目</summary>
public class MCPMarketItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
    public string? InstallNote { get; set; }
    public string? EnvNote { get; set; }
    public bool IsInstalled { get; set; }
    public string InstallStatus => IsInstalled ? "✅ 已安装" : "📥 安装";
}

/// <summary>插件发现项目</summary>
public class PluginDiscoveryItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string InstallSize { get; set; } = "";
    public string Rating { get; set; } = "";
    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set { _isInstalled = value; UpdateButtonState(); }
    }
    private bool _hasUpdate;
    public bool HasUpdate
    {
        get => _hasUpdate;
        set { _hasUpdate = value; UpdateButtonState(); }
    }
    public string InstallStatus { get; private set; } = "📥 安装";
    public string InstallButtonBg { get; private set; } = "#1a5a1a";
    public string InstallButtonFg { get; private set; } = "#90ee90";

    private void UpdateButtonState()
    {
        if (_hasUpdate)
        {
            InstallStatus = "🔼 更新";
            InstallButtonBg = "#5a4a00";
            InstallButtonFg = "#f0c040";
        }
        else if (_isInstalled)
        {
            InstallStatus = "✅ 已安装";
            InstallButtonBg = "#2d2d2d";
            InstallButtonFg = "#888";
        }
        else
        {
            InstallStatus = "📥 安装";
            InstallButtonBg = "#1a5a1a";
            InstallButtonFg = "#90ee90";
        }
    }
}