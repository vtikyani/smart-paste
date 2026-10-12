using SmartPaste.App.Services;
using SmartPaste.App.Shell;
using static SmartPaste.App.Tests.Shell.InputQueueHarness;

namespace SmartPaste.App.Tests.Shell;

// The type-ahead buffer decides where every keystroke lands while a smart paste is in flight. These tests drive it
// the way the hook does and check the order in which the focused app receives keys.
public sealed class InputQueueTests
{
    private static InputQueueHarness StartRequest(InputQueueHarness? harness = null)
    {
        var h = harness ?? new InputQueueHarness();
        h.Press(Ctrl);
        Assert.True(h.Press(V));
        return h;
    }

    // ---- What becomes a request ----

    [Fact]
    public void PlainTypingPassesThrough()
    {
        var h = new InputQueueHarness();
        h.Tap(A);
        h.Tap(Tab);
        Assert.Equal("+a -a +tab -tab", h.AppView);
        Assert.Empty(h.Host.Raised);
        Assert.Equal(0, h.Host.InterceptCalls);
    }

    [Fact]
    public void CtrlVBecomesARequestAndIsSwallowed()
    {
        var h = StartRequest();
        Assert.Equal(new[] { 1 }, h.Host.Raised);
        Assert.Equal("+ctrl", h.AppView);
        Assert.False(h.Queue.IsIdle);
    }

    [Fact]
    public void CtrlVGoesToTheAppWhenTheFilterSaysNo()
    {
        var h = new InputQueueHarness();
        h.Host.Intercept = false;
        h.Press(Ctrl);
        Assert.False(h.Press(V));
        h.Release(V);
        h.Release(Ctrl);
        Assert.Equal("+ctrl +v -v -ctrl", h.AppView);
        Assert.Empty(h.Host.Raised);
        Assert.True(h.Queue.IsIdle);
    }

    [Theory]
    [InlineData(Shift)]
    [InlineData(VirtualKeys.RightShift)]
    [InlineData(Alt)]
    [InlineData(VirtualKeys.RightMenu)]
    [InlineData(Win)]
    [InlineData(VirtualKeys.RightWindows)]
    public void CtrlVWithAnotherModifierIsNeverTouched(ushort modifier)
    {
        var h = new InputQueueHarness();
        h.Press(modifier);
        h.Press(Ctrl);
        Assert.False(h.Press(V));
        Assert.Equal(0, h.Host.InterceptCalls);
        Assert.Empty(h.Host.Raised);
    }

    [Fact]
    public void VWithoutCtrlIsJustTyping()
    {
        var h = new InputQueueHarness();
        Assert.False(h.Press(V));
        Assert.Equal(0, h.Host.InterceptCalls);
    }

    [Fact]
    public void RightCtrlCountsAsCtrl()
    {
        var h = new InputQueueHarness();
        h.Press(VirtualKeys.RightControl);
        Assert.True(h.Press(V));
        Assert.Equal(new[] { 1 }, h.Host.Raised);
    }

    [Fact]
    public void HoldingVDoesNotStartMoreRequests()
    {
        var h = StartRequest();
        Assert.True(h.Press(V));
        Assert.True(h.Press(V));
        Assert.True(h.Release(V));
        Assert.Equal(new[] { 1 }, h.Host.Raised);

        h.Release(Ctrl);
        h.Paste();
        h.Complete(1);
        h.EchoAll();
        // The repeats and release of the paste's own V never reach the app.
        Assert.Equal("+ctrl PASTE -ctrl", h.AppView);
    }

    [Fact]
    public void ARepeatedVAfterTheFilterSaidNoIsNotARequest()
    {
        var h = new InputQueueHarness();
        h.Host.Intercept = false;
        h.Press(Ctrl);
        h.Press(V);
        h.Host.Intercept = true;
        Assert.False(h.Press(V));
        Assert.Empty(h.Host.Raised);
    }

    // ---- Holding and replaying ----

