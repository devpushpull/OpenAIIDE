using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

/// <summary>算法库显示列表项</summary>
public class AlgorithmDisplayItem : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Complexity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int LineCount { get; set; }

    public string DisplayText => $"[{Category}] {Name} ({Language})";
    public string SubText => string.IsNullOrEmpty(Complexity)
        ? $"{LineCount} 行"
        : $"{Complexity} · {LineCount} 行";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
