namespace AIIDEWPF.Services;

/// <summary>默认文本文件处理器——将文件内容嵌入为代码块上下文，支持大文件自动分块</summary>
public class TextFileAttachmentHandler : IFileAttachmentHandler
{
    public string HandlerName => "TextContext";

    /// <summary>单文件原始大小上限 2MB（超过则按字符数分块）</summary>
    public long MaxFileSize => 2 * 1024 * 1024;

    /// <summary>单块字符数上限 ≈ 50000 字符（约 12000 tokens）</summary>
    public int MaxCharsPerChunk => 50000;

    /// <summary>所有附件总计字符数上限 ≈ 200000 字符（约 50000 tokens）</summary>
    public int MaxTotalChars => 200000;

    public string[] SupportedExtensions => new[]
    {
        ".cs", ".java", ".py", ".js", ".ts", ".jsx", ".tsx", ".go", ".rs",
        ".c", ".cpp", ".h", ".hpp", ".swift", ".kt", ".dart", ".php", ".rb",
        ".pl", ".lua", ".r", ".scala", ".hs", ".erl", ".ex", ".clj", ".fs",
        ".vb", ".sql", ".sh", ".ps1", ".bat", ".cmd", ".m", ".jl", ".groovy",
        ".mm", ".asm", ".v", ".zig", ".nim", ".cr", ".ml", ".re", ".purs",
        ".elm", ".hx", ".vala", ".adb", ".f90", ".cbl", ".pas", ".d", ".scm",
        ".rkt", ".tcl",
        ".md", ".txt", ".log", ".csv", ".json", ".xml", ".yaml", ".yml",
        ".toml", ".ini", ".cfg", ".conf", ".config", ".props", ".targets",
        ".csproj", ".sln", ".fsproj", ".vbproj", ".xaml", ".axaml",
        ".html", ".css", ".scss", ".less", ".svg",
        ".gitignore", ".dockerignore", ".editorconfig", ".env", ".makefile"
    };

    public bool CanHandle(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    public string FormatForPrompt(string fileName, string content)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant().TrimStart('.');
        var lang = string.IsNullOrEmpty(ext) ? "" : ext;
        return $"\n<attached_file name=\"{fileName}\">\n```{lang}\n{content}\n```\n</attached_file>\n";
    }
}