    [Fact]
    public void KeysTypedDuringARequestAreHeldThenReplayedInOrder()
    {
        var h = StartRequest();
        h.Release(V);
        h.Release(Ctrl);
        h.Tap(Tab);
        h.Tap(A);
        Assert.Equal("+ctrl", h.AppView);
        Assert.Empty(h.Host.Injected);

        h.Paste();
        h.Complete(1);
        var (_, fence) = Assert.Single(h.Host.Injected);
        Assert.Equal("-ctrl +tab -tab +a -a", h.Host.InjectedView(0));
        Assert.NotEqual((byte)0, fence);

        h.Echo();
        Assert.Equal("+ctrl PASTE -ctrl +tab -tab +a -a", h.AppView);
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void ReplayKeepsScanCodesAndExtendedFlags()
    {
        var h = StartRequest();
        var rightCtrl = new KeyEvent(VirtualKeys.RightControl, 0x1D, IsUp: false, IsExtended: true);
        var arrow = new KeyEvent(VirtualKeys.Down, 0x50, IsUp: false, IsExtended: true);
        var packet = new KeyEvent(VirtualKeys.Packet, 'é', IsUp: false, IsExtended: false);
        h.Key(rightCtrl);
        h.Key(arrow);
        h.Key(packet);
        h.Complete(1);
        Assert.Equal(new[] { rightCtrl, arrow, packet }, h.Host.Injected[0].Keys);
    }

    [Fact]
    public void NothingIsInjectedWhenNoKeysWereTyped()
    {
        var h = StartRequest();
        h.Paste();
        h.Complete(1);
        Assert.Empty(h.Host.Injected);
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void KeysTypedWhileAReplayIsInFlightFollowIt()
    {
        var h = StartRequest();
        h.Release(V);
        h.Tap(A);
        h.Complete(1);
        Assert.Equal(1, h.Unechoed);

        // Typed before the replayed batch came back through the hook: held, then sent as the next batch.
        Assert.True(h.Press(B));
        Assert.True(h.Release(B));
        h.Echo();
        Assert.Equal(2, h.Host.Injected.Count);
        Assert.Equal("+b -b", h.Host.InjectedView(1));

        h.Echo();
        Assert.True(h.Queue.IsIdle);
        Assert.False(h.Press(C));
        Assert.Equal("+ctrl +a -a +b -b +c", h.AppView);
    }

    [Fact]
    public void ReplayOrderMatchesTypingOrderAcrossSeveralBatches()
    {
        var h = StartRequest();
        h.Release(V);
        h.Release(Ctrl);
        h.Tap(A);
        h.Complete(1);
        h.Tap(B);
        h.Echo();
        h.Tap(C);
        h.Echo();
        h.Echo();
        Assert.Equal("+ctrl -ctrl +a -a +b -b +c -c", h.AppView);
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void AForeignFenceIsSwallowedButDoesNotEndTheDrain()
    {
        var h = StartRequest();
        h.Tap(A);
        h.Complete(1);
        var fence = h.Host.Injected[0].Fence;
        Assert.True(h.Queue.OnOwnInjected(KeyEvent.Up(VirtualKeys.Fence, (ushort)(fence + 1)), h.Now));
        Assert.False(h.Queue.IsIdle);
        Assert.Contains("type_ahead_fence_mismatch", h.Host.Reports);
        h.Echo();
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void FencesAreSwallowedEvenWhenNotWaitingForOne()
    {
        var h = new InputQueueHarness();
        Assert.True(h.Queue.OnOwnInjected(KeyEvent.Up(VirtualKeys.Fence, 9), h.Now));
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void OurOwnInjectedKeysAreNeverHeld()
    {
        var h = StartRequest();
        Assert.False(h.Queue.OnOwnInjected(KeyEvent.Down(A), h.Now));
        Assert.False(h.Queue.OnOwnInjected(KeyEvent.Up(A), h.Now));
        Assert.Equal(0, h.Queue.HeldCount);
    }

    [Fact]
    public void FenceIdsCycleAndAreNeverZero()
    {
        var h = new InputQueueHarness();
        for (var i = 0; i < 300; i++)
        {
            h.Press(Ctrl);
            h.Press(V);
            h.Release(V);
            h.Release(Ctrl);
            h.Complete(i + 1);
            h.EchoAll();
        }
        Assert.All(h.Host.Injected, batch => Assert.InRange(batch.Fence, (byte)1, (byte)127));
        Assert.Equal(300, h.Host.Injected.Count);
    }

    // ---- Ctrl+V typed while a request is pending ----

    [Fact]
    public void ASecondCtrlVBecomesTheNextRequestAtItsPlace()
    {
        var h = StartRequest();
        h.Release(V);
        h.Tap(Tab);
        Assert.True(h.Press(V));
        h.Release(V);
        h.Tap(A);
        h.Release(Ctrl);
        Assert.Equal(new[] { 1 }, h.Host.Raised);

        h.Paste();
        h.Complete(1);
        Assert.Equal("+tab -tab", h.Host.InjectedView(0));
        h.Echo();

        // Not raised before the app has had time to handle the replayed Tab.
        Assert.Equal(new[] { 1 }, h.Host.Raised);
        h.Advance(99);
        Assert.Equal(new[] { 1 }, h.Host.Raised);
        h.Advance(1);
        Assert.Equal(new[] { 1, 2 }, h.Host.Raised);

        h.Paste();
        h.Complete(2);
        Assert.Equal("+a -a -ctrl", h.Host.InjectedView(1));
        h.Echo();
        Assert.Equal("+ctrl PASTE +tab -tab PASTE +a -a -ctrl", h.AppView);
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void AQueuedCtrlVIsDecidedWhenRaisedNotWhenTyped()
    {
        var h = StartRequest();
        h.Release(V);
        h.Tap(Tab);
        h.Press(V);
        h.Release(V);
        h.Tap(A);
        h.Release(Ctrl);

        h.Paste();
        h.Complete(1);
        h.Echo();
        var callsBefore = h.Host.InterceptCalls;
        h.Host.Intercept = false;
        h.Advance(100);

        Assert.Equal(callsBefore + 1, h.Host.InterceptCalls);
        Assert.Equal(new[] { 1 }, h.Host.Raised);
        h.EchoAll();
        // A plain Ctrl+V for the app, exactly as typed.
        Assert.Equal("+ctrl PASTE +tab -tab +v -v +a -a -ctrl", h.AppView);
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void BackToBackCtrlVNeedsNoReplayBetween()
    {
        var h = StartRequest();
        h.Release(V);
        h.Press(V);
        h.Release(V);
        h.Paste();
        h.Complete(1);
        Assert.Empty(h.Host.Injected);
        h.Advance(100);
        Assert.Equal(new[] { 1, 2 }, h.Host.Raised);
    }

    [Fact]
    public void AQueuedVStillHeldWhenRaisedKeepsItsReleaseFromTheApp()
    {
        var h = StartRequest();
        h.Release(V);
        h.Press(V);
        h.Paste();
        h.Complete(1);
        h.Advance(100);
        Assert.Equal(new[] { 1, 2 }, h.Host.Raised);

        Assert.True(h.Press(V));
        Assert.True(h.Release(V));
        h.Release(Ctrl);
        h.Paste();
        h.Complete(2);
        h.EchoAll();
        Assert.Equal("+ctrl PASTE PASTE -ctrl", h.AppView);
    }

    [Fact]
    public void ThreeRequestsInARowStayInOrder()
    {
        var h = StartRequest();
        h.Release(V);
        h.Tap(Tab);
        h.Press(V);
        h.Release(V);
        h.Tap(Tab);
        h.Press(V);
        h.Release(V);
        h.Release(Ctrl);

        for (var sequence = 1; sequence <= 3; sequence++)
        {
            h.Paste();
            h.Complete(sequence);
            h.EchoAll();
            h.Advance(100);
        }

        Assert.Equal(new[] { 1, 2, 3 }, h.Host.Raised);
        Assert.Equal("+ctrl PASTE +tab -tab PASTE +tab -tab PASTE -ctrl", h.AppView);
        Assert.True(h.Queue.IsIdle);
    }

    [Fact]
    public void ARequestRaisedFromTheQueueHasItsOwnTimeout()
    {
        var h = StartRequest();
        h.Release(V);
        h.Press(V);
        h.Release(V);
        h.Complete(1);
        h.Advance(100);
        Assert.Equal(new[] { 1, 2 }, h.Host.Raised);
        h.Advance(3999);
        Assert.Empty(h.Host.Expired);
        h.Advance(1);
        Assert.Equal(new[] { (2, "timeout") }, h.Host.Expired);
    }

    // ---- Timeouts and safety ----

    [Fact]
    public void AnUncompletedRequestIsReleasedAfterFourSeconds()
    {
        var h = StartRequest();
        h.Tap(A);
        h.Advance(3999);
        Assert.Empty(h.Host.Expired);
        Assert.Empty(h.Host.Injected);

        h.Advance(1);
        Assert.Equal(new[] { (1, "timeout") }, h.Host.Expired);
        Assert.Equal("+a -a", h.Host.InjectedView(0));
        Assert.Contains("type_ahead_timeout", h.Host.Reports);

        // A late Complete changes nothing.
        h.Complete(1);
        Assert.Single(h.Host.Injected);
    }

    [Fact]
    public void PickerTimeDoesNotCountTowardsTheTimeout()
    {
        var h = StartRequest();
        h.Advance(1000);
        h.Queue.SetCaptureActive(true, h.Now);
        h.Advance(30_000);
        Assert.Empty(h.Host.Expired);
        Assert.Null(h.Queue.NextDeadline);

        h.Queue.SetCaptureActive(false, h.Now);
        Assert.Equal(h.Now + 3000, h.Queue.NextDeadline);
        h.Advance(2999);
        Assert.Empty(h.Host.Expired);
        h.Advance(1);
        Assert.Equal(new[] { (1, "timeout") }, h.Host.Expired);
    }

    [Fact]
    public void ARequestStartedDuringACaptureWaitsForItToEnd()
    {
        var h = new InputQueueHarness();
        h.Queue.SetCaptureActive(true, h.Now);
        StartRequest(h);
        h.Advance(10_000);
        Assert.Empty(h.Host.Expired);
        h.Queue.SetCaptureActive(false, h.Now);
        h.Advance(4000);
        Assert.Single(h.Host.Expired);
    }

    [Fact]
    public void NextDeadlineFollowsEachPhase()
    {
        var h = new InputQueueHarness();
        Assert.Null(h.Queue.NextDeadline);

        StartRequest(h);
        Assert.Equal(h.Now + 4000, h.Queue.NextDeadline);

        h.Release(V);
        h.Tap(A);
        h.Press(V);
        h.Complete(1);
        Assert.Equal(h.Now + 1000, h.Queue.NextDeadline);

        h.Echo();
        Assert.Equal(h.Now + 100, h.Queue.NextDeadline);
    }

    [Fact]
    public void AReplayThatNeverComesBackIsAbandonedAfterASecond()
    {
        var h = StartRequest();
        h.Tap(A);
        h.Complete(1);
        h.Tap(B);
        h.Advance(999);
        Assert.Single(h.Host.Injected);
        h.Advance(1);
        Assert.Contains("type_ahead_drain_timeout", h.Host.Reports);
        Assert.Equal("+b -b", h.Host.InjectedView(1));
    }

    [Fact]
    public void AFailedInjectionMovesOnAtOnce()
    {
        var h = StartRequest();
        h.Tap(A);
        h.Complete(1);
        h.Tap(B);
        h.Queue.OnInjectionFailed(h.Host.Injected[0].Fence, h.Now);
        Assert.Equal("+b -b", h.Host.InjectedView(1));
    }

    [Fact]
    public void CompletingAnUnknownOrFinishedRequestDoesNothing()
    {
        var h = StartRequest();
        h.Tap(A);
        h.Complete(7);
        Assert.Empty(h.Host.Injected);
        h.Complete(1);
        h.Complete(1);
        Assert.Single(h.Host.Injected);
    }

    [Fact]
    public void TooManyHeldKeysReleaseTheRequestEarly()
    {
        var h = new InputQueueHarness(new InputQueueOptions { MaxBuffered = 4 });
        StartRequest(h);
        h.Tap(A);
        h.Tap(B);
        Assert.Empty(h.Host.Expired);
        h.Press(C);
        Assert.Equal(new[] { (1, "overflow") }, h.Host.Expired);
        Assert.Equal("+a -a +b -b +c", h.Host.InjectedView(0));
        Assert.Contains("type_ahead_overflow", h.Host.Reports);
    }

    [Fact]
    public void ShutdownHandsBackEverythingAndQueuedPastesBecomePlainKeys()
    {
        var h = StartRequest();
        h.Release(V);
        h.Tap(Tab);
        h.Press(V);
        h.Release(V);

        var held = h.Queue.Shutdown();

        Assert.Equal("+tab -tab +v -v", string.Join(" ", held.Select(Describe)));
        Assert.Equal(new[] { (1, "shutdown") }, h.Host.Expired);
        Assert.True(h.Queue.IsIdle);
    }

    // ---- Capture (the picker) ----

    [Fact]
    public void ACaptureTakesTheKeysItAccepts()
    {
        var h = new InputQueueHarness();
        var sink = new Sink(stroke => stroke.VirtualKey == Down);
        h.Capture = sink;

        Assert.True(h.Press(Down));
        Assert.True(h.Release(Down));

        var stroke = Assert.Single(h.Host.Delivered);
        Assert.Equal(Down, stroke.VirtualKey);
        Assert.False(stroke.IsRepeat);
        Assert.Equal(0, h.Host.OtherKeys);
        Assert.Empty(h.AppView);
    }

    [Fact]
    public void AKeyTheCaptureRejectsGoesToTheApp()
    {
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => false);
        Assert.False(h.Press(A));
        Assert.Equal(1, h.Host.OtherKeys);
        Assert.Equal("+a", h.AppView);
    }

    [Fact]
    public void ModifiersAndLocksAreNotOfferedToTheCapture()
    {
        var h = new InputQueueHarness();
        var sink = new Sink(_ => true);
        h.Capture = sink;
        Assert.False(h.Press(Shift));
        Assert.False(h.Press(Shift));
        Assert.False(h.Press(VirtualKeys.Capital));
        Assert.Empty(sink.Offered);
        Assert.Equal(0, h.Host.OtherKeys);
    }

    [Fact]
    public void CapturedStrokesCarryCharacterAndModifiers()
    {
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => true);
        h.Press(Shift);
        h.Press(A);
        h.Release(Shift);
        h.Press(B);

        Assert.Equal(new KeyStroke(A, 'A', KeyModifiers.Shift, false), h.Host.Delivered[0]);
        Assert.Equal(new KeyStroke(B, 'b', KeyModifiers.None, false), h.Host.Delivered[1]);
    }

    [Fact]
    public void ControlCharactersAreNotReportedAsCharacters()
    {
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => true);
        h.Press(Ctrl);
        h.Press(A);
        h.Release(Ctrl);
        h.Press(VirtualKeys.Enter);

        Assert.Equal(new KeyStroke(A, null, KeyModifiers.Control, false), h.Host.Delivered[0]);
        Assert.Null(h.Host.Delivered[1].Character);
    }

    [Fact]
    public void UnicodeInputCarriesItsCharacter()
    {
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => true);
        h.Key(new KeyEvent(VirtualKeys.Packet, 'ü', IsUp: false, IsExtended: false));
        Assert.Equal('ü', Assert.Single(h.Host.Delivered).Character);
    }

    [Fact]
    public void WinCombinationsAndAltShortcutsAlwaysGoToTheApp()
    {
        var h = new InputQueueHarness();
        var sink = new Sink(_ => true);
        h.Capture = sink;
        h.Press(Win);
        Assert.False(h.Press(D));
        h.Release(D);
        h.Release(Win);
        h.Press(Alt);
        Assert.False(h.Press(Tab));

        Assert.Empty(sink.Offered);
        Assert.Equal(2, h.Host.OtherKeys);
    }

    [Fact]
    public void AltCombinationsStayWithTheCaptureOnlyWhenTheyTypeACharacter()
    {
        // AltGr (right Alt plus the left Ctrl Windows adds for it) with a key that types nothing goes to the app.
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => true);
        h.Press(Ctrl);
        h.Press(VirtualKeys.RightMenu);
        Assert.False(h.Press(Tab));
        Assert.Equal(1, h.Host.OtherKeys);

        // With a key that types a character it stays with the capture.
        var typing = new InputQueueHarness();
        typing.Capture = new Sink(_ => true);
        typing.Press(Alt);
        Assert.True(typing.Press(0x31));
        Assert.Equal('1', Assert.Single(typing.Host.Delivered).Character);
    }

    [Fact]
    public void RepeatsOfACapturedKeyStayWithTheCapture()
    {
        var h = new InputQueueHarness();
        var acceptRepeats = true;
        h.Capture = new Sink(stroke => !stroke.IsRepeat || acceptRepeats);
        h.Press(Down);
        Assert.True(h.Press(Down));
        acceptRepeats = false;
        Assert.True(h.Press(Down));

        Assert.Equal(2, h.Host.Delivered.Count);
        Assert.True(h.Host.Delivered[1].IsRepeat);
        Assert.Empty(h.AppView);
    }

    [Fact]
    public void AKeyHeldWhenTheCaptureEndsKeepsItsReleaseFromTheApp()
    {
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => true);
        h.Press(Down);
        h.Capture = null;
        Assert.True(h.Release(Down));
        Assert.False(h.Press(Down));
        Assert.Equal("+down", h.AppView);
    }

    [Fact]
    public void DuringARequestRejectedKeysAreHeldNotPassed()
    {
        var h = StartRequest();
        h.Release(V);
        h.Capture = new Sink(stroke => stroke.VirtualKey == Down);
        h.Tap(Down);
        h.Tap(Tab);
        Assert.Equal("+ctrl", h.AppView);
        Assert.Equal(1, h.Host.OtherKeys);

        h.Capture = null;
        h.Release(Ctrl);
        h.Complete(1);
        h.EchoAll();
        Assert.Equal("+ctrl +tab -tab -ctrl", h.AppView);
    }

    [Fact]
    public void ACtrlVTheCaptureRejectsStillBecomesAPaste()
    {
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => false);
        h.Press(Ctrl);
        Assert.True(h.Press(V));
        Assert.Equal(new[] { 1 }, h.Host.Raised);
        Assert.Equal(1, h.Host.OtherKeys);
    }

