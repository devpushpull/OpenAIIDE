using System.IO;

namespace AIIDEWPF.Services;

/// <summary>@ 提及服务——实时模糊搜索项目文件和文件夹</summary>
public class AtMentionService
{
    private string _projectPath = string.Empty;
    private List<AtMentionItem>? _cachedItems;
    private DateTime _cacheTime;

    public void SetProjectPath(string path)
    {
        if (_projectPath == path && _cachedItems != null) return;
        _projectPath = path;
        _cachedItems = null;
    }

    /// <summary>收集项目所有文件和文件夹（带缓存）</summary>
    private List<AtMentionItem> GetItems()
    {
        if (_cachedItems != null && (DateTime.Now - _cacheTime).TotalSeconds < 30)
            return _cachedItems;

        var items = new List<AtMentionItem>();
        if (string.IsNullOrEmpty(_projectPath) || !Directory.Exists(_projectPath))
        {
            _cachedItems = items;
            return items;
        }

        try
        {
            CollectEntries(_projectPath, "", items, 3);
        }
        catch { }

        _cachedItems = items;
        _cacheTime = DateTime.Now;
        return items;
    }

    private void CollectEntries(string basePath, string relativePath, List<AtMentionItem> items, int maxDepth)
    {
        if (maxDepth < 0) return;
        var fullDir = string.IsNullOrEmpty(relativePath) ? basePath : Path.Combine(basePath, relativePath);

        try
        {
            // 添加子目录
            foreach (var dir in Directory.GetDirectories(fullDir))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name == "node_modules" || name == "bin" || name == "obj" || name == "dist") continue;

                var relDir = string.IsNullOrEmpty(relativePath) ? name : relativePath + "/" + name;
                items.Add(new AtMentionItem
                {
                    Name = name,
                    RelativePath = relDir,
                    IsDirectory = true
                });
                CollectEntries(basePath, relDir, items, maxDepth - 1);
            }

            // 添加文件
            foreach (var file in Directory.GetFiles(fullDir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith('.')) continue;
                var relFile = string.IsNullOrEmpty(relativePath) ? name : relativePath + "/" + name;
                items.Add(new AtMentionItem
                {
                    Name = name,
                    RelativePath = relFile,
                    IsDirectory = false,
                    FullPath = file
                });
            }
        }
        catch { }
    }

    /// <summary>模糊搜索——输入 @ 后面的文字，返回匹配列表</summary>
    public List<AtMentionItem> Search(string query)
    {
        var items = GetItems();
        if (string.IsNullOrEmpty(query))
            return items.OrderByDescending(i => i.Score).Take(12).ToList();

        var q = query.ToLowerInvariant();
        return items
            .Where(i => i.Name.ToLowerInvariant().Contains(q) || i.RelativePath.ToLowerInvariant().Contains(q))
            .OrderBy(i => i.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.Name.Length)
            .Take(12)
            .ToList();
    }

    /// <summary>检测输入中是否包含 @file 或 @folder 命令并解析出参数</summary>
    public AtMentionCommand? ParseCommand(string text, int cursorPos)
    {
        if (cursorPos <= 0) return null;

        // 从光标位置向前找 @ 符号
        var searchStart = Math.Max(0, cursorPos - 1);
        var atIdx = text.LastIndexOf('@', searchStart);
        if (atIdx < 0) return null;
        if (atIdx > 0 && !char.IsWhiteSpace(text[atIdx - 1])) return null; // @ 前面不能是字母

        var afterAt = text[(atIdx + 1)..Math.Min(cursorPos, text.Length)];

        // 检查是否是 @file 或 @folder 命令
        if (afterAt.StartsWith("file", StringComparison.OrdinalIgnoreCase))
        {
            var query = afterAt.Length > 4 && afterAt[4] == ' ' ? afterAt[5..].TrimStart() : "";
            return new AtMentionCommand { Type = MentionType.File, Query = query, StartIndex = atIdx };
        }
        if (afterAt.StartsWith("folder", StringComparison.OrdinalIgnoreCase))
        {
            var query = afterAt.Length > 6 && afterAt[6] == ' ' ? afterAt[7..].TrimStart() : "";
            return new AtMentionCommand { Type = MentionType.Folder, Query = query, StartIndex = atIdx };
        }

        // 单独的 @ 就触发通用文件搜索
        var textUpToCursor = text[..cursorPos];
        // 确保 @ 后面没有空格或只跟了部分文件名
        var partial = afterAt.Trim();
        if (string.IsNullOrEmpty(partial) || (partial.Length > 0 && !partial.Contains(' ')))
            return new AtMentionCommand { Type = MentionType.Auto, Query = partial, StartIndex = atIdx };

        return null;
    }

    /// <summary>替换文本中的 @file/@folder 命令为引用标签</summary>
    public string ResolveCommand(string text, AtMentionItem item)
    {
        // 找到最后一个 @file 或 @folder 或 @ 并替换
        var atIdx = text.LastIndexOf('@');
        if (atIdx < 0) return text;

        var prefix = atIdx > 0 && !char.IsWhiteSpace(text[atIdx - 1]) ? text : text[..atIdx];
        var label = item.IsDirectory ? $"@folder:{item.RelativePath}" : $"@file:{item.RelativePath}";
        return prefix + label + " ";
    }

    /// <summary>刷新缓存</summary>
    public void Refresh() => _cachedItems = null;
}

/// <summary>@ 提及项目条目</summary>
public class AtMentionItem
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string Icon => IsDirectory ? "📁" : "📄";
    public string TypeLabel => IsDirectory ? "文件夹" : "文件";
    /// <summary>评分：当前编辑文件排第一</summary>
    public int Score { get; set; }
}

/// <summary>@ 命令解析结果</summary>
public class AtMentionCommand
{
    public MentionType Type { get; set; }
    public string Query { get; set; } = string.Empty;
    public int StartIndex { get; set; }
}

public enum MentionType { File, Folder, Auto }
