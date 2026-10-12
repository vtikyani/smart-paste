using System.Diagnostics;
using Interop.UIAutomationClient;

namespace SmartPaste.E2E;

// End-to-end tests of SmartPaste.exe on a real desktop: real forms (WPF, Win32, and Edge), real keyboard input,
// the real clipboard. Usage: SmartPaste.E2E [--app <path to SmartPaste.exe>] [--only wpf|win32|edge]
// It takes over the keyboard and focus for about a minute; the clipboard is backed up and restored.
internal static class Program
{
    private const string MainBlock =
        "Jordan Rivera\r\n123 Main St Apt 4B\r\nSpringfield, NJ 07081\r\njordan.rivera@example.com\r\n(201) 555-0100";

    private const string Win32Block =
        "Jordan Rivera\r\n123 Main St\r\nSpringfield, New Jersey 07081-1234\r\n(201) 555-0100\r\njordan.rivera@example.com";

    private const string TwoAddressBlock =
        "Jordan Rivera\r\nBilling address: 1 Main St, Springfield, NJ 07081\r\nShipping address: 2 Oak Ave, Trenton, NJ 08608\r\njordan.rivera@example.com";

    private const string SettingsJson = """
        {
          "version": 2,
          "enabled": true,
          "startAtLogin": false,
          "onboardingCompleted": true,
          "checkForUpdates": false,
          "askJevAhead": true,
          "showFieldHints": true,
          "showPasteConfirmation": true,
          "pickerHotkey": "Win+Alt+V"
        }
        """;

    private static readonly List<Result> Results = [];
    private static readonly Uia Automation = new();

    [STAThread]
    private static int Main(string[] args)
    {
        var appPath = Argument(args, "--app") ?? DefaultAppPath();
        var only = Argument(args, "--only");
        if (!File.Exists(appPath))
        {
            Console.Error.WriteLine($"SmartPaste.exe not found at {appPath}. Build the app first or pass --app.");
            return 2;
        }

        Console.WriteLine("Smart Paste end-to-end tests. Hands off the keyboard and mouse for about a minute.");
        var backup = ClipboardBackup.Take();
        Console.WriteLine($"Clipboard backed up ({backup.FormatCount} formats).");
        try
        {
            using var app = new AppUnderTest(appPath, SettingsJson);
            if (!app.WaitUntilReady(TimeSpan.FromSeconds(10)))
            {
                Console.Error.WriteLine("SmartPaste.exe exited during start-up.");
                return 3;
            }
            try
            {
                if (only is null or "wpf") RunWpf(app);
                if (only is null or "win32") RunWin32();
                if (only is null or "edge") RunEdge();
            }
            finally
            {
                if (Results.Any(r => !r.Passed))
                {
                    Console.WriteLine();
                    Console.WriteLine("App log:");
                    Console.WriteLine(app.Logs());
                }
            }
        }
        finally
        {
            backup.Restore();
            Console.WriteLine("Clipboard restored.");
        }

        Report();
        return Results.All(r => r.Passed) ? 0 : 1;
    }

