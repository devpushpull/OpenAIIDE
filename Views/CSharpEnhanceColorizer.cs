using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace AIIDEWPF.Views;

/// <summary>C# 语法增强着色 —— 为 async/await/Task/var 等现代关键词增加自定义颜色</summary>
public class CSharpEnhanceColorizer : DocumentColorizingTransformer
{
    // 需要特殊着色的关键词列表
    private static readonly (string Keyword, Color Color)[] Enhancements =
    {
        ("async",    Color.FromRgb(86, 156, 214)),   // 蓝色
        ("await",    Color.FromRgb(86, 156, 214)),
        ("var",      Color.FromRgb(86, 156, 214)),
        ("Task",     Color.FromRgb(78, 201, 176)),    // 青色
        ("Task<",    Color.FromRgb(78, 201, 176)),
        ("ValueTask",Color.FromRgb(78, 201, 176)),
        ("string",   Color.FromRgb(78, 201, 176)),
        ("int",      Color.FromRgb(78, 201, 176)),
        ("bool",     Color.FromRgb(78, 201, 176)),
        ("void",     Color.FromRgb(78, 201, 176)),
        ("double",   Color.FromRgb(78, 201, 176)),
        ("float",    Color.FromRgb(78, 201, 176)),
        ("long",     Color.FromRgb(78, 201, 176)),
        ("decimal",  Color.FromRgb(78, 201, 176)),
        ("object",   Color.FromRgb(78, 201, 176)),
        ("dynamic",  Color.FromRgb(78, 201, 176)),
        ("record",   Color.FromRgb(216, 160, 223)),   // 紫色
        ("init",     Color.FromRgb(216, 160, 223)),
        ("required", Color.FromRgb(216, 160, 223)),
        ("sealed",   Color.FromRgb(216, 160, 223)),
        ("partial",  Color.FromRgb(216, 160, 223)),
        ("readonly", Color.FromRgb(216, 160, 223)),
    };

    private static readonly Regex WordRegex = new(
        @"\b(async|await|var|Task|ValueTask|record|init|required|sealed|partial|readonly|string|int|bool|void|double|float|long|decimal|object|dynamic)\b",
        RegexOptions.Compiled);

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        foreach (Match m in WordRegex.Matches(text))
        {
            var keyword = m.Value;
            var color = Colors.White;
            foreach (var (kw, clr) in Enhancements)
            {
                if (kw == keyword) { color = clr; break; }
            }
            if (color != Colors.White)
            {
                ChangeLinePart(
                    line.Offset + m.Index,
                    line.Offset + m.Index + m.Length,
                    element => element.TextRunProperties.SetForegroundBrush(
                        new SolidColorBrush(color)));
            }
        }
    }
}
