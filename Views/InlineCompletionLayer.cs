using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AIIDEWPF.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace AIIDEWPF.Views;

/// <summary>AvalonEdit 幽灵文本补全层 —— 支持 FIM、智能上下文、注释跳过、缓存加速</summary>
public class InlineCompletionLayer : Canvas
{
    private readonly TextEditor _editor;
    private readonly CodeCompletionService _service;
    private readonly DispatcherTimer _debounceTimer;
    private string _currentSuggestion = "";
    private bool _isVisible;
    private int _lastRequestCaretOffset = -1;
    private string? _cachedFileContent;
    /// <summary>上一条补全的缓存键，用于避免重复请求</summary>
    private string? _lastCacheKey;

    public InlineCompletionLayer(TextEditor editor, CodeCompletionService service)
    {
        _editor = editor;
        _service = service;

        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        _editor.TextArea.TextView.InsertLayer(this, KnownLayer.Text, LayerInsertionPosition.Above);

        // 防抖定时器（动态间隔）
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(service.DebounceMs) };
        _debounceTimer.Tick += OnDebounceTick;

        // 监听文本变化（只在暂停输入后触发）
        _editor.TextArea.Caret.PositionChanged += OnCaretChanged;
        _editor.TextArea.PreviewKeyDown += OnKeyDown;
        _editor.TextArea.PreviewTextInput += OnTextInput;
        _editor.TextArea.LostFocus += (s, e) => HideSuggestion();