    private static void RunWpf(AppUnderTest app)
    {
        using var form = new WpfTargetForm();
        var root = Automation.Element(form.Handle);
        IUIAutomationElement Field(string id) => Automation.FindById(root, id, TimeSpan.FromSeconds(5))
            ?? throw new InvalidOperationException($"Field {id} not found.");

        Copy(MainBlock);
        Expect("wpf", "First name", PasteInto(form.Handle, Field("e2e-1")), "Jordan");
        Expect("wpf", "Last name", PasteInto(form.Handle, Field("e2e-2")), "Rivera");
        Expect("wpf", "Email address", PasteInto(form.Handle, Field("e2e-3")), "jordan.rivera@example.com");
        Expect("wpf", "Phone", PasteInto(form.Handle, Field("e2e-4")), "(201) 555-0100");
        Expect("wpf", "Street address", PasteInto(form.Handle, Field("e2e-5")), "123 Main St");
        Expect("wpf", "City (label beside it)", PasteInto(form.Handle, Field("e2e-6")), "Springfield");
        Expect("wpf", "State", PasteInto(form.Handle, Field("e2e-7")), "NJ");
        Expect("wpf", "ZIP code", PasteInto(form.Handle, Field("e2e-8")), "07081");
        ExpectPlainPaste("wpf", "Card number (never smart)", PasteInto(form.Handle, Field("e2e-9")), MainBlock);
        Expect("wpf", "Email (label is a picture: OCR)", PasteInto(form.Handle, Field("e2e-10")), "jordan.rivera@example.com");
        ExpectClipboard("wpf", "clipboard restored after pastes", MainBlock);

        // Fast form filling: Ctrl+V, Tab, Ctrl+V, Tab, Ctrl+V with no pauses must land each value in its own field.
        form.Clear();
        Copy(MainBlock);
        if (Prepare(form.Handle, Field("e2e-1")))
        {
            Native.Chord(Native.VkControl, Native.VkV);
            Native.Tap(Native.VkTab);
            Native.Chord(Native.VkControl, Native.VkV);
            Native.Tap(Native.VkTab);
            Native.Chord(Native.VkControl, Native.VkV);
            Thread.Sleep(4000);
            Expect("wpf-fast", "First name", Automation.Value(Field("e2e-1")), "Jordan");
            Expect("wpf-fast", "Last name", Automation.Value(Field("e2e-2")), "Rivera");
            Expect("wpf-fast", "Email address", Automation.Value(Field("e2e-3")), "jordan.rivera@example.com");
        }

        // Two values fit a plain "Street address" field: the picker appears without taking focus.
        form.Clear();
        Copy(TwoAddressBlock);
        if (Prepare(form.Handle, Field("e2e-5")))
        {
            Native.Chord(Native.VkControl, Native.VkV);
            var picker = WaitForPicker(app.ProcessId);
            Expect("wpf-picker", "picker shown", picker != IntPtr.Zero ? "shown" : "not shown", "shown");
            Expect("wpf-picker", "target kept focus", Native.RootOf(Native.GetForegroundWindow()) == form.Handle ? "yes" : "no", "yes");
            if (picker != IntPtr.Zero)
            {
                Native.Tap(0x32); // "2"
                var value = WaitForValue(Field("e2e-5"), "");
                ExpectOneOf("wpf-picker", "chosen street", value, "1 Main St", "2 Oak Ave");
            }
        }
        form.Clear();
        if (Prepare(form.Handle, Field("e2e-5")))
        {
            Native.Chord(Native.VkControl, Native.VkV);
            if (WaitForPicker(app.ProcessId) != IntPtr.Zero)
            {
                Native.Tap(0x1B); // Escape: paste the original
                ExpectPlainPaste("wpf-picker", "Escape pastes original", WaitForValue(Field("e2e-5"), ""), TwoAddressBlock);
            }
        }

        // The picker shortcut lists every value of the copied block.
        form.Clear();
        Copy(MainBlock);
        if (Prepare(form.Handle, Field("e2e-2")))
        {
            Native.Chord(Native.VkLWin, Native.VkMenu, Native.VkV);
            var picker = WaitForPicker(app.ProcessId);
            Expect("wpf-shortcut", "picker shown", picker != IntPtr.Zero ? "shown" : "not shown", "shown");
            if (picker != IntPtr.Zero)
            {
                Native.Tap(0x31); // "1"
                var value = WaitForValue(Field("e2e-2"), "");
                Expect("wpf-shortcut", "a copied value was pasted", value is { Length: > 0 } && MainBlock.Contains(value, StringComparison.Ordinal) ? "yes" : value, "yes");
            }
        }
    }

