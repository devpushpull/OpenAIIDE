using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AIIDEWPF.Models;
using AIIDEWPF.Services;
using AIIDEWPF.Views;

namespace AIIDEWPF.ViewModels;

public partial class MainViewModel
{
    private async void StartDebug()
    {
        if (!RequireAuth("调试代码")) return;
        if (string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            Chat.AddMessage("system", "请先打开一个项目才能开始调试。");
            return;
        }
        _debugService.SetProjectPath(FileTree.ProjectPath);
        IsDebugging = true;
        DebugStatus = "🔍 启动调试...";
        await _debugService.StartDebugAsync();
        DebugStatus = "▶ 调试运行中";
    }

    private void StopDebug()
    {
        _debugService.StopDebug();
        IsDebugging = false;
        DebugStatus = "调试";
    }

    private void ToggleBreakpoint(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(Editor.CurrentFile))
            return;
        var line = CodeEditor?.TextArea?.Caret?.Line ?? 1;
        _debugService.ToggleBreakpoint(Editor.CurrentFile, line);
        Chat.AddMessage("system", _debugService.HasBreakpoint(Editor.CurrentFile, line)
            ? $"🔴 断点已设置: {Path.GetFileName(Editor.CurrentFile)}:{line}"
            : $"⚪ 断点已移除: {Path.GetFileName(Editor.CurrentFile)}:{line}");
    }

    private void StepOver()
    {
        if (!IsDebugging) return;
        DebugStatus = "⏭ 单步跳过 (F10)...";
        _debugService.StepOver();
    }

    private void StepInto()
    {
        if (!IsDebugging) return;
        DebugStatus = "⬇ 单步进入 (F11)...";
        _debugService.StepInto();
    }

    private void StepOut()
    {
        if (!IsDebugging) return;
        DebugStatus = "⬆ 单步跳出 (Shift+F11)...";
        _debugService.StepOut();
    }

    private void ContinueDebug()
    {
        if (!IsDebugging) return;
        DebugStatus = "▶ 继续执行 (F5)...";
        _debugService.Continue();
    }

    private void RunToCursor()
    {
        if (!IsDebugging) return;
        if (string.IsNullOrEmpty(Editor.CurrentFile)) return;
        var line = CodeEditor?.TextArea?.Caret?.Line ?? 1;
        DebugStatus = $"🏃 运行到光标: 第{line}行...";
        _debugService.RunToCursor(Editor.CurrentFile, line);
    }

    private async void GoToDefinition()
    {
        await ExecuteLSPNavigationAsync(async (lsp, file, line, col) =>
        {
            var location = await lsp.GoToDefinitionAsync(file, line, col);
            if (location != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OpenFile(location.Value.FilePath);
                    Editor.NavigateToLine = location.Value.Line;
                });
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                    Chat.AddMessage("system", "未找到定义位置"));
            }
        });
    }

    private async void FindReferences()
    {
        await ExecuteLSPNavigationAsync(async (lsp, file, line, col) =>
        {
            var refs = await lsp.FindReferencesAsync(file, line, col);
            Application.Current.Dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var r in refs)
                {
                    SearchResults.Add(new GrepMatchDisplay
                    {
                        File = Path.GetRelativePath(FileTree.ProjectPath, r.FilePath),
                        FullPath = r.FilePath,
                        Line = r.Line,
                        Content = $"引用 (列 {r.Column})"
                    });
                }
                ActiveSidebar = "search";
                IsLeftSidebarVisible = true;
                Chat.AddMessage("system", $"找到 {refs.Count} 个引用");
            });
        });
    }

    private async Task ExecuteLSPNavigationAsync(Func<LSPService, string, int, int, Task> action)
    {
        if (CodeEditor == null) return;
        var textArea = CodeEditor.TextArea;
        if (textArea == null) return;

        var caret = textArea.Caret;
        if (string.IsNullOrEmpty(Editor.CurrentFile)) return;

        var line = caret.Line;
        var column = caret.Column;

        var lspService = FindLSPService();
        if (lspService == null)
        {
            Chat.AddMessage("system", "LSP 未初始化，请先打开项目。");
            return;
        }

        await action(lspService, Editor.CurrentFile, line, column);
    }

    private LSPService? FindLSPService()
    {
        if (Application.Current?.MainWindow is MainFrameworkWindow mw)
        {
            var wv = FindWorkspaceView(mw.Content as DependencyObject);
            if (wv != null)
            {
                var field = typeof(Views.WorkspaceView).GetField("_lspService",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return field?.GetValue(wv) as LSPService;
            }
        }
        return null;
    }

    private static Views.WorkspaceView? FindWorkspaceView(DependencyObject? parent)
    {
        if (parent == null) return null;
        if (parent is Views.WorkspaceView wv) return wv;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var result = FindWorkspaceView(VisualTreeHelper.GetChild(parent, i));
            if (result != null) return result;
        }
        return null;
    }
}
