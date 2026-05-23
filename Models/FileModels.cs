namespace AIIDEWPF.Models;

public class FileItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public List<FileItem> Children { get; set; } = new();

    /// <summary>Git 状态: ' '=未修改, 'M'=已修改, 'A'=新增, 'D'=删除, '?'=未跟踪, 'R'=重命名</summary>
    public char GitStatus { get; set; } = ' ';

    /// <summary>Git 状态对应颜色</summary>
    public string GitStatusColor => GitStatus switch
    {
        'M' => "#e2c08d",  // 修改-橙色
        'A' => "#73c991",  // 新增-绿色
        'D' => "#f14c4c",  // 删除-红色
        'R' => "#c586c0",  // 重命名-紫色
        '?' => "#6ca6cd",  // 未跟踪-蓝色
        _ => "#d4d4d4"     // 默认-灰色
    };

    /// <summary>Git 状态标记文本</summary>
    public string GitStatusMark => GitStatus switch
    {
        'M' => "●",
        'A' => "+",
        'D' => "-",
        'R' => "↻",
        '?' => "?",
        _ => ""
    };
}
