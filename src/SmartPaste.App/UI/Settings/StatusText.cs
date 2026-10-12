using System.Globalization;

namespace SmartPaste.App.UI.Settings;

// Short, human wording for times and amounts that several pages show.
internal static class StatusText
{
    // "Paused until 3:40 PM", "Paused until tomorrow at 9:00 AM", "Paused until Monday at 9:00 AM".
    public static string PausedUntil(DateTimeOffset until, DateTimeOffset now) => "Paused until " + Moment(until, now);

    // "3:40 PM", "tomorrow at 9:00 AM", or "Monday at 9:00 AM", in the user's own time format.
    public static string Moment(DateTimeOffset when, DateTimeOffset now)
    {
        var local = when.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        if (local.Date == today) return time;
        if (local.Date == today.AddDays(1)) return "tomorrow at " + time;
        return local.ToString("dddd", CultureInfo.CurrentCulture) + " at " + time;
    }

    // "5 minutes", "1 hour".
    public static string Minutes(int minutes) => minutes switch
    {
        60 => "1 hour",
        1 => "1 minute",
        _ when minutes % 60 == 0 => $"{minutes / 60} hours",
        _ => $"{minutes} minutes"
    };

    // "1 label", "12 labels".
    public static string Count(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} {plural}";
}
