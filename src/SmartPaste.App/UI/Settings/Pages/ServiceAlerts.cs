using SmartPaste.App.Services;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal sealed record ServiceAlert(InfoBarSeverity Severity, string Title, string Message);

// Turns the live health of parsing and Jev into plain messages for the Jev & AI page. Each says what's wrong,
// what Smart Paste does meanwhile, and how to fix it.
internal static class ServiceAlerts
{
    public const string OpenRouter = "OpenRouter";

    // jevService: the service Jev requests go through right now ("TypeSafe" or "OpenRouter").
    public static IReadOnlyList<ServiceAlert> For(ServiceStatus parsing, ServiceStatus jev, string jevService)
    {
        // Parsing always runs on OpenRouter. When Jev does too and both have the same problem, one message covers both.
        if (jevService == OpenRouter && parsing == jev)
            return Describe(parsing, OpenRouter, Affected.Both) is { } shared ? [shared] : [];

        var alerts = new List<ServiceAlert>(2);
        if (Describe(parsing, OpenRouter, Affected.Parsing) is { } parsingAlert) alerts.Add(parsingAlert);
        if (Describe(jev, jevService, Affected.Jev) is { } jevAlert) alerts.Add(jevAlert);
        return alerts;
    }

    private enum Affected { Parsing, Jev, Both }

    private static ServiceAlert? Describe(ServiceStatus status, string service, Affected affected)
    {
        var meanwhile = affected switch
        {
            Affected.Parsing => "copied text is read on this PC",
            Affected.Jev => "built-in labels pick values",
            _ => "copied text is read on this PC and built-in labels pick values"
        };
        return status switch
        {
            ServiceStatus.KeyRejected => new ServiceAlert(InfoBarSeverity.Error,
                $"{service} didn't accept your key",
                $"Until you fix it, {meanwhile}. Check the key below, or paste a new one."),
            ServiceStatus.OutOfCredits => new ServiceAlert(InfoBarSeverity.Warning,
                $"Your {service} account is out of credits",
                $"Add credits on the {service} website. Until then, {meanwhile}."),
            ServiceStatus.RateLimited => new ServiceAlert(InfoBarSeverity.Informational,
                $"{service} is busy",
                $"It asked Smart Paste to slow down for a moment. Meanwhile, {meanwhile}."),
            ServiceStatus.Unreachable => new ServiceAlert(InfoBarSeverity.Informational,
                $"Can't reach {service}",
                $"Check your internet connection. Until it's back, {meanwhile}."),
            ServiceStatus.ServiceError => new ServiceAlert(InfoBarSeverity.Warning,
                $"{service} sent an answer Smart Paste couldn't use",
                affected == Affected.Jev
                    ? $"Smart Paste tries again in a minute. Meanwhile, {meanwhile}."
                    : $"Check the parsing model under Advanced. Meanwhile, {meanwhile}."),
            _ => null
        };
    }
}
