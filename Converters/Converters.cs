using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AIIDEWPF.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>反向布尔→可见性：true→Collapsed, false→Visible</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v ? v != Visibility.Visible : true;
}

/// <summary>布尔→编辑图标：true=✓(确认), false=✎(编辑)</summary>
public class BoolToEditIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "✓" : "✎";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class IsNotNullConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>活动栏图标颜色：选中→白色(0xFFFFFFFF), 未选中→灰色(0xFF858585)</summary>
public class ActiveBarFgConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(0x85, 0x85, 0x85));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>目录/文件图标转换：IsDirectory=true→📁, false→📄</summary>
public class DirIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "📁" : "📄";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>文件类型图标转换器：根据FileItem对象自动判断类型显示对应图标</summary>
public class FileIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Models.FileItem item) return "📄";
        if (item.IsDirectory) return "📁";

        var name = item.Name;
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();

        return ext switch
        {
            ".cs" or ".java" or ".ts" or ".js" or ".jsx" or ".tsx" or ".go" or ".rs" or ".py" or ".rb" or ".swift" or ".kt" or ".dart" => "🔷",
            ".csproj" or ".sln" or ".csproj.user" => "⚙️",
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" => "📋",
            ".cshtml" or ".html" or ".htm" or ".razor" or ".xaml" or ".svg" => "🌐",
            ".css" or ".scss" or ".less" => "🎨",
            ".md" or ".txt" or ".log" => "📝",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" => "🖼️",
            ".gitignore" or ".gitattributes" or ".editorconfig" => "⚡",
            ".dll" or ".exe" or ".so" or ".dylib" => "🔧",
            _ => "📄"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>从完整路径提取文件名</summary>
public class FileNameConverter : IValueConverter
{
    public static readonly FileNameConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
            return System.IO.Path.GetFileName(path);
        return value ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>字符串非空→Visible，空/null→Collapsed</summary>
public class StringHasContentToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>用量百分比→颜色：<80%蓝色，80-95%橙色，>=95%红色</summary>
public class UsagePercentToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush SafeBrush = new(Color.FromRgb(0x56, 0x9c, 0xd6));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xd4, 0xa8, 0x43));
    private static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(0xd1, 0x5a, 0x3a));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double pct)
        {
            if (pct >= 95) return DangerBrush;
            if (pct >= 80) return WarningBrush;
            return SafeBrush;
        }
        return SafeBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Markdown 文本 → FlowDocument 转换器</summary>
public class MarkdownToFlowDocumentConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string markdown)
            return Controls.MarkdownRenderer.Render(markdown);
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>百分比 → GridLength(*) 转换器，用于进度条</summary>
public class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = value is double d ? d : (value is int i ? i : 0);
        percent = Math.Max(0, Math.Min(100, percent));
        return new GridLength(percent, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>沙箱模式 → 背景色：sandbox=绿色系, terminal=橙色系</summary>
public class SandboxModeToBgConverter : IValueConverter
{
    private static readonly SolidColorBrush SandboxBg = new(Color.FromRgb(0x1a, 0x3a, 0x2a));
    private static readonly SolidColorBrush TerminalBg = new(Color.FromRgb(0x3a, 0x2a, 0x1a));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string mode && mode == "terminal" ? TerminalBg : SandboxBg;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
