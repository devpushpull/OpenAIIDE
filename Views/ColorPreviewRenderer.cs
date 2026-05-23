using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace AIIDEWPF.Views;

/// <summary>
/// 颜色预览渲染器 —— 在编辑器中检测颜色代码（#hex/rgb/rgba/hsl/hsla），
/// 在内联位置绘制颜色方块，鼠标悬停显示颜色详情面板。
/// 参考 VS Code editor.colorDecorators 功能实现。
/// </summary>
public class ColorPreviewRenderer : IBackgroundRenderer
{
    private readonly TextArea _textArea;
    private readonly TextView _textView;
    private Popup? _activePopup;
    
    // 当前可见的颜色匹配信息（行号 → 颜色匹配列表）
    private readonly Dictionary<int, List<ColorMatch>> _visibleMatches = new();

    public KnownLayer Layer => KnownLayer.Background;

    public ColorPreviewRenderer(TextArea textArea)
    {
        _textArea = textArea;
        _textView = textArea.TextView;
        _textView.ScrollOffsetChanged += (s, e) => _textView.InvalidateVisual();
        _textView.VisualLinesChanged += (s, e) => _textView.InvalidateVisual();
        _textView.MouseHover += OnMouseHover;
        _textView.MouseHoverStopped += OnMouseHoverStopped;
        _textView.MouseMove += OnMouseMove;
    }

    /// <summary>颜色匹配正则：支持 #RGB, #RRGGBB, #RRGGBBAA, rgb(), rgba(), hsl(), hsla()</summary>
    private static readonly Regex ColorRegex = new(
        @"(?:#([0-9a-fA-F]{3,8})\b)" +
        @"|(?:rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\))" +
        @"|(?:rgba\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*([\d.]+)\s*\))" +
        @"|(?:hsl\(\s*(\d{1,3})\s*,\s*(\d{1,3})%?\s*,\s*(\d{1,3})%?\s*\))" +
        @"|(?:hsla\(\s*(\d{1,3})\s*,\s*(\d{1,3})%?\s*,\s*(\d{1,3})%?\s*,\s*([\d.]+)\s*\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;

        _visibleMatches.Clear();
        var doc = _textArea.Document;
        if (doc == null) return;

        foreach (var visualLine in textView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine;
            var lineText = doc.GetText(line.Offset, line.Length);
            var matches = ColorRegex.Matches(lineText);
            var lineMatches = new List<ColorMatch>();

            foreach (Match match in matches)
            {
                Color? color = null;
                string displayText = match.Value;

                // #hex 格式
                if (match.Groups[1].Success)
                {
                    color = ParseHexColor(match.Groups[1].Value);
                }
                // rgb() 格式
                else if (match.Groups[2].Success)
                {
                    if (int.TryParse(match.Groups[2].Value, out int r) &&
                        int.TryParse(match.Groups[3].Value, out int g) &&
                        int.TryParse(match.Groups[4].Value, out int b))
                        color = Color.FromRgb(Clamp(r), Clamp(g), Clamp(b));
                }
                // rgba() 格式
                else if (match.Groups[5].Success)
                {
                    if (int.TryParse(match.Groups[5].Value, out int r) &&
                        int.TryParse(match.Groups[6].Value, out int g) &&
                        int.TryParse(match.Groups[7].Value, out int b) &&
                        float.TryParse(match.Groups[8].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
                        color = Color.FromArgb(ClampAlpha(a), Clamp(r), Clamp(g), Clamp(b));
                }
                // hsl() 格式
                else if (match.Groups[9].Success)
                {
                    if (int.TryParse(match.Groups[9].Value, out int h) &&
                        int.TryParse(match.Groups[10].Value, out int s) &&
                        int.TryParse(match.Groups[11].Value, out int l))
                        color = HslToRgb(h, s / 100f, l / 100f);
                }
                // hsla() 格式
                else if (match.Groups[12].Success)
                {
                    if (int.TryParse(match.Groups[12].Value, out int h) &&
                        int.TryParse(match.Groups[13].Value, out int s) &&
                        int.TryParse(match.Groups[14].Value, out int l) &&
                        float.TryParse(match.Groups[15].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
                    {
                        var c = HslToRgb(h, s / 100f, l / 100f);
                        color = Color.FromArgb(ClampAlpha(a), c.R, c.G, c.B);
                    }
                }

                if (color == null) continue;

                // 计算内联绘制的几何位置
                var startOffset = line.Offset + match.Index;
                var endOffset = startOffset + match.Length;
                var startLoc = doc.GetLocation(startOffset);
                var endLoc = doc.GetLocation(endOffset);

                // 获取可视矩形
                var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView,
                    new TextSegment { StartOffset = startOffset, Length = match.Length });

                if (rects.Any())
                {
                    var firstRect = rects.First();
                    // 在文本左侧绘制颜色方块（14x14 像素）
                    var squareSize = Math.Min(14, firstRect.Height - 2);
                    var squareX = firstRect.X - squareSize - 4;
                    var squareY = firstRect.Y + (firstRect.Height - squareSize) / 2;

                    // 绘制颜色方块边框
                    var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), 1);
                    drawingContext.DrawRectangle(null, pen,
                        new Rect(squareX, squareY, squareSize, squareSize));

                    // 绘制颜色填充
                    var brush = new SolidColorBrush(color.Value);
                    brush.Freeze();
                    drawingContext.DrawRectangle(brush, null,
                        new Rect(squareX + 1, squareY + 1, squareSize - 2, squareSize - 2));

                    lineMatches.Add(new ColorMatch
                    {
                        Color = color.Value,
                        DisplayText = displayText,
                        Bounds = new Rect(squareX, squareY, squareSize, squareSize),
                        LineNumber = line.LineNumber,
                        StartOffset = startOffset,
                        Length = match.Length
                    });
                }
            }

            if (lineMatches.Count > 0)
                _visibleMatches[line.LineNumber] = lineMatches;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        // 如果鼠标移出颜色方块区域，关闭弹窗
        if (_activePopup != null && _activePopup.IsOpen)
        {
            var mousePos = e.GetPosition(_textView);
            // 检查鼠标是否仍在任何颜色方块上
            bool overAny = false;
            foreach (var kvp in _visibleMatches)
            {
                foreach (var m in kvp.Value)
                {
                    if (m.Bounds.Contains(mousePos))
                    {
                        overAny = true;
                        break;
                    }
                }
                if (overAny) break;
            }
            if (!overAny)
                ClosePopup();
        }
    }

    private void OnMouseHover(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_textView);
        var pos2 = _textView.GetPosition(pos);
        if (pos2 == null) return;

        var line = pos2.Value.Line;
        if (_visibleMatches.TryGetValue(line, out var matches))
        {
            foreach (var match in matches)
            {
                if (match.Bounds.Contains(pos))
                {
                    ShowColorPopup(match, pos);
                    return;
                }
            }
        }
    }

    private void OnMouseHoverStopped(object sender, MouseEventArgs e) => ClosePopup();

    private void ShowColorPopup(ColorMatch match, Point screenPos)
    {
        ClosePopup();

        var color = match.Color;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (color.A < 255)
            hex += $"{color.A:X2}";

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10)
        };

