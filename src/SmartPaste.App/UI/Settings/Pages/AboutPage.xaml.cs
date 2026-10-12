using System.Windows.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal partial class AboutPage : Page
{
    public AboutPage(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
