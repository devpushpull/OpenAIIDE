using System.Windows;

namespace AIIDEWPF.ViewModels;

public class LogViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private System.Collections.ObjectModel.ObservableCollection<string> _entries = new();
    private bool _isLogVisible;

    public System.Collections.ObjectModel.ObservableCollection<string> Entries { get => _entries; set { _entries = value; OnPropertyChanged(); } }
    public bool IsLogVisible { get => _isLogVisible; set { _isLogVisible = value; OnPropertyChanged(); } }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public void Append(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _entries.Insert(0, text);
            if (_entries.Count > 300)
                _entries.RemoveAt(_entries.Count - 1);
        });
    }

    public void Clear() => Application.Current.Dispatcher.Invoke(() => _entries.Clear());
}
