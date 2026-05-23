using System.IO;
using System.Text;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 文件附件服务——管理上传文件、构建上下文
/// 支持超大文件自动分块、总量限制、隐私扫描、用户警告
/// </summary>
public class FileAttachmentService
{
    private readonly List<IFileAttachmentHandler> _handlers = new();
    private readonly List<FileAttachment> _attachments = new();
    private readonly PrivacyService _privacy = new();

    public IReadOnlyList<FileAttachment> Attachments => _attachments.AsReadOnly();
    public bool HasAttachments => _attachments.Count > 0;

    /// <summary>所有附件总字符数</summary>
    public int TotalCharCount => _attachments.Sum(a => a.CharCount);

    /// <summary>警告回调：当文件超限/被分块/总量超限/隐私阻断时触发</summary>
    public event Action<string>? OnWarning;

    /// <summary>信息回调：操作成功时通知</summary>
    public event Action<string>? OnInfo;

    public FileAttachmentService()
    {
        _handlers.Add(new TextFileAttachmentHandler());
    }

    /// <summary>注册自定义文件处理器</summary>
    public void RegisterHandler(IFileAttachmentHandler handler)
    {
        _handlers.Insert(0, handler);
    }

    public string GetFilter()
    {
        var exts = _handlers.SelectMany(h => h.SupportedExtensions).Distinct().OrderBy(e => e);
        var all = string.Join(";", exts.Select(e => $"*{e}"));
        return $"支持的文件 ({all})|{all}|所有文件 (*.*)|*.*";
    }

    /// <summary>添加文件附件（支持超大文件自动分块）</summary>
    public List<FileAttachment> AttachFiles(IEnumerable<string> filePaths)
    {
        var added = new List<FileAttachment>();
        var handler = _handlers[0]; // 使用默认处理器
        int skippedCount = 0;
        int chunkedCount = 0;
        int unsupportedCount = 0;
        int privacyBlockedCount = 0;
        long totalChunkedBytes = 0;
        var unsupportedNames = new List<string>();

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            if (_attachments.Any(a => a.FilePath == path)) continue;

            var fileName = Path.GetFileName(path);

            // === 0. 文件类型检查 ===
            if (!handler.CanHandle(path))
            {
                unsupportedCount++;
                unsupportedNames.Add(fileName);
                continue;
            }

            var info = new FileInfo(path);

            // === 1. 读取内容并做隐私扫描 ===
            string content;
            try
            {
                content = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                skippedCount++;
                OnWarning?.Invoke($"⚠️ {fileName} 读取失败: {ex.Message}");
                continue;
            }

            // === 2. 隐私扫描 ===
            var privacyResult = _privacy.Validate(content);
            if (privacyResult.ShouldBlock)
            {
                privacyBlockedCount++;
                OnWarning?.Invoke($"🔒 {fileName} 检测到以下敏感信息，已阻止上传:\n" +
                    string.Join("\n", privacyResult.BlockMatches.Select(m => $"  - {m.Pattern}: {m.MatchedText}")) +
                    $"\n请脱敏后重新添加。");
                continue;
            }
            if (privacyResult.ShouldWarn)
            {
                OnWarning?.Invoke($"⚠️ {fileName} 检测到可能敏感的凭据信息:\n" +
                    string.Join("\n", privacyResult.WarnMatches.Select(m => $"  - {m.Pattern}: {m.MatchedText}")) +
                    $"\n文件已添加，但建议检查后确认是否适合发送给大模型。");
            }

            // === 3. 文件过大 → 自动分块 ===
            if (info.Length > handler.MaxFileSize)
            {
                try
                {
                    var chunks = ChunkLargeFile(path, info, handler);
                    if (chunks.Count > 0)
                    {
                        foreach (var chunk in chunks)
                            _attachments.Add(chunk);
                        added.AddRange(chunks);
                        chunkedCount++;
                        totalChunkedBytes += info.Length;
                        OnInfo?.Invoke($"📦 {Path.GetFileName(path)} 文件较大 ({info.Length.ToSizeDisplay()})，已自动分为 {chunks.Count} 段上传");
                    }
                    else
                    {
                        skippedCount++;
                        OnWarning?.Invoke($"⚠️ {Path.GetFileName(path)} 无法读取，已跳过");
                    }
                }
                catch (Exception ex)
                {
                    skippedCount++;
                    OnWarning?.Invoke($"⚠️ {Path.GetFileName(path)} 分块失败: {ex.Message}");
                }
                continue;
            }

            // === 4. 正常大小文件 — 检查是否超过单块字符上限 ===
            try
            {
                // content 已在上方隐私扫描时读取
                if (content.Length > handler.MaxCharsPerChunk)
                {
                    // 文件不大但内容多 → 也分块
                    var chunks = SplitContentIntoChunks(path, info, content, handler);
                    foreach (var chunk in chunks)
                        _attachments.Add(chunk);
                    added.AddRange(chunks);
                    chunkedCount++;
                    totalChunkedBytes += info.Length;
                    OnInfo?.Invoke($"📦 {Path.GetFileName(path)} 内容较长 ({content.Length:N0} 字符)，已自动分为 {chunks.Count} 段上传");
                }
                else
                {
                    var attachment = new FileAttachment
                    {
                        FilePath = path,
                        FileSize = info.Length,
                        Content = content,
                        TotalChunks = 1,
                        ChunkIndex = 1
                    };
                    _attachments.Add(attachment);
                    added.Add(attachment);
                }
            }
            catch (Exception ex)
            {
                skippedCount++;
                OnWarning?.Invoke($"⚠️ {Path.GetFileName(path)} 读取失败: {ex.Message}");
            }
        }

