using System.Collections.Concurrent;

namespace SmartPaste.App.Shell;

// WPF imaging objects belong to the thread that creates them, and every thread that creates one gets a WPF
// dispatcher (with a hidden window) for good. One long-lived thread does all image conversion, so the UI thread stays
// responsive and thread-pool threads never collect dispatchers.
internal sealed class ImagingThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;

    public ImagingThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Smart Paste imaging" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _work.Add(() =>
            {
                try
                {
                    result.SetResult(work());
                }
                catch (Exception ex)
                {
                    // Handed to the awaiting caller, which decides what a failure means.
                    result.SetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // Disposed: no more work is accepted.
            result.SetCanceled();
        }
        return result.Task;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        if (_thread.Join(TimeSpan.FromSeconds(2))) _work.Dispose();
    }

    private void Run()
    {
        foreach (var work in _work.GetConsumingEnumerable()) work();
    }
}
