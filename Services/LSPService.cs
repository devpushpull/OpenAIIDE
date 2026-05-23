using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIIDEWPF.Services;

/// <summary>
/// LSP (Language Server Protocol) 客户端 —— 管理语言服务器进程生命周期，
/// 提供实时诊断、跳转定义、查找引用等代码智能功能。
/// 对标 Qoder/通义灵码/Cursor 的 LSP 能力。
/// </summary>
public class LSPService : IDisposable
{
    private Process? _serverProcess;
    private StreamWriter? _serverStdin;
    private StreamReader? _serverStdout;
    private int _nextId;
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
    private readonly Dictionary<string, List<DiagnosticItem>> _diagnostics = new();
    private string? _projectPath;
    private string? _language;
    private bool _initialized;
    private CancellationTokenSource? _readCts;

    // ===== 事件 =====
    /// <summary>诊断更新 (filePath, diagnostics)</summary>
    public event Action<string, List<DiagnosticItem>>? OnDiagnosticsChanged;
    /// <summary>定义位置结果 (filePath, line, column)</summary>
    public event Action<string, int, int>? OnGoToLocation;
    /// <summary>引用列表结果</summary>
    public event Action<List<ReferenceItem>>? OnReferencesFound;

    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;
    public string? Language => _language;

    /// <summary>获取当前项目的所有诊断</summary>
    public IReadOnlyDictionary<string, List<DiagnosticItem>> AllDiagnostics => _diagnostics.AsReadOnly();

    // ===== 启动/停止 =====

    public async Task<bool> StartAsync(string projectPath)
    {
        Stop();
        _projectPath = projectPath;
        _language = DetectLanguage(projectPath);

        var serverInfo = GetServerInfo(_language);
        if (serverInfo == null)
        {
            LogService.Instance.Info($"LSP: 不支持的语言 {_language}，跳过启动", "LSP");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = serverInfo.Value.Command,
                Arguments = serverInfo.Value.Args,
                WorkingDirectory = projectPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            _serverProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _serverProcess.Exited += (s, e) =>
                LogService.Instance.Warn($"LSP 服务器进程退出: {_language}", "LSP");
            _serverProcess.Start();

            _serverStdin = _serverProcess.StandardInput;
            _serverStdout = _serverProcess.StandardOutput;

            // 后台读取线程
            _readCts = new CancellationTokenSource();
            _ = ReadLoopAsync(_readCts.Token);

            // 发送 initialize
            var initResult = await InitializeAsync(projectPath);
            if (initResult == null)
            {
                LogService.Instance.Error("LSP: initialize 失败", "LSP");
                Stop();
                return false;
            }

            _initialized = true;
            LogService.Instance.Info($"LSP: {_language} 语言服务器已启动 ({serverInfo.Value.Command})", "LSP");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"LSP: 启动失败 - {ex.Message}", "LSP");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _initialized = false;
        _readCts?.Cancel();

        try
        {
            if (_serverStdin != null && _serverProcess != null && !_serverProcess.HasExited)
            {
                SendNotification("shutdown", null);
                SendNotification("exit", null);
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"LSP关机通知异常: {ex.Message}", "LSP"); }

        try { _serverProcess?.Kill(); } catch (Exception ex) { LogService.Instance.Debug($"LSP进程Kill异常: {ex.Message}", "LSP"); }
        _serverProcess?.Dispose();
        _serverProcess = null;
        _serverStdin = null;
        _serverStdout = null;
        _pendingRequests.Clear();
    }

    // ===== 文档同步 =====

