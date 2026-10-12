using SmartPaste.App.Services;
using SmartPaste.App.Shell;

namespace SmartPaste.App.Tests.Shell;

// Plays Windows around an InputQueue: events the queue lets through reach the "app" and update the system key state,
// swallowed ones do not, and injected batches come back through the hook when Echo is called. AppView is what the
// focused app received, in order, for example "+ctrl PASTE -ctrl +tab -tab".
internal sealed class InputQueueHarness
{
    public const ushort Ctrl = VirtualKeys.LeftControl;
    public const ushort Shift = VirtualKeys.LeftShift;
    public const ushort Alt = VirtualKeys.LeftMenu;
    public const ushort Win = VirtualKeys.LeftWindows;
    public const ushort V = VirtualKeys.V;
    public const ushort Tab = VirtualKeys.Tab;
    public const ushort A = 0x41;
    public const ushort B = 0x42;
    public const ushort C = 0x43;
    public const ushort D = 0x44;
    public const ushort Down = VirtualKeys.Down;

    private readonly List<string> _app = [];
    private int _echoed;

    public InputQueueHarness(InputQueueOptions? options = null, Func<ushort, bool>? seed = null)
    {
        Host = new FakeHost();
        Queue = new InputQueue(Host, options);
        if (seed is not null) Queue.Seed(seed);
    }

    public FakeHost Host { get; }

    public InputQueue Queue { get; }

    public long Now { get; private set; } = 1000;

    public IKeySink? Capture { get; set; }

    public string AppView => string.Join(" ", _app);

    public List<string> Swallowed { get; } = [];

    public bool Key(KeyEvent key)
    {
        var swallowed = Queue.OnKey(key, Now, Capture);
        if (swallowed) Swallowed.Add(Describe(key));
        else Deliver(key);
        return swallowed;
    }

    public bool Press(ushort key) => Key(KeyEvent.Down(key));

    public bool Release(ushort key) => Key(KeyEvent.Up(key));

    public void Tap(ushort key)
    {
        Press(key);
        Release(key);
    }

    // The paste itself, as KeySender would inject it while Ctrl is already down for the app.
    public void Paste()
    {
        Queue.OnOwnInjected(KeyEvent.Down(V), Now);
        Queue.OnOwnInjected(KeyEvent.Up(V), Now);
        _app.Add("PASTE");
    }

    public void Complete(int sequence) => Queue.Complete(sequence, Now);

    // The next injected batch comes back through the hook: its keys reach the app, then its fence.
    public void Echo()
    {
        var (keys, fence) = Host.Injected[_echoed++];
        foreach (var key in keys)
        {
            Assert.False(Queue.OnOwnInjected(key, Now));
            Deliver(key);
        }
        Assert.True(Queue.OnOwnInjected(KeyEvent.Up(VirtualKeys.Fence, fence), Now));
    }

    public void EchoAll()
    {
        while (_echoed < Host.Injected.Count) Echo();
    }

    public int Unechoed => Host.Injected.Count - _echoed;

    public void Advance(long milliseconds)
    {
        Now += milliseconds;
        Queue.Tick(Now);
    }

    public static string Describe(KeyEvent key) => (key.IsUp ? "-" : "+") + Name(key.VirtualKey);

    private void Deliver(KeyEvent key)
    {
        Host.SystemKeys[key.VirtualKey] = !key.IsUp;
        _app.Add(Describe(key));
    }

    private static string Name(ushort key) => key switch
    {
        Ctrl or VirtualKeys.RightControl => "ctrl",
        Shift or VirtualKeys.RightShift => "shift",
        Alt or VirtualKeys.RightMenu => "alt",
        Win => "win",
        Tab => "tab",
        Down => "down",
        VirtualKeys.Enter => "enter",
        VirtualKeys.Packet => "packet",
        >= 0x41 and <= 0x5A => ((char)(key + 32)).ToString(),
        _ => "vk" + key.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
    };

    internal sealed class FakeHost : IInputQueueHost
    {
        public bool Intercept { get; set; } = true;

        public int InterceptCalls { get; private set; }

        public Dictionary<ushort, bool> SystemKeys { get; } = [];

        public List<int> Raised { get; } = [];

        public List<(int Sequence, string Reason)> Expired { get; } = [];

        public List<(KeyEvent[] Keys, byte Fence)> Injected { get; } = [];

        public List<KeyStroke> Delivered { get; } = [];

        public int OtherKeys { get; private set; }

        public List<string> Reports { get; } = [];

        public bool ShouldIntercept()
        {
            InterceptCalls++;
            return Intercept;
        }

        public bool IsKeyDownNow(ushort virtualKey) => SystemKeys.TryGetValue(virtualKey, out var down) && down;

        // A US layout: letters (shifted or with Ctrl as control characters), digits, Enter.
        public char? TranslateCharacter(in KeyEvent key, KeyStateTracker keys)
        {
            var virtualKey = key.VirtualKey;
            if (virtualKey is >= 0x41 and <= 0x5A)
            {
                if (keys.ControlDown) return (char)(virtualKey - 0x40);
                return keys.ShiftDown ? (char)virtualKey : (char)(virtualKey + 32);
            }
            if (virtualKey is >= 0x30 and <= 0x39) return (char)virtualKey;
            return virtualKey == VirtualKeys.Enter ? '\r' : null;
        }

        public void RaiseRequest(int sequence) => Raised.Add(sequence);

        public void ExpireRequest(int sequence, string reason) => Expired.Add((sequence, reason));

        public void Inject(IReadOnlyList<KeyEvent> keys, byte fence) => Injected.Add((keys.ToArray(), fence));

        public void DeliverKey(IKeySink capture, KeyStroke stroke) => Delivered.Add(stroke);

        public void DeliverOtherKey(IKeySink capture) => OtherKeys++;

        public void Report(string eventName, int count, string? reason = null) => Reports.Add(eventName);

        public string InjectedView(int index) => string.Join(" ", Injected[index].Keys.Select(Describe));
    }

    internal sealed class Sink(Func<KeyStroke, bool> accepts) : IKeySink
    {
        public List<KeyStroke> Offered { get; } = [];

        public bool Accepts(in KeyStroke key)
        {
            Offered.Add(key);
            return accepts(key);
        }

        public void OnKey(KeyStroke key)
        {
        }

        public void OnOtherKey()
        {
        }

        public void OnPointerDown(int x, int y)
        {
        }
    }
}
