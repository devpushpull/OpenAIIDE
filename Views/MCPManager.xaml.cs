using System.Collections.ObjectModel;
using System.Windows;
using AIIDEWPF.Models;
using AIIDEWPF.Services;

namespace AIIDEWPF.Views;

public partial class MCPManager : Window
{
    private readonly MCPService _service;

    public ObservableCollection<MCPServerDisplay> Servers { get; } = new();
    public ObservableCollection<MCPMarketItem> MarketItems { get; } = new();

    public MCPManager(MCPService service)
    {
        _service = service;
        DataContext = this;
        InitializeComponent();
        Refresh();
        _ = LoadMarketplaceAsync();
    }

    private void Refresh()
    {
        Servers.Clear();
        foreach (var s in _service.Servers)
            Servers.Add(new MCPServerDisplay(s));
        // 同步安装状态
        var installedIds = _service.Servers.Select(s => s.Id).ToHashSet();
        foreach (var item in MarketItems)
            item.IsInstalled = installedIds.Contains(item.Id);
    }

    private async Task LoadMarketplaceAsync()
    {
        try
        {
            var items = await MarketplaceService.GetMCPMarketplaceAsync();
            var installedIds = _service.Servers.Select(s => s.Id).ToHashSet();
            MarketItems.Clear();
            foreach (var item in items)
            {
                item.IsInstalled = installedIds.Contains(item.Id);
                MarketItems.Add(item);
            }
            MarketCount.Text = $"共 {MarketItems.Count} 个工具";
        }
        catch (Exception ex)
        {
            MarketCount.Text = $"加载失败: {ex.Message}";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void MarketRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadMarketplaceAsync();

    private void InstallMCP_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is MCPMarketItem item)
        {
            if (item.IsInstalled)
            {
                MessageBox.Show($"{item.Name} 已安装。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var config = new MCPServerConfig
            {
                Id = item.Id,
                Name = item.Name,
                Command = item.Command,
                Args = item.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Enabled = true,
                Description = item.Description
            };

            _service.Add(config);
            item.IsInstalled = true;
            Refresh();
            MessageBox.Show($"✅ {item.Name} 安装成功！\n命令: {item.Command} {item.Args}",
                "安装成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new MCPEditDialog();
        dlg.Owner = this;
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            _service.Add(dlg.Result);
            Refresh();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is MCPServerDisplay display)
        {
            var dlg = new MCPEditDialog(display.ToConfig());
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                _service.Update(dlg.Result);
                Refresh();
            }
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is MCPServerDisplay display)
        {
            var result = MessageBox.Show($"确认删除服务器 \"{display.Name}\"？", "删除确认",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _service.Delete(display.Id);
                Refresh();
            }
        }
    }
}

/// <summary>MCP 服务器列表展示项</summary>
public class MCPServerDisplay : MCPServerConfig
{
    public string ArgsDisplay => Args.Length > 0 ? string.Join(" ", Args) : "";

    public MCPServerDisplay() { }
    public MCPServerDisplay(MCPServerConfig config)
    {
        Id = config.Id;
        Name = config.Name;
        Command = config.Command;
        Args = config.Args;
        Env = config.Env;
        Enabled = config.Enabled;
        Description = config.Description;
    }

    public MCPServerConfig ToConfig() => new()
    {
        Id = Id, Name = Name, Command = Command, Args = Args,
        Env = Env, Enabled = Enabled, Description = Description
    };
}
