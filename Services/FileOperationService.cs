using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIIDEWPF.Services;

/// <summary>
/// 统一的文件/文件夹操作工具（CRUD），供 AI 工具调用和 UI 使用。
/// 所有写操作锁定在项目目录内，读操作可访问任意路径。
/// </summary>
public class FileOperationService
{
    private string _projectPath = Environment.CurrentDirectory;
    private readonly FileService _fileService = new();
    private BackupService? _backup;
    private HooksService? _hooks;

    public event Action<string>? OnFileChanged;    // 文件/目录创建、修改、删除时通知 UI 刷新
    public event Action<string>? OnDirectoryChanged;

    public string ProjectPath => _projectPath;

    /// <summary>是否允许编辑工作区外的文件（用户通过设置开关控制）</summary>
    public bool AllowExternalEdit { get; set; } = false;

    public void SetProjectPath(string path) => _projectPath = path;

    /// <summary>设置钩子服务，启用文件变更后的自定义脚本</summary>
    public void SetHooksService(HooksService hooks) => _hooks = hooks;

    /// <summary>通知文件变更（触发事件 + 钩子）</summary>
    private void NotifyFileChanged(string path)
    {
        OnFileChanged?.Invoke(path);
        _ = _hooks?.RunHooksAsync("on_file_change", new Dictionary<string, string>
        {
            ["FILE"] = path,
            ["PROJECT"] = _projectPath
        });
    }

    /// <summary>设置备份服务，启用写操作前的自动备份</summary>
    public void SetBackupService(BackupService backup) => _backup = backup;

    // ==================== 路径安全校验 ====================

    /// <summary>确保操作路径在项目目录内（写操作用）</summary>
    public string EnsurePathInProject(string relativeOrAbsolute)
    {
        var resolved = ResolvePath(relativeOrAbsolute);

        // 如果允许外部编辑，跳过路径限制
        if (AllowExternalEdit)
            return resolved;

        if (string.IsNullOrEmpty(_projectPath))
            throw new InvalidOperationException("请先打开一个项目或文件夹。");

        var projectRoot = Path.GetFullPath(_projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedUpper = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!resolvedUpper.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !resolvedUpper.Equals(projectRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"文件操作被拒绝：路径 '{resolved}' 不在当前项目目录 '{_projectPath}' 内。");

        return resolved;
    }

    /// <summary>解析路径：相对路径基于项目根，绝对路径直接使用。 / 前缀路径视为项目相对路径。</summary>
    public string ResolvePath(string filePath)
    {
        // 处理 / 开头的相对路径（如 /Services/AIService.cs）→ 视为项目相对路径
        if (filePath.Length > 1 && filePath[0] == '/' && filePath[1] != '/')
        {
            var relativePath = filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(_projectPath, relativePath));
        }
        return Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(Path.Combine(_projectPath, filePath));
    }

    // ==================== 文件操作 ====================

    /// <summary>
    /// 原子写入文件：先写临时文件，校验后原子替换，防止进程崩溃导致文件写一半损坏。
    /// 任何步骤失败，原文件不受影响。
    /// </summary>
    public static void SafeWriteAllText(string filePath, string content)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var tmpPath = Path.Combine(dir, $".tmp.{Guid.NewGuid():N}");

