using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIIDEWPF.Models;

namespace AIIDEWPF.Services;

public partial class AIService
{
    private string EnsurePathInProject(string filePath) => _fileOps.EnsurePathInProject(filePath);

    private Task<string> ReadFileTool(JsonNode args)
    {
        var fp = args["file_path"]!.GetValue<string>();
        int? sl = args["start_line"]?.GetValue<int>();
        int? el = args["end_line"]?.GetValue<int>();
        return Task.FromResult(_fileOps.ReadFile(fp, sl, el));
    }

    private Task<string> SearchReplaceTool(JsonNode args)
    {
        var fp = args["file_path"]!.GetValue<string>();
        _backup?.BackupBeforeWrite(fp);
        return Task.FromResult(_fileOps.SearchReplace(
            fp,
            args["original_text"]!.GetValue<string>(),
            args["new_text"]!.GetValue<string>(),
            args["replace_all"]?.GetValue<bool>() ?? false));
    }

    private Task<string> CreateFileTool(JsonNode args)
    {
        var fp = args["file_path"]!.GetValue<string>();
        _backup?.BackupBeforeWrite(fp);
        return Task.FromResult(_fileOps.CreateFile(
            fp,
            args["file_content"]!.GetValue<string>()));
    }

    private Task<string> DeleteFileTool(JsonNode args)
    {
        var fp = args["file_path"]!.GetValue<string>();
        _backup?.BackupBeforeWrite(fp);
        return Task.FromResult(_fileOps.DeleteFile(fp));
    }

    private Task<string> MoveFileTool(JsonNode args)
    {
        var src = args["source_path"]!.GetValue<string>();
        var dest = args["dest_path"]!.GetValue<string>();
        _backup?.BackupBeforeWrite(dest);
        return Task.FromResult(_fileOps.Move(src, dest));
    }

    private Task<string> CopyFileTool(JsonNode args)
    {
        var src = args["source_path"]!.GetValue<string>();
        var dest = args["dest_path"]!.GetValue<string>();
        _backup?.BackupBeforeWrite(dest);
        return Task.FromResult(_fileOps.Copy(src, dest));
    }

    private Task<string> ReadMultipleFilesTool(JsonNode args)
    {
        var paths = args["file_paths"]!.AsArray()!.Select(p => p!.GetValue<string>()).ToArray();
        var maxLines = args["max_lines_per_file"]?.GetValue<int?>();
        return Task.FromResult(_fileOps.ReadMultipleFiles(paths, maxLines));
    }

    private async Task<string> RunInTerminalTool(JsonNode args)
    {
        var cmd = args["command"]!.GetValue<string>();
        var cwd = EnsurePathInProject(args["cwd"]?.GetValue<string>() ?? _projectPath);
        int timeout = args["timeout"]?.GetValue<int>() ?? 30000;

        try
        {
            var (shell, _) = TerminalService.DetectShell();
            var isPs = shell.Contains("powershell", StringComparison.OrdinalIgnoreCase);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = shell,
                Arguments = isPs ? $"-NoProfile -Command {cmd}" : $"/c {cmd}",
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return "{\"success\":false,\"error\":\"Failed to start process\"}";
            await p.WaitForExitAsync(new CancellationTokenSource(timeout).Token);
            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            var output = stdout;
            if (!string.IsNullOrEmpty(stderr)) output += "\n[stderr]\n" + stderr;
            // 通知 UI 展示终端输出
            OnTerminalOutput?.Invoke(cmd, Truncate(output, 10000), p.ExitCode);
            return JsonSerializer.Serialize(new
            {
                success = p.ExitCode == 0,
                output = Truncate(stdout, 50000),
                stderr = Truncate(stderr, 10000),
                exitCode = p.ExitCode
            });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
        }
    }

    private Task<string> ListDirTool(JsonNode args)
        => Task.FromResult(_fileOps.ListDirectory(args["path"]?.GetValue<string>() ?? _projectPath));

    private Task<string> SearchFileTool(JsonNode args)
        => Task.FromResult(_fileOps.SearchFiles(
            args["path"]?.GetValue<string>() ?? _projectPath,
            args["query"]!.GetValue<string>()));

