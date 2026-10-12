using System.Runtime.InteropServices;

namespace SmartPaste.App.Shell;

// Standard clipboard format ids.
internal static class ClipboardFormatIds
{
    public const uint Text = 1;
    public const uint Bitmap = 2;
    public const uint MetafilePict = 3;
    public const uint OemText = 7;
    public const uint Dib = 8;
    public const uint Palette = 9;
    public const uint UnicodeText = 13;
    public const uint EnhMetafile = 14;
    public const uint DibV5 = 17;
    public const uint OwnerDisplay = 0x0080;
    public const uint DspBitmap = 0x0082;
    public const uint DspMetafilePict = 0x0083;
    public const uint DspEnhMetafile = 0x008E;
    public const uint PrivateFirst = 0x0200;
    public const uint PrivateLast = 0x02FF;
    public const uint GdiObjectFirst = 0x0300;
    public const uint GdiObjectLast = 0x03FF;
    public const uint RegisteredFirst = 0xC000;
}

// Thin wrappers over the Win32 clipboard. Everything here requires the clipboard to be open on the calling thread,
// except TryOpen itself.
internal static class ClipboardAccess
{
    // Reads larger than this are refused: a clipboard item is untrusted input from another app.
    public const int MaxBytes = 64 * 1024 * 1024;

    // Another app may hold the clipboard for a moment while it writes; try for about 150 ms.
    public static bool TryOpen(IntPtr owner)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (NativeMethods.OpenClipboard(owner)) return true;
            Thread.Sleep(15);
        }
        return false;
    }

    // Every format on the clipboard, in the owner's order of preference, with names for registered formats.
    public static List<(uint Id, string? Name)> EnumerateFormats()
    {
        var formats = new List<(uint, string?)>();
        var buffer = new char[256];
        for (var format = NativeMethods.EnumClipboardFormats(0); format != 0; format = NativeMethods.EnumClipboardFormats(format))
        {
            string? name = null;
            if (format >= ClipboardFormatIds.RegisteredFirst)
            {
                var length = NativeMethods.GetClipboardFormatName(format, buffer, buffer.Length);
                if (length > 0) name = new string(buffer, 0, length);
            }
            formats.Add((format, name));
        }
        return formats;
    }

    // A copy of one format's bytes. Null when the format holds no global memory (a GDI handle), is empty, could not
    // be rendered by its owner, or is larger than maxBytes.
    public static byte[]? ReadBytes(uint format, int maxBytes = MaxBytes)
    {
        var handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;
        var size = NativeMethods.GlobalSize(handle);
        if (size == 0 || size > (nuint)maxBytes) return null;
        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    public static uint? ReadDword(uint format)
    {
        var bytes = ReadBytes(format, maxBytes: 4096);
        return bytes is { Length: >= 4 } ? BitConverter.ToUInt32(bytes, 0) : null;
    }

    // CF_UNICODETEXT up to its terminating NUL.
    public static string? ReadUnicodeText()
    {
        var bytes = ReadBytes(ClipboardFormatIds.UnicodeText);
        if (bytes is null) return null;
        var text = MemoryMarshal.Cast<byte, char>(bytes.AsSpan(0, bytes.Length & ~1));
        var end = text.IndexOf('\0');
        return new string(end < 0 ? text : text[..end]);
    }

    // Global memory holding a copy of data, for SetClipboardData. Zero when allocation fails.
    public static IntPtr Allocate(byte[] data)
    {
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, (nuint)Math.Max(1, data.Length));
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return IntPtr.Zero;
        }
        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
        return handle;
    }

    public static IntPtr AllocateUnicode(string text)
    {
        var bytes = new byte[(text.Length + 1) * sizeof(char)];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        return Allocate(bytes);
    }

    // Puts data on the open clipboard. On success the system owns the memory; on failure it is freed here.
    public static bool SetData(uint format, IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        if (NativeMethods.SetClipboardData(format, handle) != IntPtr.Zero) return true;
        NativeMethods.GlobalFree(handle);
        return false;
    }

    public static bool SetBytes(uint format, byte[] data) => SetData(format, Allocate(data));

    // Declares a format the owner renders on request (WM_RENDERFORMAT). SetClipboardData returns NULL for a delayed
    // format whether or not it succeeded, so the error code tells.
    public static bool SetDelayed(uint format)
    {
        Marshal.SetLastPInvokeError(0);
        NativeMethods.SetClipboardData(format, IntPtr.Zero);
        return Marshal.GetLastPInvokeError() == 0;
    }

    public static uint OwnerProcessId()
    {
        var owner = NativeMethods.GetClipboardOwner();
        if (owner == IntPtr.Zero) return 0;
        NativeMethods.GetWindowThreadProcessId(owner, out var processId);
        return processId;
    }
}
