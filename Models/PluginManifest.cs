namespace AIIDEWPF.Models;

/// <summary>插件清单（第三方插件通过此清单注册能力）</summary>
public class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string EntryPoint { get; set; } = string.Empty; // dll 或 js 入口
    public bool Enabled { get; set; } = true;
    public string[] Capabilities { get; set; } = Array.Empty<string>(); // tool, view, language, theme
    public string[] Dependencies { get; set; } = Array.Empty<string>();
}
