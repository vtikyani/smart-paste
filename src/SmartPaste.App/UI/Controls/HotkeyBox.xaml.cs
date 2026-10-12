using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartPaste.App.UI.Controls;

// Records a global shortcut into Hotkey (the stored text form, such as "Win+Alt+V"; null means off).
internal partial class HotkeyBox : UserControl
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyBox)d).Render()));

    // Why the last key press wasn't accepted, or "" when there's nothing to say. Bind it to show the reason.
    public static readonly DependencyProperty ProblemProperty = DependencyProperty.Register(
        nameof(Problem), typeof(string), typeof(HotkeyBox), new FrameworkPropertyMetadata(""));

    private static readonly DependencyPropertyKey IsRecordingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsRecording), typeof(bool), typeof(HotkeyBox), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsRecordingProperty = IsRecordingPropertyKey.DependencyProperty;

    private static int _recordingCount;

    public HotkeyBox()
    {
        InitializeComponent();
        // A closed window can't be listening any more.
        Unloaded += (_, _) => StopRecording();
        Render();
    }

    // True while any recorder in the app is listening. The hotkey manager can pause global shortcuts meanwhile,
    // so pressing a shortcut that is already registered records it instead of running it.
    public static bool IsAnyRecording => _recordingCount > 0;

    public static event EventHandler? RecordingChanged;

    public string? Hotkey
    {
        get => (string?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public string Problem
    {
        get => (string)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    public bool IsRecording
    {
        get => (bool)GetValue(IsRecordingProperty);
        private set => SetValue(IsRecordingPropertyKey, value);
    }

    public void StartRecording()
    {
        if (IsRecording) return;
        Problem = "";
        IsRecording = true;
        _recordingCount++;
        RecordingChanged?.Invoke(this, EventArgs.Empty);
        Focus();
        Render();
    }

    public void CancelRecording() => StopRecording();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsRecording && !e.Handled && e.Key is Key.Enter or Key.Space)
        {
            StartRecording();
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsRecording && Record(RealKey(e), Keyboard.Modifiers)) e.Handled = true;
    }

    // One key press while recording. Returns false for keys left to WPF (Tab, so focus can move on).
    internal bool Record(Key key, ModifierKeys modifiers)
    {
        // Plain Tab still moves focus on, so keyboard users can always leave; leaving cancels.
        if (key == Key.Tab && (modifiers & ~ModifierKeys.Shift) == ModifierKeys.None) return false;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopRecording();
            return true;
        }
        if (HotkeyGesture.IsModifierKey(key) || key == Key.None)
        {
            Render();
            return true;
        }

        var gesture = new HotkeyGesture(modifiers, key);
        if (gesture.Problem() is { } problem)
        {
            Problem = problem;
            Render();
            return true;
        }
        Problem = "";
        Hotkey = gesture.ToString();
        StopRecording();
        return true;
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (!IsRecording) return;
        e.Handled = true;
        Render();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (IsRecording && !IsKeyboardFocusWithin) StopRecording();
    }

    // Alt turns keys into "system" keys and an input method can swallow them; either way, report the real key.
    private static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key
    };

    private void OnFrameClick(object sender, MouseButtonEventArgs e)
    {
        if (IsRecording) return;
        StartRecording();
        e.Handled = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Problem = "";
        Hotkey = null;
    }

    private void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _recordingCount--;
        RecordingChanged?.Invoke(this, EventArgs.Empty);
        Render();
    }

    private void Render()
    {
        if (IsRecording)
        {
            var held = HotkeyGesture.ModifierNames(Keyboard.Modifiers);
            Caps.ItemsSource = held;
            Prompt.Text = held.Count == 0 ? "Press a shortcut, or Esc to cancel" : "then a key";
            ClearButton.Visibility = Visibility.Collapsed;
            Frame.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
            Frame.SetResourceReference(Border.BackgroundProperty, "ControlFillColorInputActiveBrush");
            AutomationProperties.SetItemStatus(this, "Listening for a shortcut");
            return;
        }

        Frame.SetResourceReference(Border.BorderBrushProperty, "ControlElevationBorderBrush");
        Frame.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        if (HotkeyGesture.TryParse(Hotkey, out var gesture))
        {
            Caps.ItemsSource = gesture.DisplayParts;
            Prompt.Text = "";
            ClearButton.Visibility = Visibility.Visible;
            AutomationProperties.SetItemStatus(this, string.Join(" ", gesture.DisplayParts));
        }
        else
        {
            Caps.ItemsSource = null;
            Prompt.Text = "Select to set a shortcut";
            ClearButton.Visibility = Visibility.Collapsed;
            AutomationProperties.SetItemStatus(this, "Off");
        }
    }
}
