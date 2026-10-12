using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using SmartPaste.App.Services;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace SmartPaste.App.Infrastructure;

// Updates from GitHub Releases through Velopack, the installer and updater. A copy installed with Setup.exe or
// unpacked from the portable zip can update itself; a run from a build folder cannot (IsSupported is false).
// Network and disk problems never reach the caller: they are logged by exception type (never the message), a
// failed check returns null, and a failed download returns false.
internal sealed class UpdateService : IUpdateService
{
    // Velopack's own network timeout is 30 minutes and its check cannot be canceled. These limits keep a dead
    // connection from stalling a check or a download.
    private static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromMinutes(30);

    private readonly UpdateManager? _manager;
    private readonly Action _exitApp;
    private readonly TimeSpan _checkTimeout;
    private readonly TimeSpan _downloadTimeout;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    // exitApp closes the app the normal way (tray icon removed, pending work finished). ApplyAndRestart calls it
    // right after handing over to the updater, on the caller's thread.
    public UpdateService(Action exitApp)
        : this(CreateManager(), exitApp, DefaultCheckTimeout, DefaultDownloadTimeout)
    {
    }

    internal UpdateService(UpdateManager? manager, Action exitApp, TimeSpan checkTimeout, TimeSpan downloadTimeout)
    {
        ArgumentNullException.ThrowIfNull(exitApp);
        _manager = manager;
        _exitApp = exitApp;
        _checkTimeout = checkTimeout;
        _downloadTimeout = downloadTimeout;
    }

    public bool IsSupported => _manager is { IsInstalled: true };

    public string CurrentVersion => AppInfo.Version;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        if (_manager is not { IsInstalled: true } manager) return UpdateCheckResult.Failed;

        var check = manager.CheckForUpdatesAsync();
        // When the wait below gives up, the check keeps running. Observe its result so a late failure is not
        // reported as an unobserved task exception.
        _ = check.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            var info = await check.WaitAsync(_checkTimeout, ct).ConfigureAwait(false);
            if (info is null)
            {
                Log.Event("update_check", ("outcome", "up_to_date"));
                return UpdateCheckResult.UpToDate;
            }

            var update = new VelopackUpdate(info);
            Log.Event("update_check", ("outcome", "available"), ("version", update.Version));
            return UpdateCheckResult.Found(update);
        }
        catch (Exception ex) when (IsFailure(ex, ct))
        {
            LogFailure("update_check_error", ex);
            return UpdateCheckResult.Failed;
        }
    }

    public async Task<bool> DownloadAsync(AvailableUpdate update, IProgress<int>? progress, CancellationToken ct)
    {
        var target = Unwrap(update);
        if (_manager is not { IsInstalled: true } manager) return false;

        // One download at a time. A second caller waits, then finds the package already on disk.
        await _downloadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(_downloadTimeout);
            var started = Stopwatch.GetTimestamp();
            await manager.DownloadUpdatesAsync(target.Info, percent => progress?.Report(percent), limit.Token)
                .ConfigureAwait(false);
            Log.Event("update_download", ("outcome", "ready"), ("version", target.Version),
                ("ms", Stopwatch.GetElapsedTime(started)));
            return true;
        }
        catch (Exception ex) when (IsFailure(ex, ct))
        {
            LogFailure("update_download_error", ex);
            return false;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    public bool ApplyAndRestart(AvailableUpdate update)
    {
        var target = Unwrap(update);
        if (_manager is not { IsInstalled: true } manager) return false;

        // The newest package on disk that is newer than this version, put there by DownloadAsync.
        var downloaded = manager.UpdatePendingRestart;
        if (downloaded is null || downloaded.Version < target.Info.TargetFullRelease.Version)
        {
            Log.Event("update_apply_skipped", ("reason", "not_downloaded"));
            return false;
        }

        try
        {
            // Starts Velopack's updater. It waits for this process to exit (and stops it if it is still running a
            // minute later), installs the update, and starts the new version.
            manager.WaitExitThenApplyUpdates(downloaded, silent: false, restart: true);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Error("update_apply_error", ex);
            return false;
        }

        Log.Event("update_apply", ("version", downloaded.Version.ToString()));
        _exitApp();
        return true;
    }

    private static UpdateManager? CreateManager()
    {
        // Program.Main runs VelopackApp before anything else, and that is what finds out whether this copy was
        // installed. Without it (in a test host, say) there is nothing to update.
        if (!VelopackLocator.IsCurrentSet) return null;

        if (!Uri.TryCreate(AppInfo.RepositoryUrl, UriKind.Absolute, out var repository) ||
            repository.Scheme != Uri.UriSchemeHttps)
        {
            Log.Event("update_unavailable", ("reason", "no_repository"));
            return null;
        }

        // Stable releases only, and no token: the repository is public, and one anonymous check every few hours
        // stays far below GitHub's limit of 60 requests an hour.
        return new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false));
    }

    private static VelopackUpdate Unwrap(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update as VelopackUpdate
               ?? throw new ArgumentException("Pass an update returned by CheckAsync.", nameof(update));
    }

    // Any failure of a check or a download means "try again later" (no connection, GitHub's rate limit, no
    // releases yet, a bad feed, a full or locked disk, a checksum mismatch), so none of them may reach the
    // caller. Only the caller's own cancellation is passed on.
    private static bool IsFailure(Exception ex, CancellationToken ct) =>
        ex is not OutOfMemoryException && !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private static void LogFailure(string name, Exception ex)
    {
        switch (ex)
        {
            // Our time limit or Velopack's ran out (the caller's cancellation never gets here).
            case TimeoutException or OperationCanceledException:
                Log.Event(name, ("reason", "timeout"));
                break;
            // 403 is GitHub's limit for anonymous requests; 404 means the repository has no public releases.
            case HttpRequestException { StatusCode: { } status }:
                Log.Error(name, ex, ("status", (int)status));
                break;
            default:
                Log.Error(name, ex);
                break;
        }
    }

    // An update found by CheckAsync, with what Velopack needs to download and install it.
    private sealed class VelopackUpdate(UpdateInfo info) : AvailableUpdate
    {
        public UpdateInfo Info { get; } = info;

        public override string Version => Info.TargetFullRelease.Version.ToString();

        public override string? ReleaseNotesMarkdown =>
            string.IsNullOrWhiteSpace(Info.TargetFullRelease.NotesMarkdown) ? null : Info.TargetFullRelease.NotesMarkdown;
    }
}
