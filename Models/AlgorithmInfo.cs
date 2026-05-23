using System.Text.Json.Serialization;

namespace AIIDEWPF.Models;

/// <summary>算法信息模型</summary>
public class AlgorithmInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>编程语言</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    /// <summary>时间复杂度</summary>
    [JsonPropertyName("complexity")]
    public string Complexity { get; set; } = string.Empty;

    /// <summary>空间复杂度</summary>
    [JsonPropertyName("space_complexity")]
    public string SpaceComplexity { get; set; } = string.Empty;

    /// <summary>分类: sort, search, graph, dp, string, math, tree, greedy, backtracking, etc.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>标签</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>算法代码实现</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>来源文件路径（若从项目代码中提取）</summary>
    [JsonPropertyName("source_file")]
    public string SourceFile { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>格式化的算法标识</summary>
    [JsonIgnore]
    public string DisplayLabel => $"[{Category}] {Name} ({Language})";

    /// <summary>代码行数</summary>
    [JsonIgnore]
    public int LineCount => string.IsNullOrEmpty(Code) ? 0 : Code.Split('\n').Length;
}
