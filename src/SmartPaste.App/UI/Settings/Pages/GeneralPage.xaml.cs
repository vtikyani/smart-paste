using System.Windows.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal partial class GeneralPage : Page
{
    public GeneralPage(GeneralViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
