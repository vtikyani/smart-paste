using System.Runtime.CompilerServices;

// App tests touch process-wide things (the clipboard, keyboard state, the log folder), so they run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static class TestEnvironment
{
    // Runs before any test code. Points the app's data folder at a throwaway directory so no test can read or
    // write the real settings, learned labels, or logs.
    [ModuleInitializer]
    internal static void Isolate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sp-tests-" + Environment.ProcessId);
        Environment.SetEnvironmentVariable("SMARTPASTE_DATA_DIR", dir);
    }
}