    [Fact]
    public void AFailingCaptureIsTreatedAsRejecting()
    {
        var h = new InputQueueHarness();
        h.Capture = new Sink(_ => throw new InvalidOperationException("picker bug"));
        Assert.False(h.Press(A));
        Assert.Contains("capture_sink_error", h.Host.Reports);
        Assert.Equal("+a", h.AppView);
    }

    // ---- Key state the hook missed ----

    [Fact]
    public void AStaleCtrlFromALockedScreenIsCorrectedBeforeDeciding()
    {
        // Ctrl was down when the hook started (or its release happened on the secure desktop) and is up now.
        var h = new InputQueueHarness(seed: key => key == Ctrl);
        Assert.False(h.Press(V));
        Assert.Equal(0, h.Host.InterceptCalls);
    }

    [Fact]
    public void AModifierOurPasteReleasedIsStillHeldByTheUser()
    {
        var h = new InputQueueHarness();
        h.Press(Shift);
        // Our paste released Shift for the app while the user kept holding it.
        h.Queue.OnOwnInjected(KeyEvent.Up(Shift), h.Now);
        h.Host.SystemKeys[Shift] = false;

        h.Press(Ctrl);
        Assert.False(h.Press(V));
        Assert.Equal(0, h.Host.InterceptCalls);
    }

    [Fact]
    public void KeysHeldAtStartAreKnown()
    {
        var h = new InputQueueHarness(seed: key => key == Ctrl);
        h.Host.SystemKeys[Ctrl] = true;
        Assert.True(h.Press(V));
        Assert.Equal(new[] { 1 }, h.Host.Raised);
    }
}