    private static void RunWin32()
    {
        using var form = new Win32TargetForm();
        var root = Automation.Element(form.Handle);
        IUIAutomationElement Field(string id) => Automation.FindById(root, id, TimeSpan.FromSeconds(5))
            ?? throw new InvalidOperationException($"Field {id} not found.");

        Copy(Win32Block);
        Expect("win32", "First name", PasteInto(form.Handle, Field("w32-first")), "Jordan");
        Expect("win32", "State, 2 letters max", PasteInto(form.Handle, Field("w32-state")), "NJ");
        Expect("win32", "ZIP, 5 characters max", PasteInto(form.Handle, Field("w32-zip")), "07081");
        Expect("win32", "Phone, 10 characters max", PasteInto(form.Handle, Field("w32-phone")), "2015550100");
        ExpectClipboard("win32", "clipboard restored after pastes", Win32Block);
    }

    private static void RunEdge()
    {
        var html = Path.Combine(AppContext.BaseDirectory, "forms", "checkout.html");
        using var edge = new EdgeSession(html);
        var window = Automation.FindWindow("Smart Paste E2E checkout", TimeSpan.FromSeconds(20));
        if (window is null)
        {
            Results.Add(new("edge", "browser window", "found", "not found", false, 0));
            return;
        }
        var handle = (IntPtr)window.CurrentNativeWindowHandle;
        IUIAutomationElement Field(string id) => Automation.FindById(window, id, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException($"Field {id} not found.");

        Copy(MainBlock);
        Expect("edge", "First name", PasteInto(handle, Field("first")), "Jordan");
        Expect("edge", "Last name", PasteInto(handle, Field("last")), "Rivera");
        Expect("edge", "Email (placeholder only)", PasteInto(handle, Field("email")), "jordan.rivera@example.com");
        Expect("edge", "Phone (placeholder pattern)", PasteInto(handle, Field("phone")), "(201) 555-0100");
        Expect("edge", "Street address", PasteInto(handle, Field("street")), "123 Main St");
        Expect("edge", "City", PasteInto(handle, Field("city")), "Springfield");
        Expect("edge", "ZIP code", PasteInto(handle, Field("zip")), "07081");
        ExpectPlainPaste("edge", "Card number (never smart)", PasteInto(handle, Field("card")), MainBlock);
        ExpectClipboard("edge", "clipboard restored after pastes", MainBlock);

        // Ctrl+Shift+V is never intercepted: the browser pastes the original block as plain text.
        Automation.SetValue(Field("city"), "");
        if (Prepare(handle, Field("city")))
        {
            Native.Chord(Native.VkControl, Native.VkShift, Native.VkV);
            ExpectPlainPaste("edge", "Ctrl+Shift+V untouched", WaitForValue(Field("city"), ""), MainBlock);
        }
    }

    // ---- steps ----

    private static void Copy(string text)
    {
        Native.WriteClipboardText(text);
        // Give the app a moment to see the copy and arm the block.
        Thread.Sleep(800);
    }

    // Focuses the field in its window, refusing to type anything unless that window is in front.
    private static bool Prepare(IntPtr window, IUIAutomationElement field)
    {
        for (var i = 0; i < 40 && (Native.IsDown(Native.VkControl) || Native.IsDown(Native.VkShift) ||
                                   Native.IsDown(Native.VkMenu) || Native.IsDown(Native.VkLWin)); i++)
            Thread.Sleep(50);
        if (!Native.ForceForeground(window) || !Automation.Focus(field, TimeSpan.FromSeconds(3)))
        {
            Results.Add(new("setup", "focus " + (Automation.Name(field) ?? "field"), "focused", "could not focus", false, 0));
            return false;
        }
        Thread.Sleep(250);
        return Native.RootOf(Native.GetForegroundWindow()) == window;
    }

    private static Pasted PasteInto(IntPtr window, IUIAutomationElement field)
    {
        if (!Prepare(window, field)) return new(null, 0);
        var before = Automation.Value(field) ?? "";
        var clock = Stopwatch.StartNew();
        Native.Chord(Native.VkControl, Native.VkV);
        var value = WaitForValue(field, before);
        return new(value, clock.ElapsedMilliseconds);
    }

    private static string? WaitForValue(IUIAutomationElement field, string before)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? value = before;
        while (DateTime.UtcNow < until)
        {
            value = Automation.Value(field);
            if (value != before) break;
            Thread.Sleep(30);
        }
        Thread.Sleep(200);
        return Automation.Value(field) ?? value;
    }

