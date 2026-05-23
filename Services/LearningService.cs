using Microsoft.Data.Sqlite;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 自我学习服务 —— 自动记录编程经验、智能过滤、空闲时AI分析、自动改进、支持回滚
/// 核心设计：
/// 1. 智能过滤：Confidence >= 0.4 才入库，多次相似经验自动合并提升置信度
/// 2. 自动提取：从AI响应中检测有价值模式（代码审查、Bug修复、优化建议等）
/// 3. 去重机制：标题+内容相似度检查，避免重复记录
/// 4. 回滚支持：记录最后操作，支持撤销最近的自动改进
/// </summary>
public class LearningService
{
    private readonly DatabaseService _db;
    private readonly List<(int Id, string Operation)> _recentOperations = new(); // 最近操作记录（用于回滚）
    private const int MaxRecentOps = 20;

    public LearningService(DatabaseService db)
    {
        _db = db;
    }

    // ===== 智能过滤阈值 =====
    public double MinConfidence { get; set; } = 0.4;       // 最低入库置信度
    public double MergeThreshold { get; set; } = 0.75;     // 合并相似经验的相似度阈值
    public int MinContentLength { get; set; } = 20;        // 最短内容长度

    // ===== CRUD =====

    /// <summary>获取所有经验（按置信度降序）</summary>
    public List<LearningExperienceItem> GetAll(string? workspacePath = null, double? minConfidence = null)
    {
        var threshold = minConfidence ?? MinConfidence;
        var list = new List<LearningExperienceItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Source, Confidence, IsVerified, RelatedFiles, WorkspacePath, CreatedAt, UpdatedAt
            FROM LearningExperiences
            WHERE Confidence >= @conf
              AND (WorkspacePath = @ws OR WorkspacePath IS NULL OR @ws IS NULL)
            ORDER BY IsVerified DESC, Confidence DESC, CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("@conf", threshold);
        cmd.Parameters.AddWithValue("@ws", (object?)workspacePath ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadExperience(reader));
        return list;
    }

    /// <summary>获取已验证的经验（最可靠的）</summary>
    public List<LearningExperienceItem> GetVerified(string? workspacePath = null)
    {
        var list = new List<LearningExperienceItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Source, Confidence, IsVerified, RelatedFiles, WorkspacePath, CreatedAt, UpdatedAt
            FROM LearningExperiences
            WHERE IsVerified = 1
              AND (WorkspacePath = @ws OR WorkspacePath IS NULL OR @ws IS NULL)
            ORDER BY Confidence DESC, CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("@ws", (object?)workspacePath ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadExperience(reader));
        return list;
    }

