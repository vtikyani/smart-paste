using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SmartPaste.App.UI.Onboarding;

internal partial class PasteFlowIllustration : UserControl
{
    private readonly Storyboard _loop;
    private bool _running;
    private bool _finished;

    public PasteFlowIllustration()
    {
        InitializeComponent();
        _loop = (Storyboard)Resources["Loop"];
        // Only loop while on screen: the step is removed from the window when the tour moves on.
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    // The end state, held still: line marked, value in the field. Shown instead of the loop when Windows
    // animation effects are off.
    public void ShowFinished()
    {
        _finished = true;
        Stop();
        LineMarkScale.ScaleX = 1;
        LineMark.Opacity = 1;
        LineInk.Opacity = 0.9;
        FieldMarkScale.ScaleX = 1;
        FieldValue.Opacity = 1;
        FieldValueShift.X = 0;
        CaretShift.X = 188;
        Caret.Opacity = 0;
    }

    private void Start()
    {
        if (_finished) return;
        if (Motion.IsReduced)
        {
            ShowFinished();
            return;
        }
        if (_running) return;
        _loop.Begin(this, isControllable: true);
        _running = true;
    }

    private void Stop()
    {
        if (!_running) return;
        _loop.Remove(this);
        // Remove takes effect on the next animation tick; clearing each property now means the still picture
        // never shows a stray frame of the loop.
        foreach (var timeline in _loop.Children)
        {
            if (FindName(Storyboard.GetTargetName(timeline)) is not IAnimatable target || target is not DependencyObject owner)
                continue;
            var property = DependencyPropertyDescriptor.FromName(Storyboard.GetTargetProperty(timeline).Path, owner.GetType(), owner.GetType());
            if (property is not null) target.BeginAnimation(property.DependencyProperty, null);
        }
        _running = false;
    }
}
