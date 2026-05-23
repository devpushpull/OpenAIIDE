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

namespace AIIDEWPF.ViewModels;

public partial class MainViewModel
{
    public void ToggleTerminal()
    {
        if (ActiveBottomTab == "terminal")
            ActiveBottomTab = "";
        else
        {
            ActiveBottomTab = "terminal";
            if (!_terminalService.IsRunning)
                CreateTerminal();
        }
    }

    public void ToggleLog()
    {
        if (ActiveBottomTab == "log")
            ActiveBottomTab = "";
        else
            ActiveBottomTab = "log";
    }

    /// <summary>打开 Web 预览面板，自动检测 dev server URL</summary>
    public void OpenPreview()
    {
        if (ActiveBottomTab == "preview")
        {
            ActiveBottomTab = "";
            return;
        }

        ActiveBottomTab = "preview";

        if (string.IsNullOrEmpty(PreviewUrl))
            AutoDetectDevServer();
    }

    private void AutoDetectDevServer()
    {
        var commonPorts = new[] { 5173, 3000, 4200, 8080, 5000, 8000, 9000 };
        foreach (var port in commonPorts)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                var url = $"http://localhost:{port}";
                var response = client.GetAsync(url).Result;
                if (response.IsSuccessStatusCode)
                {
                    PreviewUrl = url;
                    return;
                }
            }
            catch { }
        }
    }

    public void CreateTerminal()
    {
        var cwd = string.IsNullOrEmpty(FileTree.ProjectPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : FileTree.ProjectPath;
        _terminalService.CreateTerminal(cwd);
    }

    public void RunSearch()
    {
        var query = SearchInput?.Trim();
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(FileTree.ProjectPath)) return;
        IsSearching = true;
        var results = _searchService.Grep(FileTree.ProjectPath, query);
        Application.Current.Dispatcher.Invoke(() =>
        {
            _searchResults.Clear();
            foreach (var r in results)
            {
                _searchResults.Add(new GrepMatchDisplay
                {
                    File = Path.GetRelativePath(FileTree.ProjectPath, r.File),
                    FullPath = r.File,
                    Line = r.Line,
                    Content = r.Content
                });
            }
            IsSearching = false;
        });
    }

    private void OnTerminalData(string id, string data)
    {
        Application.Current.Dispatcher.Invoke(() => Terminal.AppendOutput(data));
    }

    private void OnTerminalExit(string id, int code)
    {
        Application.Current.Dispatcher.Invoke(() =>
            Terminal.AppendOutput($"\r\n[进程已退出，代码: {code}]\r\n"));
    }

    public void TerminalInput_KeyDown(string text)
    {
        _terminalService.WriteInput(text + "\r\n");
    }

    private async Task<(bool accepted, bool rememberChoice, bool alwaysAllow)> OnTerminalCommandConfirmAsync(string command)
    {
        var tcs = new TaskCompletionSource<(bool accepted, bool rememberChoice, bool alwaysAllow)>();

        var item = new TerminalOutputItem
        {
            Command = command,
            IsPending = true,
            Timestamp = DateTime.Now,
            Confirmation = tcs
        };

        Application.Current.Dispatcher.Invoke(() =>
        {
            Chat.TerminalOutputs.Insert(0, item);
            Chat.IsTerminalExpanded = true;
        });

        var result = await tcs.Task;
        return result;
    }

    private void OnAITerminalOutput(string command, string output, int exitCode)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var item = new TerminalOutputItem
            {
                Command = command,
                Output = output,
                ExitCode = exitCode,
                Timestamp = DateTime.Now
            };
            Chat.TerminalOutputs.Insert(0, item);
            Chat.IsTerminalExpanded = true;
            Terminal.AppendOutput($"\r\n> {command}\r\n{output}\r\n");
        });
    }
}