        // === 3. 总量超限警告 ===
        var totalChars = TotalCharCount;
        if (totalChars > handler.MaxTotalChars)
        {
            OnWarning?.Invoke(
                $"⚠️ 所有附件合计 {totalChars:N0} 字符，已超过推荐上限 " +
                $"{handler.MaxTotalChars:N0} 字符（约 50000 tokens）。" +
                $"建议删除部分文件后分批上传，否则可能导致大模型上下文溢出。");
        }

        // 汇总提示
        if (unsupportedCount > 0)
            OnWarning?.Invoke($"⚠️ {unsupportedCount} 个文件类型不支持: {string.Join(", ", unsupportedNames)}。当前仅支持文本类文件（代码、文档、配置等）。");
        if (privacyBlockedCount > 0)
            OnWarning?.Invoke($"🔒 {privacyBlockedCount} 个文件因包含敏感隐私信息已被阻止上传。");
        if (chunkedCount > 0)
            OnInfo?.Invoke($"✅ 共添加 {added.Count} 个文件段（{chunkedCount} 个文件被自动分段，总计 {totalChunkedBytes.ToSizeDisplay()}）");

        return added;
    }

    /// <summary>移除单个附件（及其所有分块）</summary>
    public void RemoveAttachment(FileAttachment attachment)
    {
        if (attachment.IsChunked)
        {
            // 移除同一文件的所有分块
            _attachments.RemoveAll(a => a.FilePath == attachment.FilePath);
        }
        else
        {
            _attachments.Remove(attachment);
        }
    }

    public void Clear()
    {
        _attachments.Clear();
    }

    /// <summary>构建附加文件上下文</summary>
    public string BuildContext()
    {
        if (_attachments.Count == 0) return string.Empty;

        var handler = _handlers[0];
        var sb = new StringBuilder();
        sb.AppendLine("\n<attached_files>");

        foreach (var a in _attachments)
        {
            if (!handler.CanHandle(a.FilePath)) continue;

            if (a.IsChunked)
            {
                // 分块文件加上段号标记
                sb.AppendLine($"\n<!-- 文件分块: {a.DisplayName} -->");
            }
            sb.Append(handler.FormatForPrompt(a.ChunkHeader, a.Content));
        }
        sb.AppendLine("</attached_files>");
        return sb.ToString();
    }

    public string Summary => _attachments.Count == 0
        ? ""
        : $"📎 {_attachments.Count} 个文件段 ({_attachments.Sum(a => a.FileSize).ToSizeDisplay()})";

    // ===== 分块核心逻辑 =====

    /// <summary>将超大文件按行分块</summary>
    private List<FileAttachment> ChunkLargeFile(string path, FileInfo info, IFileAttachmentHandler handler)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
        return SplitContentIntoChunks(path, info, content, handler);
    }

    /// <summary>将文件内容按字符上限拆分为多个 FileAttachment</summary>
    private static List<FileAttachment> SplitContentIntoChunks(
        string path, FileInfo info, string content, IFileAttachmentHandler handler)
    {
        var chunks = new List<FileAttachment>();
        if (string.IsNullOrEmpty(content)) return chunks;

        var lines = content.Split('\n');
        var maxChars = handler.MaxCharsPerChunk;
        int chunkIndex = 1;
        var currentChunk = new StringBuilder();
        int currentChars = 0;

        foreach (var line in lines)
        {
            var lineWithNewline = line + "\n";
            int lineLen = lineWithNewline.Length;

            // 如果当前块加上这行会超限
            if (currentChars + lineLen > maxChars && currentChars > 0)
            {
                // 保存当前块
                chunks.Add(CreateChunk(path, info, currentChunk.ToString(), chunkIndex, handler));
                chunkIndex++;
                currentChunk.Clear();
                currentChars = 0;
            }

            currentChunk.Append(lineWithNewline);
            currentChars += lineLen;

            // 单行超长：强制截断
            if (currentChars >= maxChars)
            {
                chunks.Add(CreateChunk(path, info, currentChunk.ToString(), chunkIndex, handler));
                chunkIndex++;
                currentChunk.Clear();
                currentChars = 0;
            }
        }

        // 保存最后一块
        if (currentChunk.Length > 0)
        {
            chunks.Add(CreateChunk(path, info, currentChunk.ToString(), chunkIndex, handler));
        }

        // 回填 TotalChunks
        int total = chunks.Count;
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].TotalChunks = total;
            chunks[i].Chunks = chunks; // 所有分块共享引用
        }

        return chunks;
    }

    private static FileAttachment CreateChunk(string path, FileInfo info, string content, int index, IFileAttachmentHandler handler)
    {
        return new FileAttachment
        {
            FilePath = path,
            FileSize = info.Length,
            Content = content.TrimEnd(),
            ChunkIndex = index,
            TotalChunks = 1 // 稍后回填
        };
    }
}

internal static class FileSizeExtensions
{
    public static string ToSizeDisplay(this long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
