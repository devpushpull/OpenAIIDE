using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AIIDEWPF.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace AIIDEWPF.Views;

/// <summary>
/// AvalonEdit 断点装订线 —— 显示断点红点、当前调试行黄色高亮、点击切换断点
/// </summary>
public class BreakpointMargin : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly DebugService _debugService;

    // 当前调试行高亮颜色
    private static readonly SolidColorBrush DebugLineBrush = new(Color.FromArgb(40, 255, 255, 0));
    private static readonly Pen DebugLineBorder = new(new SolidColorBrush(Color.FromRgb(200, 180, 0)), 1);

    public KnownLayer Layer => KnownLayer.Background;

    public BreakpointMargin(TextEditor editor, DebugService debugService)
    {
        _editor = editor;
        _debugService = debugService;

        // 监听鼠标左键点击左侧装订线区域
        _editor.TextArea.PreviewMouseLeftButtonDown += OnGutterClick;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!_editor.TextArea.IsVisible) return;
        if (textView.VisualLines.Count == 0) return;

        var lineNumberMargin = _editor.TextArea.LeftMargins
            .FirstOrDefault(m => m is LineNumberMargin);
        var lineNumberWidth = lineNumberMargin != null ? 40.0 : 50.0;
        var fileName = textView.Document.FileName ?? "";
        var isDebugFile = _debugService.IsRunning &&
            string.Equals(_debugService.CurrentFile, fileName, StringComparison.OrdinalIgnoreCase);
        var debugLine = isDebugFile ? _debugService.CurrentLine : -1;

        foreach (var visualLine in textView.VisualLines)
        {
            var docLine = visualLine.FirstDocumentLine;
            var lineNumber = docLine.LineNumber;

            // === 调试当前行黄色高亮 ===
            if (lineNumber == debugLine)
            {
                var bgRect = new Rect(
                    visualLine.GetTextLineVisualXPosition(visualLine.TextLines[0], 0),
                    visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineTop),
                    textView.ActualWidth,
                    visualLine.Height);
                drawingContext.DrawRectangle(DebugLineBrush, DebugLineBorder, bgRect);

                // 行号左侧黄色箭头
                var arrowY = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineTop);
                var arrowX = lineNumberWidth + 4;
                var arrowBrush = new SolidColorBrush(Color.FromRgb(240, 200, 0));
                var arrowPen = new Pen(new SolidColorBrush(Color.FromRgb(180, 140, 0)), 1.5);
                var center = new Point(arrowX + 5, arrowY + visualLine.Height / 2.0);
                drawingContext.DrawEllipse(arrowBrush, arrowPen, center, 5, 5);
            }

            // === 断点红点 ===
            if (_debugService.HasBreakpoint(fileName, lineNumber))
            {
                var y = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineTop);
                var x = lineNumberWidth + 6;

                var center = new Point(x, y + visualLine.Height / 2.0);
                var radius = 6.0;

                // 条件断点用橙色
                var hasCondition = _debugService.GetBreakpointCondition(fileName, lineNumber) != null;
                var hasHitCount = _debugService.BreakpointHitCounts.TryGetValue((fileName, lineNumber), out var hc) && hc > 0;
                var brush = hasCondition || hasHitCount
                    ? new SolidColorBrush(Color.FromRgb(240, 150, 30))  // 橙色 = 条件断点
                    : new SolidColorBrush(Color.FromRgb(220, 50, 50));  // 红色 = 普通断点
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(180, 30, 30)), 1);

                drawingContext.DrawEllipse(brush, pen, center, radius, radius);

                // 条件标记: "?" 或 "#"
                if (hasCondition || hasHitCount)
                {
                    var markerText = hasCondition ? "?" : "#";
                    var ft = new FormattedText(markerText,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                        8,
                        Brushes.White,
                        1.0);
                    var textX = center.X - ft.Width / 2.0;
                    var textY = center.Y - ft.Height / 2.0;
                    drawingContext.DrawText(ft, new Point(textX, textY));
                }

                // 如果同一行也是调试当前行，红色圆点叠加黄色边框
                if (lineNumber == debugLine)
                {
                    var hlPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 200, 0)), 2);
                    drawingContext.DrawEllipse(null, hlPen, center, radius + 1.5, radius + 1.5);
                }
            }
        }
    }

    private void OnGutterClick(object sender, MouseButtonEventArgs e)
    {
        if (!_editor.TextArea.IsVisible) return;

        var pos = e.GetPosition(_editor.TextArea.TextView);
        var lineNumberWidth = _editor.TextArea.LeftMargins
            .FirstOrDefault(m => m is LineNumberMargin) != null ? 40.0 : 50.0;
        var gutterWidth = lineNumberWidth + 18; // 行号宽度 + 断点图标空间

        // 点击必须在左侧装订线区域内
        if (pos.X < 0 || pos.X > gutterWidth) return;

        var textView = _editor.TextArea.TextView;
        var visualLine = textView.GetVisualLineFromVisualTop(pos.Y);
        if (visualLine == null) return;

        var docLine = visualLine.FirstDocumentLine;
        var lineNumber = docLine.LineNumber;
        var filePath = textView.Document.FileName ?? "";
        if (string.IsNullOrEmpty(filePath)) return;

        // 切换断点
        _debugService.ToggleBreakpoint(filePath, lineNumber);
        e.Handled = true;

        // 强制重绘装订线
        textView.InvalidateLayer(KnownLayer.Background);
    }
}
