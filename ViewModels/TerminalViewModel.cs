using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AIIDEWPF.ViewModels;

public class TerminalViewModel : INotifyPropertyChanged
{
    private string _terminalOutput = string.Empty;
    private string _terminalInput = string.Empty;
    private bool _isTerminalVisible;

    public string TerminalOutput { get => _terminalOutput; set { _terminalOutput = value; OnPropertyChanged(); } }
    public string TerminalInput { get => _terminalInput; set { _terminalInput = value; OnPropertyChanged(); } }
    public bool IsTerminalVisible { get => _isTerminalVisible; set { _isTerminalVisible = value; OnPropertyChanged(); } }

    public event Action<string>? InputSubmitted;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void AppendOutput(string text) => TerminalOutput += text;
}
