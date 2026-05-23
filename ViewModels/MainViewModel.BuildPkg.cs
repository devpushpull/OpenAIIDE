using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using AIIDEWPF.Models;
using AIIDEWPF.Services;
using AIIDEWPF.Views;

namespace AIIDEWPF.ViewModels;

public partial class MainViewModel
{
    private async void RunBuild()
    {
        if (!RequireAuth("编译项目")) return;
        if (string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            Chat.AddMessage("system", "请先打开一个项目。");
            return;
        }
        BuildLanguage = _aiService.BuildSvc.DetectLanguage();
        Chat.AddMessage("system", $"🔨 正在编译 ({BuildLanguage})...");
        Chat.AIStatus = "AI: 编译中...";
        var result = await _aiService.BuildSvc.BuildAsync();
        Chat.AIStatus = "AI: 就绪";
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result);
        var ok = parsed.TryGetProperty("success", out var s) && s.GetBoolean();
        if (ok)
            Chat.AddMessage("system", "✅ 编译成功");
        else
        {
            var err = parsed.TryGetProperty("error", out var e) ? e.GetString() : "未知错误";
            Chat.AddMessage("system", $"❌ 编译失败: {err}");
        }
        if (parsed.TryGetProperty("output", out var output) && !string.IsNullOrEmpty(output.GetString()))
            Terminal.AppendOutput(output.GetString()!);
        if (parsed.TryGetProperty("stderr", out var stderr) && !string.IsNullOrEmpty(stderr.GetString()))
            Terminal.AppendOutput(stderr.GetString()!);
    }

    private void OpenPluginManager()
    {
        if (!RequireAuth("管理插件")) return;
        _pluginService ??= new PluginService(FileTree.ProjectPath);
        var window = new Views.PluginManager(_pluginService, MarkPluginNeedsRestart)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
        RefreshInstalledPlugins();
    }

    private void OpenMCPManager()
    {
        if (!RequireAuth("管理 MCP")) return;
        _mcpService ??= new MCPService(FileTree.ProjectPath);
        var window = new Views.MCPManager(_mcpService)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private async void RunPackage()
    {
        if (!RequireAuth("打包项目")) return;
        if (string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            Chat.AddMessage("system", "请先打开一个项目。");
            return;
        }
        BuildLanguage = _aiService.BuildSvc.DetectLanguage();
        Chat.AddMessage("system", $"📦 正在打包 ({BuildLanguage})...");
        Chat.AIStatus = "AI: 打包中...";
        var result = await _aiService.BuildSvc.PackageAsync();
        Chat.AIStatus = "AI: 就绪";
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result);
        var ok = parsed.TryGetProperty("success", out var s) && s.GetBoolean();
        if (ok)
            Chat.AddMessage("system", "✅ 打包成功");
        else
        {
            var err = parsed.TryGetProperty("error", out var e) ? e.GetString() : "未知错误";
            Chat.AddMessage("system", $"❌ 打包失败: {err}");
        }
        if (parsed.TryGetProperty("output", out var output) && !string.IsNullOrEmpty(output.GetString()))
            Terminal.AppendOutput(output.GetString()!);
        if (parsed.TryGetProperty("stderr", out var stderr) && !string.IsNullOrEmpty(stderr.GetString()))
            Terminal.AppendOutput(stderr.GetString()!);
    }
}
