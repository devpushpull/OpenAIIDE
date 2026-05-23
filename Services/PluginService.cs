using System.IO;
using System.Text.Json;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>插件服务：扫描 plugins/ 目录下的插件清单（仅管理UI，实际能力由第三方实现）</summary>
public class PluginService
{
    private readonly string _pluginDir;
    private readonly string _downloadCacheDir;

    public PluginService(string? projectPath = null)
    {
        _pluginDir = Path.Combine(projectPath ?? Environment.CurrentDirectory, "plugins");
        _downloadCacheDir = Path.Combine(_pluginDir, ".download-cache");
    }

    public List<PluginManifest> ScanPlugins()
    {
        var plugins = new List<PluginManifest>();
        if (!Directory.Exists(_pluginDir)) return plugins;

        foreach (var dir in Directory.GetDirectories(_pluginDir))
        {
            // 跳过隐藏目录（如下载缓存、备份等）
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(".")) continue;

            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json);
                if (manifest != null && !string.IsNullOrEmpty(manifest.Id))
                {
                    manifest.Id = Path.GetFileName(dir);
                    plugins.Add(manifest);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warn($"插件清单解析失败: {dir} | {ex.Message}", "Plugin");
            }
        }

        LogService.Instance.Info($"插件扫描完成: {plugins.Count} 个已安装", "Plugin");
        return plugins;
    }

    public void SaveManifest(string pluginId, PluginManifest manifest)
    {
        var dir = Path.Combine(_pluginDir, pluginId);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, "manifest.json"), json);
        LogService.Instance.Info($"插件清单已保存: {pluginId}", "Plugin");
    }

    /// <summary>卸载插件：删除插件目录、清理下载缓存、清除更新标记</summary>
    public void DeletePlugin(string pluginId)
    {
        var dir = Path.Combine(_pluginDir, pluginId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
            LogService.Instance.Info($"插件目录已删除: {pluginId}", "Plugin");
        }

        // 清理下载缓存中该插件的文件
        ClearDownloadCache(pluginId);

        // 清除更新标记（如果有）
        var updateMarker = Path.Combine(_pluginDir, pluginId, ".update_pending");
        // 目录已删除，标记也随之删除，无需额外处理

        LogService.Instance.Info($"插件卸载完成: {pluginId}（含残留清理）", "Plugin");
    }

    /// <summary>清理指定插件的下载缓存文件</summary>
    public void ClearDownloadCache(string? pluginId = null)
    {
        if (!Directory.Exists(_downloadCacheDir)) return;

        if (pluginId != null)
        {
            // 清理特定插件的缓存文件
            var pattern = $"{pluginId}.*";
            foreach (var file in Directory.GetFiles(_downloadCacheDir, pattern))
            {
                try
                {
                    File.Delete(file);
                    LogService.Instance.Info($"下载缓存已清理: {Path.GetFileName(file)}", "Plugin");
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warn($"清理缓存文件失败: {file} | {ex.Message}", "Plugin");
                }
            }
        }
        else
        {
            // 清理全部缓存
            try
            {
                Directory.Delete(_downloadCacheDir, true);
                LogService.Instance.Info("下载缓存目录已全部清理", "Plugin");
            }
            catch (Exception ex)
            {
                LogService.Instance.Warn($"清理缓存目录失败: {ex.Message}", "Plugin");
            }
        }
    }

    /// <summary>获取下载缓存目录大小（用于UI显示）</summary>
    public string GetCacheSizeDisplay()
    {
        if (!Directory.Exists(_downloadCacheDir)) return "0 KB";
        try
        {
            long totalSize = 0;
            foreach (var file in Directory.GetFiles(_downloadCacheDir, "*", SearchOption.AllDirectories))
            {
                totalSize += new FileInfo(file).Length;
            }
            return totalSize switch
            {
                > 1024 * 1024 => $"{totalSize / (1024.0 * 1024.0):F1} MB",
                > 1024 => $"{totalSize / 1024.0:F1} KB",
                _ => $"{totalSize} B"
            };
        }
        catch { return "N/A"; }
    }

    /// <summary>比较两个版本号字符串，返回 1(更新) / 0(相同) / -1(更旧)</summary>
    public static int CompareVersions(string v1, string v2)
    {
        try
        {
            var ver1 = new Version(v1.TrimStart('v', 'V'));
            var ver2 = new Version(v2.TrimStart('v', 'V'));
            return ver1.CompareTo(ver2);
        }
        catch
        {
            return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>判断指定插件是否有可用更新</summary>
    public bool HasUpdate(string pluginId, string installedVersion, string latestVersion)
    {
        return CompareVersions(latestVersion, installedVersion) > 0;
    }

    public string GetPluginDir() => _pluginDir;

    /// <summary>获取下载缓存目录路径</summary>
    public string GetDownloadCacheDir() => _downloadCacheDir;
}
