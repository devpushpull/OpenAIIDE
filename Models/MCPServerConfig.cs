namespace AIIDEWPF.Models;

/// <summary>MCP 服务器配置（MCP协议由第三方实现，此处仅管理配置）</summary>
public class MCPServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty; // e.g. "npx @modelcontextprotocol/server-filesystem"
    public string[] Args { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Env { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}
