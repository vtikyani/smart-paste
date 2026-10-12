using System.Windows;

namespace SmartPaste.App.UI.Navigation;

// Plain text on the Windows clipboard. Only the welcome tour's practice form uses it, and only when the user
// asks: the Copy button, or Ctrl+V in a practice field.
internal interface IClipboardText
{
    // Null when the clipboard holds no text. Throws ExternalException when another app keeps the clipboard locked.
    string? GetText();

    // Puts tutorial text on the clipboard. Throws ExternalException when another app keeps the clipboard locked.
    void SetPracticeText(string text);
}

internal sealed class SystemClipboardText : IClipboardText
{
    public static readonly SystemClipboardText Instance = new();

    private SystemClipboardText()
    {
    }

    public string? GetText() =>
        Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;

    public void SetPracticeText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        // The sample is tutorial text. Marking it private keeps it out of clipboard history and cloud sync, and
        // tells Smart Paste's own clipboard watcher to skip it, so the sample is never armed for a real form.
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        Clipboard.SetDataObject(data, copy: true);
    }
}
