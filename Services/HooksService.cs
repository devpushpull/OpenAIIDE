using System.Diagnostics;
using System.Text.Json;
using System.IO;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

/// <summary>
/// 钩子服务 —— 在 AI 操作生命周期的关键节点执行自定义脚本
/// 配置文件: {project}/.aiide/hooks.json
/// 支持事件: pre_tool, post_tool, pre_ai_call, post_ai_call, on_file_change
/// 支持占位符: {FILE}, {TOOL}, {ARGS}, {PROJECT}
/// </summary>
public class HooksService
{
    private readonly string? _projectPath;
    private readonly string _hooksConfigPath;
    private List<HookConfig> _hooks = new();

    public HooksService(string? projectPath = null)
    {
        _projectPath = projectPath;
        if (!string.IsNullOrEmpty(projectPath))
        {
            _hooksConfigPath = Path.Combine(projectPath, ".aiide", "hooks.json");
            Load();
        }
        else
        {
            _hooksConfigPath = string.Empty;
        }
    }

    // ===== 配置管理 =====

    /// <summary>从配置文件中加载钩子</summary>
    private void Load()
    {
        if (string.IsNullOrEmpty(_hooksConfigPath) || !File.Exists(_hooksConfigPath))
            return;

        try
        {
            var json = File.ReadAllText(_hooksConfigPath);
            var loaded = JsonSerializer.Deserialize<List<HookConfig>>(json);
            if (loaded != null)
                _hooks = loaded;
            LogService.Instance.Info($"钩子配置已加载: {_hooks.Count} 个", "Hooks");
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"钩子配置文件解析失败: {ex.Message}", "Hooks");
        }
    }

    /// <summary>保存钩子配置到文件</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(_hooksConfigPath)) return;
        try
        {
            var dir = Path.GetDirectoryName(_hooksConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_hooks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_hooksConfigPath, json);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"钩子配置保存失败: {ex.Message}", "Hooks");
        }
    }

    /// <summary>获取所有钩子配置（可编辑）</summary>
    public List<HookConfig> GetAll() => _hooks;

    /// <summary>按事件类型获取已启用的钩子</summary>
    public List<HookConfig> GetEnabledByEvent(string eventType)
    {
        return _hooks
            .Where(h => h.Enabled && string.Equals(h.Event, eventType, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ===== 钩子执行 =====

    /// <summary>
    /// 执行指定事件类型的所有已启用钩子
    /// 返回 null 表示全部成功/跳过；返回错误消息表示有阻断钩子失败
    /// </summary>
    public async Task<string?> RunHooksAsync(string eventType, Dictionary<string, string>? placeholders = null)
    {
        var hooks = GetEnabledByEvent(eventType);
        if (hooks.Count == 0) return null;

        LogService.Instance.Info($"执行 {eventType} 钩子: {hooks.Count} 个", "Hooks");

        foreach (var hook in hooks)
        {
            var result = await RunSingleHookAsync(hook, placeholders);
            if (result != null && hook.BlockOnFailure)
            {
                LogService.Instance.Warn($"钩子失败并阻断: {hook.Id} | {result}", "Hooks");
                return result;
            }
            if (result != null)
            {
                LogService.Instance.Warn($"钩子失败（非阻断）: {hook.Id} | {result}", "Hooks");
            }
        }

        LogService.Instance.Info($"{eventType} 钩子执行完成", "Hooks");
        return null;
    }

    /// <summary>执行单个钩子</summary>
    private async Task<string?> RunSingleHookAsync(HookConfig hook, Dictionary<string, string>? placeholders)
    {
        var command = ReplacePlaceholders(hook.Command, placeholders);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _projectPath ?? Environment.CurrentDirectory
                }
            };

            var timeoutMs = Math.Min(hook.TimeoutSeconds * 1000, 60000); // 最大60秒
            process.Start();

            var readOutput = process.StandardOutput.ReadToEndAsync();
            var readError = process.StandardError.ReadToEndAsync();

            if (await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(timeoutMs)) != process.WaitForExitAsync())
            {
                try { process.Kill(); } catch { }
                LogService.Instance.Warn($"钩子超时: {hook.Id} (>{hook.TimeoutSeconds}s)", "Hooks");
                return $"钩子执行超时 ({hook.TimeoutSeconds}s)";
            }

            var stdout = await readOutput;
            var stderr = await readError;

            if (process.ExitCode == 0)
            {
                if (!string.IsNullOrWhiteSpace(stdout))
                    LogService.Instance.Info($"钩子输出 [{hook.Id}]: {stdout.Trim()[..Math.Min(200, stdout.Trim().Length)]}", "Hooks");
                return null; // 成功
            }
            else
            {
                var errorMsg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                LogService.Instance.Warn($"钩子失败 [{hook.Id}] (exit={process.ExitCode}): {errorMsg.Trim()[..Math.Min(200, errorMsg.Trim().Length)]}", "Hooks");
                return $"退出码 {process.ExitCode}: {errorMsg.Trim()[..Math.Min(100, errorMsg.Trim().Length)]}";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"钩子异常 [{hook.Id}]: {ex.Message}", "Hooks");
            return ex.Message;
        }
    }

    /// <summary>替换命令中的占位符</summary>
    private static string ReplacePlaceholders(string command, Dictionary<string, string>? placeholders)
    {
        if (placeholders == null) return command;

        var result = command;
        foreach (var kv in placeholders)
            result = result.Replace($"{{{kv.Key}}}", kv.Value);
        return result;
    }

    /// <summary>重新加载配置文件</summary>
    public void Reload()
    {
        _hooks.Clear();
        Load();
    }
}