        var stack = new StackPanel();

        // 大颜色预览块
        var preview = new Border
        {
            Width = 80,
            Height = 40,
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(preview);

        // 颜色信息
        stack.Children.Add(CreateInfoRow("HEX", hex));
        stack.Children.Add(CreateInfoRow("RGB", $"{color.R}, {color.G}, {color.B}"));
        if (color.A < 255)
            stack.Children.Add(CreateInfoRow("Alpha", $"{color.A} ({color.A / 255f:P0})"));

        // HSL
        var (h, s, l) = RgbToHsl(color);
        stack.Children.Add(CreateInfoRow("HSL", $"{h:F0}°, {s:P0}, {l:P0}"));

        border.Child = stack;

        _activePopup = new Popup
        {
            Child = border,
            PlacementTarget = _textView,
            Placement = PlacementMode.Relative,
            HorizontalOffset = screenPos.X + 16,
            VerticalOffset = screenPos.Y - 20,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade
        };
        _activePopup.IsOpen = true;
    }

    private static FrameworkElement CreateInfoRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelTb = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            Margin = new Thickness(0, 1, 8, 1)
        };
        Grid.SetColumn(labelTb, 0);
        grid.Children.Add(labelTb);

        var valueTb = new TextBlock
        {
            Text = value,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4)),
            Margin = new Thickness(0, 1, 0, 1)
        };
        Grid.SetColumn(valueTb, 1);
        grid.Children.Add(valueTb);

        return grid;
    }

    private void ClosePopup()
    {
        if (_activePopup != null)
        {
            _activePopup.IsOpen = false;
            _activePopup = null;
        }
    }

    // ===== 颜色解析工具方法 =====

    private static Color? ParseHexColor(string hex)
    {
        try
        {
            return hex.Length switch
            {
                3 => Color.FromRgb(
                    (byte)(Convert.ToByte(hex.Substring(0, 1), 16) * 17),
                    (byte)(Convert.ToByte(hex.Substring(1, 1), 16) * 17),
                    (byte)(Convert.ToByte(hex.Substring(2, 1), 16) * 17)),
                6 => Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(hex.Substring(6, 2), 16),
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)),
                _ => null
            };
        }
        catch { return null; }
    }

    private static byte Clamp(int value) => (byte)Math.Max(0, Math.Min(255, value));
    private static byte ClampAlpha(float value) => (byte)Math.Max(0, Math.Min(255, (int)(value * 255)));

    private static Color HslToRgb(int h360, float s, float l)
    {
        var h = (h360 % 360) / 360f;
        float r, g, b;

        if (Math.Abs(s) < 0.001f)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1f / 3f);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1f / 3f);
        }

        return Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6 * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
        return p;
    }

    private static (double h, double s, double l) RgbToHsl(Color c)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;

        var l = (max + min) / 2;
        double h = 0;
        var s = d < 0.001 ? 0 : l > 0.5 ? d / (2 - max - min) : d / (max + min);

        if (d >= 0.001)
        {
            if (Math.Abs(max - r) < 0.001)
                h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
            else if (Math.Abs(max - g) < 0.001)
                h = ((b - r) / d + 2) / 6;
            else
                h = ((r - g) / d + 4) / 6;
        }

        return (h * 360, s, l);
    }

    /// <summary>内部颜色匹配数据结构</summary>
    private class ColorMatch
    {
        public Color Color { get; init; }
        public string DisplayText { get; init; } = "";
        public Rect Bounds { get; init; }
        public int LineNumber { get; init; }
        public int StartOffset { get; init; }
        public int Length { get; init; }
    }
}
