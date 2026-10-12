using System.Windows;
using System.Windows.Controls;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Text;

namespace SmartPaste.App.UI.Popups;

// A small pill next to the field that the mouse passes straight through: "Thinking" while a decision takes a
// moment, a check with what was pasted, or a hint saying what Ctrl+V will paste. One window per use, reused.
internal partial class ChipWindow
{
    private const double Gap = 6;

    public ChipWindow() : base(clickThrough: true)
    {
        InitializeComponent();
    }

    public void ShowWorking(ScreenRect anchor)
    {
        SetWorking();
        PresentNear(anchor);
    }

    public void ShowPasted(ScreenRect anchor, string what)
    {
        SetPasted(what);
        PresentNear(anchor);
        Announce("Pasted " + PastedText.Text);
    }

    public void ShowNotice(ScreenRect anchor, string message)
    {
        NoticeText.Text = DisplayText.OneLine(message, 60);
        Select(Notice);
        PresentNear(anchor);
        Announce(NoticeText.Text);
    }

    // Updates the text in place when the hint is already showing for this field.
    public void ShowHint(ScreenRect field, FieldHint hint, bool animate)
    {
        SetHint(hint);
        var above = true;
        ShowAt(field, (width, height, display) =>
        {
            var (x, y) = PopupPlacement.FieldHint(field, width, height, display.WorkArea, display.ToPixels(4));
            above = y < field.Top;
            return (x, y);
        });
        if (animate) FadeIn(TimeSpan.FromMilliseconds(150), above ? 3 : -3);
    }

    public void Dismiss(TimeSpan fade) => FadeOutAndHide(fade);

    internal void SetWorking() => Select(Working);

    internal void SetPasted(string what)
    {
        PastedText.Text = DisplayText.OneLine(what, 40);
        Select(Pasted);
    }

    internal void SetHint(FieldHint hint)
    {
        if (hint.NeedsChoice)
        {
            // The softer variant: Ctrl+V will ask, so there is no single value to promise.
            HintName.Text = "choose";
            HintName.FontWeight = FontWeights.Normal;
            HintName.SetResourceReference(TextBlock.ForegroundProperty, "Brand.PopupSubtleTextBrush");
            HintValue.Text = "";
        }
        else
        {
            HintName.Text = DisplayText.OneLine(hint.KeyDisplayName, 28);
            HintName.FontWeight = FontWeights.SemiBold;
            HintName.SetResourceReference(TextBlock.ForegroundProperty, "Brand.PopupTextBrush");
            HintValue.Text = string.IsNullOrWhiteSpace(hint.ValuePreview) ? "" : " · " + DisplayText.OneLine(hint.ValuePreview, 28);
        }
        Select(Hint);
    }

    private void PresentNear(ScreenRect anchor)
    {
        var below = true;
        ShowAt(anchor, (width, height, display) =>
        {
            var (x, y) = PopupPlacement.BelowOrAbove(anchor, width, height, display.WorkArea, display.ToPixels(Gap));
            below = y >= anchor.Bottom;
            return (x, y);
        });
        FadeIn(TimeSpan.FromMilliseconds(120), below ? -3 : 3);
    }

    private void Select(FrameworkElement content)
    {
        Working.Visibility = content == Working ? Visibility.Visible : Visibility.Collapsed;
        Pasted.Visibility = content == Pasted ? Visibility.Visible : Visibility.Collapsed;
        Notice.Visibility = content == Notice ? Visibility.Visible : Visibility.Collapsed;
        Hint.Visibility = content == Hint ? Visibility.Visible : Visibility.Collapsed;
    }
}
