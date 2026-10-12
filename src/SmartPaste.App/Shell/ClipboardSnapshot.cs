using SmartPaste.Core;

namespace SmartPaste.App.Shell;

// One clipboard format and a copy of its bytes.
internal sealed record ClipboardItem(uint Format, string? Name, byte[] Data);

// What a smart paste saves before it borrows the clipboard, and what it puts back afterwards.
internal static class ClipboardSnapshot
{
    // OLE's bookkeeping formats point at the previous owner's data object, which is released once the clipboard is
    // emptied; restoring them would leave a dangling reference for OLE-aware apps.
    private static readonly HashSet<string> OleBookkeeping = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataObject",
        "Ole Private Data"
    };

    // Formats that are copied as bytes and restored faithfully: anything held in global memory, except what Windows
    // synthesizes from CF_UNICODETEXT (CF_TEXT, CF_OEMTEXT). GDI handles, owner-drawn formats, and private ranges
    // cannot be copied as bytes.
    public static bool ShouldSnapshot(uint format, string? name) => format switch
    {
        ClipboardFormatIds.Text or ClipboardFormatIds.OemText => false,
        ClipboardFormatIds.Bitmap or ClipboardFormatIds.MetafilePict or ClipboardFormatIds.Palette or ClipboardFormatIds.EnhMetafile => false,
        ClipboardFormatIds.OwnerDisplay or ClipboardFormatIds.DspBitmap or ClipboardFormatIds.DspMetafilePict
            or ClipboardFormatIds.DspEnhMetafile => false,
        >= ClipboardFormatIds.PrivateFirst and <= ClipboardFormatIds.PrivateLast => false,
        >= ClipboardFormatIds.GdiObjectFirst and <= ClipboardFormatIds.GdiObjectLast => false,
        _ => name is null || !OleBookkeeping.Contains(name)
    };

    // The items to put back. Unless the original copy said otherwise, the restored data is marked so Windows
    // clipboard history and cloud sync skip it: it is the same copy the user already made, and every smart paste
    // would otherwise add a duplicate.
    public static IReadOnlyList<ClipboardItem> PlanRestore(IReadOnlyList<ClipboardItem> snapshot, uint historyFormat, uint cloudFormat)
    {
        var items = new List<ClipboardItem>(snapshot);
        if (!snapshot.Any(item => item.Format == historyFormat))
            items.Add(new ClipboardItem(historyFormat, ClipboardPrivacy.HistoryFormat, new byte[4]));
        if (!snapshot.Any(item => item.Format == cloudFormat))
            items.Add(new ClipboardItem(cloudFormat, ClipboardPrivacy.CloudFormat, new byte[4]));
        return items;
    }
}
