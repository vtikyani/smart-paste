using System.Windows;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Navigation;

namespace SmartPaste.App.Tests.Settings;

// The navigator keeps one settings window and one welcome tour. Windows here open off-screen and never take focus.
public sealed class WindowNavigatorTests
{
    [Fact]
    public void OneSettingsWindowOpensOnTheRequestedPage() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        var presented = new List<Window>();
        var navigator = new WindowNavigator(ui.Services, window =>
        {
            presented.Add(window);
            UiTestHost.ShowOffScreen(window);
            WindowNavigator.Restore(window);
        });
        try
        {
            navigator.ShowSettings(SettingsPages.Shortcuts);
            await UiTestHost.Settle(300);
            var first = navigator.Settings;
            Assert.NotNull(first);
            Assert.Equal(SettingsPages.Shortcuts, first.CurrentPage);

            navigator.ShowSettings(SettingsPages.Privacy);
            await UiTestHost.Settle(300);
            Assert.Same(first, navigator.Settings);
            Assert.Equal(SettingsPages.Privacy, first.CurrentPage);

            // No page: keep the one on screen.
            navigator.ShowSettings();
            await UiTestHost.Settle();
            Assert.Equal(SettingsPages.Privacy, first.CurrentPage);
            Assert.Equal(3, presented.Count);
            Assert.All(presented, w => Assert.Same(first, w));
        }
        finally
        {
            navigator.Settings?.Close();
        }
        Assert.Null(navigator.Settings);
    });

    [Fact]
    public void PagesCanBeVisitedAgain() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        var navigator = new WindowNavigator(ui.Services, UiTestHost.ShowOffScreen);
        try
        {
            foreach (var page in new[] { SettingsPages.General, SettingsPages.About, SettingsPages.General, SettingsPages.Jev, SettingsPages.About })
            {
                navigator.ShowSettings(page);
                await UiTestHost.Settle(250);
                Assert.Equal(page, navigator.Settings!.CurrentPage);
                Assert.True(UiTestHost.HasContent(UiTestHost.Render(navigator.Settings)));
            }
        }
        finally
        {
            navigator.Settings?.Close();
        }
    });

    [Fact]
    public void AMinimizedWindowIsRestored() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        var navigator = new WindowNavigator(ui.Services, window =>
        {
            UiTestHost.ShowOffScreen(window);
            WindowNavigator.Restore(window);
        });
        try
        {
            navigator.ShowSettings();
            await UiTestHost.Settle(200);
            var window = navigator.Settings!;
            window.WindowState = WindowState.Minimized;

            navigator.ShowSettings();
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally
        {
            navigator.Settings?.Close();
        }
    });

    [Fact]
    public void AnUnknownPageOpensGeneral() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        var navigator = new WindowNavigator(ui.Services, UiTestHost.ShowOffScreen);
        try
        {
            navigator.ShowSettings("no-such-page");
            await UiTestHost.Settle(300);
            Assert.Equal(SettingsPages.General, navigator.Settings!.CurrentPage);
        }
        finally
        {
            navigator.Settings?.Close();
        }
    });

    [Fact]
    public void OneWelcomeTourAtATime() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        var navigator = new WindowNavigator(ui.Services, window =>
        {
            if (!window.IsVisible) UiTestHost.ShowOffScreen(window);
        });
        try
        {
            navigator.ShowOnboarding();
            var first = navigator.Onboarding;
            navigator.ShowOnboarding();
            Assert.NotNull(first);
            Assert.Same(first, navigator.Onboarding);

            first.Close();
            await UiTestHost.Settle();
            Assert.Null(navigator.Onboarding);
            Assert.True(ui.Settings.Current.OnboardingCompleted);

            navigator.ShowOnboarding();
            Assert.NotSame(first, navigator.Onboarding);
        }
        finally
        {
            navigator.Onboarding?.Close();
        }
    });

    [Fact]
    public void RequestsFromOtherThreadsAreHandledOnTheUiThread() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        var navigator = new WindowNavigator(ui.Services, UiTestHost.ShowOffScreen);
        try
        {
            await Task.Run(() => navigator.ShowSettings(SettingsPages.About));
            await UiTestHost.Settle(300);
            Assert.Equal(SettingsPages.About, navigator.Settings?.CurrentPage);
        }
        finally
        {
            navigator.Settings?.Close();
        }
    });
}
