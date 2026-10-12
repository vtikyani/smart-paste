using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Navigation;
using SmartPaste.App.UI.Settings;
using SmartPaste.Core;

namespace SmartPaste.App.Tests.Settings;

// In-memory stand-ins for the Windows services the settings window and the welcome tour use. None of them
// touches Credential Manager, the clipboard, the browser, or other processes.

internal sealed class FakeKeyStore : IApiKeyStore
{
    private readonly Dictionary<string, string> _keys = [];

    public event Action<string>? Changed;

    public bool FailWrites { get; set; }

    public string? Get(string target) => _keys.GetValueOrDefault(target);

    public bool Exists(string target) => _keys.ContainsKey(target);

    public void Set(string target, string key)
    {
        if (FailWrites) throw new System.ComponentModel.Win32Exception(5, "Access denied");
        _keys[target] = key.Trim();
        Changed?.Invoke(target);
    }

    public void Delete(string target)
    {
        if (FailWrites) throw new System.ComponentModel.Win32Exception(5, "Access denied");
        if (_keys.Remove(target)) Changed?.Invoke(target);
    }
}

internal sealed class FakeClipboard : IClipboardText
{
    public string? Text { get; set; }

    public bool Busy { get; set; }

    public string? GetText() => Busy ? throw new ExternalException("Clipboard busy", unchecked((int)0x800401D0)) : Text;

    public void SetPracticeText(string text)
    {
        if (Busy) throw new ExternalException("Clipboard busy", unchecked((int)0x800401D0));
        Text = text;
    }
}

internal sealed class FakeShell : IShellLauncher
{
    public List<string> Opened { get; } = [];

    public void OpenUrl(string url) => Opened.Add(url);

    public void OpenFolder(string path) => Opened.Add(path);
}

internal sealed class FakeAppCatalog : IAppCatalog
{
    public List<RunningApp> Running { get; } = [];

    public IReadOnlyList<RunningApp> GetRunningApps() => Running.ToArray();

    public RunningApp? Find(string exeName) =>
        Running.FirstOrDefault(a => a.ExeName.Equals(exeName, StringComparison.OrdinalIgnoreCase));
}

internal sealed class FakeConnectionTester : IConnectionTester
{
    public ConnectionCheck Result { get; set; } = new(true, "Connected.");

    // When set, checks wait for it, so a test can look at the "checking" state.
    public TaskCompletionSource? Gate { get; set; }

    public List<string> Checked { get; } = [];

    public Task<ConnectionCheck> CheckOpenRouterAsync(string apiKey, CancellationToken ct) => Check("openrouter:" + apiKey, ct);

    public Task<ConnectionCheck> CheckTypeSafeAsync(string apiKey, CancellationToken ct) => Check("typesafe:" + apiKey, ct);

    private async Task<ConnectionCheck> Check(string what, CancellationToken ct)
    {
        Checked.Add(what);
        if (Gate is { } gate) await gate.Task.WaitAsync(ct);
        return Result;
    }
}

internal sealed class FakeUpdate(string version) : AvailableUpdate
{
    public override string Version => version;

    public override string? ReleaseNotesMarkdown => null;
}

internal sealed class FakeUpdateService : IUpdateService
{
    public bool IsSupported { get; set; } = true;

    public string CurrentVersion => AppInfo.Version;

    public AvailableUpdate? Available { get; set; }

    public bool FailChecks { get; set; }

    public AvailableUpdate? Applied { get; private set; }

    public Task<AvailableUpdate?> CheckAsync(CancellationToken ct) =>
        FailChecks ? Task.FromException<AvailableUpdate?>(new HttpRequestException("offline")) : Task.FromResult(Available);

    public Task DownloadAsync(AvailableUpdate update, IProgress<int>? progress, CancellationToken ct)
    {
        progress?.Report(50);
        progress?.Report(100);
        return Task.CompletedTask;
    }

    public void ApplyAndRestart(AvailableUpdate update) => Applied = update;
}

internal sealed class FakePracticeEngine : IPracticeEngine
{
    public Dictionary<string, PracticeResult?> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Label, string Text)> Calls { get; } = [];

    public Task<PracticeResult?> PasteIntoAsync(string fieldLabel, string clipboardText, CancellationToken ct)
    {
        Calls.Add((fieldLabel, clipboardText));
        return Task.FromResult(Results.GetValueOrDefault(fieldLabel));
    }
}

internal sealed class FakeHotkeyStatus : IHotkeyStatus
{
    public Dictionary<string, bool?> Registered { get; } = [];

    public event EventHandler? Changed;

    public bool? IsRegistered(string name) => Registered.GetValueOrDefault(name);

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeNavigator : IWindowNavigator
{
    public List<string?> SettingsRequests { get; } = [];

    public int OnboardingRequests { get; private set; }

    public void ShowSettings(string? page = null) => SettingsRequests.Add(page);

    public void ShowOnboarding() => OnboardingRequests++;
}

internal sealed class FakePrompts : IUserPrompts
{
    public bool Answer { get; set; } = true;

    public List<Confirmation> Asked { get; } = [];

    public Task<bool> ConfirmAsync(Confirmation confirmation)
    {
        Asked.Add(confirmation);
        return Task.FromResult(Answer);
    }
}

// A complete set of services for one test: real settings, state, controller and label cache in a temp folder,
// fakes for everything that would reach outside the process. Create and use it on the UI test thread.
internal sealed class TestUi : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sp-ui-" + Guid.NewGuid().ToString("N"));

    public TestUi(IReadOnlyList<string>? protectedApps = null)
    {
        Directory.CreateDirectory(_folder);
        Settings = new SettingsStore(Path.Combine(_folder, "settings.json"));
        Controller = new AppController(State, Settings, Dispatcher.CurrentDispatcher);
        Labels = new LabelCache(Path.Combine(_folder, "label-cache.json"));
        Services = new UiServices(State, Controller, Settings, Labels, Connections, Updates, Practice, Hotkeys,
            protectedApps ?? ["1password.exe", "bitwarden.exe", "keepass.exe"])
        {
            Keys = Keys,
            Clipboard = Clipboard,
            Shell = Shell,
            Apps = Apps
        };
        Context = new SettingsContext(Services, Navigator, Prompts, Dispatcher.CurrentDispatcher);
    }

    public AppState State { get; } = new();

    public SettingsStore Settings { get; }

    public AppController Controller { get; }

    public LabelCache Labels { get; }

    public FakeKeyStore Keys { get; } = new();

    public FakeClipboard Clipboard { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeAppCatalog Apps { get; } = new();

    public FakeConnectionTester Connections { get; } = new();

    public FakeUpdateService Updates { get; } = new();

    public FakePracticeEngine Practice { get; } = new();

    public FakeHotkeyStatus Hotkeys { get; } = new();

    public FakeNavigator Navigator { get; } = new();

    public FakePrompts Prompts { get; } = new();

    public UiServices Services { get; }

    public SettingsContext Context { get; }

    public void Dispose()
    {
        Controller.Dispose();
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }
}
