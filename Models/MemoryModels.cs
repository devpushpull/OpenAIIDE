namespace AIIDEWPF.Models;

/// <summary>记忆条目</summary>
public class MemoryItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "user_preferences"; // user_preferences / project_info / development_standards / lessons_learned
    public string Scope { get; set; } = "global";       // global / project / session
    public string? WorkspacePath { get; set; }           // project 作用域时关联的工作区路径
    public string Keywords { get; set; } = string.Empty; // 逗号分隔的关键词
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>规则条目</summary>
public class RuleItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string RuleType { get; set; } = "always"; // always / manual / model_decision / glob
    public string? GlobPattern { get; set; }         // 文件匹配模式，如 *.cs, src/*.java
    public string? Description { get; set; }          // model_decision 类型的场景描述
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>提示词库条目 —— 用户保存的常用提示词模板</summary>
public class PromptLibraryItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "general"; // general / code_review / bug_fix / architecture / testing / refactoring
    public string Scope { get; set; } = "global";      // global / project / session
    public string? WorkspacePath { get; set; }
    public string Tags { get; set; } = string.Empty;   // 逗号分隔的标签
    public int UsageCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>学习经验条目 —— 自我学习系统自动记录的编程经验</summary>
public class LearningExperienceItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "general";       // general / coding_pattern / bug_fix / refactoring / optimization / tool_usage
    public string Source { get; set; } = "auto_detected";    // auto_detected / ai_self_eval / user_verified
    public double Confidence { get; set; } = 0.5;            // 0.0 ~ 1.0 置信度
    public bool IsVerified { get; set; }                      // 用户是否已验证
    public string RelatedFiles { get; set; } = string.Empty;  // 逗号分隔的相关文件路径
    public string? WorkspacePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
