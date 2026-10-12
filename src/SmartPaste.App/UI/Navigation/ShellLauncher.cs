using System.Diagnostics;

namespace SmartPaste.App.UI.Navigation;

// Opens web pages and folders the way Windows does when you double-click them.
internal interface IShellLauncher
{
    // Opens an https page in the default browser. Throws Win32Exception when Windows can't open it.
    void OpenUrl(string url);

    // Opens a folder in File Explorer, creating it first if it doesn't exist yet.
    void OpenFolder(string path);
}

internal sealed class SystemShellLauncher : IShellLauncher
{
    public static readonly SystemShellLauncher Instance = new();

    private SystemShellLauncher()
    {
    }

    public void OpenUrl(string url)
    {
        // Only fixed https links are ever opened, so a crafted value can never start a program.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Only https links can be opened.", nameof(url));
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }
}
