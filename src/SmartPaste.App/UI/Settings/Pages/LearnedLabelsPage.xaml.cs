using System.Windows.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal partial class LearnedLabelsPage : Page
{
    public LearnedLabelsPage(LearnedLabelsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
