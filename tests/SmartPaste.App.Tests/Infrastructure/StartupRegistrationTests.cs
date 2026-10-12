using SmartPaste.App.Infrastructure;

// "Start when I sign in" against Windows' own switch for the entry (Task Manager, Settings > Apps > Startup).
// The registry values are kept in memory here; the real registry is never touched.
public sealed class StartupRegistrationTests
{
    private const string Command = "\"C:\\Users\\me\\AppData\\Local\\SmartPaste\\current\\SmartPaste.exe\" --background";

    // As Task Manager writes them: switched off on a given day, and switched back on.
    private static readonly byte[] SwitchedOff = [0x03, 0, 0, 0, 0x5F, 0x3A, 0x1C, 0x8E, 0x0B, 0x3F, 0xDB, 0x01];
    private static readonly byte[] SwitchedOn = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Theory]
    [InlineData(null, false)]
    [InlineData(new byte[0], false)]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x03, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, true)]
    public void AnOddFirstByteMeansWindowsSwitchedItOff(byte[]? value, bool disabled) =>
        Assert.Equal(disabled, StartupApproval.IsDisabled(value));

    [Fact]
    public void AnEntrySwitchedOffInWindowsIsOff()
    {
        var values = new FakeStartupValues { Run = Command };
        Assert.True(StartupRegistration.IsEnabled(values, Command));

        values.Approval = SwitchedOff;
        Assert.False(StartupRegistration.IsEnabled(values, Command));

        values.Approval = SwitchedOn;
        Assert.True(StartupRegistration.IsEnabled(values, Command));
    }

    [Fact]
    public void AnEntryForAnotherCopyOrNoEntryIsOff()
    {
        Assert.False(StartupRegistration.IsEnabled(new FakeStartupValues(), Command));
        Assert.False(StartupRegistration.IsEnabled(new FakeStartupValues { Run = "\"D:\\old\\SmartPaste.exe\" --background" }, Command));
        Assert.True(StartupRegistration.IsEnabled(new FakeStartupValues { Run = Command.ToUpperInvariant() }, Command));
    }

    [Fact]
    public void TurningItOnInSmartPasteClearsTheOffSwitchInWindows()
    {
        var values = new FakeStartupValues { Approval = SwitchedOff };

        StartupRegistration.Enable(values, Command);

        Assert.Equal(Command, values.Run);
        Assert.Equal(SwitchedOn, values.Approval);
        Assert.True(StartupRegistration.IsEnabled(values, Command));
    }

    [Fact]
    public void TurningItOnLeavesWindowsAloneWhenItIsNotSwitchedOff()
    {
        var none = new FakeStartupValues();
        StartupRegistration.Enable(none, Command);
        Assert.Null(none.Approval);
        Assert.Equal(0, none.ApprovalWrites);

        var on = new FakeStartupValues { Approval = [0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] };
        StartupRegistration.Enable(on, Command);
        Assert.Equal(0, on.ApprovalWrites);
    }

    [Fact]
    public void TheCheckAtEveryStartKeepsAnOffChosenInWindows()
    {
        var values = new FakeStartupValues { Run = "\"D:\\old\\SmartPaste.exe\" --background", Approval = SwitchedOff };

        StartupRegistration.Refresh(values, Command);

        Assert.Equal(Command, values.Run);
        Assert.Equal(SwitchedOff, values.Approval);
        Assert.False(StartupRegistration.IsEnabled(values, Command));
    }

    [Fact]
    public void TheCheckAtEveryStartWritesNothingWhenTheEntryIsCurrent()
    {
        var values = new FakeStartupValues { Run = Command };
        StartupRegistration.Refresh(values, Command);
        Assert.Equal(0, values.RunWrites);
    }

    [Fact]
    public void TurningItOffRemovesTheEntryAndWindowsSwitch()
    {
        var values = new FakeStartupValues { Run = Command, Approval = SwitchedOff };

        StartupRegistration.Disable(values);

        Assert.Null(values.Run);
        Assert.Null(values.Approval);
    }

    private sealed class FakeStartupValues : IStartupValues
    {
        public string? Run { get; set; }

        public byte[]? Approval { get; set; }

        public int RunWrites { get; private set; }

        public int ApprovalWrites { get; private set; }

        public string? ReadRunCommand() => Run;

        public void WriteRunCommand(string command)
        {
            Run = command;
            RunWrites++;
        }

        public void DeleteRunCommand() => Run = null;

        public byte[]? ReadApproval() => Approval;

        public void WriteApproval(byte[] value)
        {
            Approval = value;
            ApprovalWrites++;
        }

        public void DeleteApproval() => Approval = null;
    }
}
