using System.Collections.Generic;

namespace AIIDEWPF.Models;

/// <summary>用户上传的文件附件</summary>
public class FileAttachment
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public long FileSize { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>内容字符数</summary>
    public int CharCount => Content?.Length ?? 0;

    // ===== 分块支持 =====
    /// <summary>是否为被分块的大文件</summary>
    public bool IsChunked => Chunks.Count > 1;
    /// <summary>当前分块索引（从1开始）</summary>
    public int ChunkIndex { get; set; } = 1;
    /// <summary>总分块数</summary>
    public int TotalChunks { get; set; } = 1;
    /// <summary>分块头部描述</summary>
    public string ChunkHeader => IsChunked ? $"【{FileName} - 第 {ChunkIndex}/{TotalChunks} 段】" : FileName;
    /// <summary>所有分块子项</summary>
    public List<FileAttachment> Chunks { get; set; } = new();
    /// <summary>是否为主文件（分块中的第一个）</summary>
    public bool IsMaster => ChunkIndex == 1;

    /// <summary>文件图标（按扩展名）</summary>
    public string Icon => System.IO.Path.GetExtension(FilePath).ToLowerInvariant() switch
    {
        ".cs" or ".java" or ".go" or ".rs" or ".ts" or ".js" or ".py" or ".cpp" or ".c" => "📝",
        ".md" or ".txt" or ".log" => "📄",
        ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".config" => "⚙️",
        ".csproj" or ".sln" or ".props" or ".targets" => "🏗️",
        ".sql" => "🗃️",
        ".sh" or ".ps1" or ".bat" or ".cmd" => "⚡",
        ".html" or ".css" or ".scss" => "🌐",
        _ => "📎"
    };

    /// <summary>分块图标</summary>
    public string DisplayIcon => IsChunked && ChunkIndex > 1 ? "📋" : Icon;

    /// <summary>文件大小显示</summary>
    public string SizeDisplay => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):F1} MB"
    };

    /// <summary>UI 显示名称（含分块信息）</summary>
    public string DisplayName => IsChunked ? $"{FileName} [{ChunkIndex}/{TotalChunks}]" : FileName;

    /// <summary>UI 工具提示</summary>
    public string ToolTip => IsChunked
        ? $"{FileName} — 已自动分段 ({FileSize:N0} 字节, 共 {TotalChunks} 段)"
        : $"{FileName} — {SizeDisplay}";
}
