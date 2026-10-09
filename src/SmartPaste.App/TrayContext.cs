using SmartPaste.App.Native;

namespace SmartPaste.App;
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings=AppSettings.Load();
    private readonly HttpClient _http=new();
    private readonly ProfileStore _store=new();
    private readonly Control _dispatcher=new();
    private readonly ClipboardWatcher _watcher;
    private readonly CopyPipeline _pipeline;
    private readonly FocusInspector _focus=new();
    private readonly LabelOcr _ocr=new();
    private readonly KeyboardHook _hook;
    private readonly NotifyIcon _icon=new();
    private readonly System.Windows.Forms.Timer _expiry=new() { Interval=30000 };
    private readonly ToolStripMenuItem _enabled=new("Enabled");
    private readonly ToolStripMenuItem _armed=new("Armed: none") { Enabled=false };
    private readonly bool _firstRun;
    internal TrayContext()
    {
        _firstRun=!File.Exists(Path.Combine(AppSettings.DirectoryPath,"settings.json"));
        _dispatcher.CreateControl(); var _=_dispatcher.Handle;
        _watcher=new ClipboardWatcher(_settings);
        _pipeline=new CopyPipeline(_watcher,_store,_http);
        var gate=new InterceptGate(_settings,_store);
        _hook=new KeyboardHook(gate,_dispatcher);
        var paste=new PasteController(_store,_focus,_ocr,new ClipboardSwap(_watcher),_http);
        _hook.SmartPasteRequested+=paste.SmartPasteRequested;
        var menu=new ContextMenuStrip();
        _enabled.Click+=(_,_)=> { _settings.Enabled=!_settings.Enabled; _settings.Save(); ApplyEnabled(); };
        menu.Items.Add(_enabled); menu.Items.Add(_armed);
        menu.Items.Add("Clear profile",null,(_,_)=>_store.Clear());
        menu.Items.Add("Settings...",null,(_,_)=> { using var form=new SettingsForm(_settings); form.ShowDialog(); });
        menu.Items.Add("Exit",null,(_,_)=>ExitThread());
        _icon.ContextMenuStrip=menu; _icon.Text="Smart Paste: off";
        _store.Changed+=(_,_)=> { if(_dispatcher.IsHandleCreated) _dispatcher.BeginInvoke((Action)RefreshIcon); };
        _expiry.Tick+=(_,_)=>RefreshIcon(); _expiry.Start();
        ApplyEnabled();
        _icon.Visible=true;
        if(_firstRun && CredentialStore.Get(CredentialStore.OpenRouter) is null)
        {
            _icon.BalloonTipTitle="Smart Paste";
            _icon.BalloonTipText="Add your OpenRouter key in Settings. Without it only email, phone, and zip are recognized.";
            _icon.ShowBalloonTip(5000);
        }
    }
    private void ApplyEnabled()
    {
        if(_settings.Enabled) { _watcher.Start(); _hook.Start(); _ocr.WarmUpAsync(); }
        else { _hook.Stop(); _watcher.Stop(); _store.Clear(); }
        RefreshIcon();
    }
    private void RefreshIcon()
    {
        var profile=_store.Current;
        string keys=profile is null?"none":string.Join(", ",profile.Keys.Take(3))+(profile.Keys.Count>3?$" +{profile.Keys.Count-3}":"");
        _armed.Text="Armed: "+keys;
        _enabled.Checked=_settings.Enabled;
        var color=!_settings.Enabled?Color.Gray:profile is null?Color.DodgerBlue:Color.ForestGreen;
        var old=_icon.Icon; _icon.Icon=DrawIcon(color); old?.Dispose();
        string tooltip=!_settings.Enabled?"Smart Paste: off":profile is null?"Smart Paste: on":"Smart Paste: armed: "+keys;
        _icon.Text=tooltip.Length>63?tooltip[..63]:tooltip;
    }
    private static Icon DrawIcon(Color color)
    {
        using var bitmap=new Bitmap(32,32);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush=new SolidBrush(color); using var pen=new Pen(Color.White,2);
            g.FillEllipse(brush,2,2,28,28); using var font=new Font("Segoe UI",18,FontStyle.Bold); g.DrawString("S",font,Brushes.White,6,1);
        }
        IntPtr handle=bitmap.GetHicon();
        try { using var temporary=Icon.FromHandle(handle); return (Icon)temporary.Clone(); }
        finally { Win32.DestroyIcon(handle); }
    }
    protected override void ExitThreadCore()
    {
        _expiry.Stop(); _expiry.Dispose(); _hook.Dispose(); _pipeline.Dispose(); _watcher.Dispose();
        _store.Dispose(); _focus.Dispose(); try { _ocr.DisposeAsync().AsTask().Wait(2000); } catch { Log.Event("ocr_dispose_error"); }
        _http.Dispose(); _icon.Visible=false; _icon.Icon?.Dispose(); _icon.Dispose(); _dispatcher.Dispose();
        base.ExitThreadCore();
    }
}
