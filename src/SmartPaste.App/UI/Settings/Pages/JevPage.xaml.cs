using System.Windows.Controls;
using System.Windows.Input;

namespace SmartPaste.App.UI.Settings.Pages;

internal partial class JevPage : Page
{
    public JevPage(JevViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // The model id saves when the box loses focus; Enter saves it right away too.
    private void OnModelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
