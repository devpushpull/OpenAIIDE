using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIIDEWPF.Views;

/// <summary>内联代码编辑弹窗 —— 对标 Cursor Ctrl+K</summary>
public partial class InlineEditPopup : Window
{
    private readonly string _selectedCode;
    private readonly string _filePath;
    private readonly string _language;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;
    private string? _generatedCode;

    /// <summary>用户确认替换后的回传结果</summary>
    public string? ResultCode { get; private set; }

    public InlineEditPopup(string selectedCode, string filePath, string language,
        string apiKey, string baseUrl, string model)
    {
        InitializeComponent();

        _selectedCode = selectedCode;
        _filePath = filePath;
        _language = language;
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _model = model;

        CodePreviewBox.Text = TrimCode(selectedCode, 10);
        InstructionBox.Focus();
        InstructionBox.SelectAll();
    }

    private static string TrimCode(string code, int maxLines)
    {
        var lines = code.Split('\n');
        if (lines.Length <= maxLines) return code;
        return string.Join('\n', lines.Take(maxLines)) + $"\n// ... (省略 {lines.Length - maxLines} 行)";
    }

    private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        var instruction = InstructionBox.Text.Trim();
        if (string.IsNullOrEmpty(instruction)) return;

        GenerateBtn.IsEnabled = false;
        GenerateBtn.Content = "⏳ 生成中...";

        try
        {
            _generatedCode = await GenerateCodeAsync(instruction);
            if (!string.IsNullOrEmpty(_generatedCode))
            {
                ResultBox.Text = _generatedCode;
                ResultLabel.Visibility = Visibility.Visible;
                ResultBorder.Visibility = Visibility.Visible;
                ApplyBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            ResultBox.Text = $"生成失败: {ex.Message}";
            ResultBox.Foreground = new SolidColorBrush(Color.FromRgb(0xf4, 0x87, 0x71));
            ResultLabel.Visibility = Visibility.Visible;
            ResultBorder.Visibility = Visibility.Visible;
        }
        finally
        {
            GenerateBtn.IsEnabled = true;
            GenerateBtn.Content = "✨ 生成";
        }
    }

    private async Task<string?> GenerateCodeAsync(string instruction)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var prompt = BuildInlineEditPrompt(instruction);
        var messages = new[]
        {
            new { role = "user", content = prompt }
        };

        var reqDict = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["temperature"] = 0.2,
            ["stream"] = false,
            ["max_tokens"] = Math.Max(_selectedCode.Length / 2, 1000)
        };

        var json = JsonSerializer.Serialize(reqDict);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return CleanGeneratedCode(content ?? "");
    }

    private string BuildInlineEditPrompt(string instruction)
    {
        var fileName = System.IO.Path.GetFileName(_filePath);

        return $$"""
你是世界顶级的代码编辑引擎。根据用户的指令修改代码，只输出修改后的完整代码块，不要任何解释。

## 文件信息
- 文件名: {{fileName}}
- 语言: {{_language}}

## 原始代码
```
{{_selectedCode}}
```

## 编辑指令
{{instruction}}

## 要求
- 只输出修改后的代码，不要任何 markdown 标记或解释
- 保持原有的缩进风格和命名规范
- 只修改指令要求的部分，其余部分保持不变
""";
    }

    private static string CleanGeneratedCode(string raw)
    {
        var result = raw.Trim();

        // 去掉 markdown 代码块标记
        if (result.StartsWith("```"))
        {
            var firstNewline = result.IndexOf('\n');
            if (firstNewline > 0)
                result = result[(firstNewline + 1)..];
            if (result.EndsWith("```"))
                result = result[..^3].TrimEnd();
        }

        return result;
    }

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedCode)) return;
        ResultCode = _generatedCode;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Enter 快速生成
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            GenerateBtn_Click(sender, e);
        }
        // Esc 取消
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelBtn_Click(sender, e);
        }
    }
}
