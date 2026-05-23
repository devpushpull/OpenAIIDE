using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AIIDEWPF.Models;
using AIIDEWPF.Services;
using AIIDEWPF.Views;
using Microsoft.Win32;

namespace AIIDEWPF.ViewModels;

public partial class MainViewModel
{
    public void ShowSettings()
    {
        if (!RequireAuth("打开设置")) return;
        var settingsVM = new SettingsViewModel(_config, MemorySvc ?? new MemoryService(_db!), _modelManager, _slashCommandService, _networkService, _mcpService ??= new MCPService(FileTree.ProjectPath), FileTree.ProjectPath, LearningSvc);
        var window = new Views.SettingsWindow(settingsVM)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    private void ShowLogin()
    {
        if (_auth == null) return;
        if (_auth.IsLoggedIn) _auth.Logout();

        var loginWindow = new Views.LoginWindow(_auth)
        {
            Owner = Application.Current.MainWindow
        };
        var result = loginWindow.ShowDialog();
        if (result == true)
        {
            OnPropertyChanged(nameof(IsLoggedIn));
            OnPropertyChanged(nameof(CurrentUsername));
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(NotLoggedInVisibility));
        }
    }

    private bool RequireAuth(string actionDescription = "使用此功能")
    {
        if (IsLoggedIn) return true;

        var result = MessageBox.Show(
            $"您尚未登录，请先登录后再{actionDescription}。\n\n如果没有账号，请联系超级管理员（开发者）开通。",
            "需要登录",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.OK)
            ShowLogin();

        return false;
    }

    private void Stop()
    {
        _aiService.Cancel();
        Chat.IsStreaming = false;
        Chat.AIStatus = "AI: 已停止";
        _scheduler.SetStreaming(false);

        _questWindow?.OnQuestError("用户手动停止");
    }

    private async Task<(bool accepted, bool rememberChoice)> OnWebSearchConsentAsync(string query, string toolName)
    {
        var tcs = new TaskCompletionSource<(bool accepted, bool rememberChoice)>();

        var label = toolName == "search_web" ? "搜索" : "获取网页";
        var item = new WebSearchConsentItem
        {
            Query = query,
            ToolName = toolName,
            Label = $"🌐 联网{label}",
            Detail = $"AI 想要「{(query.Length > 60 ? query[..60] + "..." : query)}」",
            Timestamp = DateTime.Now,
            Confirmation = tcs
        };

        Application.Current.Dispatcher.Invoke(() =>
        {
            Chat.WebSearchConsents.Insert(0, item);
            Chat.AddMessage("system", $"🌐 AI 请求联网{label}: {item.Detail}");
        });

        var result = await tcs.Task;
        return result;
    }