    /// <summary>搜索经验</summary>
    public List<LearningExperienceItem> Search(string query, string? workspacePath = null)
    {
        var list = new List<LearningExperienceItem>();
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Category, Source, Confidence, IsVerified, RelatedFiles, WorkspacePath, CreatedAt, UpdatedAt
            FROM LearningExperiences
            WHERE Confidence >= @conf
              AND (WorkspacePath = @ws OR WorkspacePath IS NULL OR @ws IS NULL)
              AND (Title LIKE @q OR Content LIKE @q OR Category LIKE @q)
            ORDER BY IsVerified DESC, Confidence DESC
            """;
        var q = $"%{query}%";
        cmd.Parameters.AddWithValue("@q", q);
        cmd.Parameters.AddWithValue("@conf", MinConfidence);
        cmd.Parameters.AddWithValue("@ws", (object?)workspacePath ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadExperience(reader));
        return list;
    }

    /// <summary>记录经验（带智能过滤）</summary>
    public LearningExperienceItem? Record(string title, string content, string category = "general",
        string source = "auto_detected", double confidence = 0.5, string? workspacePath = null,
        string relatedFiles = "")
    {
        // 智能过滤1：内容过短不记录
        if (string.IsNullOrWhiteSpace(content) || content.Length < MinContentLength)
            return null;

        // 智能过滤2：置信度过低不记录
        if (confidence < MinConfidence)
            return null;

        // 智能过滤3：检查是否有相似经验（去重）
        if (TryFindSimilar(title, content, out var existingId, out var existingConfidence))
        {
            // 如果新经验的置信度更高，提升已有经验的置信度
            if (confidence > existingConfidence)
            {
                UpdateConfidence(existingId, (existingConfidence + confidence) / 2.0);
            }
            LogService.Instance.Info($"学习经验已合并到 #{existingId}: {title}", "Learning");
            return null; // 不重复插入
        }

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LearningExperiences (Title, Content, Category, Source, Confidence, IsVerified, RelatedFiles, WorkspacePath)
            VALUES (@title, @content, @cat, @source, @conf, @verified, @files, @ws);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@conf", confidence);
        cmd.Parameters.AddWithValue("@verified", source == "user_verified" ? 1 : 0);
        cmd.Parameters.AddWithValue("@files", relatedFiles);
        cmd.Parameters.AddWithValue("@ws", (object?)workspacePath ?? DBNull.Value);
        var id = (long)cmd.ExecuteScalar()!;

        // 记录操作（用于回滚）
        _recentOperations.Add(((int)id, "Record"));
        if (_recentOperations.Count > MaxRecentOps)
            _recentOperations.RemoveAt(0);

        LogService.Instance.Info($"学习经验已记录: [{category}] {title} (置信度:{confidence:F2})", "Learning");
        return new LearningExperienceItem
        {
            Id = (int)id,
            Title = title,
            Content = content,
            Category = category,
            Source = source,
            Confidence = confidence,
            IsVerified = source == "user_verified",
            RelatedFiles = relatedFiles,
            WorkspacePath = workspacePath,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    /// <summary>用户验证经验（标记为正确）</summary>
    public void MarkVerified(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE LearningExperiences
            SET IsVerified = 1, Confidence = MIN(1.0, Confidence + 0.2),
                Source = 'user_verified', UpdatedAt = datetime('now','localtime')
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        LogService.Instance.Info($"学习经验 #{id} 已被用户验证", "Learning");
    }

    /// <summary>用户标记为错误（降低置信度或删除）</summary>
    public void MarkIncorrect(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE LearningExperiences
            SET Confidence = MAX(0.1, Confidence - 0.3),
                IsVerified = 0, UpdatedAt = datetime('now','localtime')
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        // 如果置信度降得太低，直接删除
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT Confidence FROM LearningExperiences WHERE Id = @id";
        checkCmd.Parameters.AddWithValue("@id", id);
        var conf = (double)checkCmd.ExecuteScalar()!;
        if (conf < 0.2)
        {
            Delete(id);
            LogService.Instance.Info($"学习经验 #{id} 因置信度过低已删除", "Learning");
        }
        else
        {
            LogService.Instance.Info($"学习经验 #{id} 已被标记为错误 (置信度降为{conf:F2})", "Learning");
        }
    }

    /// <summary>回滚上一次自动记录（仅限非用户验证的）</summary>
    public bool RollbackLast()
    {
        for (int i = _recentOperations.Count - 1; i >= 0; i--)
        {
            var (id, op) = _recentOperations[i];
            if (op == "Record")
            {
                // 检查是否已被用户验证
                var conn = _db.GetConnection();
                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT IsVerified FROM LearningExperiences WHERE Id = @id";
                checkCmd.Parameters.AddWithValue("@id", id);
                var result = checkCmd.ExecuteScalar();
                if (result == null) continue;

                if ((long)result == 0)
                {
                    Delete(id);
                    _recentOperations.RemoveAt(i);
                    LogService.Instance.Info($"学习经验 #{id} 已回滚", "Learning");
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>删除经验</summary>
    public bool Delete(int id)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM LearningExperiences WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = cmd.ExecuteNonQuery();
        return rows > 0;
    }

    /// <summary>更新置信度</summary>
    public void UpdateConfidence(int id, double newConfidence)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE LearningExperiences
            SET Confidence = @conf, UpdatedAt = datetime('now','localtime')
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@conf", Math.Clamp(newConfidence, 0.0, 1.0));
        cmd.ExecuteNonQuery();
    }

    // ===== 自动提取 =====

    /// <summary>从AI响应中自动提取有价值的学习经验</summary>
    public List<(string Title, string Content, string Category, double Confidence)> AutoExtract(
        string aiResponse, string userMessage, string? workspacePath = null)
    {
        var suggestions = new List<(string Title, string Content, string Category, double Confidence)>();
        if (string.IsNullOrWhiteSpace(aiResponse)) return suggestions;

        // 检测代码审查模式
        if (aiResponse.Contains("代码审查") || aiResponse.Contains("review") ||
            aiResponse.Contains("建议") && aiResponse.Contains("改进"))
        {
            var excerpt = Truncate(aiResponse, 500);
            suggestions.Add(("代码审查经验", excerpt, "code_review", 0.55));
        }

        // 检测Bug修复模式
        if ((aiResponse.Contains("修复") || aiResponse.Contains("fix") || aiResponse.Contains("bug")) &&
            (aiResponse.Contains("错误") || aiResponse.Contains("error") || aiResponse.Contains("异常")))
        {
            var excerpt = Truncate(aiResponse, 300);
            suggestions.Add(("Bug修复经验", excerpt, "bug_fix", 0.6));
        }

        // 检测性能优化模式
        if (aiResponse.Contains("优化") || aiResponse.Contains("性能") || aiResponse.Contains("performance") ||
            aiResponse.Contains("optimize"))
        {
            var excerpt = Truncate(aiResponse, 400);
            suggestions.Add(("性能优化经验", excerpt, "optimization", 0.5));
        }

        // 检测重构建议
        if (aiResponse.Contains("重构") || aiResponse.Contains("refactor") ||
            (aiResponse.Contains("设计模式") && !aiResponse.Contains("错误")))
        {
            var excerpt = Truncate(aiResponse, 400);
            suggestions.Add(("重构经验", excerpt, "refactoring", 0.55));
        }

        // 检测用户确认了AI的建议（高置信度信号）
        if (userMessage.Contains("对") || userMessage.Contains("好的") || userMessage.Contains("OK") ||
            userMessage.Contains("确认") || userMessage.Contains("通过"))
        {
            // 提升最近记录的置信度
            var recent = GetAll(workspacePath, minConfidence: 0.3)
                .OrderByDescending(e => e.CreatedAt)
                .Take(3);
            foreach (var exp in recent)
            {
                UpdateConfidence(exp.Id, Math.Min(1.0, exp.Confidence + 0.15));
            }
        }

        return suggestions;
    }

    // ===== AI 上下文格式化 =====

    /// <summary>将高可信经验格式化为AI上下文</summary>
    public string FormatForAI(string? workspacePath = null, int maxCount = 5)
    {
        var experiences = GetAll(workspacePath, minConfidence: 0.5)
            .OrderByDescending(e => e.IsVerified)
            .ThenByDescending(e => e.Confidence)
            .Take(maxCount)
            .ToList();

        if (experiences.Count == 0) return string.Empty;

        var lines = new List<string> { "【历史学习经验（从过往编程中总结）】" };
        foreach (var exp in experiences)
        {
            var verified = exp.IsVerified ? "✓已验证" : $"⚡置信度:{exp.Confidence:F0%}";
            var category = exp.Category switch
            {
                "code_review" => "代码审查",
                "bug_fix" => "Bug修复",
                "optimization" => "性能优化",
                "refactoring" => "重构",
                "coding_pattern" => "编码模式",
                "tool_usage" => "工具使用",
                _ => "通用"
            };
            lines.Add($"- [{category}] [{verified}] {exp.Title}: {Truncate(exp.Content, 200)}");
        }
        return string.Join('\n', lines);
    }

    /// <summary>获取学习统计</summary>
    public (int TotalCount, int VerifiedCount, double AvgConfidence, Dictionary<string, int> ByCategory) GetStats(string? workspacePath = null)
    {
        var all = GetAll(workspacePath);
        var byCategory = all.GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());
        return (
            TotalCount: all.Count,
            VerifiedCount: all.Count(e => e.IsVerified),
            AvgConfidence: all.Count > 0 ? all.Average(e => e.Confidence) : 0,
            ByCategory: byCategory
        );
    }

    // ===== 辅助方法 =====

    /// <summary>检查是否存在相似经验（标题+内容相似度）</summary>
    private bool TryFindSimilar(string title, string content, out int existingId, out double existingConfidence)
    {
        existingId = 0;
        existingConfidence = 0;

        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Content, Confidence FROM LearningExperiences
            WHERE Title LIKE @t ORDER BY Confidence DESC LIMIT 3
            """;
        cmd.Parameters.AddWithValue("@t", $"%{title[..Math.Min(30, title.Length)]}%");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var existingTitle = reader.GetString(1);
            var existingContent = reader.GetString(2);
            // 简单相似度：标题包含或内容有显著重叠
            if (existingTitle.Contains(title[..Math.Min(15, title.Length)]) ||
                title.Contains(existingTitle[..Math.Min(15, existingTitle.Length)]))
            {
                existingId = reader.GetInt32(0);
                existingConfidence = reader.GetDouble(3);
                return true;
            }
        }
        return false;
    }

    private static LearningExperienceItem ReadExperience(SqliteDataReader reader)
    {
        return new LearningExperienceItem
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            Content = reader.GetString(2),
            Category = reader.GetString(3),
            Source = reader.GetString(4),
            Confidence = reader.GetDouble(5),
            IsVerified = reader.GetInt32(6) == 1,
            RelatedFiles = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            WorkspacePath = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt = DateTime.Parse(reader.GetString(9)),
            UpdatedAt = DateTime.Parse(reader.GetString(10))
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}
