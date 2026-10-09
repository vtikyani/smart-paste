using SmartPaste.App.Native;
using SmartPaste.Core;

namespace SmartPaste.App;
internal sealed class PickerForm : Form
{
    private readonly IReadOnlyList<Candidate> _candidates;
    private readonly System.Windows.Forms.Timer _timer=new() { Interval=15000 };
    internal Candidate? Chosen { get; private set; }
    internal PickerForm(IReadOnlyList<Candidate> candidates,IntPtr target)
    {
        _candidates=candidates.Take(9).ToArray();
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
        StartPosition=FormStartPosition.Manual; AutoSize=true; AutoSizeMode=AutoSizeMode.GrowAndShrink;
        var panel=new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Padding=new Padding(6) };
        for(int i=0;i<_candidates.Count;i++)
        {
            int index=i; var candidate=_candidates[i];
            string value=candidate.Value.Length>50?candidate.Value[..50]+"…":candidate.Value;
            var button=new Button { Text=$"{i+1}  {candidate.EntityLabel} · {value}",AutoSize=true,MinimumSize=new Size(230,30),TextAlign=ContentAlignment.MiddleLeft };
            button.Click+=(_,_)=>Choose(index); panel.Controls.Add(button);
        }
        var original=new Button { Text="Paste original",AutoSize=true,MinimumSize=new Size(230,30) };
        original.Click+=(_,_)=>Close(); panel.Controls.Add(original); Controls.Add(panel);
        uint thread=Win32.GetWindowThreadProcessId(target,out _);
        var info=new Win32.GuiThreadInfo { Size=System.Runtime.InteropServices.Marshal.SizeOf<Win32.GuiThreadInfo>() };
        if(Win32.GetGUIThreadInfo(thread,ref info) && info.HwndCaret!=IntPtr.Zero)
        {
            var point=new Win32.Point(info.CaretRect.Left,info.CaretRect.Bottom);
            Win32.ClientToScreen(info.HwndCaret,ref point); Location=new Point(point.X,point.Y+5);
        }
        else { Win32.GetCursorPos(out var cursor); Location=new Point(cursor.X,cursor.Y); }
        Load+=(_,_)=> { var area=Screen.FromPoint(Location).WorkingArea;
            Location=new Point(Math.Clamp(Location.X,area.Left,Math.Max(area.Left,area.Right-Width)),Math.Clamp(Location.Y,area.Top,Math.Max(area.Top,area.Bottom-Height))); };
        _timer.Tick+=(_,_)=>Close(); Shown+=(_,_)=> { _timer.Start(); Activate(); };
        KeyPreview=true;
        KeyDown+=(_,e)=> { if(e.KeyCode==Keys.Escape) Close();
            else if(e.KeyCode>=Keys.D1 && e.KeyCode<Keys.D1+_candidates.Count) Choose(e.KeyCode-Keys.D1); };
    }
    private void Choose(int index) { Chosen=_candidates[index]; Close(); }
    protected override void Dispose(bool disposing) { if(disposing) _timer.Dispose(); base.Dispose(disposing); }
}
