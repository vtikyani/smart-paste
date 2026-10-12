using SmartPaste.App.UI.Text;

namespace SmartPaste.App.Tests.Settings;

// Release notes come as Markdown (a CHANGELOG section); the About page shows them as plain text.
public sealed class ReleaseNotesTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("   \n  ", "")]
    [InlineData("### Fixed\r\n\r\n\r\n* A `picker` crash\r\n+ Tray icon on __high DPI__\r\n", "Fixed\n\n• A picker crash\n• Tray icon on high DPI")]
    [InlineData("See [the guide](https://example.com/guide) ![logo](logo.png)", "See the guide logo")]
    [InlineData("**Bold** start and 1. numbered", "Bold start and 1. numbered")]
    public void MarkdownBecomesPlainText(string? markdown, string expected) =>
        Assert.Equal(expected, ReleaseNotesText.ToPlainText(markdown));

    [Fact]
    public void LongNotesAreCut()
    {
        var markdown = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"- Change {i}"));
        var lines = ReleaseNotesText.ToPlainText(markdown).Split('\n');
        Assert.Equal(ReleaseNotesText.MaxLines + 1, lines.Length);
        Assert.Equal("…", lines[^1]);
    }
}
