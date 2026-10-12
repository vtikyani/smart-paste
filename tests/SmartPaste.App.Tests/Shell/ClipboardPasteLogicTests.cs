using SmartPaste.App.Shell;
using SmartPaste.Core;

namespace SmartPaste.App.Tests.Shell;

// The decisions behind a smart paste: what is saved, who counts as having read the value, and what is put back.
public sealed class ClipboardPasteLogicTests
{
    private static readonly PasteTarget Target = new(new IntPtr(0x1234), WindowProcessId: 100, FocusProcessId: 200);

    [Theory]
    [InlineData(ClipboardFormatIds.UnicodeText)]
    [InlineData(ClipboardFormatIds.Dib)]
    [InlineData(ClipboardFormatIds.DibV5)]
    [InlineData(15u)] // CF_HDROP
    [InlineData(16u)] // CF_LOCALE
    [InlineData(0x81u)] // CF_DSPTEXT
    [InlineData(0xC123u)]
    public void CopiesFormatsHeldInGlobalMemory(uint format)
    {
        Assert.True(ClipboardSnapshot.ShouldSnapshot(format, format >= 0xC000 ? "HTML Format" : null));
    }

    [Theory]
    [InlineData(ClipboardFormatIds.Text)]
    [InlineData(ClipboardFormatIds.OemText)]
    [InlineData(ClipboardFormatIds.Bitmap)]
    [InlineData(ClipboardFormatIds.MetafilePict)]
    [InlineData(ClipboardFormatIds.Palette)]
    [InlineData(ClipboardFormatIds.EnhMetafile)]
    [InlineData(ClipboardFormatIds.OwnerDisplay)]
    [InlineData(ClipboardFormatIds.DspBitmap)]
    [InlineData(ClipboardFormatIds.DspMetafilePict)]
    [InlineData(ClipboardFormatIds.DspEnhMetafile)]
    [InlineData(0x200u)]
    [InlineData(0x2FFu)]
    [InlineData(0x300u)]
    [InlineData(0x3FFu)]
    public void SkipsSynthesizedHandleAndPrivateFormats(uint format)
    {
        Assert.False(ClipboardSnapshot.ShouldSnapshot(format, null));
    }

    [Theory]
    [InlineData("DataObject")]
    [InlineData("Ole Private Data")]
    [InlineData("ole private data")]
    public void SkipsOleBookkeeping(string name)
    {
        Assert.False(ClipboardSnapshot.ShouldSnapshot(0xC0F0, name));
    }

    [Fact]
    public void RestoredDataIsKeptOutOfHistoryAndCloudSync()
    {
        ClipboardItem[] snapshot =
        [
            new(ClipboardFormatIds.UnicodeText, null, [0x41, 0, 0, 0]),
            new(0xC010, "HTML Format", [1, 2, 3])
        ];

        var restore = ClipboardSnapshot.PlanRestore(snapshot, historyFormat: 0xC050, cloudFormat: 0xC051);

        Assert.Equal(4, restore.Count);
        Assert.Same(snapshot[0], restore[0]);
        Assert.Same(snapshot[1], restore[1]);
        Assert.Equal((0xC050u, ClipboardPrivacy.HistoryFormat), (restore[2].Format, restore[2].Name));
        Assert.Equal(new byte[4], restore[2].Data);
        Assert.Equal((0xC051u, ClipboardPrivacy.CloudFormat), (restore[3].Format, restore[3].Name));
        Assert.Equal(new byte[4], restore[3].Data);
    }

    [Fact]
    public void TheOriginalCopysOwnHistorySettingsWin()
    {
        ClipboardItem[] snapshot =
        [
            new(ClipboardFormatIds.UnicodeText, null, [0x41, 0, 0, 0]),
            new(0xC050, ClipboardPrivacy.HistoryFormat, [1, 0, 0, 0]),
            new(0xC051, ClipboardPrivacy.CloudFormat, [0, 0, 0, 0])
        ];

        var restore = ClipboardSnapshot.PlanRestore(snapshot, historyFormat: 0xC050, cloudFormat: 0xC051);

        Assert.Equal(snapshot, restore);
    }