        // 补全回调
        _service.OnCompletionReady += OnCompletionReady;
        _service.OnCompletionCancelled += () => Dispatcher.Invoke(HideSuggestion);
    }

    private void OnCaretChanged(object? sender, EventArgs e)
    {
        var currentOffset = _editor.TextArea.Caret.Offset;

        // 光标不在同一位置或还没请求过 → 重新启动防抖
        if (currentOffset != _lastRequestCaretOffset)
        {
            HideSuggestion();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnTextInput(object? sender, TextCompositionEventArgs e)
    {
        HideSuggestion();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isVisible) return;

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            AcceptSuggestion();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideSuggestion();
        }
        else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+→ 逐词接受（接受第一个单词）
            e.Handled = true;
            AcceptWord();
        }
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        RequestCompletion();
    }

    public async void RequestCompletion()
    {
        if (!_service.Enabled) return;
        if (string.IsNullOrEmpty(_editor.Document?.Text)) return;

        var caret = _editor.TextArea.Caret.Offset;
        if (caret <= 0) return;

        var filePath = _editor.Document.FileName ?? "untitled";
        var language = GuessLanguage(filePath);
        var fullText = _editor.Document.Text;

        // === 跳过注释/字符串中的补全 ===
        var prefixStart = Math.Max(0, caret - _service.MaxPrefixChars);
        var codeBeforeCursor = fullText[prefixStart..caret];

        if (CodeCompletionService.IsCursorInCommentOrString(codeBeforeCursor, language))
            return;

        _cachedFileContent = fullText;
        _lastRequestCaretOffset = caret;

        // === FIM: 提取光标后代码 ===
        var suffixEnd = Math.Min(fullText.Length, caret + _service.MaxSuffixChars);
        var codeAfterCursor = caret < fullText.Length ? fullText[caret..suffixEnd] : null;

        // === 先查缓存 ===
        if (_service.TryGetCached(codeBeforeCursor, codeAfterCursor, filePath, language, out var cachedSuggestion))
        {
            if (!string.IsNullOrEmpty(cachedSuggestion))
                Dispatcher.Invoke(() => ShowSuggestion(cachedSuggestion));
            return;
        }

        // === 提取文件头部 imports/usings ===
        var importsHeader = CodeCompletionService.ExtractImportsHeader(fullText, language);

        await _service.RequestCompletionAsync(codeBeforeCursor, codeAfterCursor,
            filePath, language, importsHeader);
    }

    private void OnCompletionReady(string suggestion)
    {
        Dispatcher.Invoke(() => ShowSuggestion(suggestion));
    }

    private void ShowSuggestion(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // 验证光标位置未变
        if (_editor.TextArea.Caret.Offset != _lastRequestCaretOffset)
            return;

        _currentSuggestion = text;

        var caret = _editor.TextArea.Caret;
        var lines = text.Split('\n');

        // === 缩进对齐：检测当前行的缩进，应用到补全续行 ===
        var currentLine = _editor.Document.GetLineByOffset(_editor.TextArea.Caret.Offset);
        var currentLineText = _editor.Document.GetText(currentLine.Offset, Math.Min(currentLine.Length, caret.Column - 1));
        var indent = GetIndentation(currentLineText);
        // 如果光标在行首（刚回车），从上一行获取缩进
        if (caret.Column == 1 && currentLine.PreviousLine != null)
        {
            var prevLine = _editor.Document.GetText(currentLine.PreviousLine.Offset, currentLine.PreviousLine.Length);
            indent = GetIndentation(prevLine);
            // 如果是 Python 或 C# 大括号风格，增加一级缩进
            if (prevLine.TrimEnd().EndsWith(':') || prevLine.TrimEnd().EndsWith('{'))
                indent += "    ";
        }
        // 对多行补全，续行应用相同的缩进
        if (lines.Length > 1 && indent.Length > 0)
        {
            for (int i = 1; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(indent) && lines[i].Trim().Length > 0)
                    lines[i] = indent + lines[i].TrimStart();
            }
            text = string.Join('\n', lines);
            _currentSuggestion = text;
        }

        var maxDisplayLines = Math.Min(lines.Length, 5);

        Children.Clear();

        var lineHeight = _editor.TextArea.TextView.DefaultLineHeight;
        var defaultWidth = CalculateTextWidth("W") * 60; // 60 字符宽度的估算占位

        for (int i = 0; i < maxDisplayLines; i++)
        {
            var lineText = i < maxDisplayLines - 1 || lines.Length <= maxDisplayLines
                ? lines[i]
                : lines[i] + "…";

            // 计算该行的视觉位置
            var targetLine = caret.Location.Line + i;
            var column = i == 0 ? caret.Location.Column : 1;
            var visualPos = _editor.TextArea.TextView.GetVisualPosition(
                new ICSharpCode.AvalonEdit.TextViewPosition(targetLine, column),
                ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineTop);

            var x = i == 0
                ? visualPos.X + _editor.TextArea.TextView.WideSpaceWidth
                : visualPos.X;
            var y = visualPos.Y;

            var tb = new TextBlock
            {
                Text = lineText,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                FontStyle = FontStyles.Italic,
                FontFamily = _editor.FontFamily,
                FontSize = _editor.FontSize - 1,
                Background = Brushes.Transparent,
                Opacity = 0.0, // 初始透明，动画淡入
                ToolTip = i == 0
                    ? $"[Tab] 接受全部  [Ctrl+→] 逐词接受  [Esc] 取消\n\n{text}"
                    : null
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            Children.Add(tb);

            // 淡入动画
            var fadeIn = new DoubleAnimation(0.0, 0.72, TimeSpan.FromMilliseconds(150))
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 20) // 逐行错开
            };
            tb.BeginAnimation(OpacityProperty, fadeIn);
        }
        _isVisible = true;
    }

    private void HideSuggestion()
    {
        // 快速淡出（如果可见）
        if (_isVisible && Children.Count > 0)
        {
            foreach (var child in Children)
            {
                if (child is TextBlock tb)
                {
                    var fadeOut = new DoubleAnimation(tb.Opacity, 0.0, TimeSpan.FromMilliseconds(80));
                    tb.BeginAnimation(OpacityProperty, fadeOut);
                }
            }
            // 延迟清除以保证动画完成
            Dispatcher.BeginInvoke(() =>
            {
                Children.Clear();
                _currentSuggestion = "";
                _isVisible = false;
            }, DispatcherPriority.Background);
        }
        else
        {
            Children.Clear();
            _currentSuggestion = "";
            _isVisible = false;
        }
    }

    private void AcceptSuggestion()
    {
        if (string.IsNullOrEmpty(_currentSuggestion)) return;

        var caret = _editor.TextArea.Caret.Offset;
        _editor.Document.Insert(caret, _currentSuggestion);
        _editor.TextArea.Caret.Offset = caret + _currentSuggestion.Length;
        HideSuggestion();
    }

    /// <summary>逐词接受：只接受补全的第一个单词/标识符</summary>
    private void AcceptWord()
    {
        if (string.IsNullOrEmpty(_currentSuggestion)) return;

        var word = ExtractFirstWord(_currentSuggestion);
        if (string.IsNullOrEmpty(word)) return;

        var caret = _editor.TextArea.Caret.Offset;
        _editor.Document.Insert(caret, word);
        _editor.TextArea.Caret.Offset = caret + word.Length;
        _lastRequestCaretOffset = _editor.TextArea.Caret.Offset;

        // 更新剩余建议
        _currentSuggestion = _currentSuggestion[word.Length..].TrimStart();
        if (string.IsNullOrEmpty(_currentSuggestion))
        {
            HideSuggestion();
        }
        else
        {
            // 刷新显示
            ShowSuggestion(_currentSuggestion);
        }
    }

    private static string ExtractFirstWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var firstLine = text.Split('\n')[0];
        var match = System.Text.RegularExpressions.Regex.Match(firstLine, @"^[\w_]+");
        return match.Success ? match.Value : text[..Math.Min(1, text.Length)];
    }

    private static string TruncateDisplay(string text, int maxLines)
    {
        var lines = text.Split('\n');
        if (lines.Length <= maxLines) return text;
        return string.Join('\n', lines.Take(maxLines)) + "…";
    }

    /// <summary>提取行首空白字符作为缩进</summary>
    private static string GetIndentation(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        var indentEnd = 0;
        while (indentEnd < line.Length && (line[indentEnd] == ' ' || line[indentEnd] == '\t'))
            indentEnd++;
        return line[..indentEnd];
    }

    /// <summary>自适应防抖：根据连续输入速度动态调整间隔</summary>
    private void AdjustDebounce(bool isFastTyping)
    {
        if (isFastTyping)
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(150, _service.DebounceMs / 3));
        else
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(_service.DebounceMs);
    }

    private double CalculateTextWidth(string text)
    {
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(_editor.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            _editor.FontSize,
            Brushes.Black,
            1.0);
        return ft.Width;
    }

    private static string GuessLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "C#",
            ".js" => "JavaScript",
            ".ts" or ".tsx" => "TypeScript",
            ".py" => "Python",
            ".java" => "Java",
            ".go" => "Go",
            ".rs" => "Rust",
            ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => "C++",
            ".c" => "C",
            ".html" or ".htm" => "HTML",
            ".css" => "CSS",
            ".json" => "JSON",
            ".xml" => "XML",
            ".yaml" or ".yml" => "YAML",
            ".sql" => "SQL",
            ".md" => "Markdown",
            ".sh" or ".bash" => "Shell",
            ".ps1" => "PowerShell",
            _ => "plaintext"
        };
    }
}
