using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace AIIDEWPF.Views;

/// <summary>
/// AvalonEdit 背景渲染器 —— 在代码编辑器上绘制诊断波浪线（错误红色/警告黄色/提示灰色），
/// 对标 VS Code / Cursor 的实时错误显示体验。
/// </summary>
public class DiagnosticsMargin : IBackgroundRenderer
{
    private readonly ICSharpCode.AvalonEdit.TextEditor _editor;
    private Dictionary<string, List<Services.DiagnosticItem>> _diagnostics = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetDiagnostics(Dictionary<string, List<Services.DiagnosticItem>> diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public DiagnosticsMargin(ICSharpCode.AvalonEdit.TextEditor editor)
    {
        _editor = editor;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!_diagnostics.TryGetValue(_editor.Document.FileName ?? "", out var items))
            return;

        textView.EnsureVisualLines();

        foreach (var diag in items)
        {
            DrawDiagnostic(drawingContext, textView, diag);
        }
    }

    private static void DrawDiagnostic(DrawingContext dc, TextView textView, Services.DiagnosticItem diag)
    {
        if (diag.EndLine < diag.StartLine) return;
        if (diag.EndLine == diag.StartLine && diag.EndColumn <= diag.StartColumn) return;

        // 获取起始和结束位置的可视矩形
        var startLoc = textView.Document.GetLocation(
            textView.Document.GetOffset(diag.StartLine, diag.StartColumn));
        var endLoc = textView.Document.GetLocation(
            textView.Document.GetOffset(diag.EndLine, diag.EndColumn));

        var segStart = new TextViewPosition(startLoc);
        var segEnd = new TextViewPosition(endLoc);

        // 获取起始位置的可视矩形
        var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView,
            new TextSegment { StartOffset = textView.Document.GetOffset(diag.StartLine, diag.StartColumn),
                              Length = 1 });

        if (!rects.Any()) return;

        // 计算错误范围的总矩形
        var startOffset = textView.Document.GetOffset(diag.StartLine, diag.StartColumn);
        var endOffset = textView.Document.GetOffset(diag.EndLine, diag.EndColumn);
        var fullRects = BackgroundGeometryBuilder.GetRectsForSegment(textView,
            new TextSegment { StartOffset = startOffset, Length = Math.Max(1, endOffset - startOffset) });

        var color = diag.Severity switch
        {
            Services.DiagnosticSeverity.Error => Color.FromRgb(0xF4, 0x47, 0x47),    // 红色
            Services.DiagnosticSeverity.Warning => Color.FromRgb(0xCC, 0xA7, 0x00),   // 黄色
            Services.DiagnosticSeverity.Info => Color.FromRgb(0x57, 0x9B, 0xFF),      // 蓝色
            Services.DiagnosticSeverity.Hint => Color.FromRgb(0x80, 0x80, 0x80),      // 灰色
            _ => Color.FromRgb(0xCC, 0xA7, 0x00)
        };

        var pen = new Pen(new SolidColorBrush(color), 1.5);
        pen.Freeze();

        // 对于多行诊断，在上面的行画到行末，最后一行画到列位置
        if (diag.StartLine == diag.EndLine)
        {
            // 单行诊断：画波浪线
            DrawSquigglyLineOnRects(dc, pen, fullRects);
        }
        else
        {
            // 多行诊断：每行画波浪线
            DrawMultiLineSquiggly(dc, textView, pen, diag);
        }
    }

    private static void DrawSquigglyLineOnRects(DrawingContext dc, Pen pen, IEnumerable<Rect> rects)
    {
        foreach (var r in rects)
        {
            var y = r.Bottom - 1.5;
            var x = r.Left;
            var width = r.Width;
            const double waveLength = 4;
            const double amplitude = 2.5;

            var figure = new PathFigure { StartPoint = new Point(x, y) };
            double t = 0;
            while (t < width)
            {
                var segLen = Math.Min(waveLength / 2, width - t);
                var up = (int)(t / (waveLength / 2)) % 2 == 0;
                var dy = up ? -amplitude : amplitude;
                figure.Segments.Add(new LineSegment(new Point(x + t + segLen, y + dy), true));
                t += segLen;
            }

            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private static void DrawMultiLineSquiggly(DrawingContext dc, TextView textView, Pen pen,
        Services.DiagnosticItem diag)
    {
        // 第一行：从起始列到行末
        var firstLine = textView.Document.GetLineByNumber(diag.StartLine);
        var firstStartOff = textView.Document.GetOffset(diag.StartLine, diag.StartColumn);
        var firstSegRects = BackgroundGeometryBuilder.GetRectsForSegment(textView,
            new TextSegment { StartOffset = firstStartOff, Length = Math.Max(1, firstLine.EndOffset - firstStartOff) });
        DrawSquigglyLineOnRects(dc, pen, firstSegRects);

        // 中间行：整行
        for (int line = diag.StartLine + 1; line < diag.EndLine; line++)
        {
            var docLine = textView.Document.GetLineByNumber(line);
            var midRects = BackgroundGeometryBuilder.GetRectsForSegment(textView,
                new TextSegment { StartOffset = docLine.Offset, Length = Math.Max(1, docLine.Length) });
            DrawSquigglyLineOnRects(dc, pen, midRects);
        }

        // 最后一行：从行首到结束列
        var lastLine = textView.Document.GetLineByNumber(diag.EndLine);
        var lastEndOff = textView.Document.GetOffset(diag.EndLine, diag.EndColumn);
        var lastSegRects = BackgroundGeometryBuilder.GetRectsForSegment(textView,
            new TextSegment { StartOffset = lastLine.Offset, Length = Math.Max(1, lastEndOff - lastLine.Offset) });
        DrawSquigglyLineOnRects(dc, pen, lastSegRects);
    }
}