    [Fact]
    public void AnEmptyClipboardIsRestoredAsEmptyButPrivate()
    {
        var restore = ClipboardSnapshot.PlanRestore([], historyFormat: 0xC050, cloudFormat: 0xC051);
        Assert.Equal(new[] { 0xC050u, 0xC051u }, restore.Select(item => item.Format));
    }

    [Theory]
    [InlineData(100u, nameof(Reader.Target))]
    [InlineData(200u, nameof(Reader.Target))]
    [InlineData(0u, nameof(Reader.Unidentified))]
    [InlineData(300u, nameof(Reader.Other))]
    public void ClassifiesWhoReadTheValue(uint processId, string expected)
    {
        Assert.Equal(expected, PasteSession.Classify(processId, Target).ToString());
    }

    [Fact]
    public void TheTargetReadingTheValueSettlesTheSession()
    {
        var session = new PasteSession("Jordan", Target);
        Assert.False(session.Settled.IsCompleted);

        Assert.Equal(Reader.Target, session.OnRendered(200));

        Assert.True(session.Consumed);
        Assert.Equal(Reader.Target, session.ConsumedBy);
        Assert.True(session.Settled.IsCompleted);
    }

    [Fact]
    public void AClipboardManagerReadingFirstDoesNotCount()
    {
        var session = new PasteSession("Jordan", Target);

        Assert.Equal(Reader.Other, session.OnRendered(999));
        Assert.False(session.Consumed);
        Assert.False(session.Settled.IsCompleted);
        Assert.Equal(1, session.OtherReaders);

        session.OnRendered(100);
        Assert.True(session.Consumed);
        Assert.Equal(1, session.OtherReaders);
    }

    [Fact]
    public void AReaderWithoutAWindowCountsAsTheTarget()
    {
        var session = new PasteSession("Jordan", Target);
        session.OnRendered(0);
        Assert.True(session.Consumed);
        Assert.Equal(Reader.Unidentified, session.ConsumedBy);
    }

    [Fact]
    public void ANewCopyMeanwhileSettlesTheSessionAndBlocksTheRestore()
    {
        var session = new PasteSession("Jordan", Target);
        session.OnSuperseded();

        Assert.True(session.Settled.IsCompleted);
        Assert.False(session.ShouldRestore(stillOwner: true));
        Assert.Equal(PasteOutcome.Superseded, session.Outcome(RestoreResult.LeftAlone));
    }

    [Fact]
    public void RestoresOnlyWhileTheClipboardIsStillOurs()
    {
        var session = new PasteSession("Jordan", Target);
        Assert.True(session.ShouldRestore(stillOwner: true));
        Assert.False(session.ShouldRestore(stillOwner: false));
    }

    [Fact]
    public void OutcomeReflectsWhatHappened()
    {
        var consumed = new PasteSession("Jordan", Target);
        consumed.OnRendered(100);
        Assert.Equal(PasteOutcome.Consumed, consumed.Outcome(RestoreResult.Restored));
        Assert.Equal(PasteOutcome.Consumed, consumed.Outcome(RestoreResult.Failed));
        // Someone else owned the clipboard by restore time: their copy stays, which is a newer copy.
        Assert.Equal(PasteOutcome.Superseded, consumed.Outcome(RestoreResult.LeftAlone));

        var unread = new PasteSession("Jordan", Target);
        Assert.Equal(PasteOutcome.NotConsumed, unread.Outcome(RestoreResult.Restored));

        var readByManagerOnly = new PasteSession("Jordan", Target);
        readByManagerOnly.OnRendered(999);
        Assert.Equal(PasteOutcome.NotConsumed, readByManagerOnly.Outcome(RestoreResult.Restored));
    }
}
