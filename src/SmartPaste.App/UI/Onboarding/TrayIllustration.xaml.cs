using System.Globalization;
using System.Windows.Controls;

namespace SmartPaste.App.UI.Onboarding;

internal partial class TrayIllustration : UserControl
{
    public TrayIllustration()
    {
        InitializeComponent();
        Loaded += (_, _) => ShowClock(DateTime.Now);
    }

    // The taskbar clock, in the user's own time and date format.
    public void ShowClock(DateTime now)
    {
        Time.Text = now.ToString("t", CultureInfo.CurrentCulture);
        Date.Text = now.ToString("d", CultureInfo.CurrentCulture);
    }
}
