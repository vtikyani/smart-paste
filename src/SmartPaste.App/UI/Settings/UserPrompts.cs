using System.Windows;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SmartPaste.App.UI.Settings;

// A question before something that can't be undone. ConfirmText names the action ("Remove key"), never "OK".
internal sealed record Confirmation(string Title, string Message, string ConfirmText);

internal interface IUserPrompts
{
    // True when the user chose ConfirmText; false for Cancel, Escape, or closing the window.
    Task<bool> ConfirmAsync(Confirmation confirmation);
}

// Asks inside the window with a Fluent content dialog, so the question stays next to what it is about.
internal sealed class ContentDialogPrompts : IUserPrompts
{
    private readonly ContentDialogHost _host;

    public ContentDialogPrompts(ContentDialogHost host)
    {
        _host = host;
    }

    public async Task<bool> ConfirmAsync(Confirmation confirmation)
    {
        var dialog = new ContentDialog(_host)
        {
            Title = confirmation.Title,
            Content = new TextBlock { Text = confirmation.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 },
            PrimaryButtonText = confirmation.ConfirmText,
            PrimaryButtonAppearance = ControlAppearance.Primary,
            CloseButtonText = "Cancel",
            // Enter must never confirm something destructive by accident.
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
