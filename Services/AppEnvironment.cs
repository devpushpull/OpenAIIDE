using System.IO;

namespace AIIDEWPF.Services;

public static class AppEnvironment
{
    /// <summary>
    /// Gets the application root directory (where .csproj/.sln lives).
    /// Walks up from bin output to find it, falls back to BaseDirectory.
    /// </summary>
    public static string AppRoot
    {
        get
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                if (Directory.GetFiles(dir, "*.csproj").Length > 0 ||
                    Directory.GetFiles(dir, "*.sln").Length > 0)
                    return dir;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }

    public static string SettingDir => Path.Combine(AppRoot, "setting");
    public static string LogDir => Path.Combine(AppRoot, "logs");
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIIDE");
}
