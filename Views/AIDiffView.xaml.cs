using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIIDEWPF.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace AIIDEWPF.Views;

/// <summary>
/// 代码 Diff 对比视图 —— 对标 Qoder / 通义灵码 双栏 Diff 视图
/// 支持双栏对比 + 统一模式，行级别着色
/// </summary>
public partial class AIDiffView : UserControl
{
    private DiffResult? _currentDiff;
    private bool _isSideBySide = true;
    private int _currentHunkIndex = -1;
    private List<DiffHunk> _visibleHunks = new();

    public AIDiffView()
    {
        InitializeComponent();
    }

    public void ShowDiff(DiffResult diff)
    {
        _currentDiff = diff;
        _visibleHunks = diff.Hunks.ToList();
        _currentHunkIndex = _visibleHunks.Count > 0 ? 0 : -1;
        RefreshView();
    }

    private void RefreshView()
    {
        if (_currentDiff == null) return;

        OldEditor.Document.Text = string.Empty;
        NewEditor.Document.Text = string.Empty;

        foreach (var hunk in _visibleHunks)
        {
            foreach (var line in hunk.Lines)
            {
                if (line.Type == DiffLineType.Removed || line.Type == DiffLineType.Unchanged)
                    OldEditor.Document.Text += line.Text + "\n";
                if (line.Type == DiffLineType.Added || line.Type == DiffLineType.Unchanged)
                    NewEditor.Document.Text += line.Text + "\n";
            }
        }

        UpdateHunkNavigation();
    }

    private void UpdateHunkNavigation()
    {
        TxtHunkPos.Text = _visibleHunks.Count > 0
            ? $"Hunk {_currentHunkIndex + 1} / {_visibleHunks.Count}"
            : "No changes";
        PrevHunkBtn.IsEnabled = _currentHunkIndex > 0;
        NextHunkBtn.IsEnabled = _currentHunkIndex < _visibleHunks.Count - 1;
    }

    private void PrevHunk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentHunkIndex > 0)
        {
            _currentHunkIndex--;
            RefreshView();
        }
    }

    private void NextHunk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentHunkIndex < _visibleHunks.Count - 1)
        {
            _currentHunkIndex++;
            RefreshView();
        }
    }

    private void ToggleView_Click(object sender, RoutedEventArgs e)
    {
        _isSideBySide = !_isSideBySide;
        RefreshView();
    }

    private void BtnSideBySide_Click(object sender, RoutedEventArgs e)
    {
        _isSideBySide = true;
        RefreshView();
    }

    private void BtnUnified_Click(object sender, RoutedEventArgs e)
    {
        _isSideBySide = false;
        RefreshView();
    }

    private void BtnAccept_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 应用 diff 变更
    }

    private void BtnReject_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 拒绝 diff 变更
    }

    /// <summary>双栏模式左侧行背景着色器</summary>
    internal class SideBySideBackgroundRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private readonly bool _isOld;
        private DiffResult? _diff;

        public KnownLayer Layer => KnownLayer.Background;

        public SideBySideBackgroundRenderer(TextEditor editor, bool isOld)
        {
            _editor = editor;
            _isOld = isOld;
        }

        public void SetDiff(DiffResult diff)
        {
            _diff = diff;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_diff == null) return;

            var visualLines = textView.VisualLines;
            if (visualLines.Count == 0) return;

            foreach (var visualLine in visualLines)
            {
                var line = visualLine.FirstDocumentLine;
                var lineNum = line.LineNumber;

                // Find corresponding diff line
                DiffLine? diffLine = null;
                foreach (var hunk in _diff.Hunks)
                {
                    diffLine = hunk.Lines.FirstOrDefault(l =>
                        _isOld ? l.OldLineNumber == lineNum : l.NewLineNumber == lineNum);
                    if (diffLine != null) break;
                }

                if (diffLine == null) continue;

                Brush? brush = null;
                if (_isOld && diffLine.Type == DiffLineType.Removed)
                    brush = new SolidColorBrush(Color.FromRgb(90, 30, 30)) { Opacity = 0.4 };
                else if (!_isOld && diffLine.Type == DiffLineType.Added)
                    brush = new SolidColorBrush(Color.FromRgb(30, 70, 30)) { Opacity = 0.4 };

                if (brush == null) continue;

                var geo = new RectangleGeometry(new Rect(
                    0, visualLine.VisualTop - textView.ScrollOffset.Y,
                    textView.ActualWidth, visualLine.Height));
                drawingContext.DrawGeometry(brush, null, geo);
            }
        }
    }

    /// <summary>统一模式行背景着色器</summary>
    internal class UnifiedBackgroundRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;

        public KnownLayer Layer => KnownLayer.Background;

        public UnifiedBackgroundRenderer(TextEditor editor)
        {
            _editor = editor;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var visualLines = textView.VisualLines;
            if (visualLines.Count == 0) return;

            foreach (var visualLine in visualLines)
            {
                var line = visualLine.FirstDocumentLine;
                var text = _editor.Document.GetText(line.Offset, line.Length);

                Brush? brush = null;
                if (text.StartsWith('+'))
                    brush = new SolidColorBrush(Color.FromRgb(30, 70, 30)) { Opacity = 0.35 };
                else if (text.StartsWith('-'))
                    brush = new SolidColorBrush(Color.FromRgb(90, 30, 30)) { Opacity = 0.35 };

                if (brush == null) continue;

                var geo = new RectangleGeometry(new Rect(
                    0, visualLine.VisualTop - textView.ScrollOffset.Y,
                    textView.ActualWidth, visualLine.Height));
                drawingContext.DrawGeometry(brush, null, geo);
            }
        }
    }
}
