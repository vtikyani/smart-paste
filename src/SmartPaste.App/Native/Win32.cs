using System.Runtime.InteropServices;
using System.Text;

namespace SmartPaste.App.Native;
internal static class Win32
{
    internal const int WmClipboardUpdate = 0x031D, WmKeyDown = 0x0100, WmSysKeyDown = 0x0104, WhKeyboardLl = 13;
    internal const int VkV = 0x56, VkControl = 0x11, VkShift = 0x10, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;
    internal const uint CfBitmap = 2, CfMetafilePict = 3, CfDib = 8, CfPalette = 9, CfUnicodeText = 13, CfEnhMetafile = 14, CfDibV5 = 17;
    internal const uint CfOwnerDisplay = 0x80, CfDspBitmap = 0x82, CfDspMetafilePict = 0x83, CfDspEnhMetafile = 0x8E;
    internal const uint GmemMoveable = 2, KeyeventfKeyup = 2, KeyeventfScancode = 8, InputKeyboard = 1;
    internal const uint ProcessQueryLimitedInformation = 0x1000, TokenQuery = 8, TokenElevation = 20;
    internal const uint CredTypeGeneric = 1, CredPersistLocalMachine = 2;
    internal static readonly IntPtr HwndMessage = new(-3);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError=true)] internal static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern int GetClipboardFormatName(uint format, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] internal static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr GlobalFree(IntPtr memory);
    [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", SetLastError=true)] internal static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    internal const uint WmQuit = 0x0012;
    [DllImport("user32.dll")] internal static extern int GetMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    internal const uint PmNoRemove = 0;
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);
    [StructLayout(LayoutKind.Sequential)] internal struct Msg { internal IntPtr Hwnd; internal uint Message; internal IntPtr WParam, LParam; internal uint Time; internal Point Pt; internal uint Private; }
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref uint length);
    [DllImport("advapi32.dll", SetLastError=true)] internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)] internal static extern bool GetTokenInformation(IntPtr token, uint infoClass, out int elevation, int length, out int returned);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, EntryPoint="CredReadW", SetLastError=true)] internal static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, EntryPoint="CredWriteW", SetLastError=true)] internal static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, EntryPoint="CredDeleteW", SetLastError=true)] internal static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll", EntryPoint="CredFree")] internal static extern void CredFree(IntPtr credential);
    internal static bool OpenClipboardRetry(IntPtr owner = default)
    {
        for (int i=0; i<10; i++) { if (OpenClipboard(owner)) return true; Thread.Sleep(15); }
        return false;
    }
    internal static byte[]? CopyGlobal(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        ulong size=GlobalSize(handle).ToUInt64();
        if (size==0 || size>64*1024*1024) return null;
        IntPtr ptr=GlobalLock(handle); if (ptr==IntPtr.Zero) return null;
        try { byte[] bytes=new byte[(int)size]; Marshal.Copy(ptr,bytes,0,bytes.Length); return bytes; }
        finally { GlobalUnlock(handle); }
    }
    internal static bool SetGlobal(uint format, byte[] bytes)
    {
        IntPtr handle=GlobalAlloc(GmemMoveable,(UIntPtr)Math.Max(1,bytes.Length)); if(handle==IntPtr.Zero) return false;
        IntPtr ptr=GlobalLock(handle); if(ptr==IntPtr.Zero) { GlobalFree(handle); return false; }
        try { Marshal.Copy(bytes,0,ptr,bytes.Length); } finally { GlobalUnlock(handle); }
        if(SetClipboardData(format,handle)!=IntPtr.Zero) return true;
        GlobalFree(handle); return false;
    }
    internal static string? ProcessName(uint pid)
    {
        if(pid==0) return null;
        IntPtr process=OpenProcess(ProcessQueryLimitedInformation,false,pid); if(process==IntPtr.Zero) return null;
        try { var name=new StringBuilder(1024); uint length=(uint)name.Capacity;
            return QueryFullProcessImageName(process,0,name,ref length)?Path.GetFileName(name.ToString()):null; }
        finally { CloseHandle(process); }
    }
    internal delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X,Y; internal Point(int x,int y) { X=x; Y=y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo { internal int Size; internal uint Flags; internal IntPtr HwndActive,HwndFocus,HwndCapture,HwndMenuOwner,HwndMoveSize,HwndCaret; internal Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential)] internal struct KbdLlHookStruct { internal uint VkCode,ScanCode,Flags,Time; internal nuint ExtraInfo; }
    // INPUT is 40 bytes on x64: type (4), padding (4), union (32).
    [StructLayout(LayoutKind.Explicit,Size=40)] internal struct Input { [FieldOffset(0)] internal uint Type; [FieldOffset(8)] internal KeybdInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeybdInput { internal ushort Vk,Scan; internal uint Flags,Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct Credential { internal uint Flags,Type; internal string TargetName; internal string? Comment; internal long LastWritten; internal uint CredentialBlobSize; internal IntPtr CredentialBlob; internal uint Persist,AttributeCount; internal IntPtr Attributes; internal string? TargetAlias,UserName; }
}
