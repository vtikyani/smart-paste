using System.Windows.Threading;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

// The hook itself needs a real desktop (tests/SmartPaste.E2E); what runs without one is tested here.
public sealed class KeyboardHookTests
{
    [Fact]
    public async Task AHoldAskedWhileTheHookIsStoppedIsRefusedAtOnce()
    {
        using var hook = new KeyboardHook(Dispatcher.CurrentDispatcher);
        var hold = hook.HoldKeysAsync();
        Assert.True(hold.IsCompleted);
        Assert.Null(await hold);
    }
}
