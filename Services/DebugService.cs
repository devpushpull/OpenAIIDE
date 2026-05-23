using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>多语言调试器 —— 通过终端启动程序，支持断点、单步、变量查看、自动定位当前行</summary>
public class DebugService
{
    private readonly TerminalService _terminal;
    private string? _projectPath;
    private string? _entryFile;
    private string _debuggerType = "none"; // pdb, node-inspect, dotnet-run
    private string? _terminalSubId; // 订阅终端输出时的 ID

    public event Action<string>? OnOutput;
    /// <summary>断点变更事件（供 UI 刷新装订线）</summary>
    public event Action? OnBreakpointsChanged;
    /// <summary>调试器当前暂停位置变更: (文件路径, 行号, 函数名)</summary>
    public event Action<string, int, string?>? OnPositionChanged;

    /// <summary>断点集合 (文件路径 -> 行号集合)</summary>
    public Dictionary<string, HashSet<int>> Breakpoints { get; } = new();
    /// <summary>断点条件 (文件路径+行号 -> 条件表达式，如 "i > 10")</summary>
    public Dictionary<(string FilePath, int Line), string> BreakpointConditions { get; } = new();
    /// <summary>断点命中次数 ("文件路径+行号" -> 剩余命中次数，-1=无条件)</summary>
    public Dictionary<(string FilePath, int Line), int> BreakpointHitCounts { get; } = new();
    public bool IsRunning { get; private set; }

    // 当前停止位置
    public string? CurrentFile { get; private set; }
    public int CurrentLine { get; private set; } = -1;
    public string? CurrentFunction { get; private set; }

    // pdb/node/gdb 输出的位置跟踪正则
    private static readonly Regex PdbPositionRegex = new(
        @">\s*(.+?)\((\d+)\)(\w+)\(\)", RegexOptions.Compiled);
    private static readonly Regex NodePositionRegex = new(
        @"break in\s+(.+?):(\d+)", RegexOptions.Compiled);
    private static readonly Regex GenericFileLineRegex = new(
        @"(?:at|in)\s+(.+?):(\d+)", RegexOptions.Compiled);

    public DebugService(TerminalService terminal)
    {
        _terminal = terminal;
    }

    public void SetProjectPath(string path, string? entryFile = null)
    {
        _projectPath = path;
        _entryFile = entryFile;
    }

