using System.Diagnostics;

namespace SmartPaste.App;
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _openrouter=new() { UseSystemPasswordChar=true,Width=270 };
    private readonly TextBox _typesafe=new() { UseSystemPasswordChar=true,Width=270 };
    private readonly CheckBox _screenshots=new() { Text="Parse screenshots",AutoSize=true };
    private readonly CheckBox _startup=new() { Text="Start at login",AutoSize=true };
    internal SettingsForm(AppSettings settings)
    {
        _settings=settings; Text="Smart Paste settings"; Width=340; Height=260; FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterScreen;
        var panel=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12) };
        _openrouter.PlaceholderText=CredentialStore.Get(CredentialStore.OpenRouter) is null?"OpenRouter key":"saved";
        _typesafe.PlaceholderText=CredentialStore.Get(CredentialStore.TypeSafe) is null?"TypeSafe key":"saved";
        panel.Controls.Add(new Label { Text="OpenRouter key",AutoSize=true }); panel.Controls.Add(_openrouter);
        panel.Controls.Add(new Label { Text="TypeSafe key",AutoSize=true }); panel.Controls.Add(_typesafe);
        _screenshots.Checked=settings.ParseScreenshots; _startup.Checked=StartupRegistration.IsEnabled();
        panel.Controls.Add(_screenshots); panel.Controls.Add(_startup);
        var save=new Button { Text="Save",AutoSize=true }; save.Click+=(_,_)=>Save(); panel.Controls.Add(save);
        var logs=new LinkLabel { Text="Open log folder",AutoSize=true };
        logs.Click+=(_,_)=> { Directory.CreateDirectory(Log.Folder); Process.Start(new ProcessStartInfo(Log.Folder) { UseShellExecute=true }); };
        panel.Controls.Add(logs); Controls.Add(panel);
    }
    private void Save()
    {
        try
        {
            if(!string.IsNullOrWhiteSpace(_openrouter.Text)) CredentialStore.Set(CredentialStore.OpenRouter,_openrouter.Text.Trim());
            if(!string.IsNullOrWhiteSpace(_typesafe.Text)) CredentialStore.Set(CredentialStore.TypeSafe,_typesafe.Text.Trim());
            _settings.ParseScreenshots=_screenshots.Checked; _settings.StartAtLogin=_startup.Checked;
            if(_startup.Checked) StartupRegistration.Enable(); else StartupRegistration.Disable();
            _settings.Save(); Close();
        }
        catch { Log.Event("settings_save_error"); MessageBox.Show("Settings could not be saved.","Smart Paste"); }
    }
}
