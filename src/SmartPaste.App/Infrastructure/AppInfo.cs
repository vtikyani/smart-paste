using System.Reflection;

namespace SmartPaste.App.Infrastructure;

// Facts about this build, read from assembly metadata set in Directory.Build.props (or by the release workflow).
internal static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public const string ProductName = "Smart Paste";

    // "1.2.0", without the "+commit" suffix the SDK appends to the informational version.
    public static string Version { get; } = ReadVersion();

    public static string RepositoryUrl { get; } = Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value ?? "";

    public static string ExecutablePath { get; } = Environment.ProcessPath ?? "";

    private static string ReadVersion()
    {
        var informational = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
