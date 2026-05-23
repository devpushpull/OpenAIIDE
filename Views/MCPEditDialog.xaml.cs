using System.Windows;
using AIIDEWPF.Models;

namespace AIIDEWPF.Views;

public partial class MCPEditDialog : Window
{
    public MCPServerConfig? Result { get; private set; }
    private readonly MCPServerConfig? _existing;

    public MCPEditDialog(MCPServerConfig? existing = null)
    {
        _existing = existing;
        InitializeComponent();

        if (existing != null)
        {
            TxtName.Text = existing.Name;
            TxtCommand.Text = existing.Command;
            TxtArgs.Text = string.Join(Environment.NewLine, existing.Args);
            ChkEnabled.IsChecked = existing.Enabled;
            TxtDescription.Text = existing.Description;
            Title = "编辑 MCP 服务器";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        var command = TxtCommand.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(command))
        {
            MessageBox.Show("名称和命令不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new MCPServerConfig
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Command = command,
            Args = TxtArgs.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
            Enabled = ChkEnabled.IsChecked == true,
            Description = TxtDescription.Text.Trim()
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
