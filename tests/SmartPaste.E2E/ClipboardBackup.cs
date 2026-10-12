namespace SmartPaste.E2E;

// The tests overwrite the clipboard, so whatever the person had copied is saved first and put back at the end.
internal sealed class ClipboardBackup
{
    private readonly List<(uint Format, byte[] Data)> _items = [];

    public static ClipboardBackup Take()
    {
        var backup = new ClipboardBackup();
        if (!Native.OpenClipboardRetry()) return backup;
        try
        {
            for (uint format = 0; (format = Native.EnumClipboardFormats(format)) != 0;)
            {
                // Handle-based formats (bitmaps, metafiles, private and GDI ranges) cannot be copied as bytes;
                // Windows recreates bitmaps from the DIB formats that are copied.
                if (format is 2 or 3 or 9 or 14 or 0x80 or 0x82 or 0x83 or 0x8E || format is >= 0x200 and <= 0x3FF) continue;
                if (Native.CopyGlobal(Native.GetClipboardData(format)) is { } data) backup._items.Add((format, data));
            }
        }
        finally
        {
            Native.CloseClipboard();
        }
        return backup;
    }

    public int FormatCount => _items.Count;

    public void Restore()
    {
        if (!Native.OpenClipboardRetry()) return;
        try
        {
            Native.EmptyClipboard();
            foreach (var (format, data) in _items) Native.SetGlobal(format, data);
        }
        finally
        {
            Native.CloseClipboard();
        }
    }
}
