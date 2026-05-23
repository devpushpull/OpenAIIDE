using System.IO;
using System.Text.Json;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>MCP 服务：管理 MCP 服务器配置（增删改查），实际协议能力由第三方实现</summary>
public class MCPService
{
    private readonly string _configPath;
    private List<MCPServerConfig> _servers = new();

    public MCPService(string? projectPath = null)
    {
        var dir = Path.Combine(projectPath ?? Environment.CurrentDirectory, ".codeartsdoer", "mcp");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "servers.json");
        Load();
        LogService.Instance.Info($"MCP服务已初始化, 已加载 {_servers.Count} 个服务器", "MCP");
    }

    /// <summary>只读的 MCP 服务器列表</summary>
    public IReadOnlyList<MCPServerConfig> Servers => _servers.AsReadOnly();

    private void Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _servers = JsonSerializer.Deserialize<List<MCPServerConfig>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"MCP配置加载失败: {ex.Message}", "MCP");
            _servers = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_servers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"MCP配置保存失败: {ex.Message}", "MCP");
        }
    }

    /// <summary>添加 MCP 服务器配置并持久化</summary>
    public void Add(MCPServerConfig config)
    {
        _servers.Add(config);
        Save();
        LogService.Instance.Info($"MCP服务器已添加: {config.Id} ({config.Name})", "MCP");
    }

    /// <summary>更新 MCP 服务器配置</summary>
    public void Update(MCPServerConfig config)
    {
        var idx = _servers.FindIndex(s => s.Id == config.Id);
        if (idx >= 0)
        {
            _servers[idx] = config;
            Save();
            LogService.Instance.Info($"MCP服务器已更新: {config.Id}", "MCP");
        }
    }

    /// <summary>删除 MCP 服务器配置</summary>
    public void Delete(string id)
    {
        _servers.RemoveAll(s => s.Id == id);
        Save();
        LogService.Instance.Info($"MCP服务器已删除: {id}", "MCP");
    }

    /// <summary>按 ID 获取 MCP 服务器配置</summary>
    public MCPServerConfig? Get(string id) => _servers.FirstOrDefault(s => s.Id == id);
}
