using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.Core;

namespace SmartPaste.App.UI.Navigation;

// Everything the settings window and the welcome tour need, handed over by the composition root.
// ProtectedApps: executable names Smart Paste never touches (password managers and the like), shown read-only.
internal sealed record UiServices(
    AppState State,
    AppController Controller,
    SettingsStore Settings,
    LabelCache Labels,
    IConnectionTester Connections,
    IUpdateService Updates,
    IPracticeEngine Practice,
    IHotkeyStatus Hotkeys,
    IReadOnlyList<string> ProtectedApps)
{
    // The real Windows services by default. Tests replace them, so a test can never read or delete a saved key,
    // touch the clipboard, open a browser, or depend on which apps happen to be running.
    public IApiKeyStore Keys { get; init; } = CredentialKeyStore.Instance;

    public IClipboardText Clipboard { get; init; } = SystemClipboardText.Instance;

    public IShellLauncher Shell { get; init; } = SystemShellLauncher.Instance;

    public IAppCatalog Apps { get; init; } = RunningAppCatalog.Instance;
}
