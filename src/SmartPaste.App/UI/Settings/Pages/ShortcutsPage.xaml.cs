using System.Windows.Controls;

namespace SmartPaste.App.UI.Settings.Pages;

internal partial class ShortcutsPage : Page
{
    public ShortcutsPage(ShortcutsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
