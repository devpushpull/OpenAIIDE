using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

public class SearchService
{
    public List<GrepMatch> Grep(string root, string regex, string? globFilter = null)
    {
        var results = new List<GrepMatch>();
        var re = new Regex(regex, RegexOptions.Compiled);
        GrepRecursive(root, root, re, globFilter, results);
        return results.Take(200).ToList();
    }

    private void GrepRecursive(string basePath, string currentDir, Regex regex, string? globFilter, List<GrepMatch> results)
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
                    GrepRecursive(basePath, entry, regex, globFilter, results);
                }
                else
                {
                    if (!string.IsNullOrEmpty(globFilter) && !MatchGlob(name, globFilter)) continue;
                    if (results.Count >= 200) return;
                    try
                    {
                        var lines = File.ReadAllLines(entry);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (regex.IsMatch(lines[i]))
                            {
                                results.Add(new GrepMatch
                                {
                                    File = entry,
                                    Line = i + 1,
                                    Content = lines[i].Trim()
                                });
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public List<SymbolMatch> SearchSymbol(string root, string symbolName)
    {
        var results = new List<SymbolMatch>();
        var escaped = Regex.Escape(symbolName);
        var re = new Regex($@"\b{escaped}\b", RegexOptions.Compiled);
        SymbolSearchRecursive(root, root, re, results);
        return results.Take(100).ToList();
    }

    private void SymbolSearchRecursive(string basePath, string currentDir, Regex regex, List<SymbolMatch> results)
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
                    SymbolSearchRecursive(basePath, entry, regex, results);
                }
                else
                {
                    var ext = Path.GetExtension(name).ToLower();
                    var codeExts = new[] { ".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".go", ".java", ".rs", ".cpp", ".c", ".h" };
                    if (!codeExts.Contains(ext)) continue;
                    if (results.Count >= 100) return;
                    try
                    {
                        var lines = File.ReadAllLines(entry);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (regex.IsMatch(lines[i]))
                            {
                                results.Add(new SymbolMatch
                                {
                                    File = entry,
                                    Line = i + 1,
                                    Content = lines[i].Trim()
                                });
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public List<SearchResult> SemanticSearch(string root, List<string> keywords)
    {
        var results = new List<SearchResult>();
        SemanticSearchRecursive(root, root, keywords, results);
        return results.OrderByDescending(r => r.Relevance).Take(50).ToList();
    }

    private void SemanticSearchRecursive(string basePath, string currentDir, List<string> keywords, List<SearchResult> results)
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
                    SemanticSearchRecursive(basePath, entry, keywords, results);
                }
                else
                {
                    var fi = new FileInfo(entry);
                    if (fi.Length > 500000) continue;
                    try
                    {
                        var content = File.ReadAllText(entry).ToLower();
                        var score = keywords.Count(k => content.Contains(k.ToLower()));
                        var pathScore = keywords.Count(k => entry.ToLower().Contains(k.ToLower()));
                        var totalScore = score * 2 + pathScore;
                        if (totalScore > 0)
                            results.Add(new SearchResult { File = entry, Relevance = totalScore });
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public string GetProjectStructure(string root, int maxDepth = 4)
    {
        var sb = new System.Text.StringBuilder();
        GetStructureRecursive(root, root, sb, 0, maxDepth);
        return sb.ToString();
    }

    private void GetStructureRecursive(string basePath, string currentDir, System.Text.StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        try
        {
            var entries = Directory.GetFileSystemEntries(currentDir)
                .Select(e => (Path: e, Name: Path.GetFileName(e)))
                .Where(e => !e.Name.StartsWith('.') && e.Name != "node_modules")
                .OrderBy(e => !Directory.Exists(e.Path))
                .ThenBy(e => e.Name)
                .ToList();

            foreach (var entry in entries)
            {
                var isDir = Directory.Exists(entry.Path);
                sb.AppendLine(new string(' ', depth * 2) + (isDir ? "📁 " : "📄 ") + entry.Name);
                if (isDir)
                    GetStructureRecursive(basePath, entry.Path, sb, depth + 1, maxDepth);
            }
        }
        catch { }
    }

    private static bool MatchGlob(string fileName, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
    }
}

public class GrepMatch
{
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class SymbolMatch
{
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class SearchResult
{
    public string File { get; set; } = string.Empty;
    public int Relevance { get; set; }
}
