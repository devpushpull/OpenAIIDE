namespace AIIDEWPF.Services;

/// <summary>文件附件处理器接口——各LLM提供商可实现不同的文件处理策略</summary>
public interface IFileAttachmentHandler
{
    /// <summary>是否支持处理此类型文件</summary>
    bool CanHandle(string filePath);

    /// <summary>将文件内容格式化为 prompt 上下文</summary>
    string FormatForPrompt(string fileName, string content);

    /// <summary>单文件原始大小上限（字节），超过后自动分块处理</summary>
    long MaxFileSize { get; }

    /// <summary>单块字符数上限（用于自动分块，约 50000 字符 ≈ 12000 tokens）</summary>
    int MaxCharsPerChunk { get; }

    /// <summary>所有附件总计字符数上限（约 200000 字符 ≈ 50000 tokens）</summary>
    int MaxTotalChars { get; }

    /// <summary>支持的文件扩展名（含点，如 ".cs", ".py"）</summary>
    string[] SupportedExtensions { get; }

    /// <summary>处理策略名称</summary>
    string HandlerName { get; }
}
