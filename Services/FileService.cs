using System.IO;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

public class FileService
{
    /// <summary>读取文件内容，支持指定行范围</summary>
    /// <param name="path">文件绝对路径</param>
    /// <param name="startLine">起始行（1-based），null 表示从头开始</param>
    /// <param name="endLine">结束行（1-based），null 表示到文件末尾</param>
    public string ReadFile(string path, int? startLine = null, int? endLine = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        if (startLine == null && endLine == null) return File.ReadAllText(path);

        var lines = File.ReadAllLines(path);
        var start = Math.Max(0, (startLine ?? 1) - 1);
        var end = Math.Min(lines.Length, endLine ?? lines.Length);
        return string.Join("\n", lines.Skip(start).Take(end - start));
    }

    /// <summary>写入文件（自动创建父目录）</summary>
    public void SaveFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        LogService.Instance.Debug($"文件已保存: {Path.GetFileName(path)}", "File");
    }

    /// <summary>在文件中搜索并替换文本，支持全部替换或单次替换</summary>
    public SearchReplaceResult SearchReplace(string path, string originalText, string newText, bool replaceAll)
    {
        if (!File.Exists(path)) return new SearchReplaceResult { Success = false, Error = "File not found" };
        var content = File.ReadAllText(path);
        if (!content.Contains(originalText)) return new SearchReplaceResult { Success = false, Error = "Text not found in file" };

        if (replaceAll)
        {
            var count = content.Split(originalText).Length - 1;
            content = content.Replace(originalText, newText);
            File.WriteAllText(path, content);
            LogService.Instance.Debug($"文件已替换: {Path.GetFileName(path)} ({count}处)", "File");
            return new SearchReplaceResult { Success = true, Replacements = count };
        }

        var idx = content.IndexOf(originalText, StringComparison.Ordinal);
        content = content.Substring(0, idx) + newText + content.Substring(idx + originalText.Length);
        File.WriteAllText(path, content);
        LogService.Instance.Debug($"文件已替换: {Path.GetFileName(path)} (1处)", "File");
        return new SearchReplaceResult { Success = true, Replacements = 1 };
    }

    /// <summary>创建文件（自动创建父目录）</summary>
    public void CreateFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        LogService.Instance.Info($"文件已创建: {Path.GetFileName(path)}", "File");
    }

    /// <summary>创建目录，返回 true 表示成功创建</summary>
    public bool CreateDirectory(string path)
    {
        if (Directory.Exists(path)) return false;
        Directory.CreateDirectory(path);
        LogService.Instance.Debug($"目录已创建: {Path.GetFileName(path)}", "File");
        return true;
    }

    /// <summary>删除文件</summary>
    /// <returns>true 表示成功删除，false 表示文件不存在</returns>
    public bool DeleteFile(string path)
    {
        if (!File.Exists(path)) return false;
        File.Delete(path);
        LogService.Instance.Info($"文件已删除: {Path.GetFileName(path)}", "File");
        return true;
    }

    /// <summary>递归删除目录</summary>
    public bool DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        Directory.Delete(path, true);
        LogService.Instance.Info($"目录已删除: {Path.GetFileName(path)}", "File");
        return true;
    }

    /// <summary>列出目录下的文件和子目录（目录优先）</summary>
    public List<DirEntry> ListDir(string path)
    {
        var result = new List<DirEntry>();
        if (!Directory.Exists(path)) return result;
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            var attr = File.GetAttributes(entry);
            result.Add(new DirEntry
            {
                Name = Path.GetFileName(entry),
                IsDirectory = attr.HasFlag(FileAttributes.Directory),
                IsFile = !attr.HasFlag(FileAttributes.Directory)
            });
        }
        return result.OrderBy(e => e.IsDirectory ? 0 : 1).ThenBy(e => e.Name).ToList();
    }

    /// <summary>按 glob 模式搜索文件（结果上限200个）</summary>
    public List<string> SearchFiles(string root, string glob)
    {
        var results = new List<string>();
        SearchFilesRecursive(root, root, glob, results);
        return results.Take(200).ToList();
    }

    private void SearchFilesRecursive(string basePath, string currentDir, string pattern, List<string> results)
    {
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(currentDir))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || name == "node_modules") continue;
                var attr = File.GetAttributes(entry);
                if (attr.HasFlag(FileAttributes.Directory))
                {
                    SearchFilesRecursive(basePath, entry, pattern, results);
                }
                else if (MatchGlob(name, pattern))
                {
                    results.Add(entry);
                }
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"文件搜索跳过异常目录: {ex.Message}", "FileService"); }
    }

    private static bool MatchGlob(string fileName, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
    }

    /// <summary>根据文件扩展名返回语言名称</summary>
    public string GetLanguageFromExtension(string path)
    {
        var ext = Path.GetExtension(path)?.ToLower() ?? "";
        return ext switch
        {
            ".cs" => "C#",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".jsx" => "JSX",
            ".tsx" => "TSX",
            ".py" => "Python",
            ".html" or ".htm" => "HTML",
            ".css" => "CSS",
            ".json" => "JSON",
            ".xml" => "XML",
            ".yaml" or ".yml" => "YAML",
            ".md" => "Markdown",
            ".sql" => "SQL",
            ".java" => "Java",
            ".go" => "Go",
            ".rs" => "Rust",
            ".cpp" or ".c" or ".h" => "C/C++",
            _ => "Plain Text"
        };
    }
}

/// <summary>文件搜索替换操作结果</summary>
public class SearchReplaceResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public int Replacements { get; set; }
}

/// <summary>目录条目</summary>
public class DirEntry
{
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool IsFile { get; set; }
}
