using System.Text.Json.Serialization;

namespace SmartPaste.App.Infrastructure;

public enum ThemePreference { System, Light, Dark }

// Which service answers "what goes in this field". Auto uses the TypeSafe key when one is saved, otherwise the
// OpenRouter key.
public enum JevRoute { Auto, TypeSafe, OpenRouter }

// Everything the user can change. Immutable: change it with SettingsStore.Update(s => s with { ... }), so a
// snapshot read on one thread can never change under another.
public sealed record AppSettings
{
    public const int CurrentVersion = 2;
    public const string DefaultParsingModel = "anthropic/claude-haiku-5.5";
    public const string DefaultPickerHotkey = "Win+Alt+V";
    public static readonly IReadOnlyList<int> LifetimeChoices = [5, 15, 30, 60];

    public int Version { get; init; } = CurrentVersion;

    // Master switch. When off, Smart Paste does not watch the clipboard or keyboard and makes no network calls.
    public bool Enabled { get; init; } = true;

    // When set and in the future, Smart Paste behaves as if Enabled were false until that moment.
    public DateTimeOffset? PausedUntil { get; init; }

    public bool StartAtLogin { get; init; } = true;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    // How long a copied block stays armed for smart pasting.
    public int ProfileLifetimeMinutes { get; init; } = 15;

    // Read text out of copied screenshots with the built-in Windows OCR. Off by default: screenshots often
    // hold private things, and their text may go to the parsing model when it looks like contact details.
    public bool ReadScreenshots { get; init; }

    // OpenRouter model id used to turn a copied block into fields.
    public string ParsingModel { get; init; } = DefaultParsingModel;

    public JevRoute JevRoute { get; init; } = JevRoute.Auto;

    // Ask Jev about a field as soon as it gets focus while a block is armed, so Ctrl+V is instant.
    public bool AskJevAhead { get; init; } = true;

    // Show a small hint next to the focused field saying what Ctrl+V will paste.
    public bool ShowFieldHints { get; init; } = true;

    // Show a brief "Pasted: First name" confirmation after a smart paste.
    public bool ShowPasteConfirmation { get; init; } = true;

    // Show a notification when a copied block is armed.
    public bool NotifyWhenArmed { get; init; }

    // Opens the picker with every value of the armed block. Null disables the shortcut.
    public string? PickerHotkey { get; init; } = DefaultPickerHotkey;

    // Turns Smart Paste on or off. Null (the default) means no shortcut.
    public string? ToggleHotkey { get; init; }

    // Executable names (for example "slack.exe") where Ctrl+V is never intercepted.
    public IReadOnlyList<string> ExcludedApps { get; init; } = [];

    public bool CheckForUpdates { get; init; } = true;

    public bool OnboardingCompleted { get; init; }

    [JsonIgnore]
    public TimeSpan ProfileLifetime => TimeSpan.FromMinutes(ProfileLifetimeMinutes);

    public bool IsPausedAt(DateTimeOffset now) => PausedUntil is { } until && until > now;

    // True when Smart Paste should be watching right now.
    public bool IsActiveAt(DateTimeOffset now) => Enabled && !IsPausedAt(now);

    // Clamps values a hand-edited file could get wrong.
    public AppSettings Normalized() => this with
    {
        Version = CurrentVersion,
        ProfileLifetimeMinutes = LifetimeChoices.Contains(ProfileLifetimeMinutes) ? ProfileLifetimeMinutes : 15,
        ParsingModel = string.IsNullOrWhiteSpace(ParsingModel) ? DefaultParsingModel : ParsingModel.Trim(),
        PickerHotkey = string.IsNullOrWhiteSpace(PickerHotkey) ? null : PickerHotkey.Trim(),
        ToggleHotkey = string.IsNullOrWhiteSpace(ToggleHotkey) ? null : ToggleHotkey.Trim(),
        ExcludedApps = (ExcludedApps ?? [])
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Select(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a : a + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()
    };
}
