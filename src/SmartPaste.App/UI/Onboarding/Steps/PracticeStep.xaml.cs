using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartPaste.App.UI.Onboarding.Steps;

// Ctrl+V in a practice field never does a normal paste: the paste command is caught on its way down to the text
// box (Preview), so the practice pipeline decides instead. This covers Ctrl+V, Shift+Insert, and the context menu.
internal partial class PracticeStep : UserControl
{
    public PracticeStep()
    {
        InitializeComponent();
        CommandManager.AddPreviewCanExecuteHandler(this, OnPreviewCanExecute);
        CommandManager.AddPreviewExecutedHandler(this, OnPreviewExecuted);
    }

    private PracticeViewModel? Practice => (DataContext as OnboardingViewModel)?.Practice;

    private static PracticeFieldViewModel? FieldFor(object source) =>
        source is TextBox { DataContext: PracticeFieldViewModel field } ? field : null;

    // Paste is always available in a practice field, even with an empty clipboard, so the field can say why
    // nothing happened.
    private void OnPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || FieldFor(e.OriginalSource) is null) return;
        e.CanExecute = true;
        e.Handled = true;
    }

    private async void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || FieldFor(e.OriginalSource) is not { } field || Practice is not { } practice)
            return;
        e.Handled = true;
        await practice.PasteAsync(field);
    }
}
