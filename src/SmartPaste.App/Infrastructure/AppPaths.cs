using System.Security.Cryptography;
using System.Text;

namespace SmartPaste.App.Infrastructure;

// Where Smart Paste keeps its files. Everything lives in one folder, so uninstall can remove it whole.
internal static class AppPaths
{
    // Set SMARTPASTE_DATA_DIR to run an isolated copy (end-to-end tests, development) that never touches the
    // real settings, learned labels, or logs.
    public const string DataDirectoryVariable = "SMARTPASTE_DATA_DIR";

    public static bool IsIsolated { get; } =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataDirectoryVariable));

    public static string DataDirectory { get; } = IsIsolated
        ? Path.GetFullPath(Environment.GetEnvironmentVariable(DataDirectoryVariable)!)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartPaste");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LabelCacheFile => Path.Combine(DataDirectory, "label-cache.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    // Short, stable id for this data folder. Names the single-instance mutex, so an isolated test copy can run
    // next to the real app.
    public static string InstanceId { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..16];
}
