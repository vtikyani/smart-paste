using SmartPaste.Core;

public class ClipboardPrivacyTests
{
    [Fact] public void ExcludeAlwaysSkips() => Assert.True(ClipboardPrivacy.ShouldSkip([ClipboardPrivacy.ExcludeFormat], _ => 1, "chrome.exe"));
    [Theory]
    [InlineData(ClipboardPrivacy.HistoryFormat)]
    [InlineData(ClipboardPrivacy.CloudFormat)]
    public void ZeroAndUnreadableSkip(string format)
    {
        Assert.True(ClipboardPrivacy.ShouldSkip([format], _ => 0, null));
        Assert.True(ClipboardPrivacy.ShouldSkip([format], _ => null, null));
        Assert.False(ClipboardPrivacy.ShouldSkip([format], _ => 1, null));
    }
    [Theory]
    [InlineData("1password")]
    [InlineData("1Password.exe")]
    public void BlockedOwnerSkips(string owner) => Assert.True(ClipboardPrivacy.ShouldSkip([], _ => null, owner));
    [Fact] public void NormalOwnerPasses() => Assert.False(ClipboardPrivacy.ShouldSkip([], _ => null, "chrome.exe"));
}
