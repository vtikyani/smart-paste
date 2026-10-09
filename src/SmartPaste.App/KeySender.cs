using System.Runtime.InteropServices;
using SmartPaste.App.Native;

namespace SmartPaste.App;
internal static class KeySender
{
    internal static bool SendPaste()
    {
        if(Marshal.SizeOf<Win32.Input>()!=40) return false;
        bool Down(int vk)=>(Win32.GetAsyncKeyState(vk)&0x8000)!=0;
        bool ctrl=Down(Win32.VkControl), shift=Down(Win32.VkShift);
        var inputs=new List<Win32.Input>(6);
        if(shift) inputs.Add(Key(Win32.VkShift,true));
        if(!ctrl) inputs.Add(Key(Win32.VkControl,false));
        inputs.Add(Key(Win32.VkV,false)); inputs.Add(Key(Win32.VkV,true));
        if(!ctrl) inputs.Add(Key(Win32.VkControl,true));
        if(shift) inputs.Add(Key(Win32.VkShift,false));
        return Win32.SendInput((uint)inputs.Count,inputs.ToArray(),40)==inputs.Count;
    }
    private static Win32.Input Key(int vk,bool up) => new() { Type=Win32.InputKeyboard, Keyboard=new Win32.KeybdInput {
        Vk=(ushort)vk, Scan=(ushort)Win32.MapVirtualKey((uint)vk,0), Flags=up?Win32.KeyeventfKeyup:0,
        ExtraInfo=KeyboardHook.InjectedMarker } };
}
