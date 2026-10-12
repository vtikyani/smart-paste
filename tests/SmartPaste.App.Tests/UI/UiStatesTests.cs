using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Popups;
using SmartPaste.App.UI.Text;
using SmartPaste.App.UI.Theme;
using SmartPaste.App.UI.Tray;
using SmartPaste.Core;

// Renders the flyout and the picker in every state they have, in light and dark, and fails on any WPF binding
// error. Set SMARTPASTE_UI_REVIEW to a folder to keep the renders for a visual review.
public sealed class UiStatesTests : IDisposable
{
    private static readonly string[] FlyoutStates = ["waiting", "parsing", "paused", "off", "attention", "update", "armed"];

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "sp-states-settings-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly string? _review = Environment.GetEnvironmentVariable("SMARTPASTE_UI_REVIEW");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    [Fact]
    public void FlyoutRendersEveryStateWithoutBindingErrors() => WpfHost.Run(() =>
    {
        using var errors = new BindingErrors();
        var settings = new SettingsStore(_settingsPath);
        using var signals = new SystemSignals(Dispatcher.CurrentDispatcher);
        using var theme = new ThemeService(Application.Current, settings, signals);
        theme.Start();
        foreach (var preference in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            settings.Update(s => s with { Theme = preference });
            foreach (var name in FlyoutStates)
            {
                var bitmap = RenderFlyout(settings, name, preference == ThemePreference.Dark);
                Assert.True(UiRender.VisiblePixels(bitmap) > 10_000, name);
                Keep(bitmap, $"flyout-{name}-{preference.ToString().ToLowerInvariant()}.png");
            }
        }
        Assert.True(errors.Text.Length == 0, errors.Text);
    });

    [Fact]
    public void PickerRendersEveryModeWithoutBindingErrors() => WpfHost.Run(() =>
    {
        using var errors = new BindingErrors();
        var settings = new SettingsStore(_settingsPath);
        using var signals = new SystemSignals(Dispatcher.CurrentDispatcher);
        using var theme = new ThemeService(Application.Current, settings, signals);
        theme.Start();
        var anchor = new ScreenRect(0, 0, 240, 28);
        Candidate[] streets =
        [
            new(FieldKeys.Street, "billing address", "18 Harbor View Rd"),
            new(FieldKeys.Street, "shipping address", "123 Main St")
        ];
        foreach (var preference in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            settings.Update(s => s with { Theme = preference });
            var suffix = preference.ToString().ToLowerInvariant();

            var ambiguous = new PickerWindow(new PickerViewModel());
            ambiguous.ViewModel.Load(new PickRequest(streets, PickMode.Ambiguous, anchor, "Street address"));
            Assert.All(ambiguous.ViewModel.Rows, row => Assert.NotNull(row.Entity));
            Keep(Render(ambiguous), $"picker-entities-{suffix}.png");

            var all = new PickerWindow(new PickerViewModel());
            var values = DocSample.Profile(DateTimeOffset.Now).Entities
                .SelectMany(e => e.Fields.Select(f => new Candidate(f.Key, e.Label, f.Value))).ToArray();
            all.ViewModel.Load(new PickRequest(values, PickMode.AllValues, anchor, null));
            foreach (var c in "spr") all.ViewModel.Type(c);
            Assert.Equal("Springfield", Assert.Single(all.ViewModel.Rows).Value);
            Keep(Render(all), $"picker-filter-{suffix}.png");

            var none = new PickerWindow(new PickerViewModel());
            none.ViewModel.Load(new PickRequest(values, PickMode.AllValues, anchor, null));
            foreach (var c in "zzz") none.ViewModel.Type(c);
            Assert.True(none.ViewModel.HasNoMatch);
            Keep(Render(none), $"picker-nomatch-{suffix}.png");

            foreach (var window in new[] { ambiguous, all, none }) window.Close();
        }
        Assert.True(errors.Text.Length == 0, errors.Text);
    });

    private static BitmapSource Render(PickerWindow picker)
    {
        var bitmap = UiRender.Element(UiRender.Detach(picker), scale: 2);
        Assert.True(UiRender.VisiblePixels(bitmap) > 10_000);
        return bitmap;
    }

    private static BitmapSource RenderFlyout(SettingsStore settings, string name, bool dark)
    {
        var clock = new FixedClock(DateTimeOffset.Now);
        var state = new AppState();
        using var controller = new AppController(state, settings, Dispatcher.CurrentDispatcher);
        state.JevStatus = ServiceStatus.Ready;
        state.ParsingStatus = ServiceStatus.Ready;
        switch (name)
        {
            case "parsing":
                state.IsParsing = true;
                break;
            case "paused":
                state.PausedUntil = clock.Now.AddMinutes(45);
                break;
            case "off":
                state.IsEnabled = false;
                break;
            case "attention":
                state.Armed = DocSample.Armed(clock.Now);
                state.JevStatus = ServiceStatus.KeyRejected;
                break;
            case "update":
                state.Update = new SampleUpdate("1.3.0");
                break;
            case "armed":
                state.Armed = DocSample.Armed(clock.Now);
                foreach (var entry in DocSample.Activity(clock.Now)) state.AddActivity(entry);
                break;
        }
        using var model = new FlyoutViewModel(state, controller, settings, new RecordingNavigator(), clock);
        model.Opened();
        var expected = name switch
        {
            "parsing" => model.IsParsing,
            "paused" => model.IsPaused && model.StatusLine.StartsWith("Paused until ", StringComparison.Ordinal),
            "off" => model.IsOff && !model.IsOn && model.StatusLine == "Off",
            "attention" => model.IsArmed && model.Services.Tone == Tone.Critical,
            "update" => model.UpdateVersion == "1.3.0" && model.IsWaiting,
            "armed" => model.IsArmed && model.Armed!.Groups.SelectMany(g => g.Rows).All(r => r.DisplayValue == DisplayText.Masked),
            _ => model.IsWaiting && !model.HasRecent
        };
        Assert.True(expected, name);

        var window = new FlyoutWindow(model);
        try
        {
            var frame = UiRender.FlyoutFrame(UiRender.Detach(window), dark);
            UiRender.Element(frame, width: 360 + 72);
            UiRender.Settle(TimeSpan.FromMilliseconds(250));
            return UiRender.Element(frame, scale: 2, width: 360 + 72);
        }
        finally
        {
            model.Closed();
            window.Close();
        }
    }

    private void Keep(BitmapSource bitmap, string name)
    {
        if (!string.IsNullOrWhiteSpace(_review)) UiRender.SavePng(bitmap, Path.Combine(_review, name));
    }

    private sealed class SampleUpdate(string version) : AvailableUpdate
    {
        public override string Version { get; } = version;

        public override string? ReleaseNotesMarkdown => null;
    }
}
