using System.Globalization;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Text;

// How serious a service state is, for the color of its dot.
internal enum Tone { Good, Neutral, Caution, Critical }

// One line about the decision services, for the flyout's chip and the tray tooltip. Detail says what happens until
// a problem is fixed; it is set only for problems the user has to fix.
internal sealed record ServiceSummary(string Text, Tone Tone, string? Detail = null)
{
    public bool IsProblem => Detail is not null;
}

// Every status sentence the tray and flyout show, in one place so they stay consistent. Pure functions of the
// state and the clock.
internal static class StatusText
{
    public const string Product = "Smart Paste";
    public const string AddKey = "Add a key in Settings";

    // The tray tooltip: one plain line, for example "Smart Paste: 8 fields armed, 12 min left".
    public static string Tooltip(AppState state, DateTimeOffset now, CultureInfo culture)
    {
        var detail = state.Status switch
        {
            TrayStatus.Off => "off",
            TrayStatus.Paused => "paused until " + Until(state.PausedUntil ?? now, now, culture),
            TrayStatus.Attention => Problem(state.JevStatus, state.ParsingStatus)?.Text ?? "ready",
            TrayStatus.Parsing => "reading your copy…",
            TrayStatus.Armed => Armed(state.Armed!, now),
            _ => "ready"
        };
        if (state.Status == TrayStatus.Attention && state.Armed is { } armed) detail += "; " + Armed(armed, now);
        return $"{Product}: {detail}";
    }

    // The state under "Smart Paste" in the flyout header, in a few words; the card below holds the details and
    // service problems get their own banner.
    public static string FlyoutStatus(AppState state, DateTimeOffset now, CultureInfo culture)
    {
        if (!state.IsEnabled) return "Off";
        if (state.PausedUntil is { } until && until > now) return "Paused until " + Until(until, now, culture);
        if (state.Armed is not null) return "Armed";
        if (state.IsParsing) return "Reading your copy…";
        return "Ready for your next copy";
    }

    // "1 field" or "8 fields".
    public static string FieldCount(int count) => count == 1 ? "1 field" : $"{count} fields";

    // "12 min left", rounded up so "1 min left" means there is still time.
    public static string TimeLeft(TimeSpan remaining) =>
        remaining >= TimeSpan.FromMinutes(1)
            ? $"{(int)Math.Ceiling(remaining.TotalMinutes - 0.0001)} min left"
            : "under a minute left";

    // "Clears in 12 min" for the armed card.
    public static string ClearsIn(TimeSpan remaining) =>
        remaining >= TimeSpan.FromMinutes(1)
            ? $"Clears in {(int)Math.Ceiling(remaining.TotalMinutes - 0.0001)} min"
            : "Clears in under a minute";

    // When a pause ends: "3:40 PM" today, "tomorrow" at midnight, "tomorrow at 9:00 AM", or "Wed 9:00 AM".
    public static string Until(DateTimeOffset until, DateTimeOffset now, CultureInfo culture)
    {
        var end = until.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var time = end.ToString("t", culture);
        if (end.Date == today) return time;
        if (end.Date == today.AddDays(1)) return end.TimeOfDay == TimeSpan.Zero ? "tomorrow" : "tomorrow at " + time;
        return end.ToString("ddd ", culture) + time;
    }

    // The chip at the bottom of the flyout. Problems the user must fix come first, then Jev's own state.
    public static ServiceSummary Services(ServiceStatus jev, ServiceStatus parsing) =>
        Problem(jev, parsing) ?? jev switch
        {
            ServiceStatus.Ready => new("Jev ready", Tone.Good),
            ServiceStatus.RateLimited => new("Jev is busy · using built-in labels", Tone.Caution),
            ServiceStatus.Unreachable => new("Offline · using built-in labels", Tone.Neutral),
            _ => new(AddKey, Tone.Neutral)
        };

    // A key that was rejected or ran out of credits; these put the tray icon in its attention state.
    public static ServiceSummary? Problem(ServiceStatus jev, ServiceStatus parsing)
    {
        const string Labels = "Fields are matched with built-in labels until ";
        const string Local = "Copied text is read on this PC only until ";
        if (jev == ServiceStatus.KeyRejected) return new("Jev key rejected", Tone.Critical, Labels + "you add a working key.");
        if (parsing == ServiceStatus.KeyRejected) return new("OpenRouter key rejected", Tone.Critical, Local + "you add a working key.");
        if (jev == ServiceStatus.OutOfCredits) return new("Jev is out of credits", Tone.Caution, Labels + "credits are added.");
        if (parsing == ServiceStatus.OutOfCredits) return new("OpenRouter is out of credits", Tone.Caution, Local + "credits are added.");
        return null;
    }

    private static string Armed(ArmedProfile armed, DateTimeOffset now) =>
        $"{FieldCount(armed.FieldCount)} armed, {TimeLeft(armed.ExpiresAt - now)}";
}
