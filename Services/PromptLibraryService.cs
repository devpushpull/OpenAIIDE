using Microsoft.Data.Sqlite;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 提示词库服务 —— 管理用户保存的提示词模板，支持增删改查 + AI上下文格式化
/// </summary>
public class PromptLibraryService
{
    private readonly DatabaseService _db;

    public PromptLibraryService(DatabaseService db)
    {
        _db = db;
    }

    // ===== CRUD =====

    /// <summary>获取所有活跃的提示词（含全局和项目级）</summary>
    public List<PromptLibraryItem> GetAll(string? workspacePath = null)
    {
        var list = new List<PromptLibraryItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Scope, WorkspacePath, Tags, UsageCount, IsActive, CreatedAt, UpdatedAt
            FROM PromptLibrary
            WHERE IsActive = 1
              AND (Scope = 'global' OR (Scope = 'project' AND WorkspacePath = @ws))
            ORDER BY UsageCount DESC, Category, CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("@ws", workspacePath ?? string.Empty);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadPrompt(reader));
        return list;
    }

    /// <summary>按类别获取提示词</summary>
    public List<PromptLibraryItem> GetByCategory(string category, string? workspacePath = null)
    {
        var list = new List<PromptLibraryItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Scope, WorkspacePath, Tags, UsageCount, IsActive, CreatedAt, UpdatedAt
            FROM PromptLibrary
            WHERE IsActive = 1 AND Category = @cat
              AND (Scope = 'global' OR (Scope = 'project' AND WorkspacePath = @ws))
            ORDER BY UsageCount DESC
            """;
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@ws", workspacePath ?? string.Empty);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadPrompt(reader));
        return list;
    }

    /// <summary>搜索提示词（标题+内容+标签模糊匹配）</summary>
    public List<PromptLibraryItem> Search(string query, string? workspacePath = null)
    {
        var list = new List<PromptLibraryItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Scope, WorkspacePath, Tags, UsageCount, IsActive, CreatedAt, UpdatedAt
            FROM PromptLibrary
            WHERE IsActive = 1
              AND (Scope = 'global' OR (Scope = 'project' AND WorkspacePath = @ws))
              AND (Title LIKE @q OR Content LIKE @q OR Tags LIKE @q)
            ORDER BY UsageCount DESC
            """;
        var q = $"%{query}%";
        cmd.Parameters.AddWithValue("@q", q);
        cmd.Parameters.AddWithValue("@ws", workspacePath ?? string.Empty);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadPrompt(reader));
        return list;
    }

    /// <summary>新增提示词</summary>
    public PromptLibraryItem Add(string title, string content, string category = "general",
        string scope = "global", string? workspacePath = null, string tags = "")
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PromptLibrary (Title, Content, Category, Scope, WorkspacePath, Tags)
            VALUES (@title, @content, @cat, @scope, @ws, @tags);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@scope", scope);
        cmd.Parameters.AddWithValue("@ws", scope == "project" ? (object?)workspacePath : DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", tags);
        var id = (long)cmd.ExecuteScalar()!;

        LogService.Instance.Info($"提示词已创建: [{category}] {title}");
        return new PromptLibraryItem
        {
            Id = (int)id,
            Title = title,
            Content = content,
            Category = category,
            Scope = scope,
            WorkspacePath = workspacePath,
            Tags = tags,
            UsageCount = 0,
            IsActive = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    /// <summary>更新提示词</summary>
    public bool Update(int id, string title, string content, string category, string? tags = null)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE PromptLibrary
            SET Title = @title, Content = @content, Category = @cat,
                Tags = COALESCE(@tags, Tags),
                UpdatedAt = datetime('now','localtime')
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@tags", tags ?? (object)DBNull.Value);
        var rows = cmd.ExecuteNonQuery();
        if (rows > 0)
            LogService.Instance.Info($"提示词已更新: #{id}");
        return rows > 0;
    }

    /// <summary>增加使用次数</summary>
    public void IncrementUsage(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE PromptLibrary
            SET UsageCount = UsageCount + 1,
                UpdatedAt = datetime('now','localtime')
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>软删除提示词（标记为不活跃）</summary>
    public bool Deactivate(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PromptLibrary SET IsActive = 0 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = cmd.ExecuteNonQuery();
        if (rows > 0)
            LogService.Instance.Info($"提示词已停用: #{id}");
        return rows > 0;
    }

    /// <summary>恢复提示词</summary>
    public bool Activate(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PromptLibrary SET IsActive = 1 WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = cmd.ExecuteNonQuery();
        return rows > 0;
    }

    /// <summary>永久删除提示词</summary>
    public bool Delete(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PromptLibrary WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = cmd.ExecuteNonQuery();
        if (rows > 0)
            LogService.Instance.Info($"提示词已删除: #{id}");
        return rows > 0;
    }

    // ===== AI 上下文格式化 =====

    /// <summary>将活跃的提示词格式化为 AI 可用的提示文本</summary>
    public string FormatForAI(string? workspacePath = null, string? taskCategory = null)
    {
        // 如果有任务类别，优先返回匹配类别的提示词
        var prompts = !string.IsNullOrEmpty(taskCategory)
            ? GetByCategory(taskCategory, workspacePath)
            : GetAll(workspacePath);

        // 限制最多返回5条最常用的提示词，避免占用太多上下文
        var topPrompts = prompts.OrderByDescending(p => p.UsageCount).Take(5).ToList();
        if (topPrompts.Count == 0) return string.Empty;

        var lines = new List<string> { "【用户提示词模板库】" };
        foreach (var p in topPrompts)
        {
            var scopeTag = p.Scope == "project" ? "[项目]" : "[全局]";
            var tagsStr = !string.IsNullOrEmpty(p.Tags) ? $" [标签:{p.Tags}]" : "";
            lines.Add($"- {scopeTag} [{p.Category}]{tagsStr} {p.Title}: {p.Content}");
        }
        return string.Join('\n', lines);
    }

    // ===== 自动提取 =====

    /// <summary>从AI响应中自动检测值得保存的提示词模式</summary>
    public List<(string Title, string Content, string Category)> AutoExtract(string aiResponse)
    {
        var suggestions = new List<(string, string, string)>();
        if (string.IsNullOrWhiteSpace(aiResponse)) return suggestions;

        // 检测代码审查模式
        if (aiResponse.Contains("代码审查") || aiResponse.Contains("code review"))
            suggestions.Add(("代码审查提示词", aiResponse.Length > 200 ? aiResponse[..200] : aiResponse, "code_review"));

        // 检测Bug修复模式
        if (aiResponse.Contains("bug") || aiResponse.Contains("修复") || aiResponse.Contains("fix"))
            suggestions.Add(("Bug修复提示词", aiResponse.Length > 200 ? aiResponse[..200] : aiResponse, "bug_fix"));

        // 检测架构建议
        if (aiResponse.Contains("架构") || aiResponse.Contains("设计模式") || aiResponse.Contains("architecture"))
            suggestions.Add(("架构设计提示词", aiResponse.Length > 200 ? aiResponse[..200] : aiResponse, "architecture"));

        return suggestions;
    }

    private static PromptLibraryItem ReadPrompt(SqliteDataReader reader)
    {
        return new PromptLibraryItem
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            Content = reader.GetString(2),
            Category = reader.GetString(3),
            Scope = reader.GetString(4),
            WorkspacePath = reader.IsDBNull(5) ? null : reader.GetString(5),
            Tags = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            UsageCount = reader.GetInt32(7),
            IsActive = reader.GetInt32(8) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(9)),
            UpdatedAt = DateTime.Parse(reader.GetString(10))
        };
    }
}