        try
        {
            // 0. 磁盘空间检查（至少需要 content 大小的 3 倍空间以保证安全）
            var requiredBytes = content.Length * 3L;
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir)) ?? "C:\\");
                if (driveInfo.AvailableFreeSpace < requiredBytes)
                {
                    throw new InvalidOperationException(
                        $"磁盘空间不足: 需要 {requiredBytes / 1024}KB, 可用 {driveInfo.AvailableFreeSpace / 1024}KB");
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { /* 无法检测时不阻塞 */ }

            // 1. 若原文件存在，创建 .bak 兜底备份（极端情况下的最后恢复手段）
            if (File.Exists(filePath))
            {
                try
                {
                    var bakPath = filePath + ".bak";
                    File.Copy(filePath, bakPath, overwrite: true);
                }
                catch { /* .bak 创建失败不阻塞主流程 */ }
            }

            // 2. 写入临时文件
            File.WriteAllText(tmpPath, content);

            // 3. 完整性校验：读取临时文件确认长度匹配
            var tmpContent = File.ReadAllText(tmpPath);
            if (tmpContent.Length != content.Length)
            {
                SafeDelete(tmpPath);
                throw new InvalidOperationException($"临时文件写入不完整: 期望{content.Length}字节, 实际{tmpContent.Length}字节");
            }

            // 4. 使用 File.Replace 原子替换原文件
            if (File.Exists(filePath))
            {
                try
                {
                    File.Replace(tmpPath, filePath, null); // null = 不保留备份
                }
                catch (IOException)
                {
                    // 跨盘时 File.Replace 可能不可用，回退到 Copy+Delete
                    File.Copy(tmpPath, filePath, overwrite: true);
                    SafeDelete(tmpPath);
                }
            }
            else
            {
                File.Move(tmpPath, filePath);
            }

            // 5. 写入成功后删除 .bak 兜底备份
            try
            {
                var bakPath = filePath + ".bak";
                if (File.Exists(bakPath)) File.Delete(bakPath);
            }
            catch { }
        }
        catch
        {
            // 任何失败都要清理临时文件，但保留 .bak 备份
            SafeDelete(tmpPath);
            throw;
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>读取文件内容</summary>
    public string ReadFile(string path, int? startLine = null, int? endLine = null)
    {
        var fp = ResolvePath(path);
        if (!File.Exists(fp))
        {
            // 精确路径不存在时，进行模糊查找并返回建议
            var suggestions = SmartFindFile(path);
            if (suggestions.Count > 0)
            {
                var suggestionList = string.Join("\n", suggestions.Take(5).Select((s, i) =>
                    $"  [{i + 1}] {Path.GetRelativePath(_projectPath, s).Replace('\\', '/')}"));
                return $"{{\"success\":false,\"error\":\"文件不存在: {Escape(path)}\\n\\n🔍 发现以下相似文件，请使用正确路径重试 read_file:\\n{suggestionList}\"}}";
            }
            return $"{{\"success\":false,\"error\":\"文件不存在: {Escape(fp)}\"}}";
        }

        try
        {
            var content = _fileService.ReadFile(fp, startLine, endLine);
            return JsonSerializer.Serialize(new { success = true, content });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>创建/覆盖文件（自动创建父目录）</summary>
    public string CreateFile(string path, string content)
    {
        try
        {
            var fp = EnsurePathInProject(path);
            var dir = Path.GetDirectoryName(fp);
            var dirCreated = false;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                dirCreated = true;
            }

            // 若文件已存在，先备份再覆盖（防止崩溃导致数据丢失）
            _backup?.BackupBeforeWrite(fp);
            var existed = File.Exists(fp);
            SafeWriteAllText(fp, content);
            var lineCount = content.Count(c => c == '\n') + 1;
            NotifyFileChanged(fp);
            var fileName = Path.GetFileName(fp);
            var extInfo = dirCreated
                ? $"{{\"success\":true,\"file_path\":\"{Escape(fp)}\",\"file_name\":\"{Escape(fileName)}\",\"line_count\":{lineCount},\"created_parent_dir\":true,\"existed\":{existed.ToString().ToLower()}}}"
                : $"{{\"success\":true,\"file_path\":\"{Escape(fp)}\",\"file_name\":\"{Escape(fileName)}\",\"line_count\":{lineCount},\"existed\":{existed.ToString().ToLower()}}}";
            return extInfo;
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>搜索替换文件内容</summary>
    public string SearchReplace(string path, string originalText, string newText, bool replaceAll)
    {
        try
        {
            var fp = EnsurePathInProject(path);
            if (!File.Exists(fp))
                return $"{{\"success\":false,\"error\":\"文件不存在: {Escape(fp)}\"}}";

            var content = File.ReadAllText(fp);
            if (!content.Contains(originalText))
            {
                // 提供更详细的错误信息帮助 LLM 修复
                var snippet = originalText.Length > 80 ? originalText[..80] + "..." : originalText;
                return $"{{\"success\":false,\"error\":\"未找到匹配文本。请用 read_file 重新读取文件确认内容。搜索的文本片段: {Escape(snippet)}\"}}";
            }

            // 备份原始内容（防止修改过程中崩溃导致文件损坏）
            _backup?.BackupContentChange(fp, content);

            var lines = content.Split('\n');

            if (replaceAll)
            {
                var count = 0;
                var lineNumbers = new List<int>();
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(originalText))
                    {
                        count++;
                        lineNumbers.Add(i + 1); // 1-based line numbers
                    }
                }
                content = content.Replace(originalText, newText);
                SafeWriteAllText(fp, content);
                NotifyFileChanged(fp);
                var fileName = Path.GetFileName(fp);
                var lineInfo = lineNumbers.Count <= 3
                    ? $"行 {string.Join(", ", lineNumbers)}"
                    : $"行 {string.Join(", ", lineNumbers.Take(3))} 等共 {lineNumbers.Count} 处";
                return $"{{\"success\":true,\"file_path\":\"{Escape(fp)}\",\"file_name\":\"{Escape(fileName)}\",\"replacements\":{count},\"lines\":\"{Escape(lineInfo)}\"}}";
            }

            var idx = content.IndexOf(originalText, StringComparison.Ordinal);
            var lineNumber = content[..idx].Count(c => c == '\n') + 1; // 1-based
            content = content[..idx] + newText + content[(idx + originalText.Length)..];
            SafeWriteAllText(fp, content);
            NotifyFileChanged(fp);
            var fName = Path.GetFileName(fp);
            return $"{{\"success\":true,\"file_path\":\"{Escape(fp)}\",\"file_name\":\"{Escape(fName)}\",\"replacements\":1,\"line\":{lineNumber}}}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>删除文件</summary>
    public string DeleteFile(string path)
    {
        try
        {
            var fp = EnsurePathInProject(path);
            if (!File.Exists(fp))
                return $"{{\"success\":false,\"error\":\"文件不存在: {Escape(fp)}\"}}";

            // 删除前先备份，防止误删导致数据丢失
            _backup?.BackupBeforeWrite(fp);
            var fileName = Path.GetFileName(fp);
            File.Delete(fp);
            NotifyFileChanged(fp);
            return $"{{\"success\":true,\"file_path\":\"{Escape(fp)}\",\"file_name\":\"{Escape(fileName)}\"}}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>重命名文件或目录</summary>
    public string Rename(string oldPath, string newName)
    {
        try
        {
            var fp = EnsurePathInProject(oldPath);
            var dir = Path.GetDirectoryName(fp) ?? _projectPath;
            var newPath = Path.Combine(dir, newName);
            EnsurePathInProject(newPath);

            if (File.Exists(fp))
            {
                if (File.Exists(newPath))
                    return $"{{\"success\":false,\"error\":\"目标文件已存在: {Escape(newPath)}\"}}";
                File.Move(fp, newPath);
            }
            else if (Directory.Exists(fp))
            {
                if (Directory.Exists(newPath))
                    return $"{{\"success\":false,\"error\":\"目标目录已存在: {Escape(newPath)}\"}}";
                Directory.Move(fp, newPath);
            }
            else
            {
                return $"{{\"success\":false,\"error\":\"路径不存在: {Escape(fp)}\"}}";
            }

            return $"{{\"success\":true,\"old_path\":\"{Escape(fp)}\",\"new_path\":\"{Escape(newPath)}\"}}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    // ==================== 文件夹操作 ====================

    /// <summary>创建目录（自动创建父目录）</summary>
    public string CreateDirectory(string path)
    {
        try
        {
            var dp = EnsurePathInProject(path);
            if (Directory.Exists(dp))
                return $"{{\"success\":false,\"error\":\"目录已存在\"}}";

            Directory.CreateDirectory(dp);
            NotifyFileChanged(dp);
            return $"{{\"success\":true,\"path\":\"{Escape(dp)}\"}}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>删除目录（含所有内容）</summary>
    public string DeleteDirectory(string path)
    {
        try
        {
            var dp = EnsurePathInProject(path);
            if (!Directory.Exists(dp))
                return $"{{\"success\":false,\"error\":\"目录不存在\"}}";

            Directory.Delete(dp, true);
            return $"{{\"success\":true}}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>列出目录内容</summary>
    public string ListDirectory(string path)
    {
        try
        {
            var dp = ResolvePath(path);
            if (!Directory.Exists(dp))
                return $"{{\"success\":false,\"error\":\"目录不存在: {Escape(dp)}\"}}";

            var items = new List<object>();
            foreach (var entry in Directory.GetFileSystemEntries(dp).OrderBy(e => e))
            {
                var name = Path.GetFileName(entry);
                var isDir = Directory.Exists(entry);
                items.Add(new
                {
                    name,
                    isDirectory = isDir,
                    isFile = !isDir,
                    size = isDir ? (long?)null : new FileInfo(entry).Length
                });
            }

            return JsonSerializer.Serialize(new { success = true, path = dp, items });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>按 glob 模式搜索文件</summary>
    public string SearchFiles(string root, string glob)
    {
        try
        {
            var rp = ResolvePath(root);
            var pattern = "^" + Regex.Escape(glob).Replace("\\*", ".*") + "$";
            var results = new List<string>();

            SearchFilesRecursive(rp, pattern, results);

            return JsonSerializer.Serialize(new
            {
                success = true,
                files = results.Take(200).Select(f => Path.GetRelativePath(_projectPath, f))
            });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    private void SearchFilesRecursive(string dir, string pattern, List<string> results)
    {
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(dir))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || name == "node_modules") continue;

                if (Directory.Exists(entry))
                    SearchFilesRecursive(entry, pattern, results);
                else if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    results.Add(entry);
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"文件搜索跳过异常目录: {ex.Message}", "FileOp"); }
    }

    /// <summary>扫描项目结构（供 AI 理解项目布局）</summary>
    public string ScanProject(string path, int maxDepth = 3)
    {
        try
        {
            var rp = ResolvePath(path);
            var results = new List<object>();
            ScanDir(rp, rp, 0, maxDepth, results);
            return JsonSerializer.Serialize(new { success = true, project_path = rp, structure = results });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    private void ScanDir(string basePath, string currentPath, int depth, int maxDepth, List<object> results)
    {
        if (depth > maxDepth) return;
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(currentPath).OrderBy(e => e))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || name == "node_modules" || name == "bin" || name == "obj") continue;

                var isDir = Directory.Exists(entry);
                var relPath = Path.GetRelativePath(basePath, entry).Replace('\\', '/');
                var item = new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["path"] = relPath,
                    ["type"] = isDir ? "dir" : "file"
                };
                if (!isDir)
                {
                    try { item["size"] = new FileInfo(entry).Length; } catch (Exception ex) { LogService.Instance.Debug($"获取文件大小异常: {ex.Message}", "FileOp"); }
                    try { item["ext"] = Path.GetExtension(name)?.ToLower() ?? ""; } catch (Exception ex) { LogService.Instance.Debug($"获取文件扩展名异常: {ex.Message}", "FileOp"); }
                }
                if (isDir && depth < maxDepth)
                {
                    var children = new List<object>();
                    ScanDir(basePath, entry, depth + 1, maxDepth, children);
                    item["children"] = children;
                }
                results.Add(item);
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"扫描项目结构异常: {ex.Message}", "FileOp"); }
    }

    // ==================== 全局搜索替换 ====================

    /// <summary>
    /// 在项目目录中多文件搜索并批量替换。
    /// 对标 Qoder/Cursor 的 search_replace 工具，支持跨文件批量操作。
    /// 当 executeReplace=false 时仅搜索计数；当 executeReplace=true 时执行替换。
    /// </summary>
    public string GlobalSearchReplace(string pattern, string replacement, string? fileGlob = null, bool executeReplace = false)
    {
        if (string.IsNullOrEmpty(_projectPath) || !Directory.Exists(_projectPath))
            return "{\"success\":false,\"error\":\"项目路径无效\"}";

        if (string.IsNullOrEmpty(pattern))
            return "{\"success\":false,\"error\":\"搜索模式不能为空\"}";

        try
        {
            var matches = new List<object>();
            int totalReplacements = 0;
            var files = string.IsNullOrEmpty(fileGlob)
                ? Directory.GetFiles(_projectPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("\\node_modules\\") && !f.Contains("\\.git\\")
                        && !f.Contains("\\bin\\") && !f.Contains("\\obj\\")
                        && !f.Contains("\\.aiide\\") && !f.Contains("\\bak"))
                : Directory.GetFiles(_projectPath, fileGlob, SearchOption.AllDirectories);

            foreach (var file in files.Take(200))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var escapedPattern = Regex.Escape(pattern);
                    var count = Regex.Matches(content, escapedPattern).Count;
                    if (count > 0)
                    {
                        if (executeReplace)
                        {
                            // 执行替换：使用简单字符串替换（与 SearchReplace 保持一致）
                            var newContent = content.Replace(pattern, replacement);
                            if (newContent != content)
                            {
                                // 使用 BackupService 统一备份（防止崩溃导致数据丢失）
                                _backup?.BackupContentChange(file, content);

                                SafeWriteAllText(file, newContent);
                                NotifyFileChanged(file);
                                var actualCount = (content.Length - newContent.Length) / Math.Max(1, pattern.Length - replacement.Length);
                                if (actualCount <= 0) actualCount = count;
                                totalReplacements += actualCount;
                                matches.Add(new
                                {
                                    file = Path.GetRelativePath(_projectPath, file),
                                    matches = count,
                                    replaced = actualCount
                                });
                            }
                        }
                        else
                        {
                            matches.Add(new { file = Path.GetRelativePath(_projectPath, file), matches = count });
                        }
                    }
                }
                catch (Exception ex) { LogService.Instance.Debug($"搜索替换异常: {ex.Message}", "FileOp"); }
            }

            if (matches.Count == 0)
                return $"{{\"success\":true,\"message\":\"未找到匹配: {Escape(pattern)}\",\"count\":0}}";

            if (executeReplace)
            {
                var warning = matches.Count >= 3
                    ? $"⚠️ 已批量修改 {matches.Count} 个文件（{totalReplacements} 处替换）。建议手动确认修改正确性，必要时用 git 回滚。"
                    : null;
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    pattern,
                    replacement,
                    executed = true,
                    totalFiles = matches.Count,
                    totalReplacements,
                    warning,
                    files = matches
                });
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                pattern,
                totalFiles = matches.Count,
                files = matches
            });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    /// <summary>模糊查找文件：按优先级尝试 basename+ext → basename → 部分路径匹配</summary>
    /// <returns>匹配的文件绝对路径列表，按相关度排序</returns>
    public List<string> SmartFindFile(string partialPath)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(partialPath) || !Directory.Exists(_projectPath))
            return results;

        var searchName = Path.GetFileName(partialPath);
        var searchNameNoExt = Path.GetFileNameWithoutExtension(partialPath);
        var searchExt = Path.GetExtension(partialPath)?.ToLowerInvariant() ?? "";
        var normalizedPartial = partialPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).ToLowerInvariant();

        try
        {
            var allFiles = Directory.GetFiles(_projectPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.") 
                    && !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}.bak"));

            var scored = new List<(string Path, int Score)>();
            foreach (var f in allFiles)
            {
                var fName = Path.GetFileName(f);
                var fNameNoExt = Path.GetFileNameWithoutExtension(f);
                var fExt = Path.GetExtension(f)?.ToLowerInvariant() ?? "";
                var relPath = Path.GetRelativePath(_projectPath, f).Replace('\\', '/').ToLowerInvariant();
                int score = 0;

                // 优先级1: basename+ext 精确匹配 (最高优先级)
                if (string.Equals(fName, searchName, StringComparison.OrdinalIgnoreCase))
                    score += 100;
                // 优先级2: basename 匹配（忽略扩展名）
                else if (!string.IsNullOrEmpty(searchNameNoExt) && 
                    string.Equals(fNameNoExt, searchNameNoExt, StringComparison.OrdinalIgnoreCase))
                    score += 75;
                // 优先级3: basename 包含搜索词
                else if (!string.IsNullOrEmpty(searchNameNoExt) && 
                    fNameNoExt.Contains(searchNameNoExt, StringComparison.OrdinalIgnoreCase))
                    score += 50;
                // 优先级4: 完整路径包含部分路径
                else if (!string.IsNullOrEmpty(normalizedPartial) && 
                    relPath.Contains(normalizedPartial, StringComparison.OrdinalIgnoreCase))
                    score += 30;
                // 优先级5: 文件名包含搜索词
                else if (!string.IsNullOrEmpty(searchNameNoExt) && 
                    fName.Contains(searchNameNoExt, StringComparison.OrdinalIgnoreCase))
                    score += 15;

                // 扩展名匹配加分
                if (!string.IsNullOrEmpty(searchExt) && string.Equals(fExt, searchExt, StringComparison.OrdinalIgnoreCase))
                    score += 10;

                if (score > 0)
                    scored.Add((f, score));
            }

            results = scored.OrderByDescending(s => s.Score).Take(10).Select(s => s.Path).ToList();
        }
        catch (Exception ex) { LogService.Instance.Debug($"模糊文件查找异常: {ex.Message}", "FileOp"); }

        return results;
    }

    // ==================== 移动/复制文件 ====================

    public string Move(string sourcePath, string destPath)
    {
        try
        {
            var src = EnsurePathInProject(sourcePath);
            var dest = EnsurePathInProject(destPath);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);
            if (File.Exists(src))
            {
                if (File.Exists(dest))
                    return $"{{\"success\":false,\"error\":\"目标文件已存在: {Escape(dest)}\"}}";
                // 移动前备份源文件和目标位置（若目标存在）
                _backup?.BackupBeforeWrite(src);
                _backup?.BackupBeforeWrite(dest);
                File.Move(src, dest);
            }
            else if (Directory.Exists(src))
            {
                if (Directory.Exists(dest))
                    return $"{{\"success\":false,\"error\":\"目标目录已存在: {Escape(dest)}\"}}";
                Directory.Move(src, dest);
            }
            else return $"{{\"success\":false,\"error\":\"源路径不存在: {Escape(src)}\"}}";
            NotifyFileChanged(src);
            NotifyFileChanged(dest);
            return $"{{\"success\":true,\"source\":\"{Escape(src)}\",\"dest\":\"{Escape(dest)}\"}}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    public string Copy(string sourcePath, string destPath)
    {
        try
        {
            var src = EnsurePathInProject(sourcePath);
            var dest = EnsurePathInProject(destPath);
            if (!File.Exists(src))
                return $"{{\"success\":false,\"error\":\"源文件不存在: {Escape(src)}\"}}";
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);
            File.Copy(src, dest, overwrite: false);
            NotifyFileChanged(dest);
            return $"{{\"success\":true,\"source\":\"{Escape(src)}\",\"dest\":\"{Escape(dest)}\"}}";
        }
        catch (IOException)
        {
            return "{\"success\":false,\"error\":\"目标文件已存在，如需覆盖请先删除\"}";
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    public string ReadMultipleFiles(string[] paths, int? maxLinesPerFile = null)
    {
        try
        {
            var results = new List<object>();
            foreach (var path in paths.Take(10))
            {
                var fp = ResolvePath(path);
                if (!File.Exists(fp)) { results.Add(new { file = path, error = "文件不存在" }); continue; }
                try
                {
                    var content = _fileService.ReadFile(fp);
                    if (maxLinesPerFile.HasValue && maxLinesPerFile > 0)
                    {
                        var lines = content.Split('\n');
                        if (lines.Length > maxLinesPerFile.Value)
                            content = string.Join("\n", lines.Take(maxLinesPerFile.Value)) + $"\n... (共 {lines.Length} 行)";
                    }
                    results.Add(new { file = Path.GetRelativePath(_projectPath, fp).Replace('\\', '/'), content, size = new FileInfo(fp).Length });
                }
                catch (Exception ex) { results.Add(new { file = path, error = ex.Message }); }
            }
            return JsonSerializer.Serialize(new { success = true, count = results.Count, files = results });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    // ==================== 高级目录操作 ====================

    /// <summary>递归列出目录内容（支持过滤和深度控制）</summary>
    public string ListDirectoryRecursive(string path, string? filter = null, int maxDepth = 3)
    {
        try
        {
            var rp = ResolvePath(path);
            if (!Directory.Exists(rp))
                return $"{{\"success\":false,\"error\":\"目录不存在: {Escape(rp)}\"}}";

            var results = new List<object>();
            ListDirRecursive(rp, rp, 0, maxDepth, filter, results);
            return JsonSerializer.Serialize(new { success = true, path = rp, depth = maxDepth, count = results.Count, items = results });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    private void ListDirRecursive(string root, string currentPath, int depth, int maxDepth, string? filter, List<object> results)
    {
        if (depth > maxDepth) return;
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(currentPath).OrderBy(e => e))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || name == "node_modules" || name == "bin" || name == "obj") continue;

                var isDir = Directory.Exists(entry);
                var relPath = Path.GetRelativePath(root, entry).Replace('\\', '/');

                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    if (!isDir) continue;
                }

                var item = new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["path"] = relPath,
                    ["type"] = isDir ? "dir" : "file"
                };
                if (!isDir)
                {
                    try { item["size"] = new FileInfo(entry).Length; } catch { }
                    try { item["ext"] = Path.GetExtension(name)?.ToLower() ?? ""; } catch { }
                }
                if (isDir && depth < maxDepth)
                {
                    var children = new List<object>();
                    ListDirRecursive(root, entry, depth + 1, maxDepth, filter, children);
                    if (children.Count > 0)
                        item["children"] = children;
                }
                results.Add(item);
            }
        }
        catch (Exception ex) { LogService.Instance.Debug($"递归列出目录跳过异常: {ex.Message}", "FileOp"); }
    }

    /// <summary>获取项目结构摘要（供 AI 快速理解项目布局）</summary>
    public string GetProjectStructure(int depth = 2, bool includeFiles = true)
    {
        try
        {
            var rp = _projectPath;
            if (string.IsNullOrEmpty(rp) || !Directory.Exists(rp))
                return "{\"success\":false,\"error\":\"项目路径无效\"}";

            var results = new List<object>();
            ScanDir(rp, rp, 0, depth, results);

            var fileCount = CountFiles(results);
            var dirCount = CountDirs(results);

            return JsonSerializer.Serialize(new
            {
                success = true,
                project = Path.GetFileName(rp),
                root = rp,
                depth,
                directoryCount = dirCount,
                fileCount,
                structure = results
            });
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"error\":\"{Escape(ex.Message)}\"}}";
        }
    }

    private static int CountFiles(List<object> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item is Dictionary<string, object> d)
            {
                if (d.TryGetValue("type", out var t) && t is string type)
                {
                    if (type == "file") count++;
                    if (d.TryGetValue("children", out var children) && children is List<object> childList)
                        count += CountFiles(childList);
                }
            }
        }
        return count;
    }

    private static int CountDirs(List<object> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item is Dictionary<string, object> d)
            {
                if (d.TryGetValue("type", out var t) && t is string type)
                {
                    if (type == "dir") count++;
                    if (d.TryGetValue("children", out var children) && children is List<object> childList)
                        count += CountDirs(childList);
                }
            }
        }
        return count;
    }

    // ==================== 工具方法 ====================

    private static string Escape(string s) => CommonUtils.EscapeForJson(s);
}
