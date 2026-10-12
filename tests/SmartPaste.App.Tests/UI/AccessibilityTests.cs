using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Navigation;
using SmartPaste.App.UI.Onboarding;
using SmartPaste.App.UI.Popups;
using SmartPaste.App.UI.Settings.Pages;
using SmartPaste.App.UI.Tray;
using Wpf.Ui.Controls;

// What screen readers hear in lists: a real name for every item, never a type name or a record's field dump, and
// "hidden" for a masked value instead of the dots. Read through WPF's automation peers, as UI Automation reads them.
public sealed class AccessibilityTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "sp-a11y-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    [Fact]
    public void FlyoutListsAreReadByNameAndHiddenValuesSayHidden() => WpfHost.Run(() =>
    {
        var settings = new SettingsStore(_settingsPath);
        var clock = new FixedClock(DateTimeOffset.Now);
        var state = new AppState { JevStatus = ServiceStatus.Ready, ParsingStatus = ServiceStatus.Ready };
        using var controller = new AppController(state, settings, Dispatcher.CurrentDispatcher);
        state.Armed = DocSample.Armed(clock.Now);
        foreach (var entry in DocSample.Activity(clock.Now)) state.AddActivity(entry);
        using var model = new FlyoutViewModel(state, controller, settings, new RecordingNavigator(), clock);
        model.Opened();
        var window = new FlyoutWindow(model);
        try
        {
            var root = UiRender.Detach(window);
            UiRender.Element(root, width: 360);
            var names = SpokenNames(root);
            Assert.Contains("Person", names);
            Assert.Contains("First name: hidden", names);
            Assert.Contains("Street: hidden", names);
            Assert.Contains("hidden", names);
            Assert.Contains(names, n => n.StartsWith("Given name, First name · Jev · chrome, ", StringComparison.Ordinal));
            AssertReadable(names);

            model.ToggleRevealCommand.Execute(null);
            UiRender.Element(root, width: 360);
            names = SpokenNames(root);
            Assert.Contains("First name: Jordan", names);
            AssertReadable(names);
        }
        finally
        {
            model.Closed();
            window.Close();
        }
    });

    [Fact]
    public void PickerRowsAreReadAsWhatTheyPaste() => WpfHost.Run(() =>
    {
        var picker = new PickerWindow(new PickerViewModel());
        try
        {
            picker.ViewModel.Load(DocSample.NamePick(new ScreenRect(0, 0, 240, 28)));
            var root = UiRender.Detach(picker);
            UiRender.Element(root);
            var names = SpokenNames(root);
            Assert.Contains("Full name: Jordan Rivera", names);
            Assert.Contains("First name: Jordan", names);
            AssertReadable(names);
        }
        finally
        {
            picker.Close();
        }
    });

    [Fact]
    public void ListItemsOnTheSettingsPagesAndTheTourHaveSpokenNames() => WpfHost.Run(() =>
    {
        var app = new AppItem("code.exe");
        Assert.Equal("code.exe", app.ToString());
        app.DisplayName = "Visual Studio Code";

        object[] items =
        [
            new RunningApp("slack.exe", "Slack", null),
            app,
            new LearnedLabelRow("Given name", "First name"),
            new ThemeChoice(ThemePreference.Dark, "Dark"),
            new LifetimeChoice(15, "15 minutes"),
            new RouteChoice(JevRoute.Auto, "Automatic"),
            new ServiceAlert(InfoBarSeverity.Warning, "Your OpenRouter account is out of credits", "Add credits."),
            new PauseChoice("1 hour", new RelayCommand(() => { })),
            new StepDot(OnboardingStep.Connect) { IsCurrent = true },
            new PracticeFieldViewModel("ZIP code")
        ];
        Assert.Equal(
        [
            "Slack, slack.exe",
            "Visual Studio Code, code.exe",
            "Given name: First name",
            "Dark",
            "15 minutes",
            "Automatic",
            "Your OpenRouter account is out of credits. Add credits.",
            "1 hour",
            "Step 3 of 6, current",
            "ZIP code"
        ], items.Select(item => item.ToString()));
    });

    private static void AssertReadable(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            Assert.DoesNotContain("SmartPaste.", name, StringComparison.Ordinal);
            Assert.DoesNotContain("ViewModel", name, StringComparison.Ordinal);
            Assert.DoesNotContain(" { ", name, StringComparison.Ordinal);
            Assert.DoesNotContain("•", name, StringComparison.Ordinal);
        }
    }

    // Every name UI Automation would read from the lists under root, items and their parts.
    private static List<string> SpokenNames(DependencyObject root)
    {
        var names = new List<string>();
        foreach (var list in Descendants<ItemsControl>(root))
            if (UIElementAutomationPeer.CreatePeerForElement(list) is { } peer) Collect(peer.GetChildren(), names);
        return names;
    }

    private static void Collect(IEnumerable<AutomationPeer>? peers, List<string> names)
    {
        if (peers is null) return;
        foreach (var peer in peers)
        {
            names.Add(peer.GetName());
            Collect(peer.GetChildren(), names);
        }
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
}
