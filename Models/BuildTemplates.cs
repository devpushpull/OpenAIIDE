namespace AIIDEWPF.Models;

/// <summary>编译/打包模板</summary>
public class BuildTemplate
{
    public string Language { get; set; } = string.Empty;
    public string[] Extensions { get; set; } = Array.Empty<string>();
    public string[] BuildFiles { get; set; } = Array.Empty<string>();
    public string BuildCommand { get; set; } = string.Empty;
    public string PackageCommand { get; set; } = string.Empty;
    public string RunCommand { get; set; } = string.Empty;
}
