using System.IO;
using System.Text.Json;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>Git 平台预设持久化服务</summary>
public static class GitPlatformStore
{
    private static string GetPath(string projectPath)
        => Path.Combine(projectPath, ".aiide", "git_platforms.json");

    public static List<GitPlatformPreset> Load(string projectPath)
    {
        var path = GetPath(projectPath);
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<GitPlatformPreset>>(json);
                if (list != null) return list;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Git 平台预设加载失败: {ex.Message}", "GitPlatform");
        }
        return new();
    }

    public static void Save(string projectPath, List<GitPlatformPreset> presets)
    {
        var path = GetPath(projectPath);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Git 平台预设保存失败: {ex.Message}", "GitPlatform");
        }
    }
}