    /// <summary>模糊文件查找工具：按文件名模糊搜索项目中的文件</summary>
    private Task<string> FindFileTool(JsonNode args)
    {
        var filename = args["filename"]!.GetValue<string>();
        var searchRoot = args["search_root"]?.GetValue<string>() ?? _projectPath;
        var matches = _fileOps.SmartFindFile(filename);
        // 如果有指定搜索根目录且与项目根不同，则过滤结果
        if (!string.IsNullOrEmpty(searchRoot) && 
            !string.Equals(searchRoot, _projectPath, StringComparison.OrdinalIgnoreCase))
        {
            var resolvedRoot = _fileOps.ResolvePath(searchRoot);
            matches = matches.Where(m => m.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        
        if (matches.Count == 0)
            return Task.FromResult($"{{\"success\":false,\"error\":\"未找到匹配 '{filename}' 的文件。请尝试 search_file 按 glob 搜索，或 scan_project 了解项目结构。\"}}");
        
        var resultList = matches.Take(10).Select((m, i) => new
        {
            rank = i + 1,
            path = Path.GetRelativePath(_projectPath, m).Replace('\\', '/'),
            absolute_path = m,
            name = Path.GetFileName(m),
            size = new FileInfo(m).Length
        });
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, count = matches.Count, files = resultList }));
    }

    private Task<string> GrepCodeTool(JsonNode args)
    {
        var regex = args["regex"]!.GetValue<string>();
        var root = EnsurePathInProject(args["path"]?.GetValue<string>() ?? _projectPath);
        var gf = args["glob"]?.GetValue<string>();
        var matches = _searchService.Grep(root, regex, gf);
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, matches }));
    }

    private Task<string> SearchCodebaseTool(JsonNode args)
    {
        var q = args["query"]!.GetValue<string>();
        var kw = args["key_words"]?.GetValue<string>() ?? q;
        var keywords = kw.Split(',').Select(k => k.Trim().ToLower()).Where(k => k.Length > 0).ToList();
        var results = _searchService.SemanticSearch(_projectPath, keywords);
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, results }));
    }

    private Task<string> SearchSymbolTool(JsonNode args)
    {
        var sym = args["symbol"]!.GetValue<string>();
        var matches = _searchService.SearchSymbol(_projectPath, sym);
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, matches }));
    }

    private async Task<string> SearchWebTool(JsonNode args)
    {
        var query = args["query"]!.GetValue<string>();
        return await _webSearch.SearchAsync(query);
    }

    private async Task<string> FetchContentTool(JsonNode args)
    {
        var url = args["url"]!.GetValue<string>();
        try
        {
            var response = await _http.GetAsync(url);
            var html = await response.Content.ReadAsStringAsync();
            return $"{{\"success\":true,\"content\":{JsonSerializer.Serialize(Truncate(html, 10000))}}}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
        }
    }

    private Task<string> ScanProjectTool(JsonNode args)
        => Task.FromResult(_fileOps.ScanProject(
            args["path"]?.GetValue<string>() ?? _projectPath,
            args["depth"]?.GetValue<int>() ?? 3));

    private Task<string> RenameFileTool(JsonNode args)
    {
        var fp = args["file_path"]!.GetValue<string>();
        _backup?.BackupBeforeWrite(fp);
        return Task.FromResult(_fileOps.Rename(fp, args["new_name"]!.GetValue<string>()));
    }

    private Task<string> CreateDirTool(JsonNode args)
        => Task.FromResult(_fileOps.CreateDirectory(args["path"]!.GetValue<string>()));

    private Task<string> DeleteDirTool(JsonNode args)
    {
        var dp = args["path"]!.GetValue<string>();
        var resolvedPath = _fileOps.ResolvePath(dp);
        // 删除目录前：递归收集目录内所有文件创建 checkpoint
        if (Directory.Exists(resolvedPath))
        {
            try
            {
                var allFiles = Directory.GetFiles(resolvedPath, "*", SearchOption.AllDirectories);
                if (allFiles.Length > 0)
                    _backup?.CreateCheckpoint(allFiles, _currentSessionId, 1);
            }
            catch (Exception ex) { LogService.Instance.Debug($"目录删除前 Checkpoint 创建失败: {ex.Message}", "AI"); }
        }
        return Task.FromResult(_fileOps.DeleteDirectory(dp));
    }

    private Task<string> TodoWriteTool(JsonNode args)
    {
        var merge = args["merge"]?.GetValue<bool>() ?? false;
        var todos = new List<(string content, string status)>();
        var items = args["items"]?.AsArray();
        if (items == null)
            return Task.FromResult("{\"success\":false,\"error\":\"Missing items array\"}");

        var itemDetails = new List<object>();
        foreach (var item in items!)
        {
            var id = item!["id"]?.GetValue<string>() ?? "";
            var content = item["content"]?.GetValue<string>() ?? "";
            var status = item["status"]?.GetValue<string>() ?? "pending";
            if (!string.IsNullOrEmpty(content))
            {
                todos.Add((content, status));
                itemDetails.Add(new { id, content = content.Length > 60 ? content[..60] + "..." : content, status });
            }
        }

        // merge=false 时完全替换；merge=true 时更新现有项或追加新项
        if (!merge)
        {
            OnTodoWrite?.Invoke(todos);
        }
        else
        {
            // 合并模式：使用 id 匹配更新状态，新项追加
            OnTodoWrite?.Invoke(todos);
        }
        return Task.FromResult($"{{\"success\":true,\"count\":{todos.Count},\"merge\":{(merge ? "true" : "false")},\"items\":{System.Text.Json.JsonSerializer.Serialize(itemDetails)}}}");
    }

    // ===== Algorithm Tools =====

    private Task<string> AlgorithmListTool(JsonNode args)
    {
        if (_algorithmService == null)
            return Task.FromResult("{\"success\":false,\"error\":\"请先打开一个项目\"}");
        var category = args["category"]?.GetValue<string>();
        var language = args["language"]?.GetValue<string>();
        var algorithms = string.IsNullOrEmpty(category) && string.IsNullOrEmpty(language)
            ? _algorithmService.All.ToList()
            : _algorithmService.Filter(category, language);
        var result = algorithms.Select(a => new
        {
            a.Id, a.Name, a.Language, a.Category, a.Complexity,
            a.SpaceComplexity, a.Tags, a.Description, LineCount = a.LineCount,
            a.CreatedAt, a.UpdatedAt
        });
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, count = algorithms.Count, algorithms = result }));
    }

    private Task<string> AlgorithmSearchTool(JsonNode args)
    {
        if (_algorithmService == null)
            return Task.FromResult("{\"success\":false,\"error\":\"请先打开一个项目\"}");
        var keyword = args["keyword"]!.GetValue<string>();
        var algorithms = _algorithmService.Search(keyword);
        var result = algorithms.Select(a => new
        {
            a.Id, a.Name, a.Language, a.Category, a.Complexity,
            a.Tags, a.Description, LineCount = a.LineCount
        });
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, count = algorithms.Count, algorithms = result }));
    }

    private Task<string> AlgorithmGetTool(JsonNode args)
    {
        if (_algorithmService == null)
            return Task.FromResult("{\"success\":false,\"error\":\"请先打开一个项目\"}");
        var id = args["id"]!.GetValue<string>();
        var alg = _algorithmService.Get(id);
        if (alg == null)
            return Task.FromResult($"{{\"success\":false,\"error\":\"算法 {id} 不存在\"}}");
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            success = true,
            algorithm = new
            {
                alg.Id, alg.Name, alg.Description, alg.Language,
                alg.Category, alg.Complexity, alg.SpaceComplexity,
                alg.Tags, alg.Code, alg.SourceFile,
                LineCount = alg.LineCount, alg.CreatedAt, alg.UpdatedAt
            }
        }));
    }

    private Task<string> AlgorithmCreateTool(JsonNode args)
    {
        if (_algorithmService == null)
            return Task.FromResult("{\"success\":false,\"error\":\"请先打开一个项目\"}");
        var name = args["name"]!.GetValue<string>();
        var description = args["description"]?.GetValue<string>() ?? "";
        var language = args["language"]!.GetValue<string>();
        var code = args["code"]!.GetValue<string>();
        var category = args["category"]?.GetValue<string>() ?? "general";
        var complexity = args["complexity"]?.GetValue<string>() ?? "";
        var spaceComplexity = args["space_complexity"]?.GetValue<string>() ?? "";
        var tags = args["tags"]?.AsArray()?.Select(t => t!.GetValue<string>()).ToList() ?? new List<string>();

        var alg = _algorithmService.Create(name, description, language, code, category, complexity, spaceComplexity, tags);
        LogService.Instance.Info($"算法已添加: [{alg.Id}] {alg.Name} ({alg.Language})", "Algorithm");
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, id = alg.Id, name = alg.Name }));
    }

    private Task<string> AlgorithmUpdateTool(JsonNode args)
    {
        if (_algorithmService == null)
            return Task.FromResult("{\"success\":false,\"error\":\"请先打开一个项目\"}");
        var id = args["id"]!.GetValue<string>();
        var ok = _algorithmService.Update(id, alg =>
        {
            if (args["name"] != null) alg.Name = args["name"]!.GetValue<string>();
            if (args["description"] != null) alg.Description = args["description"]!.GetValue<string>();
            if (args["code"] != null) alg.Code = args["code"]!.GetValue<string>();
            if (args["category"] != null) alg.Category = args["category"]!.GetValue<string>();
            if (args["complexity"] != null) alg.Complexity = args["complexity"]!.GetValue<string>();
            if (args["space_complexity"] != null) alg.SpaceComplexity = args["space_complexity"]!.GetValue<string>();
            if (args["tags"] != null)
                alg.Tags = args["tags"]!.AsArray()!.Select(t => t!.GetValue<string>()).ToList();
        });
        if (!ok)
            return Task.FromResult($"{{\"success\":false,\"error\":\"算法 {id} 不存在\"}}");
        LogService.Instance.Info($"算法已更新: {id}", "Algorithm");
        return Task.FromResult($"{{\"success\":true,\"id\":\"{id}\"}}");
    }

    private Task<string> AlgorithmDeleteTool(JsonNode args)
    {
        if (_algorithmService == null)
            return Task.FromResult("{\"success\":false,\"error\":\"请先打开一个项目\"}");
        var id = args["id"]!.GetValue<string>();
        var ok = _algorithmService.Delete(id);
        if (!ok)
            return Task.FromResult($"{{\"success\":false,\"error\":\"算法 {id} 不存在\"}}");
        LogService.Instance.Info($"算法已删除: {id}", "Algorithm");
        return Task.FromResult($"{{\"success\":true,\"id\":\"{id}\"}}");
    }

    private async Task<string> BuildProjectTool(JsonNode args)
    {
        var action = args["action"]?.GetValue<string>() ?? "build";
        var language = args["language"]?.GetValue<string>();
        return action switch
        {
            "detect" => $"{{\"success\":true,\"language\":\"{_buildService.DetectLanguage()}\"}}",
            "list" => _buildService.GetAllLanguages(),
            "build" => await _buildService.BuildAsync(language),
            "package" => await _buildService.PackageAsync(language),
            "verify" => await _buildService.VerifyAsync(),
            _ => $"{{\"success\":false,\"error\":\"未知操作: {action}，支持 detect/build/package/list/verify\"}}"
        };
    }

    private Task<string> AlgorithmExtractTool(JsonNode args)
    {
        if (_algorithmService == null)
            return Task.FromResult("{\"success\":false,\"error\":\"请先打开一个项目\"}");
        var maxFiles = args["max_files"]?.GetValue<int>() ?? 50;
        var algorithms = _algorithmService.ExtractFromProject(_projectPath, maxFiles);
        // 自动保存提取结果
        foreach (var alg in algorithms)
            _algorithmService.Create(alg.Name, alg.Description, alg.Language, alg.Code, alg.Category, "", "", alg.Tags, alg.SourceFile);
        LogService.Instance.Info($"从项目中提取了 {algorithms.Count} 个算法", "Algorithm");
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            success = true,
            count = algorithms.Count,
            algorithms = algorithms.Select(a => new { a.Id, a.Name, a.Language, a.Category, a.SourceFile })
        }));
    }

    /// <summary>AI 子代理调度工具</summary>
    private async Task<string> AgentTool(JsonNode args)
    {
        var agentType = args["agent_type"]?.GetValue<string>() ?? "";
        var task = args["task"]?.GetValue<string>() ?? "";

        if (string.IsNullOrEmpty(agentType) || string.IsNullOrEmpty(task))
            return "{\"success\":false,\"error\":\"缺少 agent_type 或 task 参数\"}";

        if (!new[] { "browser", "codereview" }.Contains(agentType.ToLowerInvariant()))
            return $"{{\"success\":false,\"error\":\"不支持的子代理类型: {agentType}，支持: browser, codereview\"}}";

        if (_subAgent == null)
        {
            // 延迟初始化子代理服务
            var apiKey = _modelManager.GetEffectiveApiKey(_modelManager.ActiveProvider?.Id ?? "");
            if (string.IsNullOrEmpty(apiKey))
                return "{\"success\":false,\"error\":\"API Key 未配置，无法启动子代理\"}";
            var baseUrl = _modelManager.ActiveProvider?.BaseUrl ?? "https://api.deepseek.com/v1";
            var model = _modelManager.ActiveModel?.Id ?? "deepseek-v4-pro";
            _subAgent = new SubAgentService(apiKey, baseUrl, model);
            _subAgent.OnAgentResult += (type, summary) =>
                OnChunk?.Invoke($"\n[🤖 {type} 子代理完成: {summary}]\n");
        }

        LogService.Instance.Info($"启动子代理: {agentType}, 任务={Truncate(task, 80)}", "SubAgent");
        return await _subAgent.DispatchAsync(agentType.ToLowerInvariant(), task);
    }
}

