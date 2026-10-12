using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Controls;
using SmartPaste.App.UI.Navigation;
using SmartPaste.App.UI.Settings;
using SmartPaste.App.UI.Settings.Pages;
using SmartPaste.Core;
using Wpf.Ui.Appearance;

namespace SmartPaste.App.Tests.Settings;

// Builds the real settings window on the UI test thread, shows each page off-screen, and renders it, in light
// and dark. Set SMARTPASTE_SNAPSHOT_DIR to keep the images.
public sealed class SettingsRenderTests
{
    public static TheoryData<string> Pages => new()
    {
        SettingsPages.General,
        SettingsPages.Jev,
        SettingsPages.Privacy,
        SettingsPages.LearnedLabels,
        SettingsPages.Shortcuts,
        SettingsPages.About
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void PageRendersInLightAndDark(string page) => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        Populate(ui);
        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            UiTestHost.UseTheme(theme);
            var window = new SettingsWindow(ui.Services, ui.Navigator, page);
            try
            {
                UiTestHost.ShowOffScreen(window);
                await UiTestHost.Settle(500);
                Assert.Equal(page, window.CurrentPage);
                var image = UiTestHost.Render(window, scale: 2);
                Assert.True(UiTestHost.HasContent(image));
                UiTestHost.SaveSnapshot(image, $"settings-{page}-{theme.ToString().ToLowerInvariant()}");
            }
            finally
            {
                window.Close();
            }
        }
        UiTestHost.UseTheme(ApplicationTheme.Dark);
    });

    [Fact]
    public void EmptyLearnedLabelsShowsTheEmptyState() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        UiTestHost.UseTheme(ApplicationTheme.Light);
        var window = new SettingsWindow(ui.Services, ui.Navigator, SettingsPages.LearnedLabels);
        try
        {
            UiTestHost.ShowOffScreen(window);
            await UiTestHost.Settle(500);
            var image = UiTestHost.Render(window, scale: 2);
            Assert.True(UiTestHost.HasContent(image));
            UiTestHost.SaveSnapshot(image, "settings-labels-empty-light");
        }
        finally
        {
            window.Close();
            UiTestHost.UseTheme(ApplicationTheme.Dark);
        }
    });

    // The states people meet along the way: a shortcut being recorded, an app being added, a service problem,
    // and an update ready to install.
    [Fact]
    public void InteractiveStatesRender() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        Populate(ui);
        ui.Apps.Running.Add(new RunningApp("slack.exe", "Slack", null));
        ui.Apps.Running.Add(new RunningApp("Teams.exe", "Microsoft Teams", null));
        ui.Updates.Available = new FakeUpdate("1.1.0", "### Added\n- Paste into more kinds of forms\n\n### Fixed\n- Keys typed right after choosing in the picker reach the app");
        UiTestHost.UseTheme(ApplicationTheme.Light);
        var window = new SettingsWindow(ui.Services, ui.Navigator, SettingsPages.Shortcuts);
        try
        {
            UiTestHost.ShowOffScreen(window);
            await UiTestHost.Settle(500);

            var recorder = Descendants<HotkeyBox>(window).Last();
            recorder.StartRecording();
            recorder.Record(Key.K, ModifierKeys.Shift);
            await UiTestHost.Settle();
            Snapshot(window, "settings-shortcuts-recording-light");
            recorder.CancelRecording();

            window.ShowPage(SettingsPages.Privacy);
            await UiTestHost.Settle(400);
            var privacy = (PrivacyViewModel)Descendants<PrivacyPage>(window).Single().DataContext;
            privacy.IsAddingApp = true;
            privacy.NewAppName = "1password";
            privacy.AddTypedAppCommand.Execute(null);
            await UiTestHost.Settle(200);
            Descendants<ScrollViewer>(window).First(s => s.ScrollableHeight > 0).ScrollToVerticalOffset(760);
            await UiTestHost.Settle(100);
            Snapshot(window, "settings-privacy-add-app-light");

            ui.State.ParsingStatus = ServiceStatus.KeyRejected;
            ui.State.JevStatus = ServiceStatus.KeyRejected;
            window.ShowPage(SettingsPages.Jev);
            await UiTestHost.Settle(400);
            Snapshot(window, "settings-jev-alert-light");

            window.ShowPage(SettingsPages.About);
            await UiTestHost.Settle(400);
            var about = (AboutViewModel)Descendants<AboutPage>(window).Single().DataContext;
            await about.CheckCommand.ExecuteAsync(null);
            await UiTestHost.Settle();
            Snapshot(window, "settings-about-update-light");
        }
        finally
        {
            window.Close();
            UiTestHost.UseTheme(ApplicationTheme.Dark);
        }
    });

    private static void Snapshot(System.Windows.Window window, string name)
    {
        var image = UiTestHost.Render(window, scale: 2);
        Assert.True(UiTestHost.HasContent(image));
        UiTestHost.SaveSnapshot(image, name);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    // Realistic state: a saved key that works, a block ready to paste, a few learned labels and excluded apps.
    private static void Populate(TestUi ui)
    {
        ui.Keys.Set(CredentialStore.OpenRouter, "sk-or-test-key");
        ui.Settings.Update(s => s with { ExcludedApps = ["code.exe", "slack.exe"] });
        ui.State.Armed = new ArmedProfile(
            new Profile([new Entity("Contact", new Dictionary<string, string>
            {
                [FieldKeys.FirstName] = "Jordan",
                [FieldKeys.Email] = "jordan.rivera@example.com",
                [FieldKeys.Zip] = "07030"
            })], DateTimeOffset.Now),
            DateTimeOffset.Now, DateTimeOffset.Now.AddMinutes(15), ProfileOrigin.CopiedText, ParsedByAi: true);
        ui.Labels.Set("Given name", FieldKeys.FirstName);
        ui.Labels.Set("E-mail (work)", FieldKeys.Email);
        ui.Labels.Set("Postal / ZIP", FieldKeys.Zip);
        ui.Labels.Set("Mobile number", FieldKeys.Phone);
        ui.Hotkeys.Registered[HotkeyNames.Picker] = true;
    }
}
