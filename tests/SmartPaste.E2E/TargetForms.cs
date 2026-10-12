using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace SmartPaste.E2E;

// A form field the scenarios fill: what UI Automation can find it by, and what it is called on screen.
internal sealed record FieldSpec(string AutomationId, string Label);

// A WPF form on its own UI thread. Most fields are labeled the accessible way (LabeledBy); "City" only has a
// label next to it; one field's label is a picture, so only OCR can read it.
internal sealed class WpfTargetForm : IDisposable
{
    public const string Title = "Smart Paste E2E - WPF form";

    public static readonly FieldSpec[] Fields =
    [
        new("e2e-1", "First name"), new("e2e-2", "Last name"), new("e2e-3", "Email address"), new("e2e-4", "Phone"),
        new("e2e-5", "Street address"), new("e2e-6", "City"), new("e2e-7", "State"), new("e2e-8", "ZIP code"),
        new("e2e-9", "Card number"), new("e2e-10", "Email")
    ];

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Dispatcher? _dispatcher;
    private Window? _window;

    public WpfTargetForm()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "E2E WPF form" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("WPF form did not start.");
    }

    public IntPtr Handle { get; private set; }

    // Clears every field, so each scenario starts empty.
    public void Clear() => _dispatcher!.Invoke(() =>
    {
        foreach (var box in FindAll<TextBox>(_window!)) box.Clear();
    });

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        var grid = new Grid { Margin = new Thickness(24) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        for (var i = 0; i < Fields.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var spec = Fields[i];
            var box = new TextBox { Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(4), FontSize = 15 };
            AutomationProperties.SetAutomationId(box, spec.AutomationId);
            FrameworkElement label;
            if (spec.AutomationId == "e2e-10")
            {
                label = PictureOf(spec.Label);
            }
            else
            {
                var text = new Label { Content = spec.Label, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
                label = text;
                // "City" deliberately has no programmatic link to its label: only the label beside it says what it is.
                if (spec.AutomationId != "e2e-6") AutomationProperties.SetLabeledBy(box, text);
            }
            Grid.SetRow(label, i);
            Grid.SetRow(box, i);
            Grid.SetColumn(box, 1);
            grid.Children.Add(label);
            grid.Children.Add(box);
        }
        _window = new Window
        {
            Title = Title,
            Content = grid,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.White
        };
        _window.Loaded += (_, _) =>
        {
            Handle = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            _ready.Set();
        };
        _window.Show();
        Dispatcher.Run();
    }

    // The label as pixels only: UI Automation sees an unnamed image.
    private static System.Windows.Controls.Image PictureOf(string text)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 15 * 1.5, Brushes.Black, 1.5);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, formatted.Width + 8, formatted.Height + 8));
            dc.DrawText(formatted, new Point(4, 4));
        }
        var bitmap = new RenderTargetBitmap((int)(formatted.Width + 8), (int)(formatted.Height + 8), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return new System.Windows.Controls.Image { Source = bitmap, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center };
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }

    public void Dispose()
    {
        _dispatcher?.Invoke(() => _window?.Close());
        _dispatcher?.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}

// A classic Win32 form (WinForms text boxes are native Edit controls). Length limits here are visible to
// Smart Paste through EM_GETLIMITTEXT, so it can shape values: 5-digit ZIP, 2-letter state, digits-only phone.
internal sealed class Win32TargetForm : IDisposable
{
    public const string Title = "Smart Paste E2E - Win32 form";

    public static readonly (FieldSpec Spec, int MaxLength)[] Fields =
    [
        (new("w32-first", "First name"), 0),
        (new("w32-state", "State (2 letters)"), 2),
        (new("w32-zip", "ZIP code"), 5),
        (new("w32-phone", "Phone (digits)"), 10)
    ];

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private WinForms.Form? _form;

    public Win32TargetForm()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "E2E Win32 form" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Win32 form did not start.");
    }

    public IntPtr Handle { get; private set; }

    public void Clear() => _form!.Invoke(() =>
    {
        foreach (var box in _form.Controls.OfType<WinForms.TextBox>()) box.Clear();
    });

    private void Run()
    {
        WinForms.Application.EnableVisualStyles();
        _form = new WinForms.Form
        {
            Text = Title,
            AutoScaleMode = WinForms.AutoScaleMode.Dpi,
            ClientSize = new System.Drawing.Size(520, 40 + Fields.Length * 44),
            StartPosition = WinForms.FormStartPosition.CenterScreen,
            Font = new System.Drawing.Font("Segoe UI", 11f)
        };
        var tab = 0;
        for (var i = 0; i < Fields.Length; i++)
        {
            var (spec, max) = Fields[i];
            // Windows names an edit control after the label just before it in tab order.
            var label = new WinForms.Label { Text = spec.Label, Left = 20, Top = 24 + i * 44, Width = 170, TabIndex = tab++ };
            var box = new WinForms.TextBox { Left = 200, Top = 20 + i * 44, Width = 290, TabIndex = tab++, AccessibleName = null };
            if (max > 0) box.MaxLength = max;
            box.Name = spec.AutomationId;
            _form.Controls.Add(label);
            _form.Controls.Add(box);
        }
        _form.Shown += (_, _) =>
        {
            Handle = _form.Handle;
            _ready.Set();
        };
        WinForms.Application.Run(_form);
    }

    public void Dispose()
    {
        try { _form?.Invoke(() => _form.Close()); }
        catch (InvalidOperationException) { }
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
