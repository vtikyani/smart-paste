using System.Runtime.InteropServices;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

// Builds the injected batches without sending them.
public sealed class KeySenderTests
{
    // A US layout's scan codes, with the extended keys marked.
    private static ScanCode UsScanCode(ushort key) => key switch
    {
        VirtualKeys.LeftControl => new ScanCode(0x1D, false),
        VirtualKeys.RightControl => new ScanCode(0x1D, true),
        VirtualKeys.LeftShift => new ScanCode(0x2A, false),
        VirtualKeys.RightShift => new ScanCode(0x36, false),
        VirtualKeys.LeftMenu => new ScanCode(0x38, false),
        VirtualKeys.RightMenu => new ScanCode(0x38, true),
        VirtualKeys.LeftWindows => new ScanCode(0x5B, true),
        VirtualKeys.RightWindows => new ScanCode(0x5C, true),
        VirtualKeys.V => new ScanCode(0x2F, false),
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static string Describe(NativeMethods.Input[] inputs) => string.Join(" ", inputs.Select(input =>
    {
        var key = input.Data.Keyboard;
        var up = (key.Flags & NativeMethods.KeyeventfKeyUp) != 0;
        var name = key.VirtualKey switch
        {
            VirtualKeys.LeftControl => "lctrl",
            VirtualKeys.RightControl => "rctrl",
            VirtualKeys.LeftShift => "lshift",
            VirtualKeys.RightShift => "rshift",
            VirtualKeys.LeftMenu => "lalt",
            VirtualKeys.RightMenu => "ralt",
            VirtualKeys.LeftWindows => "lwin",
            VirtualKeys.RightWindows => "rwin",
            VirtualKeys.V => "v",
            _ => key.VirtualKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return (up ? "-" : "+") + name;
    }));

    [Fact]
    public void TheInputStructHasTheNativeLayout()
    {
        Assert.True(Environment.Is64BitProcess);
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.Input>());
        Assert.Equal(8, Marshal.OffsetOf<NativeMethods.Input>(nameof(NativeMethods.Input.Data)).ToInt32());
        Assert.Equal(24, Marshal.SizeOf<NativeMethods.KeybdInput>());
        Assert.Equal(16, Marshal.OffsetOf<NativeMethods.KeybdInput>(nameof(NativeMethods.KeybdInput.ExtraInfo)).ToInt32());
        Assert.Equal(32, Marshal.SizeOf<NativeMethods.MouseInput>());
    }

    [Fact]
    public void PastePressesCtrlWhenItIsNotHeld()
    {
        var inputs = KeySender.BuildPaste(HeldModifiers.None, UsScanCode);
        Assert.Equal("+lctrl +v -v -lctrl", Describe(inputs));
    }

    [Fact]
    public void PasteUsesTheCtrlTheUserHolds()
    {
        Assert.Equal("+v -v", Describe(KeySender.BuildPaste(HeldModifiers.LeftControl, UsScanCode)));
        Assert.Equal("+v -v", Describe(KeySender.BuildPaste(HeldModifiers.RightControl, UsScanCode)));
    }

    [Fact]
    public void PasteReleasesShiftAltAndWinWhileCtrlIsDown()
    {
        var held = HeldModifiers.LeftShift | HeldModifiers.RightAlt | HeldModifiers.LeftWindows;
        var inputs = KeySender.BuildPaste(held, UsScanCode);
        Assert.Equal("+lctrl -lshift -ralt -lwin +v -v -lctrl", Describe(inputs));
    }

    [Fact]
    public void PasteWithCtrlAndShiftHeldReleasesOnlyShift()
    {
        var inputs = KeySender.BuildPaste(HeldModifiers.LeftControl | HeldModifiers.RightShift, UsScanCode);
        Assert.Equal("-rshift +v -v", Describe(inputs));
    }

    [Fact]
    public void PasteEventsCarryScanCodesExtendedFlagsAndTheMarker()
    {
        var inputs = KeySender.BuildPaste(HeldModifiers.RightAlt, UsScanCode);
        Assert.All(inputs, input =>
        {
            Assert.Equal(NativeMethods.InputKeyboard, input.Type);
            Assert.Equal(KeySender.InjectedMarker, input.Data.Keyboard.ExtraInfo);
            Assert.Equal(0u, input.Data.Keyboard.Time);
        });
        var releaseAlt = inputs[1].Data.Keyboard;
        Assert.Equal(VirtualKeys.RightMenu, releaseAlt.VirtualKey);
        Assert.Equal((ushort)0x38, releaseAlt.ScanCode);
        Assert.Equal(NativeMethods.KeyeventfKeyUp | NativeMethods.KeyeventfExtendedKey, releaseAlt.Flags);
        var pressV = inputs[2].Data.Keyboard;
        Assert.Equal((ushort)0x2F, pressV.ScanCode);
        Assert.Equal(0u, pressV.Flags);
    }

    [Fact]
    public void ReplayKeepsEveryEventAsTypedAndEndsWithTheFence()
    {
        KeyEvent[] keys =
        [
            new(VirtualKeys.RightControl, 0x1D, IsUp: false, IsExtended: true),
            new(0x41, 0x1E, IsUp: false, IsExtended: false),
            new(0x41, 0x1E, IsUp: true, IsExtended: false),
            new(VirtualKeys.RightControl, 0x1D, IsUp: true, IsExtended: true)
        ];

        var inputs = KeySender.BuildReplay(keys, fence: 42);

        Assert.Equal(5, inputs.Length);
        var first = inputs[0].Data.Keyboard;
        Assert.Equal(VirtualKeys.RightControl, first.VirtualKey);
        Assert.Equal((ushort)0x1D, first.ScanCode);
        Assert.Equal(NativeMethods.KeyeventfExtendedKey, first.Flags);
        Assert.Equal(NativeMethods.KeyeventfKeyUp, inputs[2].Data.Keyboard.Flags);
        Assert.Equal(NativeMethods.KeyeventfKeyUp | NativeMethods.KeyeventfExtendedKey, inputs[3].Data.Keyboard.Flags);

        var fence = inputs[4].Data.Keyboard;
        Assert.Equal(VirtualKeys.Fence, fence.VirtualKey);
        Assert.Equal((ushort)42, fence.ScanCode);
        Assert.Equal(NativeMethods.KeyeventfKeyUp, fence.Flags);
        Assert.All(inputs, input => Assert.Equal(KeySender.InjectedMarker, input.Data.Keyboard.ExtraInfo));
    }

    [Fact]
    public void ReplayWithoutAFenceSendsOnlyTheKeys()
    {
        var inputs = KeySender.BuildReplay([KeyEvent.Down(0x41), KeyEvent.Up(0x41)], fence: null);
        Assert.Equal(2, inputs.Length);
        Assert.DoesNotContain(inputs, input => input.Data.Keyboard.VirtualKey == VirtualKeys.Fence);
    }

    [Fact]
    public void UnicodeInputIsReplayedAsUnicode()
    {
        var inputs = KeySender.BuildReplay([new KeyEvent(VirtualKeys.Packet, 'é', IsUp: true, IsExtended: false)], fence: null);
        var key = Assert.Single(inputs).Data.Keyboard;
        Assert.Equal((ushort)0, key.VirtualKey);
        Assert.Equal((ushort)'é', key.ScanCode);
        Assert.Equal(NativeMethods.KeyeventfUnicode | NativeMethods.KeyeventfKeyUp, key.Flags);
    }

    [Fact]
    public void AnEmptyReplayIsNothingToSend()
    {
        Assert.Empty(KeySender.BuildReplay([], fence: null));
    }
}
