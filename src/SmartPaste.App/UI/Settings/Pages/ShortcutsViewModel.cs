using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal enum ShortcutHealth { None, Working, Problem, Off }

// One global shortcut on the Shortcuts page: its keys, and whether Windows let Smart Paste have them.
internal sealed partial class ShortcutSettingViewModel : ObservableObject
{
    private readonly SettingsContext _context;
    private readonly Func<AppSettings, string?> _read;
    private readonly Func<AppSettings, string?, AppSettings> _write;
    private bool _loading;

    // Why the last recorded shortcut wasn't saved (it matched the other shortcut), or "".
    private string _conflict = "";

    public ShortcutSettingViewModel(SettingsContext context, string name, string? defaultHotkey,
        Func<AppSettings, string?> read, Func<AppSettings, string?, AppSettings> write)
    {
        _context = context;
        Name = name;
        DefaultHotkey = defaultHotkey;
        _read = read;
        _write = write;
        Load();
    }

    // One of HotkeyNames.
    public string Name { get; }

    public string? DefaultHotkey { get; }

    // The other shortcut on the page, so the two can't be the same keys.
    public ShortcutSettingViewModel? Other { get; set; }

    public string OtherDescription { get; init; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestoreDefault))]
    [NotifyCanExecuteChangedFor(nameof(RestoreDefaultCommand))]
    public partial string? Hotkey { get; set; }

    // Set by the recorder when a key press can't become a shortcut (no Ctrl, Alt, or Win; or a reserved one).
    [ObservableProperty]
    public partial string RecorderProblem { get; set; } = "";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial ShortcutHealth Health { get; set; }

    public bool CanRestoreDefault => DefaultHotkey is not null && !SameKeys(Hotkey, DefaultHotkey);

    partial void OnHotkeyChanged(string? oldValue, string? newValue)
    {
        if (_loading) return;
        _conflict = "";
        if (newValue is not null && Other?.Hotkey is { } other && SameKeys(newValue, other))
        {
            // Keep the saved shortcut and say why the new one wasn't taken. The revert waits a moment, so the
            // recorder that is still pushing the new value gets the saved one back afterwards.
            _conflict = $"Those keys are already your {Other.OtherDescription}. Pick a different shortcut.";
            Refresh();
            _context.Dispatcher.BeginInvoke(Load);
            return;
        }
        _context.TrySave(s => _write(s, newValue));
        Refresh();
    }

    partial void OnRecorderProblemChanged(string value) => Refresh();

    [RelayCommand(CanExecute = nameof(CanRestoreDefault))]
    private void RestoreDefault() => Hotkey = DefaultHotkey;

    public void Load()
    {
        _loading = true;
        try
        {
            Hotkey = _read(_context.Settings);
        }
        finally
        {
            _loading = false;
        }
        Refresh();
    }

    public void Refresh()
    {
        if (RecorderProblem.Length > 0) (Health, Message) = (ShortcutHealth.Problem, RecorderProblem);
        else if (_conflict.Length > 0) (Health, Message) = (ShortcutHealth.Problem, _conflict);
        else (Health, Message) = Describe(Hotkey, _context.Services.Hotkeys.IsRegistered(Name));
    }

    public static (ShortcutHealth Health, string Message) Describe(string? hotkey, bool? registered) => registered switch
    {
        true => (ShortcutHealth.Working, "Ready. Works in any app."),
        false => (ShortcutHealth.Problem, "Another app already uses this shortcut. Pick a different one."),
        null when hotkey is null => (ShortcutHealth.Off, "Off. Select the box to set a shortcut."),
        // Just changed; the shortcut manager hasn't reported back yet.
        null => (ShortcutHealth.None, "")
    };

    private static bool SameKeys(string? a, string? b) =>
        HotkeyGesture.TryParse(a, out var first) && HotkeyGesture.TryParse(b, out var second)
            ? first == second
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

// The Shortcuts page: Ctrl+V and Ctrl+Shift+V explained, plus the picker and on/off shortcuts.
internal sealed class ShortcutsViewModel : IDisposable
{
    private readonly SettingsContext _context;

    public ShortcutsViewModel(SettingsContext context)
    {
        _context = context;
        Picker = new ShortcutSettingViewModel(context, HotkeyNames.Picker, AppSettings.DefaultPickerHotkey,
            s => s.PickerHotkey, (s, value) => s with { PickerHotkey = value })
        {
            OtherDescription = "picker shortcut"
        };
        Toggle = new ShortcutSettingViewModel(context, HotkeyNames.Toggle, null,
            s => s.ToggleHotkey, (s, value) => s with { ToggleHotkey = value })
        {
            OtherDescription = "on/off shortcut"
        };
        Picker.Other = Toggle;
        Toggle.Other = Picker;
        context.Services.Hotkeys.Changed += OnHotkeysChanged;
        context.Services.Settings.Changed += OnSettingsChanged;
    }

    public ShortcutSettingViewModel Picker { get; }

    public ShortcutSettingViewModel Toggle { get; }

    public void Dispose()
    {
        _context.Services.Hotkeys.Changed -= OnHotkeysChanged;
        _context.Services.Settings.Changed -= OnSettingsChanged;
    }

    // The shortcut manager reports from its own thread.
    private void OnHotkeysChanged(object? sender, EventArgs e) => _context.Post(() =>
    {
        Picker.Refresh();
        Toggle.Refresh();
    });

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.PickerHotkey == current.PickerHotkey && previous.ToggleHotkey == current.ToggleHotkey) return;
        _context.Post(() =>
        {
            Picker.Load();
            Toggle.Load();
        });
    }
}