    private void AttachFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要附加的文件",
            Filter = _attachmentService.GetFilter(),
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
        {
            _attachmentService.AttachFiles(dlg.FileNames);
            SyncAttachmentsToChat();
        }
    }

    private void RemoveAttachment(Models.FileAttachment attachment)
    {
        _attachmentService.RemoveAttachment(attachment);
        SyncAttachmentsToChat();
    }

    private void SyncAttachmentsToChat()
    {
        Chat.Attachments.Clear();
        foreach (var a in _attachmentService.Attachments)
            Chat.Attachments.Add(a);
    }

    public void SaveSettings()
    {
        var cfg = _config.GetConfig();
        cfg.Providers = _modelManager.GetProviders().ToDictionary(p => p.Id);
        cfg.AI.Provider = _modelManager.ActiveProvider?.Id ?? "deepseek";
        cfg.AI.Model = _modelManager.ActiveModel?.Id ?? "deepseek-v4-pro";
        _config.Save();
        Chat.SelectedModel = _modelManager.ActiveModel?.Id ?? "deepseek-v4-pro";
        IsSettingsOpen = false;
    }

    public void SaveProjectState()
    {
        if (_projectConfig.IsLoaded)
        {
            _projectConfig.Data.LastModifiedAt = DateTime.Now;
            _projectConfig.Save();
            LogService.Instance.Info($"项目配置已保存: {_projectConfig.ProjectPath}");
        }
    }

    public async Task TryRestoreLastProjectAsync()
    {
        var recent = _config.GetConfig().RecentProjects;
        var lastProject = recent.FirstOrDefault();
        if (!string.IsNullOrEmpty(lastProject) && Directory.Exists(lastProject))
        {
            _projectConfig.LoadOrCreate(lastProject);
            _aiService.SetProjectPath(lastProject);
            _atMentionService.SetProjectPath(lastProject);
            _gitService.SetRepoPath(lastProject);
            _slashCommandService.SetProjectPath(lastProject);
            _codeClipDetector?.SetProjectPath(lastProject);
            _gitConfig = GitConfigStore.Load(lastProject);
            await Task.Run(() =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshFileTree(lastProject);
                    _maintenanceService = new SelfMaintenanceService(lastProject);
                    LogService.Instance.Info($"恢复上次项目: {lastProject}");
                    StartFileWatcher(lastProject);
                });
            });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _inputCorrectionHint = string.Empty;
    public string InputCorrectionHint { get => _inputCorrectionHint; set { _inputCorrectionHint = value; OnPropertyChanged(); } }

    private void HandleInputTextChanged(string text)
    {
        var cmd = _atMentionService.ParseCommand(text, text.Length);
        if (cmd != null)
        {
            IsAtMentionPopupOpen = true;
            IsSlashCommandPopupOpen = false;
            AtMentionQuery = cmd.Query;
            var results = _atMentionService.Search(cmd.Query);
            AtMentionResults.Clear();
            foreach (var r in results) AtMentionResults.Add(r);
        }
        else if (text.TrimStart().StartsWith('/'))
        {
            var query = text.TrimStart()[1..];
            if (string.IsNullOrEmpty(query) || query.Length <= 20)
            {
                IsSlashCommandPopupOpen = true;
                IsAtMentionPopupOpen = false;
                SlashCommandQuery = query;
                var results = _slashCommandService.Search(query);
                SlashCommandResults.Clear();
                foreach (var r in results) SlashCommandResults.Add(r);
            }
            else
            {
                IsSlashCommandPopupOpen = false;
                IsAtMentionPopupOpen = false;
            }
        }
        else
        {
            IsAtMentionPopupOpen = false;
            IsSlashCommandPopupOpen = false;
        }

        if (text.Length > 50 && CodeClipDetector.LooksLikeCode(text) && _codeClipDetector != null)
        {
            var matches = _codeClipDetector.Detect(text);
            if (matches.Count > 0)
            {
                CodeRefChips.Clear();
                foreach (var m in matches) CodeRefChips.Add(m);
            }
        }

        if (text.Length > 5 && !text.TrimStart().StartsWith('/'))
        {
            _inputCorrection.Correct(text, out var wasCorrected);
            if (wasCorrected)
                InputCorrectionHint = $"✏️ 检测到输入可能有误 ({_inputCorrection.LastCorrectionDetail})，发送时将自动纠正";
            else
                InputCorrectionHint = string.Empty;
        }
        else if (text.Length <= 3)
        {
            InputCorrectionHint = string.Empty;
        }
    }

    private void SelectAtMention(AtMentionItem item)
    {
        var resolved = _atMentionService.ResolveCommand(Chat.InputText, item);
        Chat.InputText = resolved;
        IsAtMentionPopupOpen = false;
    }

    private void SelectSlashCommandItem(SlashCommandItem item)
    {
        Chat.InputText = item.Command;
        IsSlashCommandPopupOpen = false;
    }

    private void OpenGitSettings()
    {
        if (string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            MessageBox.Show("请先打开一个项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!GitService.IsGitAvailable())
        {
            MessageBox.Show("未检测到 Git，请先安装 Git。\nhttps://git-scm.com/downloads", "Git 不可用",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Views.GitSettingsDialog(_gitService, FileTree.ProjectPath)
        {
            Owner = Application.Current.MainWindow
        };
        if (_gitConfig != null)
            dlg.PreFill(_gitConfig);

        if (dlg.ShowDialog() == true)
        {
            _gitConfig = dlg.Config;
            GitConfigStore.Save(FileTree.ProjectPath, _gitConfig);
            Chat.AddMessage("system", $"✅ Git 配置已保存：{_gitConfig.RemoteUrl}");
        }
    }

    private async void GitPushAsync()
    {
        if (!RequireAuth("推送代码")) return;
        if (string.IsNullOrEmpty(FileTree.ProjectPath))
        {
            Chat.AddMessage("system", "请先打开一个项目。");
            return;
        }

        if (!GitService.IsGitAvailable())
        {
            Chat.AddMessage("system", "❌ 未检测到 Git，请先安装 Git。");
            return;
        }

        if (_gitConfig == null || string.IsNullOrEmpty(_gitConfig.RemoteUrl))
        {
            OpenGitSettings();
            if (_gitConfig == null || string.IsNullOrEmpty(_gitConfig.RemoteUrl))
                return;
        }

        Chat.AddMessage("system", $"🚀 正在提交并推送至 {_gitConfig.RemoteName}/{_gitConfig.Branch}...");

        var result = await Task.Run(() => _gitService.CommitAndPush(_gitConfig));

        if (result.Success)
        {
            Chat.AddMessage("system", $"✅ Git 推送成功！\n{result.Output}");
        }
        else
        {
            Chat.AddMessage("system", $"❌ Git 推送失败：{result.Error}");
        }
    }

    public void DetectPastedCode(string text)
    {
        if (_codeClipDetector == null || string.IsNullOrEmpty(text) || text.Length < 50) return;
        if (!CodeClipDetector.LooksLikeCode(text)) return;

        var matches = _codeClipDetector.Detect(text);
        if (matches.Count > 0)
        {
            CodeRefChips.Clear();
            foreach (var m in matches) CodeRefChips.Add(m);
        }
    }

    private void NavigateToCode(CodeMatchResult result)
    {
        if (!File.Exists(result.FilePath)) return;
        OpenFile(result.FilePath);
        Editor.NavigateToLine = result.Line;
    }

    private void DismissCodeChip(CodeMatchResult result)
    {
        CodeRefChips.Remove(result);
    }

    public void OnAppReactivated()
    {
        IsAppActive = true;
        if (Chat.IsStreaming)
            Chat.AIStatus = "AI: 后台运行中...";
        _ = _networkService.CheckConnectivityAsync();
        if (!string.IsNullOrEmpty(FileTree.ProjectPath))
            LogService.Instance.Debug("应用恢复激活，刷新状态");
    }

    public void OnAppDeactivated()
    {
        IsAppActive = false;
        LogService.Instance.Debug("应用进入后台，后台任务继续执行");
    }

    private void NewSession()
    {
        var session = _sessionManager.CreateSession($"对话 {DateTime.Now:HH:mm}");
        _sessionManager.SwitchTo(session.Id);
        _aiService.CurrentSessionId = session.Id;
        Chat.AddMessage("system", $"📌 已创建新会话: {session.Name}");
        OnPropertyChanged(nameof(ActiveSessionName));
    }

    private void SwitchSession(string sessionId)
    {
        var session = _sessionManager.SwitchTo(sessionId);
        if (session != null)
        {
            _aiService.CurrentSessionId = session.Id;
            Chat.AddMessage("system", $"📌 已切换到: {session.Name}");
            OnPropertyChanged(nameof(ActiveSessionName));
        }
    }

    private void CloseSession(string sessionId)
    {
        if (_sessionManager.Sessions.Count <= 1)
        {
            Chat.AddMessage("system", "⚠️ 至少保留一个会话");
            return;
        }
        _sessionManager.CloseSession(sessionId);
        if (_sessionManager.ActiveSession != null)
            _aiService.CurrentSessionId = _sessionManager.ActiveSession.Id;
        OnPropertyChanged(nameof(ActiveSessionName));
    }

    private async Task CompressChatAsync()
    {
        if (Chat.IsCompressing || Chat.IsStreaming) return;

        try
        {
            Chat.IsCompressing = true;
            Chat.AIStatus = "AI: 压缩中...";
            Chat.AddMessage("system", "🔄 正在使用 LLM 智能压缩对话历史...");

            var llmSummary = await _aiService.CompressHistoryWithLLMAsync();

            if (!string.IsNullOrEmpty(llmSummary))
            {
                Chat.AddMessage("system", $"📦 对话已智能压缩完成\n\n<details><summary>点击查看摘要</summary>\n\n{llmSummary}\n\n</details>");
            }
            else
            {
                Chat.AddMessage("system", "📦 对话已压缩（使用轻量模式）");
            }

            var activeTodos = Chat.Todos
                .Where(t => t.Status != "cancelled")
                .Select(t => (t.Content ?? "", t.Status ?? "pending"))
                .ToList();
            if (activeTodos.Count > 0)
            {
                _aiService.InjectTodoContext(activeTodos);
                Chat.AddMessage("system", $"📋 已保留 {activeTodos.Count} 条任务状态，AI 可继续执行");
            }

            Chat.AIStatus = "AI: 就绪";
            UpdateContextUsage();
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"对话压缩失败: {ex.Message}", "UI");
            Chat.AIStatus = "AI: 就绪";
            Chat.AddMessage("system", $"⚠️ 对话压缩失败: {ex.Message}");
        }
        finally
        {
            Chat.IsCompressing = false;
        }
    }

    private void RequestNewChat()
    {
        if (Chat.Messages.Count > 2)
            Chat.AddMessage("system", "📌 已创建新对话会话，上下文已刷新");
        NewSession();
        UpdateContextUsage();
    }

    public async Task CheckAndOfferRecoveryAsync()
    {
        try
        {
            var checkpoints = _backup?.GetPendingCheckpoints();
            if (checkpoints == null || checkpoints.Count == 0) return;

            _pendingCheckpoints = checkpoints;
            _isCrashRecoveryMode = true;
            var totalFiles = checkpoints.Sum(c => c.Files.Count);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Chat.AddMessage("system",
                    $"⚠️ 检测到上次会话异常退出，发现 {checkpoints.Count} 个未完成的变更批次（共 {totalFiles} 个文件）。\n" +
                    "请选择：「恢复变更」将文件恢复到修改前状态，或「放弃」清除残留数据。");

                Chat.PlanSuggestionText = $"检测到 {checkpoints.Count} 个异常退出的变更批次，共 {totalFiles} 个文件可能处于不一致状态。";
                Chat.PlanSuggestionAcceptText = "📋 恢复变更";
                Chat.PlanSuggestionSkipText = "🗑 放弃";
                Chat.IsPlanSuggestionVisible = true;
            });

            LogService.Instance.Warn($"发现 {checkpoints.Count} 个残留 checkpoint", "Recovery");
        }
        catch (Exception ex)
        {
            LogService.Instance.Debug($"崩溃恢复检查失败: {ex.Message}");
        }
    }

    private void RestoreFromCrash()
    {
        if (_pendingCheckpoints == null || _pendingCheckpoints.Count == 0) return;

        try
        {
            int restored = 0, failed = 0;
            foreach (var cp in _pendingCheckpoints)
            {
                if (_backup?.RestoreCheckpoint(cp.Timestamp) == true)
                    restored++;
                else
                    failed++;
            }

            Chat.AddMessage("system",
                restored > 0
                    ? $"✅ 已从 {restored} 个 checkpoint 恢复文件，代码已回滚到修改前状态。"
                    : "⚠️ 恢复失败，请手动检查文件状态。");

            if (failed > 0)
                Chat.AddMessage("system", $"⚠️ {failed} 个 checkpoint 恢复失败，建议手动检查对应文件。");

            _pendingCheckpoints = null;
            Chat.IsPlanSuggestionVisible = false;
            Chat.PlanSuggestionAcceptText = "📋 生成计划";
            Chat.PlanSuggestionSkipText = "⏭ 跳过";
            LogService.Instance.Info($"崩溃恢复完成: 恢复{restored}个, 失败{failed}个", "Recovery");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "崩溃恢复");
            Chat.AddMessage("system", $"❌ 恢复过程出错: {ex.Message}");
        }
    }

    private void DiscardCrashRecovery()
    {
        if (_pendingCheckpoints == null) return;

        foreach (var cp in _pendingCheckpoints)
            _backup?.DiscardCheckpoint(cp.Timestamp);

        Chat.AddMessage("system",
            _pendingCheckpoints.Count > 0
                ? $"🗑 已放弃 {_pendingCheckpoints.Count} 个异常变更批次，残留数据已清除。"
                : "🗑 已清除残留数据。");

        _pendingCheckpoints = null;
        Chat.IsPlanSuggestionVisible = false;
        Chat.PlanSuggestionAcceptText = "📋 生成计划";
        Chat.PlanSuggestionSkipText = "⏭ 跳过";
        LogService.Instance.Info($"崩溃恢复已放弃", "Recovery");
    }

    private void CompareWithBackup(string filePath)
    {
        try
        {
            if (_fileCompare == null || !File.Exists(filePath))
            {
                Chat.AddMessage("system", "无法对比：文件比较服务未就绪或文件不存在。");
                return;
            }

            var scanResult = _fileCompare.ScanAndCompare(filePath);

            if (scanResult.SnapshotCount == 0)
            {
                Chat.AddMessage("system", $"📋 文件 [{Path.GetFileName(filePath)}] 暂无自动备份可对比。");
                return;
            }

            if (scanResult.LatestDiff == null || !scanResult.LatestDiff.HasChanges)
            {
                Chat.AddMessage("system", $"✅ 文件 [{Path.GetFileName(filePath)}] 与最新备份一致，无差异。\n（共 {scanResult.SnapshotCount} 个备份可用）");
                return;
            }

            var diffView = new AIDiffView();
            diffView.ShowDiff(scanResult.LatestDiff.Diff);

            var diffWindow = new Window
            {
                Title = $"对比: {Path.GetFileName(filePath)} vs {scanResult.LatestDiff.SourceLabel}",
                Content = diffView,
                Width = 1000,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                Background = System.Windows.Media.Brushes.Transparent
            };
            diffWindow.Show();

            LogService.Instance.Info($"备份对比: {filePath} ({scanResult.LatestDiff.Summary})", "Compare");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "备份对比");
            Chat.AddMessage("system", $"对比失败: {ex.Message}");
        }
    }

    private void RestoreFromBackup(string filePath)
    {
        try
        {
            if (_backup == null || _fileCompare == null || !File.Exists(filePath))
            {
                Chat.AddMessage("system", "无法恢复：备份服务未就绪或文件不存在。");
                return;
            }

            var snapshots = _fileCompare.GetAllSnapshots(filePath);
            if (snapshots.Count == 0)
            {
                Chat.AddMessage("system", $"📋 文件 [{Path.GetFileName(filePath)}] 没有可用备份，无法恢复。");
                return;
            }

            var latest = snapshots[0];
            var result = MessageBox.Show(
                $"确认将 [{Path.GetFileName(filePath)}] 恢复到:\n{latest.Label}\n\n当前内容将被自动备份。",
                "确认恢复",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK) return;

            var success = _backup.Restore(latest.SnapshotPath, filePath);
            if (success)
            {
                Chat.AddMessage("system", $"✅ 文件已从备份恢复: {Path.GetFileName(filePath)}");
                RefreshFileTree(FileTree.ProjectPath);
            }
            else
            {
                Chat.AddMessage("system", $"❌ 从备份恢复失败: {Path.GetFileName(filePath)}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex, "从备份恢复");
            Chat.AddMessage("system", $"恢复失败: {ex.Message}");
        }
    }
}