    /// <summary>检测项目语言和调试器类型</summary>
    public (string Language, string Debugger) DetectLanguage()
    {
        if (_projectPath == null) return ("unknown", "none");

        // 检测 .csproj
        if (Directory.GetFiles(_projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
            return ("dotnet", "dotnet-run");

        // 检测 package.json
        if (File.Exists(Path.Combine(_projectPath, "package.json")))
            return ("node", "node-inspect");

        // 检测 .py 文件
        var pyFiles = Directory.GetFiles(_projectPath, "*.py", SearchOption.AllDirectories);
        if (pyFiles.Any())
        {
            var mainPy = pyFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("main.py", StringComparison.OrdinalIgnoreCase))
                         ?? pyFiles.First();
            return ("python", "pdb");
        }

        // 检测 go.mod
        if (File.Exists(Path.Combine(_projectPath, "go.mod")))
            return ("go", "go-run");

        // 检测 Cargo.toml
        if (File.Exists(Path.Combine(_projectPath, "Cargo.toml")))
            return ("rust", "cargo-run");

        return ("unknown", "none");
    }

    /// <summary>启动调试（在终端中真正启动调试进程）</summary>
    public async Task StartDebugAsync()
    {
        if (_projectPath == null)
        {
            OnOutput?.Invoke("[调试器] ❌ 请先打开项目");
            return;
        }

        var (language, debugger) = DetectLanguage();
        _debuggerType = debugger;

        if (debugger == "none")
        {
            OnOutput?.Invoke($"[调试器] ❌ 不支持的项目类型: {language}");
            return;
        }

        // 确保终端已创建
        if (!_terminal.IsRunning)
        {
            _terminal.CreateTerminal(_projectPath);
            await Task.Delay(500); // 等待终端初始化
        }

        // 构建调试命令
        var cmd = debugger switch
        {
            "dotnet-run" => "dotnet run",
            "node-inspect" => $"node inspect {_entryFile ?? "."}",
            "pdb" => $"python -m pdb {GetPythonEntryFile()}",
            "go-run" => "go run .",
            "cargo-run" => "cargo run",
            _ => null
        };

        if (cmd == null)
        {
            OnOutput?.Invoke($"[调试器] ❌ 无法生成调试命令: {debugger}");
            return;
        }

        IsRunning = true;
        OnOutput?.Invoke($"🔍 [调试器] 检测到 {language} 项目 → {debugger}");
        OnOutput?.Invoke($"▶ [调试器] 执行: {cmd}");

        // === 订阅终端输出以追踪调试位置 ===
        _terminalSubId = _terminal.Id;
        _terminal.OnDataReceived += OnTerminalData;
        _terminal.OnExited += OnTerminalExited;

        // 如果是 pdb 或 node-inspect，先设置断点命令
        if (debugger == "pdb" && Breakpoints.Count > 0)
        {
            // 构建 pdb 断点设置脚本
            var bpScript = BuildPdbBreakpointScript();
            _terminal.WriteInput(bpScript);
        }

        // 启动调试命令
        _terminal.WriteInput(cmd + "\n");

        // 显示断点信息
        if (Breakpoints.Count > 0)
        {
            var totalBps = Breakpoints.Sum(b => b.Value.Count);
            OnOutput?.Invoke($"📍 [调试器] 已设置 {totalBps} 个断点:");
            foreach (var bp in Breakpoints)
                foreach (var line in bp.Value.OrderBy(l => l))
                    OnOutput?.Invoke($"   📍 {Path.GetFileName(bp.Key)}:{line}");
        }

        await Task.CompletedTask;
    }

    /// <summary>停止调试</summary>
    public void StopDebug()
    {
        // 取消终端订阅
        if (_terminalSubId != null)
        {
            _terminal.OnDataReceived -= OnTerminalData;
            _terminal.OnExited -= OnTerminalExited;
            _terminalSubId = null;
        }

        var currentDebugger = _debuggerType; // 保存当前值，后续置none
        _debuggerType = "none";
        IsRunning = false;
        CurrentFile = null;
        CurrentLine = -1;
        CurrentFunction = null;

        // 发送退出命令到终端中的调试器
        if (_terminal.IsRunning)
        {
            if (currentDebugger == "pdb")
                _terminal.WriteInput("quit\n");
            else
                _terminal.WriteInput("\x03"); // Ctrl+C

            // 等待一下再 Kill
            Task.Delay(300).ContinueWith(_ =>
            {
                if (_terminal.IsRunning)
                    _terminal.Kill();
            });
        }

        OnOutput?.Invoke("⏹ [调试器] 调试已停止");
        OnPositionChanged?.Invoke("", -1, null); // 通知UI清除高亮
    }

    /// <summary>单步跳过 (F10)</summary>
    public void StepOver()
    {
        if (!IsRunning) return;
        OnOutput?.Invoke("⏭ [调试器] 单步跳过 (next)");
        _terminal.WriteInput("next\n");
    }

    /// <summary>单步进入 (F11)</summary>
    public void StepInto()
    {
        if (!IsRunning) return;
        OnOutput?.Invoke("⬇ [调试器] 单步进入 (step)");
        _terminal.WriteInput("step\n");
    }

    /// <summary>单步跳出 (Shift+F11)</summary>
    public void StepOut()
    {
        if (!IsRunning) return;
        OnOutput?.Invoke("⬆ [调试器] 单步跳出 (return)");
        _terminal.WriteInput("return\n");
    }

    /// <summary>继续执行 (F5)</summary>
    public void Continue()
    {
        if (!IsRunning) return;
        OnOutput?.Invoke("▶ [调试器] 继续执行 (continue)");
        _terminal.WriteInput("continue\n");
    }

    /// <summary>运行到光标位置 (Ctrl+F10) —— 设置临时断点后 continue</summary>
    public void RunToCursor(string filePath, int lineNumber)
    {
        if (!IsRunning) return;
        if (string.IsNullOrEmpty(filePath)) return;

        var fileName = Path.GetFileName(filePath);
        OnOutput?.Invoke($"🏃 [调试器] 运行到光标: {fileName}:{lineNumber}");

        // pdb / node-inspect 都支持临时断点语法
        if (_debuggerType == "pdb")
            _terminal.WriteInput($"tbreak {fileName}:{lineNumber}\n");
        else if (_debuggerType == "node-inspect")
            _terminal.WriteInput($"sb({fileName},{lineNumber})\n");
        else
            _terminal.WriteInput($"break {fileName}:{lineNumber}\n"); // 兜底：普通断点

        // 立即 continue
        Task.Delay(100).ContinueWith(_ =>
        {
            if (IsRunning)
                _terminal.WriteInput("continue\n");
        });
    }

    /// <summary>查看当前反汇编/IL（pdb 支持）</summary>
    public void ShowDisassembly()
    {
        if (!IsRunning) return;
        OnOutput?.Invoke("🔬 [调试器] 查看反汇编...");
        if (_debuggerType == "pdb")
            _terminal.WriteInput("disassemble\n");
        else
            OnOutput?.Invoke("⚠️ 当前调试器不支持反汇编查看");
    }

    /// <summary>查看当前断点列表</summary>
    public void ListBreakpoints()
    {
        if (!IsRunning) return;
        if (_debuggerType == "pdb")
            _terminal.WriteInput("break\n");
        else if (_debuggerType == "node-inspect")
            _terminal.WriteInput("breakpoints\n");
    }

    /// <summary>查看当前作用域变量（发送 args 到 pdb）</summary>
    public void InspectVariables()
    {
        if (!IsRunning) return;
        OnOutput?.Invoke("🔍 [调试器] 查看变量...");
        if (_debuggerType == "pdb")
        {
            // pdb: args 显示函数参数, p 变量名 查看具体值
            _terminal.WriteInput("args\n");
        }
        else if (_debuggerType == "node-inspect")
        {
            _terminal.WriteInput("repl\n");
        }
    }

    /// <summary>查看指定变量的值</summary>
    public void InspectVariable(string varName)
    {
        if (!IsRunning || string.IsNullOrEmpty(varName)) return;
        OnOutput?.Invoke($"🔍 [调试器] 查看: {varName}");
        if (_debuggerType == "pdb")
            _terminal.WriteInput($"p {varName}\n");
        else if (_debuggerType == "node-inspect")
            _terminal.WriteInput($"exec {varName}\n");
    }

    /// <summary>查看当前调用栈</summary>
    public void ShowCallStack()
    {
        if (!IsRunning) return;
        OnOutput?.Invoke("📚 [调试器] 查看调用栈...");
        if (_debuggerType == "pdb")
            _terminal.WriteInput("bt\n");
        else if (_debuggerType == "node-inspect")
            _terminal.WriteInput("bt\n");
    }

    /// <summary>切换断点</summary>
    public void ToggleBreakpoint(string filePath, int lineNumber)
    {
        if (!Breakpoints.ContainsKey(filePath))
            Breakpoints[filePath] = new HashSet<int>();

        if (Breakpoints[filePath].Contains(lineNumber))
        {
            Breakpoints[filePath].Remove(lineNumber);
            if (Breakpoints[filePath].Count == 0)
                Breakpoints.Remove(filePath);
            ClearBreakpointExtras(filePath, lineNumber);
            OnOutput?.Invoke($"⚪ [调试器] 移除断点: {Path.GetFileName(filePath)}:{lineNumber}");
        }
        else
        {
            Breakpoints[filePath].Add(lineNumber);
            OnOutput?.Invoke($"🔴 [调试器] 设置断点: {Path.GetFileName(filePath)}:{lineNumber}");

            // 如果正在调试且是 pdb，动态添加断点
            if (IsRunning && _debuggerType == "pdb")
            {
                _terminal.WriteInput($"break {Path.GetFileName(filePath)}:{lineNumber}\n");
            }
        }

        OnBreakpointsChanged?.Invoke();
    }

    /// <summary>设置断点条件（如 "i > 10"）</summary>
    public void SetBreakpointCondition(string filePath, int lineNumber, string? condition)
    {
        var key = (filePath, lineNumber);
        if (!string.IsNullOrWhiteSpace(condition))
        {
            BreakpointConditions[key] = condition.Trim();
            OnOutput?.Invoke($"🔴 [调试器] 条件断点: {Path.GetFileName(filePath)}:{lineNumber}  → {condition.Trim()}");

            // 通知调试器（pdb 支持条件断点）
            if (IsRunning && _debuggerType == "pdb")
                _terminal.WriteInput($"break {Path.GetFileName(filePath)}:{lineNumber}, {condition.Trim()}\n");
        }
        else
        {
            BreakpointConditions.Remove(key);
        }
    }

    /// <summary>获取断点条件</summary>
    public string? GetBreakpointCondition(string filePath, int lineNumber)
    {
        return BreakpointConditions.TryGetValue((filePath, lineNumber), out var cond) ? cond : null;
    }

    /// <summary>设置断点命中次数（-1 = 无条件）</summary>
    public void SetBreakpointHitCount(string filePath, int lineNumber, int hitCount)
    {
        var key = (filePath, lineNumber);
        if (hitCount > 0)
        {
            BreakpointHitCounts[key] = hitCount;
            OnOutput?.Invoke($"🔴 [调试器] 命中次数断点: {Path.GetFileName(filePath)}:{lineNumber}  → 每 {hitCount} 次命中暂停");
        }
        else
        {
            BreakpointHitCounts.Remove(key);
        }
    }

    /// <summary>清除指定断点的所有附加属性</summary>
    private void ClearBreakpointExtras(string filePath, int lineNumber)
    {
        var key = (filePath, lineNumber);
        BreakpointConditions.Remove(key);
        BreakpointHitCounts.Remove(key);
    }

    /// <summary>判断指定位置是否有断点</summary>
    public bool HasBreakpoint(string filePath, int lineNumber)
    {
        return Breakpoints.TryGetValue(filePath, out var lines) && lines.Contains(lineNumber);
    }

    /// <summary>清除所有断点</summary>
    public void ClearAllBreakpoints()
    {
        Breakpoints.Clear();
        BreakpointConditions.Clear();
        BreakpointHitCounts.Clear();
        OnOutput?.Invoke("🗑 [调试器] 已清除所有断点");
        OnBreakpointsChanged?.Invoke();

        if (IsRunning && _debuggerType == "pdb")
            _terminal.WriteInput("clear\n");
    }

    /// <summary>获取断点总数</summary>
    public int BreakpointCount => Breakpoints.Sum(b => b.Value.Count);

    // ========== 私有方法 ==========

    private string GetPythonEntryFile()
    {
        if (_entryFile != null && File.Exists(Path.Combine(_projectPath!, _entryFile)))
            return _entryFile;

        var mainPy = Path.Combine(_projectPath!, "main.py");
        if (File.Exists(mainPy))
            return "main.py";

        // 找到第一个 .py 文件
        var firstPy = Directory.GetFiles(_projectPath!, "*.py", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (firstPy != null)
            return Path.GetFileName(firstPy);

        return "main.py";
    }

    private string BuildPdbBreakpointScript()
    {
        var script = "";
        foreach (var bp in Breakpoints)
        {
            var fileName = Path.GetFileName(bp.Key);
            foreach (var line in bp.Value.OrderBy(l => l))
            {
                script += $"break {fileName}:{line}\n";
            }
        }
        return script;
    }

    // ========== 终端输出解析 —— 自动定位当前暂停行 ==========

    private void OnTerminalData(string terminalId, string data)
    {
        if (!IsRunning) return;
        if (string.IsNullOrEmpty(data)) return;

        // 只在调试状态下解析位置信息
        var (filePath, lineNum, funcName) = TryParseDebugPosition(data);
        if (filePath != null && lineNum > 0)
        {
            // 如果能找到完整路径，使用完整路径
            var fullPath = ResolveFilePath(filePath);
            if (fullPath != null)
            {
                CurrentFile = fullPath;
                CurrentLine = lineNum;
                CurrentFunction = funcName;
                OnPositionChanged?.Invoke(fullPath, lineNum, funcName);

                var displayPath = Path.GetFileName(fullPath);
                OnOutput?.Invoke($"📍 [调试器] 停在: {displayPath}:{lineNum}{(funcName != null ? $" → {funcName}()" : "")}");
            }
        }
    }

    private void OnTerminalExited(string terminalId, int exitCode)
    {
        if (!IsRunning) return;
        IsRunning = false;
        _terminal.OnDataReceived -= OnTerminalData;
        _terminal.OnExited -= OnTerminalExited;
        _terminalSubId = null;

        OnOutput?.Invoke(exitCode == 0
            ? $"✅ [调试器] 程序正常退出 (exit code: {exitCode})"
            : $"❌ [调试器] 程序异常退出 (exit code: {exitCode})");
        OnPositionChanged?.Invoke("", -1, null);
    }

    private (string? file, int line, string? func) TryParseDebugPosition(string line)
    {
        // pdb 格式: > d:\path\to\file.py(42)func_name()
        var pdbMatch = PdbPositionRegex.Match(line);
        if (pdbMatch.Success)
        {
            var file = pdbMatch.Groups[1].Value.Trim();
            var lineNum = int.Parse(pdbMatch.Groups[2].Value);
            var func = pdbMatch.Groups[3].Value.Trim();
            return (file, lineNum, func);
        }

        // node inspect 格式: break in path/to/file.js:42
        var nodeMatch = NodePositionRegex.Match(line);
        if (nodeMatch.Success)
        {
            var file = nodeMatch.Groups[1].Value.Trim();
            var lineNum = int.Parse(nodeMatch.Groups[2].Value);
            return (file, lineNum, null);
        }

        // 通用格式: at file:line 或 in file:line
        var genMatch = GenericFileLineRegex.Match(line);
        if (genMatch.Success)
        {
            var file = genMatch.Groups[1].Value.Trim();
            var lineNum = int.Parse(genMatch.Groups[2].Value);
            return (file, lineNum, null);
        }

        return (null, -1, null);
    }

    /// <summary>将相对路径或简略路径解析为完整路径</summary>
    private string? ResolveFilePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        // 已经是完整路径
        if (Path.IsPathRooted(filePath) && File.Exists(filePath))
            return filePath;

        if (_projectPath == null) return null;

        // 相对路径 → 基于项目根目录
        var combined = Path.Combine(_projectPath, filePath);
        if (File.Exists(combined))
            return Path.GetFullPath(combined);

        // 如果只有文件名，在项目中搜索
        var fileName = Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(fileName))
        {
            var found = Directory.GetFiles(_projectPath, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found != null)
                return Path.GetFullPath(found);
        }

        return null;
    }
}
