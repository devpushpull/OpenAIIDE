using System.IO;
using System.Text.Json;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>Git 配置持久化 —— 保存到 .aiide/git_config.json</summary>
public static class GitConfigStore
{
    private static string GetPath(string projectPath)
        => Path.Combine(projectPath, ".aiide", "git_config.json");

    public static GitConfig? Load(string projectPath)
    {
        var path = GetPath(projectPath);
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<GitConfig>(json);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Git 配置加载失败: {ex.Message}", "GitConfig");
        }
        return null;
    }

    public static void Save(string projectPath, GitConfig config)
    {
        var path = GetPath(projectPath);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Git 配置保存失败: {ex.Message}", "GitConfig");
        }
    }
}
