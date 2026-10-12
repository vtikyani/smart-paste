using System.Diagnostics;

namespace SmartPaste.E2E;

// Runs SmartPaste.exe against a throwaway data folder, so the test never touches the real settings, learned
// labels, or logs, and can run next to a normal copy of the app.
internal sealed class AppUnderTest : IDisposable
{
    private readonly Process _process;

    public AppUnderTest(string exePath, string settingsJson)
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "sp-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), settingsJson);

        var start = new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath)! };
        start.Environment["SMARTPASTE_DATA_DIR"] = DataDirectory;
        // The .NET 10 runtime may be a per-user install; tell the app host where it is.
        var userDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
        if (File.Exists(Path.Combine(userDotnet, "dotnet.exe"))) start.Environment["DOTNET_ROOT"] = userDotnet;
        _process = Process.Start(start) ?? throw new InvalidOperationException("SmartPaste.exe did not start.");
    }

    public string DataDirectory { get; }

    public uint ProcessId => (uint)_process.Id;

    public bool HasExited => _process.HasExited;

    // Waits until the app has settled after start (process alive and idle on its UI thread).
    public bool WaitUntilReady(TimeSpan timeout)
    {
        try { _process.WaitForInputIdle((int)timeout.TotalMilliseconds); }
        catch (InvalidOperationException) { }
        Thread.Sleep(1500);
        return !_process.HasExited;
    }

    public string Logs()
    {
        var folder = Path.Combine(DataDirectory, "logs");
        return Directory.Exists(folder)
            ? string.Join(Environment.NewLine, Directory.EnumerateFiles(folder, "*.log").Order().Select(File.ReadAllText))
            : "";
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
        _process.Dispose();
        try { Directory.Delete(DataDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// Microsoft Edge with a fresh, temporary profile showing a local HTML form. Leaves the person's own browser
// and profile alone.
internal sealed class EdgeSession : IDisposable
{
    private readonly Process _process;
    private readonly string _profile;

    public EdgeSession(string htmlPath)
    {
        var edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
        if (!File.Exists(edge)) throw new FileNotFoundException("Microsoft Edge is not installed.", edge);
        _profile = Path.Combine(Path.GetTempPath(), "sp-e2e-edge-" + Guid.NewGuid().ToString("N"));
        var args = $"--user-data-dir=\"{_profile}\" --no-first-run --no-default-browser-check --disable-sync " +
                   $"--disable-extensions --new-window \"{new Uri(htmlPath).AbsoluteUri}\"";
        _process = Process.Start(new ProcessStartInfo(edge, args) { UseShellExecute = false })
                   ?? throw new InvalidOperationException("Edge did not start.");
    }

    public void Dispose()
    {
        // With its own --user-data-dir this Edge is a separate browser process; its renderers and helpers are
        // its children, so closing the tree closes only this test's browser.
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        _process.Dispose();
        for (var attempt = 0; attempt < 10 && Directory.Exists(_profile); attempt++)
        {
            try { Directory.Delete(_profile, recursive: true); }
            catch (IOException) { Thread.Sleep(300); }
            catch (UnauthorizedAccessException) { Thread.Sleep(300); }
        }
    }
}
