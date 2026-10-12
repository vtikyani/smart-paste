using System.ComponentModel;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Navigation;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Settings;

// What every page of a window shares: the app's services, the window's way of asking questions, and its notice
// banner. Used by the settings window and the welcome tour alike.
internal sealed class SettingsContext
{
    public SettingsContext(UiServices services, IWindowNavigator navigator, IUserPrompts prompts, Dispatcher dispatcher)
    {
        Services = services;
        Navigator = navigator;
        Prompts = prompts;
        Dispatcher = dispatcher;
    }

    public UiServices Services { get; }

    public IWindowNavigator Navigator { get; }

    public IUserPrompts Prompts { get; }

    public Dispatcher Dispatcher { get; }

    public Notice Notice { get; } = new();

    public AppSettings Settings => Services.Settings.Current;

    // Saves a settings change. When the file can't be written, says so and returns false. The change is in effect
    // either way (see SettingsStore.Update); it just may not last.
    public bool TrySave(Func<AppSettings, AppSettings> change) => TrySave(() => Services.Settings.Update(change));

    // Runs an action that saves settings (AppController's, for example), with the same error handling.
    public bool TrySave(Action save)
    {
        try
        {
            save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SettingsStore has logged it already.
            Notice.Show(InfoBarSeverity.Error, "Couldn't save your change",
                "It works for now, but Smart Paste may forget it when it closes. Make sure your disk isn't full.");
            return false;
        }
    }

    // Opens a web page, or says why it couldn't.
    public void OpenUrl(string url)
    {
        try
        {
            Services.Shell.OpenUrl(url);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Error("open_link_error", ex);
            Notice.Show(InfoBarSeverity.Warning, "Couldn't open the link",
                "Windows couldn't open the page. Check that you have a default browser set, then try again.");
        }
    }

    // Runs an action on the UI thread: now when already there, otherwise as soon as the thread is free.
    // For events that other parts of the app raise on their own threads.
    public void Post(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }
}