    // The picker is the app's one large non-activating window (the small chips are well under 80 px tall).
    private static IntPtr WaitForPicker(uint processId)
    {
        var until = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < until)
        {
            foreach (var hwnd in Native.VisibleWindowsOf(processId))
                if ((Native.GetWindowLong(hwnd, Native.GwlExStyle) & Native.WsExNoActivate) != 0 && Native.SizeOf(hwnd).Height >= 80)
                    return hwnd;
            Thread.Sleep(50);
        }
        return IntPtr.Zero;
    }

    // ---- checks ----

    private sealed record Pasted(string? Value, long Ms);

    private sealed record Result(string Scenario, string Check, string Expected, string? Actual, bool Passed, long Ms);

    private static void Expect(string scenario, string check, Pasted pasted, string expected) =>
        Results.Add(new(scenario, check, expected, pasted.Value, pasted.Value == expected, pasted.Ms));

    private static void Expect(string scenario, string check, string? actual, string expected) =>
        Results.Add(new(scenario, check, expected, actual, actual == expected, 0));

    private static void ExpectOneOf(string scenario, string check, string? actual, params string[] expected) =>
        Results.Add(new(scenario, check, string.Join(" | ", expected), actual, actual is not null && expected.Contains(actual), 0));

    // A normal paste: the field got the original block (single-line boxes keep only part of it).
    private static void ExpectPlainPaste(string scenario, string check, Pasted pasted, string block) =>
        ExpectPlainPaste(scenario, check, pasted.Value, block, pasted.Ms);

    private static void ExpectPlainPaste(string scenario, string check, string? actual, string block, long ms = 0)
    {
        var firstLine = block.Split("\r\n")[0];
        var flattened = block.Replace("\r\n", "");
        var passed = actual is not null && (actual == block || actual == firstLine || actual.Replace(" ", "") == flattened.Replace(" ", "")
                                            || actual.StartsWith(firstLine, StringComparison.Ordinal));
        Results.Add(new(scenario, check, "original block", actual, passed, ms));
    }

    private static void ExpectClipboard(string scenario, string check, string expected)
    {
        string? actual = null;
        for (var i = 0; i < 30; i++)
        {
            actual = Native.ReadClipboardText();
            if (actual == expected) break;
            Thread.Sleep(100);
        }
        Results.Add(new(scenario, check, "unchanged", actual == expected ? "unchanged" : $"changed to: {actual}", actual == expected, 0));
    }

    private static void Report()
    {
        Console.WriteLine();
        foreach (var r in Results)
        {
            var mark = r.Passed ? "PASS" : "FAIL";
            var timing = r.Ms > 0 ? $" ({r.Ms} ms)" : "";
            Console.WriteLine($"{mark}  {r.Scenario,-13} {r.Check,-36} expected: {Shorten(r.Expected)}  actual: {Shorten(r.Actual)}{timing}");
        }
        Console.WriteLine();
        Console.WriteLine($"{Results.Count(r => r.Passed)} of {Results.Count} checks passed.");
    }

    private static string Shorten(string? value)
    {
        if (value is null) return "(none)";
        var flat = value.Replace("\r\n", " / ").Replace("\n", " / ");
        return flat.Length <= 48 ? flat : flat[..45] + "...";
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string DefaultAppPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartPaste.slnx"))) dir = dir.Parent;
        var repo = dir?.FullName ?? Environment.CurrentDirectory;
        return Path.Combine(repo, "src", "SmartPaste.App", "bin", "Debug", "net10.0-windows10.0.19041.0", "SmartPaste.exe");
    }
}
