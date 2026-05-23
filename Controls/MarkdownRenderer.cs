using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AIIDEWPF.Controls;

/// <summary>简易 Markdown → WPF FlowDocument 渲染器（支持代码块、粗体、内联代码、标题）</summary>
public static class MarkdownRenderer
{
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(0xd4, 0xd4, 0xd4));
    private static readonly SolidColorBrush CodeBgBrush = new(Color.FromRgb(0x1e, 0x1e, 0x1e));
    private static readonly SolidColorBrush CodeFgBrush = new(Color.FromRgb(0xce, 0x91, 0x78));
    private static readonly SolidColorBrush BoldBrush = new(Color.FromRgb(0xe0, 0xe0, 0xe0));
    private static readonly SolidColorBrush HeaderBrush = new(Color.FromRgb(0x4e, 0xc9, 0xb0));

    public static FlowDocument Render(string markdown)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Consolas, Microsoft YaHei, sans-serif"), FontSize = 13 };
        if (string.IsNullOrEmpty(markdown))
        {
            doc.Blocks.Add(new Paragraph(new Run("")));
            return doc;
        }

        // 先按代码块分割
        var parts = Regex.Split(markdown, @"```(\w*)\n?");
        bool inCodeBlock = false;
        string codeLang = "";

        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1) // 语言标记
            {
                codeLang = parts[i];
                continue;
            }

            if (inCodeBlock)
            {
                // 代码块内容
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    var codePara = new Paragraph
                    {
                        Background = CodeBgBrush,
                        Margin = new Thickness(0, 4, 0, 4),
                        Padding = new Thickness(8, 4, 8, 4),
                        FontFamily = new FontFamily("Consolas, monospace"),
                        FontSize = 12,
                    };
                    codePara.Inlines.Add(new Run(parts[i].TrimEnd()) { Foreground = CodeFgBrush });
                    doc.Blocks.Add(codePara);
                }
                inCodeBlock = false;
            }
            else
            {
                // 普通文本块
                RenderTextBlock(doc, parts[i]);
                // 下一个是代码块
                if (i + 1 < parts.Length && i % 2 == 0)
                    inCodeBlock = true;
            }
        }

        return doc;
    }

    private static void RenderTextBlock(FlowDocument doc, string text)
    {
        var lines = text.Split('\n');
        Paragraph? currentPara = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // 空行 → 新段落
            if (string.IsNullOrEmpty(trimmed))
            {
                currentPara = null;
                continue;
            }

            // 标题
            if (trimmed.StartsWith("### "))
            {
                currentPara = new Paragraph { Margin = new Thickness(0, 8, 0, 2) };
                currentPara.Inlines.Add(new Bold(new Run(trimmed[4..]) { Foreground = HeaderBrush, FontSize = 14 }));
                doc.Blocks.Add(currentPara);
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                currentPara = new Paragraph { Margin = new Thickness(0, 10, 0, 4) };
                currentPara.Inlines.Add(new Bold(new Run(trimmed[3..]) { Foreground = HeaderBrush, FontSize = 15 }));
                doc.Blocks.Add(currentPara);
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                currentPara = new Paragraph { Margin = new Thickness(0, 12, 0, 6) };
                currentPara.Inlines.Add(new Bold(new Run(trimmed[2..]) { Foreground = HeaderBrush, FontSize = 17 }));
                doc.Blocks.Add(currentPara);
                continue;
            }

            // 无序列表
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                currentPara = new Paragraph { Margin = new Thickness(16, 1, 0, 1) };
                RenderInline(currentPara, "• " + trimmed[2..]);
                doc.Blocks.Add(currentPara);
                continue;
            }

            // 普通段落
            if (currentPara == null)
            {
                currentPara = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                doc.Blocks.Add(currentPara);
            }
            else
            {
                currentPara.Inlines.Add(new LineBreak());
            }

            RenderInline(currentPara, line);
        }
    }

    private static void RenderInline(Paragraph para, string text)
    {
        // 粗体 **text**
        var boldRegex = new Regex(@"\*\*(.+?)\*\*");
        // 内联代码 `code`
        var codeRegex = new Regex(@"`([^`]+)`");

        int lastIdx = 0;
        // 简化：交替匹配粗体和内联代码
        var matches = new List<(int Index, int Length, bool IsBold, string Content)>();
        foreach (Match m in boldRegex.Matches(text))
            matches.Add((m.Index, m.Length, true, m.Groups[1].Value));
        foreach (Match m in codeRegex.Matches(text))
            matches.Add((m.Index, m.Length, false, m.Groups[1].Value));
        matches.Sort((a, b) => a.Index.CompareTo(b.Index));

        foreach (var (idx, len, isBold, content) in matches)
        {
            if (idx < lastIdx) continue; // skip overlapping
            // 添加前面的普通文本
            if (idx > lastIdx)
                para.Inlines.Add(new Run(text[lastIdx..idx]) { Foreground = TextBrush });

            if (isBold)
                para.Inlines.Add(new Bold(new Run(content) { Foreground = BoldBrush }));
            else
                para.Inlines.Add(new Run(content)
                {
                    Foreground = CodeFgBrush,
                    FontFamily = new FontFamily("Consolas, monospace"),
                    Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d))
                });

            lastIdx = idx + len;
        }

        // 剩余文本
        if (lastIdx < text.Length)
            para.Inlines.Add(new Run(text[lastIdx..]) { Foreground = TextBrush });
    }
}
