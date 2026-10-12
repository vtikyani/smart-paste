using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Popups;
using SmartPaste.App.UI.Theme;
using SmartPaste.App.UI.Tray;
using Wpf.Ui.Appearance;

// Renders the README's screenshots from the real windows and real state, in light and dark. It always renders, so a
// broken resource key or binding fails here. The images go to the folder named by SMARTPASTE_DOC_IMAGES when it is
// set (for example docs/images) and to a throwaway folder otherwise.
public sealed class DocImagesTests : IDisposable
{
    private static readonly Color WindowsDefaultAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "sp-doc-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    [Fact]
    public void ReadmeImagesRenderFromTheRealWindows()
    {
        var requested = Environment.GetEnvironmentVariable("SMARTPASTE_DOC_IMAGES");
        var folder = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Path.GetTempPath(), "sp-doc-images-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(requested);
        try
        {
            var written = WpfHost.Run(() => Render(folder));
            Assert.Equal(7, written.Count);
            foreach (var file in written) Assert.True(new FileInfo(file).Length > 1000, file);
        }
        finally
        {
            if (string.IsNullOrWhiteSpace(requested) && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private List<string> Render(string folder)
    {
        var written = new List<string>();
        var settings = new SettingsStore(_settingsPath);
        using var signals = new SystemSignals(Dispatcher.CurrentDispatcher);
        using var theme = new ThemeService(Application.Current, settings, signals);
        theme.Start();
        foreach (var (preference, name) in new[] { (ThemePreference.Light, "light"), (ThemePreference.Dark, "dark") })
        {
            settings.Update(s => s with { Theme = preference });
            var dark = preference == ThemePreference.Dark;
            // The app follows this PC's accent color; the README shows Windows' default blue.
            ApplicationAccentColorManager.Apply(WindowsDefaultAccent, dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            written.Add(Save(Flyout(settings, dark), folder, $"flyout-{name}.png"));
            written.Add(Save(Picker(), folder, $"picker-{name}.png"));
            written.Add(Save(Chips(), folder, $"chips-{name}.png"));
        }
        written.Add(Save(TrayIcons(), folder, "tray-icons.png"));
        return written;
    }

    private static BitmapSource Flyout(SettingsStore settings, bool dark)
    {
        var clock = new FixedClock(DateTimeOffset.Now);
        var state = new AppState { JevStatus = ServiceStatus.Ready, ParsingStatus = ServiceStatus.Ready };
        using var controller = new AppController(state, settings, Dispatcher.CurrentDispatcher);
        state.Armed = DocSample.Armed(clock.Now);
        foreach (var entry in DocSample.Activity(clock.Now)) state.AddActivity(entry);
        using var model = new FlyoutViewModel(state, controller, settings, new RecordingNavigator(), clock);
        model.Opened();
        model.RevealValues = true;
        var window = new FlyoutWindow(model);
        try
        {
            var frame = UiRender.FlyoutFrame(UiRender.Detach(window), dark);
            // Controls such as the toggle switch animate into their state; let that finish before the snapshot.
            UiRender.Element(frame, width: 360 + 72);
            UiRender.Settle(TimeSpan.FromMilliseconds(400));
            return UiRender.Element(frame, scale: 2, width: 360 + 72);
        }
        finally
        {
            model.Closed();
            window.Close();
        }
    }

    private static BitmapSource Picker()
    {
        var picker = new PickerWindow(new PickerViewModel());
        try
        {
            picker.ViewModel.Load(DocSample.NamePick(new ScreenRect(0, 0, 240, 28)));
            return UiRender.Element(UiRender.Detach(picker), scale: 2);
        }
        finally
        {
            picker.Close();
        }
    }

    private static BitmapSource Chips()
    {
        var hint = new ChipWindow();
        var working = new ChipWindow();
        var pasted = new ChipWindow();
        try
        {
            hint.SetHint(new FieldHint("First name", "Jordan", NeedsChoice: false));
            working.SetWorking();
            pasted.SetPasted("First name");
            var stack = new StackPanel();
            foreach (var chip in new[] { hint, working, pasted })
            {
                var content = UiRender.Detach(chip);
                content.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(content);
            }
            return UiRender.Element(stack, scale: 2);
        }
        finally
        {
            hint.Close();
            working.Close();
            pasted.Close();
        }
    }

    // Every tray state at 200 % scale on a dark and a light taskbar.
    private static BitmapSource TrayIcons()
    {
        (TrayStatus Status, int Frame, string Label)[] states =
        [
            (TrayStatus.Ready, 0, "Ready"), (TrayStatus.Armed, 0, "Armed"), (TrayStatus.Parsing, 4, "Reading"),
            (TrayStatus.Paused, 0, "Paused"), (TrayStatus.Off, 0, "Off"), (TrayStatus.Attention, 0, "Needs you")
        ];
        const int icon = 32, cell = 96, rowHeight = 88;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rows = new[] { (TaskbarTheme.Dark, Color.FromRgb(0x1C, 0x1C, 0x1C), Colors.White), (TaskbarTheme.Light, Color.FromRgb(0xEE, 0xEE, 0xEE), Color.FromRgb(0x1A, 0x1A, 0x1A)) };
            for (var r = 0; r < rows.Length; r++)
            {
                var (taskbar, background, text) = rows[r];
                dc.DrawRoundedRectangle(new SolidColorBrush(background), null, new Rect(0, r * (rowHeight + 8), states.Length * cell, rowHeight), 8, 8);
                for (var i = 0; i < states.Length; i++)
                {
                    var x = i * cell + (cell - icon) / 2.0;
                    var y = r * (rowHeight + 8) + 16;
                    dc.DrawImage(TrayIconRenderer.Render(states[i].Status, taskbar, icon, states[i].Frame), new Rect(x, y, icon, icon));
                    var caption = new FormattedText(states[i].Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromArgb(0xC8, text.R, text.G, text.B)), 1.0);
                    dc.DrawText(caption, new Point(i * cell + (cell - caption.Width) / 2, y + icon + 12));
                }
            }
        }
        var bitmap = new RenderTargetBitmap(states.Length * cell, 2 * rowHeight + 8, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static string Save(BitmapSource bitmap, string folder, string name)
    {
        var path = Path.Combine(folder, name);
        UiRender.SavePng(bitmap, path);
        return path;
    }
}
