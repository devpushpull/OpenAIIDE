using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.Models;

/// <summary>Git 面板显示的变更文件项</summary>
public class GitChangeDisplayItem : INotifyPropertyChanged
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Status { get; set; } = " "; // git status code: M/A/D/?/R/space

    public string StatusMark => Status switch
    {
        "M" or "MM" or " M" => "M",
        "A" or " A" or "AM" => "A",
        "D" or " D" => "D",
        "R" or " R" => "R",
        "?" or "??" => "?",
        _ => "●"
    };

    public string StatusColor => StatusMark switch
    {
        "M" => "#e2c08d",
        "A" => "#73c991",
        "D" => "#f14c4c",
        "R" => "#c586c0",
        "?" => "#6ca6cd",
        _ => "#d4d4d4"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>远程连接条目（SSH/WSL/DevContainer）</summary>
public class RemoteConnectionItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "ssh"; // ssh / wsl / devcontainer
    public string DisplayName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string Distribution { get; set; } = string.Empty; // WSL distribution name
    public string ConfigPath { get; set; } = string.Empty;   // DevContainer config path

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; set { _isConnected = value; OnPropertyChanged(); } }

    public string ConnectionString => Type switch
    {
        "ssh" => $"ssh {Username}@{Host} -p {Port}",
        "wsl" => $"wsl -d {Distribution}",
        "devcontainer" => ConfigPath,
        _ => ""
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Docker 容器条目</summary>
public class DockerContainerItem : INotifyPropertyChanged
{
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // running / exited / ...
    public string Ports { get; set; } = string.Empty;

    public bool IsRunning => Status.Contains("Up") || Status.Equals("running", StringComparison.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Wiki 记忆显示条目</summary>
public class WikiMemoryDisplay : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "user_preferences";
    public string Scope { get; set; } = "global";
    public DateTime UpdatedAt { get; set; }

    public string ScopeIcon => Scope switch
    {
        "global" => "🌐",
        "project" => "📁",
        "session" => "💬",
        _ => "📄"
    };

    public string ContentPreview => Content.Length > 60 ? Content[..60] + "..." : Content;

    public string UpdatedAtStr => UpdatedAt.ToString("MM-dd HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
