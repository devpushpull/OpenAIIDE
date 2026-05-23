using System.Text;

namespace AIIDEWPF.Services;

/// <summary>Diff 行 —— 对应一行代码的差异状态</summary>
public enum DiffLineType { Unchanged, Added, Removed }

/// <summary>单行差异</summary>
public class DiffLine
{
    public int LineNumber { get; set; }
    public string Text { get; set; } = "";
    public DiffLineType Type { get; set; }
    public int OldLineNumber { get; set; }
    public int NewLineNumber { get; set; }
}

/// <summary>一个差异块</summary>
public class DiffHunk
{
    public List<DiffLine> Lines { get; set; } = new();
    public int OldStart { get; set; }
    public int NewStart { get; set; }
    public bool HasChanges => Lines.Any(l => l.Type != DiffLineType.Unchanged);
}

/// <summary>Diff 计算结果</summary>
public class DiffResult
{
    public List<DiffHunk> Hunks { get; set; } = new();
    public int AddedLines => Hunks.Sum(h => h.Lines.Count(l => l.Type == DiffLineType.Added));
    public int RemovedLines => Hunks.Sum(h => h.Lines.Count(l => l.Type == DiffLineType.Removed));
    public bool HasChanges => Hunks.Any(h => h.HasChanges);
}

/// <summary>
/// 代码差异计算服务 —— 基于 LCS 算法的行级差异
/// 对标 Qoder / 通义灵码 Diff 视图
/// </summary>
public static class DiffService
{
    /// <summary>计算两段文本的行级差异</summary>
    public static DiffResult ComputeDiff(string? oldText, string? newText)
    {
        oldText ??= "";
        newText ??= "";

        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var result = new DiffResult();
        var edits = ComputeEdits(oldLines, newLines);

        // 将编辑操作聚合成 Hunks（每个 hunk 包含前后 3 行上下文）
        var contextSize = 3;
        var hunk = new DiffHunk { OldStart = 1, NewStart = 1 };
        var afterEq = 0;

        for (int i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];

            switch (edit.Type)
            {
                case DiffLineType.Unchanged:
                    if (hunk.Lines.Count > 0 && afterEq < contextSize)
                    {
                        var ctx = new DiffLine
                        {
                            LineNumber = edit.NewIndex + 1,
                            Text = edit.Text,
                            Type = DiffLineType.Unchanged,
                            OldLineNumber = edit.OldIndex + 1,
                            NewLineNumber = edit.NewIndex + 1
                        };
                        hunk.Lines.Add(ctx);
                        afterEq++;
                    }
                    else if (hunk.Lines.Count == 0)
                    {
                        // 跳过前面的不变行
                    }
                    else
                    {
                        afterEq++;
                        if (afterEq >= contextSize && hunk.HasChanges)
                        {
                            result.Hunks.Add(hunk);
                            hunk = new DiffHunk
                            {
                                OldStart = edit.OldIndex + 1,
                                NewStart = edit.NewIndex + 1
                            };
                            afterEq = 0;
                        }
                    }
                    break;

                case DiffLineType.Added:
                case DiffLineType.Removed:
                    afterEq = 0;
                    if (hunk.Lines.Count == 0)
                    {
                        // 添加前置上下文
                        var ctxStart = Math.Max(0, edit.OldIndex - contextSize);
                        for (int c = ctxStart; c < edit.OldIndex; c++)
                        {
                            if (c >= oldLines.Length) break;
                            bool alreadyInHunk = hunk.Lines.Any(l => l.OldLineNumber == c + 1);
                            if (!alreadyInHunk)
                            {
                                hunk.Lines.Add(new DiffLine
                                {
                                    LineNumber = c + 1,
                                    Text = oldLines[c],
                                    Type = DiffLineType.Unchanged,
                                    OldLineNumber = c + 1,
                                    NewLineNumber = c + 1
                                });
                            }
                        }
                    }
                    hunk.Lines.Add(new DiffLine
                    {
                        LineNumber = edit.NewIndex + 1,
                        Text = edit.Text,
                        Type = edit.Type,
                        OldLineNumber = edit.OldIndex + 1,
                        NewLineNumber = edit.NewIndex + 1
                    });
                    break;
            }
        }

        if (hunk.HasChanges)
            result.Hunks.Add(hunk);

        return result;
    }

    /// <summary>生成 Unified Diff 格式的字符串</summary>
    public static string ToUnifiedDiff(DiffResult diff, string oldPath = "a/file", string newPath = "b/file")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- {oldPath}");
        sb.AppendLine($"+++ {newPath}");

        foreach (var hunk in diff.Hunks)
        {
            var oldStart = hunk.Lines.Where(l => l.Type != DiffLineType.Added).FirstOrDefault()?.OldLineNumber ?? hunk.OldStart;
            var oldCount = hunk.Lines.Count(l => l.Type != DiffLineType.Added);
            var newStart = hunk.Lines.Where(l => l.Type != DiffLineType.Removed).FirstOrDefault()?.NewLineNumber ?? hunk.NewStart;
            var newCount = hunk.Lines.Count(l => l.Type != DiffLineType.Removed);

            sb.AppendLine($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");

            foreach (var line in hunk.Lines)
            {
                var prefix = line.Type switch
                {
                    DiffLineType.Added => '+',
                    DiffLineType.Removed => '-',
                    _ => ' '
                };
                sb.AppendLine($"{prefix}{line.Text}");
            }
        }

        return sb.ToString();
    }

    // ---- 内部实现 ----

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private struct LineEdit
    {
        public DiffLineType Type;
        public string Text;
        public int OldIndex;
        public int NewIndex;
    }

    /// <summary>LCS 算法计算编辑序列</summary>
    private static List<LineEdit> ComputeEdits(string[] oldLines, string[] newLines)
    {
        var m = oldLines.Length;
        var n = newLines.Length;

        // 计算 LCS 表
        var dp = new int[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                if (oldLines[i - 1] == newLines[j - 1])
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);

        // 回溯生成编辑序列
        var edits = new List<LineEdit>();
        int oi = m, ni = n;

        var temp = new List<LineEdit>();

        while (oi > 0 || ni > 0)
        {
            if (oi > 0 && ni > 0 && oldLines[oi - 1] == newLines[ni - 1])
            {
                temp.Add(new LineEdit
                {
                    Type = DiffLineType.Unchanged,
                    Text = oldLines[oi - 1],
                    OldIndex = oi - 1,
                    NewIndex = ni - 1
                });
                oi--; ni--;
            }
            else if (ni > 0 && (oi == 0 || dp[oi, ni - 1] >= dp[oi - 1, ni]))
            {
                temp.Add(new LineEdit
                {
                    Type = DiffLineType.Added,
                    Text = newLines[ni - 1],
                    OldIndex = oi,
                    NewIndex = ni - 1
                });
                ni--;
            }
            else
            {
                temp.Add(new LineEdit
                {
                    Type = DiffLineType.Removed,
                    Text = oldLines[oi - 1],
                    OldIndex = oi - 1,
                    NewIndex = ni
                });
                oi--;
            }
        }

        temp.Reverse();
        return temp;
    }
}
