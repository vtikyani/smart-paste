using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Settings;

// A message at the top of a window, for things that went wrong outside the control the user just used
// (a settings file that couldn't be written, a link Windows couldn't open).
internal sealed partial class Notice : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    public void Show(InfoBarSeverity severity, string title, string message)
    {
        Severity = severity;
        Title = title;
        Message = message;
        IsOpen = true;
    }

    [RelayCommand]
    public void Dismiss() => IsOpen = false;
}
