using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// What the tray icon shows, most important first.
public enum TrayStatus
{
    // Turned off in settings.
    Off,
    // Paused for a while; resumes by itself.
    Paused,
    // A service needs the user (a rejected key, no credits). Still pastes with local rules.
    Attention,
    // A copied block is being read.
    Parsing,
    // A copied block is armed: Ctrl+V pastes the right value.
    Armed,
    // On and waiting for a copy.
    Ready
}

// Health of one cloud service (parsing on OpenRouter, or Jev).
public enum ServiceStatus
{
    NotConfigured,
    Ready,
    KeyRejected,
    OutOfCredits,
    RateLimited,
    Unreachable
}

public enum ProfileOrigin { CopiedText, Screenshot }

public enum DecisionMaker
{
    // TypeSafe's Jev decided (the normal case when a key is saved).
    Jev,
    // A label this PC learned earlier (used when Jev is not available).
    Learned,
    // The built-in label list (used when Jev is not available).
    BuiltIn,
    // The user chose in the picker.
    User
}

public enum ActivityOutcome { Pasted, PastedOriginal, Skipped }

// A copied block that Ctrl+V can paste from.
public sealed record ArmedProfile(Profile Profile, DateTimeOffset ArmedAt, DateTimeOffset ExpiresAt, ProfileOrigin Origin, bool ParsedByAi)
{
    public int FieldCount => Profile.Keys.Count;
}

// One smart paste, for the flyout's "Recent" list. In memory only; never written to disk. Holds the field's
// label and the kind of value, never the value itself.
public sealed record ActivityEntry(DateTimeOffset At, string FieldLabel, string? KeyDisplayName, ActivityOutcome Outcome, DecisionMaker? DecidedBy, string AppName);

// Live state for the tray icon, flyout, and settings. Change it on the UI thread only.
public sealed partial class AppState : ObservableObject
{
    public const int RecentLimit = 12;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial DateTimeOffset? PausedUntil { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial ArmedProfile? Armed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial bool IsParsing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial ServiceStatus ParsingStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    public partial ServiceStatus JevStatus { get; set; }

    [ObservableProperty]
    public partial AvailableUpdate? Update { get; set; }

    // Newest first.
    public ObservableCollection<ActivityEntry> Recent { get; } = [];

    public TrayStatus Status
    {
        get
        {
            if (!IsEnabled) return TrayStatus.Off;
            if (PausedUntil is { } until && until > DateTimeOffset.Now) return TrayStatus.Paused;
            if (NeedsAttention(ParsingStatus) || NeedsAttention(JevStatus)) return TrayStatus.Attention;
            if (IsParsing) return TrayStatus.Parsing;
            return Armed is null ? TrayStatus.Ready : TrayStatus.Armed;
        }
    }

    public void AddActivity(ActivityEntry entry)
    {
        Recent.Insert(0, entry);
        while (Recent.Count > RecentLimit) Recent.RemoveAt(Recent.Count - 1);
    }

    private static bool NeedsAttention(ServiceStatus status) =>
        status is ServiceStatus.KeyRejected or ServiceStatus.OutOfCredits;
}
