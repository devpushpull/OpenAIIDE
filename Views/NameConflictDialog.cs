using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIIDEWPF.Views;

/// <summary>
/// 文件名/文件夹名冲突弹窗，5秒无操作自动添加后缀。
/// 纯代码构建（无 XAML），因其 UI 简单且高度动态，XAML 分离反而降低可维护性。
/// </summary>
public partial class NameConflictDialog : Window
{
    private readonly DispatcherTimer _timer;
    private int _countdown = 5;
    private string _autoName;

    public string ResultName { get; private set; } = string.Empty;

    public NameConflictDialog(string existingName, string directory, bool isDirectory)
    {
        Title = isDirectory ? "文件夹已存在" : "文件已存在";
        Width = 420;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d));
        Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4));

        // 自动后缀名
        var ext = isDirectory ? "" : System.IO.Path.GetExtension(existingName);
        var baseName = isDirectory ? existingName : System.IO.Path.GetFileNameWithoutExtension(existingName);
        var counter = 1;
        do
        {
            counter++;
            _autoName = isDirectory
                ? $"{baseName} ({counter})"
                : $"{baseName} ({counter}){ext}";
        } while (System.IO.File.Exists(System.IO.Path.Combine(directory, _autoName))
              || System.IO.Directory.Exists(System.IO.Path.Combine(directory, _autoName)));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Margin = new Thickness(16);

        // 提示文字
        var label = new TextBlock
        {
            Text = $"名称 \"{existingName}\" 已存在，可修改名称或等待自动分配：",
            Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        // 名称输入框
        var nameBox = new TextBox
        {
            Text = existingName,
            Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(nameBox, 1);
        grid.Children.Add(nameBox);
        nameBox.Focus();
        nameBox.SelectAll();

        // 倒计时 + 按钮
        var bottomPanel = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(bottomPanel, 2);

        var countdownLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(countdownLabel, Dock.Left);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new Button
        {
            Content = "取消",
            Width = 60,
            Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 6, 6, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnOk = new Button
        {
            Content = "确认",
            Width = 60,
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7a, 0xcc)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 6, 6, 6)
        };

        btnCancel.Click += (s, e) => { ResultName = ""; Close(); };
        btnOk.Click += (s, e) => { ResultName = nameBox.Text.Trim(); Close(); };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        DockPanel.SetDock(btnPanel, Dock.Right);
        bottomPanel.Children.Add(countdownLabel);
        bottomPanel.Children.Add(btnPanel);
        grid.Children.Add(bottomPanel);
        Content = grid;

        // 5秒倒计时，无操作则自动使用后缀名
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) =>
        {
            _countdown--;
            countdownLabel.Text = $"{_countdown} 秒后自动使用: {_autoName}";
            if (_countdown <= 0)
            {
                _timer.Stop();
                ResultName = _autoName;
                Close();
            }
        };
        _timer.Start();
        countdownLabel.Text = $"{_countdown} 秒后自动使用: {_autoName}";

        // 用户编辑时重置倒计时
        nameBox.TextChanged += (s, e) =>
        {
            _countdown = 5;
            countdownLabel.Text = $"{_countdown} 秒后自动使用: {_autoName}";
        };
    }
}
