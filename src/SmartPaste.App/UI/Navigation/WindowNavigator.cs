using System.Windows;
using System.Windows.Threading;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Onboarding;
using SmartPaste.App.UI.Settings;

namespace SmartPaste.App.UI.Navigation;

// Opens the settings window and the welcome tour. There is at most one of each: asking again brings the open
// window to the front (restoring it if minimized) instead of opening a second one.
internal sealed class WindowNavigator : IWindowNavigator
{
    private readonly UiServices _services;
    private readonly Action<Window> _present;
    private readonly Dispatcher _dispatcher;

    public WindowNavigator(UiServices services) : this(services, BringToFront)
    {
    }

    // present: shows a window and brings it to the front. Tests pass one that keeps windows off-screen.
    internal WindowNavigator(UiServices services, Action<Window> present)
    {
        _services = services;
        _present = present;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    // The open windows, or null. Windows are created on first use and forgotten when closed.
    public SettingsWindow? Settings { get; private set; }

    public OnboardingWindow? Onboarding { get; private set; }

    public void ShowSettings(string? page = null)
    {
        // Callers like the single-instance wake-up run on a background thread; windows live on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => ShowSettings(page));
            return;
        }

        if (Settings is not { } window)
        {
            window = new SettingsWindow(_services, this, page ?? SettingsPages.General);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(Settings, window)) Settings = null;
            };
            Settings = window;
        }
        else if (page is not null)
        {
            window.ShowPage(page);
        }
        _present(window);
    }

    public void ShowOnboarding()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(ShowOnboarding);
            return;
        }

        if (Onboarding is not { } window)
        {
            window = new OnboardingWindow(_services, this);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(Onboarding, window)) Onboarding = null;
            };
            Onboarding = window;
        }
        _present(window);
    }

    // Shows the window, un-minimizing it if needed, without changing focus.
    internal static void Restore(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
    }

    private static void BringToFront(Window window)
    {
        Restore(window);
        window.Activate();
    }
}
