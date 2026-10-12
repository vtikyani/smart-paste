using System.Windows.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal partial class PrivacyPage : Page
{
    public PrivacyPage(PrivacyViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
