using Microsoft.Data.Sqlite;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 记忆库服务 —— 管理用户编程偏好记忆（增删改查 + 自动提取）
/// 对标 Qoder / 通义灵码 的记忆系统设计
/// </summary>
public class MemoryService
{
    private readonly DatabaseService _db;
    private readonly List<MemoryItem> _sessionMemories = new();

    public MemoryService(DatabaseService db)
    {
        _db = db;
    }

    // ===== 持久化记忆 CRUD =====

    /// <summary>获取所有持久化记忆（含全局和项目级）</summary>
    public List<MemoryItem> GetAll(string? workspacePath = null)
    {
        var list = new List<MemoryItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        // 全局记忆 + 当前项目的项目记忆
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Scope, WorkspacePath, Keywords, CreatedAt, UpdatedAt
            FROM Memories
            WHERE Scope = 'global'
               OR (Scope = 'project' AND WorkspacePath = @ws)
            ORDER BY Category, CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("@ws", workspacePath ?? string.Empty);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadMemory(reader));
        return list;
    }

    /// <summary>按类别获取记忆</summary>
    public List<MemoryItem> GetByCategory(string category, string? workspacePath = null)
    {
        var list = new List<MemoryItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Scope, WorkspacePath, Keywords, CreatedAt, UpdatedAt
            FROM Memories
            WHERE Category = @cat
              AND (Scope = 'global' OR (Scope = 'project' AND WorkspacePath = @ws))
            ORDER BY CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@ws", workspacePath ?? string.Empty);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadMemory(reader));
        return list;
    }

    /// <summary>搜索记忆（关键词 + 标题 + 内容模糊匹配）</summary>
    public List<MemoryItem> Search(string query, string? workspacePath = null)
    {
        var list = new List<MemoryItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Scope, WorkspacePath, Keywords, CreatedAt, UpdatedAt
            FROM Memories
            WHERE (Scope = 'global' OR (Scope = 'project' AND WorkspacePath = @ws))
              AND (Title LIKE @q OR Content LIKE @q OR Keywords LIKE @q)
            ORDER BY CreatedAt DESC
            """;
        var q = $"%{query}%";
        cmd.Parameters.AddWithValue("@q", q);
        cmd.Parameters.AddWithValue("@ws", workspacePath ?? string.Empty);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadMemory(reader));
        return list;
    }

    /// <summary>新增记忆</summary>
    public MemoryItem Add(string title, string content, string category = "user_preferences",
        string scope = "global", string? workspacePath = null, string keywords = "")
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Memories (Title, Content, Category, Scope, WorkspacePath, Keywords)
            VALUES (@title, @content, @cat, @scope, @ws, @keywords);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@scope", scope);
        cmd.Parameters.AddWithValue("@ws", scope == "project" ? (object?)workspacePath : DBNull.Value);
        cmd.Parameters.AddWithValue("@keywords", keywords);
        var id = (long)cmd.ExecuteScalar()!;

        LogService.Instance.Info($"记忆已创建: [{category}] {title}");
        return new MemoryItem
        {
            Id = (int)id,
            Title = title,
            Content = content,
            Category = category,
            Scope = scope,
            WorkspacePath = workspacePath,
            Keywords = keywords,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    /// <summary>更新记忆</summary>
    public bool Update(int id, string title, string content, string category, string? keywords = null)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Memories
            SET Title = @title, Content = @content, Category = @cat,
                Keywords = COALESCE(@keywords, Keywords),
                UpdatedAt = datetime('now','localtime')
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@keywords", keywords ?? (object)DBNull.Value);
        var rows = cmd.ExecuteNonQuery();
        if (rows > 0)
            LogService.Instance.Info($"记忆已更新: #{id}");
        return rows > 0;
    }

    /// <summary>删除记忆</summary>
    public bool Delete(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Memories WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = cmd.ExecuteNonQuery();
        if (rows > 0)
            LogService.Instance.Info($"记忆已删除: #{id}");
        return rows > 0;
    }

    // ===== 会话记忆（临时，会话结束即清除） =====

    /// <summary>添加会话记忆</summary>
    public void AddSession(string title, string content)
    {
        _sessionMemories.Add(new MemoryItem
        {
            Id = -1,
            Title = title,
            Content = content,
            Scope = "session",
            CreatedAt = DateTime.Now
        });
    }

    /// <summary>获取所有会话记忆</summary>
    public List<MemoryItem> GetSessionMemories() => new(_sessionMemories);

    /// <summary>清除会话记忆</summary>
    public void ClearSession() => _sessionMemories.Clear();

    // ===== 自动记忆提取 =====

    /// <summary>从对话中自动提取可能值得记忆的信息（简易关键词启发式）</summary>
    public List<(string Title, string Content, string Category)> AutoExtract(string userMessage)
    {
        var suggestions = new List<(string, string, string)>();

        // 检查是否包含显式记忆指令："记住"、"记一下"等
        if (userMessage.Contains("记住") || userMessage.Contains("记一下") || userMessage.Contains("请记住"))
        {
            // 提取"记住"后面的内容作为记忆
            var idx = Math.Max(
                userMessage.IndexOf("记住"),
                Math.Max(userMessage.IndexOf("记一下"), userMessage.IndexOf("请记住")));
            var content = userMessage[(idx + 2)..].Trim().TrimStart('：', ':', '，', ',', ' ').Trim();
            if (content.Length > 0)
            {
                var title = content.Length > 30 ? content[..30] + "..." : content;
                suggestions.Add((title, content, GuessCategory(content)));
            }
        }

        // 检测编码风格偏好关键词
        if (userMessage.Contains("缩进") || userMessage.Contains("空格") || userMessage.Contains("tab") ||
            userMessage.Contains("命名") || userMessage.Contains("风格") || userMessage.Contains("规范"))
            suggestions.Add(("编码风格偏好", userMessage, "development_standards"));

        // 检测项目信息关键词
        if (userMessage.Contains("项目") && (userMessage.Contains("架构") || userMessage.Contains("技术栈") ||
            userMessage.Contains("框架") || userMessage.Contains("数据库")))
            suggestions.Add(("项目技术信息", userMessage, "project_info"));

        return suggestions;
    }

    /// <summary>根据内容推断记忆类别</summary>
    private static string GuessCategory(string content)
    {
        if (content.Contains("规范") || content.Contains("风格") || content.Contains("命名") ||
            content.Contains("缩进") || content.Contains("格式"))
            return "development_standards";
        if (content.Contains("项目") || content.Contains("架构") || content.Contains("技术栈") ||
            content.Contains("框架") || content.Contains("数据库") || content.Contains("API"))
            return "project_info";
        return "user_preferences";
    }

    // ===== 记忆格式化（供 AI 上下文使用） =====

    /// <summary>将所有有效记忆格式化为 AI 可用的提示文本</summary>
    public string FormatForAI(string? workspacePath = null)
    {
        var memories = GetAll(workspacePath);
        if (memories.Count == 0) return string.Empty;

        var lines = new List<string> { "【用户记忆与偏好】" };
        foreach (var m in memories)
        {
            var scopeTag = m.Scope == "project" ? "[项目]" : "[全局]";
            lines.Add($"- {scopeTag} [{m.Category}] {m.Title}: {m.Content}");
        }
        return string.Join('\n', lines);
    }

    private static MemoryItem ReadMemory(SqliteDataReader reader)
    {
        return new MemoryItem
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            Content = reader.GetString(2),
            Category = reader.GetString(3),
            Scope = reader.GetString(4),
            WorkspacePath = reader.IsDBNull(5) ? null : reader.GetString(5),
            Keywords = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            CreatedAt = DateTime.Parse(reader.GetString(7)),
            UpdatedAt = DateTime.Parse(reader.GetString(8))
        };
    }
}

