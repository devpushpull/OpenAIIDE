using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using AIIDEWPF.Models;
using AIIDEWPF.Services;

namespace AIIDEWPF.Views;

public partial class PluginManager : Window
{
    private readonly PluginService _service;
    private readonly Action<string>? _onPluginInstalled;

    public ObservableCollection<PluginDisplay> Plugins { get; } = new();
    public ObservableCollection<PluginDiscoveryItem> DiscoveryItems { get; } = new();
    public string PluginDir => _service.GetPluginDir();

    public PluginManager(PluginService service, Action<string>? onPluginInstalled = null)
    {
        _service = service;
        _onPluginInstalled = onPluginInstalled;
        DataContext = this;
        InitializeComponent();
        Refresh();
        _ = LoadDiscoveryAsync();
    }

    private void Refresh()
    {
        Plugins.Clear();
        var scanned = _service.ScanPlugins();

        // 构建发现列表中最新版本映射
        var latestVersions = new Dictionary<string, string>();
        foreach (var d in DiscoveryItems)
        {
            if (!string.IsNullOrEmpty(d.Version))
                latestVersions[d.Id] = d.Version;
        }

        foreach (var p in scanned)
        {
            var display = new PluginDisplay(p);

            // 检查是否有可用更新
            if (latestVersions.TryGetValue(p.Id, out var latestVer))
            {
                display.HasUpdateAvailable = _service.HasUpdate(p.Id, p.Version, latestVer);
                display.LatestVersion = latestVer;
            }

            Plugins.Add(display);
        }

        // 同步安装状态到发现列表
        var installedIds = scanned.Select(p => p.Id).ToHashSet();
        foreach (var item in DiscoveryItems)
        {
            item.IsInstalled = installedIds.Contains(item.Id);
            // 检查更新状态
            var installed = scanned.FirstOrDefault(p => p.Id == item.Id);
            if (installed != null && !string.IsNullOrEmpty(item.Version))
            {
                item.HasUpdate = _service.HasUpdate(item.Id, installed.Version, item.Version);
            }
            else
            {
                item.HasUpdate = false;
            }
        }

        // 显示缓存大小
        CacheSizeText.Text = $"缓存: {_service.GetCacheSizeDisplay()}";
    }

