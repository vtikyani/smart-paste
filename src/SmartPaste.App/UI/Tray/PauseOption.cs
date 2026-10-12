namespace SmartPaste.App.UI.Tray;

// The pause lengths offered in the tray menu and the flyout.
internal sealed record PauseOption(string Label, Func<DateTimeOffset, TimeSpan> Length)
{
    public static readonly PauseOption FifteenMinutes = new("15 minutes", _ => TimeSpan.FromMinutes(15));
    public static readonly PauseOption OneHour = new("1 hour", _ => TimeSpan.FromHours(1));

    // Until the next local midnight: the rest of today.
    public static readonly PauseOption UntilTomorrow = new("Until tomorrow", UntilMidnight);

    public static IReadOnlyList<PauseOption> All { get; } = [FifteenMinutes, OneHour, UntilTomorrow];

    public static TimeSpan UntilMidnight(DateTimeOffset now)
    {
        var tomorrow = now.ToLocalTime().Date.AddDays(1);
        var midnight = new DateTimeOffset(tomorrow, TimeZoneInfo.Local.GetUtcOffset(tomorrow));
        return midnight - now;
    }
}
