using System.Windows;
using System.Windows.Input;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Popups;

// The picker: a short list of values next to the field, shown without taking focus from the target app. Keys
// reach it through the keyboard hook (see PickSession); the mouse works directly.
internal partial class PickerWindow
{
    // Space between the field and the picker.
    private const double Gap = 6;

    public PickerWindow(PickerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public PickerViewModel ViewModel { get; }

    // A row was clicked.
    public event EventHandler<PickerRow>? RowClicked;

    // Shows the picker for the view model's current content, below the anchor (above it when there is no room).
    public void Present(ScreenRect anchor)
    {
        // The width is settled once per request, so filtering never makes the panel jump.
        Panel.Width = double.NaN;
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Panel.Width = Panel.DesiredSize.Width;

        var below = Place(anchor);
        FadeIn(TimeSpan.FromMilliseconds(120), below ? -4 : 4);

        var title = ViewModel.Title.EndsWith('?') ? ViewModel.Title : ViewModel.Title + ".";
        var count = Math.Min(ViewModel.Rows.Count, PickerViewModel.Numbered);
        Announce($"{title} {ViewModel.Rows.Count} choices. Press 1 to {count} to choose, or Escape to paste the original.");
    }

    // Keeps the picker attached to its anchor after the list changed size.
    public void Refit(ScreenRect anchor)
    {
        if (IsShowing) Place(anchor);
    }

    public void Dismiss(bool immediately)
    {
        if (immediately) HideNow();
        else FadeOutAndHide(TimeSpan.FromMilliseconds(90));
    }

    public bool Contains(int x, int y)
    {
        if (!IsShowing) return false;
        var bounds = SurfaceBounds();
        return x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
    }

    // Scrolls the selected row into view in a long list.
    public void RevealSelection()
    {
        UpdateLayout();
        if (ViewModel.SelectedIndex >= 0 && List.ItemContainerGenerator.ContainerFromIndex(ViewModel.SelectedIndex) is FrameworkElement row)
            row.BringIntoView();
    }

    private bool Place(ScreenRect anchor)
    {
        var below = true;
        ShowAt(anchor, (width, height, display) =>
        {
            var (x, y) = PopupPlacement.BelowOrAbove(anchor, width, height, display.WorkArea, display.ToPixels(Gap));
            below = y >= anchor.Bottom;
            return (x, y);
        });
        return below;
    }

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PickerRow row) return;
        e.Handled = true;
        RowClicked?.Invoke(this, row);
    }
}