    private async Task LoadDiscoveryAsync()
    {
        try
        {
            var items = await MarketplaceService.GetPluginDiscoveryAsync();
            var installedIds = _service.ScanPlugins().Select(p => p.Id).ToHashSet();
            var installedMap = _service.ScanPlugins().ToDictionary(p => p.Id, p => p.Version);

            DiscoveryItems.Clear();
            foreach (var item in items)
            {
                item.IsInstalled = installedIds.Contains(item.Id);
                // 检查是否有更新
                if (item.IsInstalled && installedMap.TryGetValue(item.Id, out var installedVer))
                {
                    item.HasUpdate = _service.HasUpdate(item.Id, installedVer, item.Version);
                }
                else
                {
                    item.HasUpdate = false;
                }
                DiscoveryItems.Add(item);
            }
            DiscoveryCount.Text = $"共 {DiscoveryItems.Count} 个插件";
        }
        catch (Exception ex)
        {
            DiscoveryCount.Text = $"加载失败: {ex.Message}";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void DiscoveryRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadDiscoveryAsync();

    // ===== 安装 / 更新插件 =====
    private void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is PluginDiscoveryItem item)
        {
            // 判断是安装还是更新
            var existing = _service.ScanPlugins().FirstOrDefault(p => p.Id == item.Id);
            bool isUpdate = existing != null;

            if (isUpdate && !item.HasUpdate)
            {
                MessageBox.Show($"{item.Name} 已是最新版本 v{existing!.Version}。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 模拟安装/更新：保存插件 manifest
            var manifest = new PluginManifest
            {
                Id = item.Id,
                Name = item.Name,
                Version = item.Version,
                Author = item.Author,
                Description = item.Description,
                Enabled = existing?.Enabled ?? true,
                Capabilities = new[] { item.Category },
                EntryPoint = existing?.EntryPoint ?? $"{item.Id}.dll"
            };
            _service.SaveManifest(item.Id, manifest);
            item.IsInstalled = true;
            item.HasUpdate = false;
            Refresh();

            // 通知 MainViewModel 标记需要重启
            _onPluginInstalled?.Invoke(item.Id);

            if (isUpdate)
            {
                MessageBox.Show(
                    $"✅ {item.Name} 已更新！\n\n" +
                    $"版本: {existing!.Version} → {item.Version}\n" +
                    $"⚠️ 需要重启应用以使更新生效。\n\n" +
                    $"插件目录: {PluginDir}\\{item.Id}",
                    "更新成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"✅ {item.Name} v{item.Version} 安装成功！\n\n" +
                    $"⚠️ 需要重启应用以使插件生效。\n\n" +
                    $"插件目录: {PluginDir}\\{item.Id}",
                    "安装成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    // ===== 卸载插件 =====
    private void UninstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is PluginDisplay display)
        {
            var result = MessageBox.Show(
                $"确认卸载插件 「{display.Name}」v{display.Version}？\n\n" +
                $"此操作将：\n" +
                $"• 删除插件目录及所有文件\n" +
                $"• 清理下载缓存中的残留文件\n" +
                $"• 清除更新标记\n\n" +
                $"⚠️ 已加载的插件需重启应用后完全移除。",
                "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            _service.DeletePlugin(display.Id);
            Refresh();

            // 同步更新发现列表状态
            foreach (var item in DiscoveryItems)
            {
                if (item.Id == display.Id)
                {
                    item.IsInstalled = false;
                    item.HasUpdate = false;
                }
            }

            MessageBox.Show($"✅ {display.Name} 已卸载。\n\n残留文件已清理。",
                "卸载完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ===== 切换插件启用/禁用 =====
    private void TogglePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is PluginDisplay display)
        {
            display.Enabled = !display.Enabled;

            // 更新 manifest 文件
            var manifest = new PluginManifest
            {
                Id = display.Id,
                Name = display.Name,
                Version = display.Version,
                Author = display.Author,
                Description = display.Description,
                Enabled = display.Enabled,
                Capabilities = display.Capabilities,
                EntryPoint = display.EntryPoint,
                Dependencies = display.Dependencies
            };
            _service.SaveManifest(display.Id, manifest);

            // 通知需要重启
            _onPluginInstalled?.Invoke(display.Id);

            var status = display.Enabled ? "已启用" : "已禁用";
            MessageBox.Show($"{display.Name} {status}。\n⚠️ 需要重启应用以使更改生效。",
                "状态变更", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ===== 清除下载缓存 =====
    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var cacheSize = _service.GetCacheSizeDisplay();
        if (cacheSize == "0 KB" || cacheSize == "0 B")
        {
            MessageBox.Show("下载缓存为空，无需清理。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确认清除所有插件下载缓存？\n当前缓存大小: {cacheSize}\n\n此操作不可撤销。",
            "确认清除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _service.ClearDownloadCache();
        CacheSizeText.Text = "缓存: 0 KB";
        MessageBox.Show("✅ 下载缓存已全部清除。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = _service.GetPluginDir();
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", dir);
    }
}

/// <summary>插件列表展示项，附加格式化信息与 UI 状态</summary>
public class PluginDisplay : PluginManifest, INotifyPropertyChanged
{
    private bool _hasUpdateAvailable;
    private string _latestVersion = "";

    public PluginDisplay() { }
    public PluginDisplay(PluginManifest m)
    {
        Id = m.Id; Name = m.Name; Version = m.Version;
        Description = m.Description; Author = m.Author;
        EntryPoint = m.EntryPoint; Enabled = m.Enabled;
        Capabilities = m.Capabilities; Dependencies = m.Dependencies;
    }

    public bool HasUpdateAvailable
    {
        get => _hasUpdateAvailable;
        set { _hasUpdateAvailable = value; OnPropertyChanged(); }
    }

    public string LatestVersion
    {
        get => _latestVersion;
        set { _latestVersion = value; OnPropertyChanged(); }
    }

    public string EnableText => Enabled ? "🔵 禁用" : "🟢 启用";

    public string CapabilitiesStr => Capabilities.Length > 0
        ? string.Join(", ", Capabilities)
        : "未指定";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