    public void DidOpen(string filePath, string content, string language)
    {
        if (!_initialized) return;
        SendNotification("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = ToUri(filePath),
                languageId = language.ToLowerInvariant(),
                version = 1,
                text = content
            }
        });
    }

    public void DidChange(string filePath, string content)
    {
        if (!_initialized) return;
        SendNotification("textDocument/didChange", new
        {
            textDocument = new { uri = ToUri(filePath), version = DateTime.UtcNow.Ticks },
            contentChanges = new[] { new { text = content } }
        });
    }

    public void DidClose(string filePath)
    {
        if (!_initialized) return;
        SendNotification("textDocument/didClose", new
        {
            textDocument = new { uri = ToUri(filePath) }
        });
    }

    // ===== 代码导航 =====

    public async Task<(string FilePath, int Line, int Column)?> GoToDefinitionAsync(
        string filePath, int line, int character)
    {
        if (!_initialized) return null;
        var result = await SendRequestAsync("textDocument/definition", new
        {
            textDocument = new { uri = ToUri(filePath) },
            position = new { line = line - 1, character }
        });

        return ParseLocation(result);
    }

    public async Task<List<ReferenceItem>> FindReferencesAsync(
        string filePath, int line, int character)
    {
        if (!_initialized) return new();
        var result = await SendRequestAsync("textDocument/references", new
        {
            textDocument = new { uri = ToUri(filePath) },
            position = new { line = line - 1, character },
            context = new { includeDeclaration = true }
        });

        return ParseReferences(result);
    }

    // ===== 内部方法 =====

    private async Task<JsonNode?> InitializeAsync(string projectPath)
    {
        var rootUri = ToUri(projectPath);
        return await SendRequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri,
            capabilities = new
            {
                textDocument = new
                {
                    publishDiagnostics = new { relatedInformation = true },
                    definition = new { dynamicRegistration = true },
                    references = new { dynamicRegistration = true }
                }
            }
        });
    }

    private async Task<JsonNode?> SendRequestAsync(string method, object? @params)
    {
        if (_serverStdin == null) return null;
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>();
        lock (_pendingRequests)
            _pendingRequests[id.ToString()] = tcs;

        var msg = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        };
        var json = JsonSerializer.Serialize(msg);
        var contentBytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {contentBytes.Length}\r\n\r\n";
        await _serverStdin.WriteAsync(header);
        await _serverStdin.WriteAsync(json);
        await _serverStdin.FlushAsync();

        // 超时 15 秒
        var timeout = Task.Delay(15000);
        var completed = await Task.WhenAny(tcs.Task, timeout);
        if (completed == timeout)
        {
            lock (_pendingRequests)
                _pendingRequests.Remove(id.ToString());
            return null;
        }
        return await tcs.Task;
    }

    private void SendNotification(string method, object? @params)
    {
        if (_serverStdin == null) return;
        try
        {
            var msg = new { jsonrpc = "2.0", method, @params };
            var json = JsonSerializer.Serialize(msg);
            var contentBytes = Encoding.UTF8.GetBytes(json);
            var header = $"Content-Length: {contentBytes.Length}\r\n\r\n";
            _serverStdin.Write(header);
            _serverStdin.Write(json);
            _serverStdin.Flush();
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"LSP: 发送通知失败 {method} - {ex.Message}", "LSP");
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _serverStdout != null)
        {
            try
            {
                // 读取 Content-Length 头
                var header = await _serverStdout.ReadLineAsync();
                if (string.IsNullOrEmpty(header)) continue;
                if (!header.StartsWith("Content-Length:")) continue;

                var lenStr = header["Content-Length:".Length..].Trim();
                if (!int.TryParse(lenStr, out var contentLength)) continue;

                // 跳过空行
                while (true)
                {
                    var line = await _serverStdout.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) break;
                }

                // 读取消息体
                var buffer = new char[contentLength];
                var read = 0;
                while (read < contentLength)
                {
                    var r = await _serverStdout.ReadAsync(buffer, read, contentLength - read);
                    if (r == 0) break;
                    read += r;
                }
                var json = new string(buffer, 0, read);

                HandleMessage(json);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LogService.Instance.Warn($"LSP: 读取消息异常 - {ex.Message}", "LSP");
                break;
            }
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node == null) return;

            var idNode = node["id"];
            var method = node["method"]?.GetValue<string>();
            var result = node["result"];
            var error = node["error"];

            // 响应消息
            if (idNode != null)
            {
                var id = idNode.GetValue<object>().ToString()!;
                TaskCompletionSource<JsonNode?>? tcs;
                lock (_pendingRequests)
                {
                    _pendingRequests.TryGetValue(id, out tcs);
                    _pendingRequests.Remove(id);
                }
                if (tcs != null)
                {
                    if (error != null)
                        tcs.SetResult(null);
                    else
                        tcs.SetResult(result);
                }
                return;
            }

            // 通知消息
            if (method == "textDocument/publishDiagnostics")
            {
                var uri = result?["uri"]?.GetValue<string>();
                var diags = result?["diagnostics"]?.AsArray();
                if (uri != null)
                    ProcessDiagnostics(FromUri(uri), diags);
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"LSP消息处理异常: {ex.Message}", "LSP"); }
    }

    private void ProcessDiagnostics(string filePath, JsonArray? diagsArray)
    {
        var items = new List<DiagnosticItem>();
        if (diagsArray != null)
        {
            foreach (var d in diagsArray)
            {
                if (d == null) continue;
                var range = d["range"];
                var start = range?["start"];
                var end = range?["end"];
                items.Add(new DiagnosticItem
                {
                    Message = d["message"]?.GetValue<string>() ?? "",
                    Severity = (d["severity"]?.GetValue<int>() ?? 2) switch
                    {
                        1 => DiagnosticSeverity.Error,
                        2 => DiagnosticSeverity.Warning,
                        3 => DiagnosticSeverity.Info,
                        4 => DiagnosticSeverity.Hint,
                        _ => DiagnosticSeverity.Warning
                    },
                    StartLine = (start?["line"]?.GetValue<int>() ?? 0) + 1,
                    StartColumn = (start?["character"]?.GetValue<int>() ?? 0) + 1,
                    EndLine = (end?["line"]?.GetValue<int>() ?? 0) + 1,
                    EndColumn = (end?["character"]?.GetValue<int>() ?? 0) + 1,
                    Source = d["source"]?.GetValue<string>() ?? "",
                    Code = d["code"]?.GetValue<object>()?.ToString() ?? ""
                });
            }
        }
        _diagnostics[filePath] = items;
        OnDiagnosticsChanged?.Invoke(filePath, items);
    }

    private static (string FilePath, int Line, int Column)? ParseLocation(JsonNode? result)
    {
        if (result == null) return null;
        var loc = result.AsArray()?.FirstOrDefault() ?? result;
        var uri = loc?["uri"]?.GetValue<string>();
        var start = loc?["range"]?["start"];
        if (uri == null || start == null) return null;
        return (FromUri(uri), start["line"]!.GetValue<int>() + 1, start["character"]!.GetValue<int>() + 1);
    }

    private static List<ReferenceItem> ParseReferences(JsonNode? result)
    {
        var list = new List<ReferenceItem>();
        var arr = result?.AsArray();
        if (arr == null) return list;
        foreach (var r in arr)
        {
            if (r == null) continue;
            var uri = r["uri"]?.GetValue<string>();
            var start = r["range"]?["start"];
            if (uri == null || start == null) continue;
            list.Add(new ReferenceItem
            {
                FilePath = FromUri(uri),
                Line = start["line"]!.GetValue<int>() + 1,
                Column = start["character"]!.GetValue<int>() + 1
            });
        }
        return list;
    }

    // ===== 工具方法 =====

    private static string ToUri(string path) =>
        new Uri(path.Replace('\\', '/')).AbsoluteUri;

    private static string FromUri(string uri) =>
        new Uri(uri).LocalPath;

    private static string DetectLanguage(string projectPath)
    {
        try
        {
            if (Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                return "C#";
            if (File.Exists(Path.Combine(projectPath, "package.json")))
            {
                var tsConfig = Path.Combine(projectPath, "tsconfig.json");
                if (File.Exists(tsConfig)) return "TypeScript";
                return "JavaScript";
            }
            var pyFiles = Directory.GetFiles(projectPath, "*.py", SearchOption.AllDirectories);
            if (pyFiles.Any()) return "Python";
            if (File.Exists(Path.Combine(projectPath, "go.mod")))
                return "Go";
            if (File.Exists(Path.Combine(projectPath, "Cargo.toml")))
                return "Rust";
            // Java: pom.xml / build.gradle / build.gradle.kts
            if (File.Exists(Path.Combine(projectPath, "pom.xml"))
                || File.Exists(Path.Combine(projectPath, "build.gradle"))
                || File.Exists(Path.Combine(projectPath, "build.gradle.kts")))
                return "Java";
            // Kotlin (fallback to Java LSP in jdtls)
            if (Directory.GetFiles(projectPath, "*.kt", SearchOption.TopDirectoryOnly).Any())
                return "Java";
        }
        catch { }
        return "unknown";
    }

    private static (string Command, string Args)? GetServerInfo(string language)
    {
        return language switch
        {
            "C#" => ("dotnet", "omnisharp -lsp"),
            "Python" => ("pyright-langserver", "--stdio"),
            "TypeScript" or "JavaScript" => ("npx", "typescript-language-server --stdio"),
            "Java" => ("jdtls", ""),
            "Go" => ("gopls", ""),
            "Rust" => ("rust-analyzer", ""),
            "PHP" => ("intelephense", "--stdio"),
            "Ruby" => ("solargraph", "stdio"),
            "Dart" => ("dart", "language-server --client-id=aiide"),
            "Lua" => ("lua-lsp", ""),
            "Kotlin" => ("kotlin-language-server", ""),
            "Swift" => ("sourcekit-lsp", ""),
            "C" or "C++" => ("clangd", ""),
            _ => null
        };
    }

    public void Dispose()
    {
        Stop();
        _readCts?.Dispose();
    }
}

// ===== 公共类型 =====

public class DiagnosticItem
{
    public string Message { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; }
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Source { get; set; } = "";
    public string Code { get; set; } = "";

    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int Length => EndOffset - StartOffset;
}

public enum DiagnosticSeverity { Error = 1, Warning = 2, Info = 3, Hint = 4 }

public class ReferenceItem
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}
