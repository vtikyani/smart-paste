using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using SmartPaste.App.Infrastructure;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

// Update checks talk to GitHub through Velopack. These tests swap in an in-memory release feed, or the real GitHub
// source with HTTP calls that fail, so they run offline and never start the real updater.
public sealed class UpdateServiceTests : IDisposable
{
    private const string AppId = "SmartPaste.App";
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-update-" + Guid.NewGuid().ToString("N"));

    public UpdateServiceTests()
    {
        Directory.CreateDirectory(PackagesDir);
        Log.Initialize(LogDir);
    }

    private string PackagesDir => Path.Combine(_dir, "packages");

    private string LogDir => Path.Combine(_dir, "logs");

    public void Dispose()
    {
        Log.Initialize(AppPaths.LogDirectory);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task WithoutVelopackThereIsNothingToUpdate()
    {
        // The test host never ran VelopackApp, so this is not an installed copy.
        var service = new UpdateService(() => { });

        Assert.False(service.IsSupported);
        Assert.Equal(AppInfo.Version, service.CurrentVersion);
        Assert.Null(await service.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BuildFolderRunIsNotSupportedAndMakesNoNetworkCalls()
    {
        var source = new FakeSource { Feed = [Full("9.0.0")] };
        var service = new UpdateService(new UpdateManager(source, null, new NotInstalledLocator()), () => { }, Generous, Generous);

        Assert.False(service.IsSupported);
        Assert.Null(await service.CheckAsync(CancellationToken.None));
        Assert.Equal(0, source.FeedRequests);
    }

    [Fact]
    public async Task NewerReleaseIsOfferedWithItsNotes()
    {
        var service = Installed(new FakeSource { Feed = [Full("1.0.0"), Full("1.1.0", "Fixes the picker.")] });

        Assert.True(service.IsSupported);
        var update = await service.CheckAsync(CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("1.1.0", update.Version);
        Assert.Equal("Fixes the picker.", update.ReleaseNotesMarkdown);
        Assert.Contains("update_check outcome=available version=1.1.0", Logged());
    }

    [Fact]
    public async Task SameVersionIsNotAnUpdate()
    {
        var service = Installed(new FakeSource { Feed = [Full("1.0.0")] });

        Assert.Null(await service.CheckAsync(CancellationToken.None));
        Assert.Contains("update_check outcome=up_to_date", Logged());
    }

    [Fact]
    public async Task GitHubErrorMeansNoUpdateAndLogsOnlyTheStatus()
    {
        var github = new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false,
            new FailingDownloader(HttpStatusCode.NotFound));
        var service = Installed(github);

        Assert.Null(await service.CheckAsync(CancellationToken.None));
        var log = Logged();
        Assert.Contains("update_check_error status=404 error=HttpRequestException", log);
        Assert.DoesNotContain("github", log);
    }

    [Fact]
    public async Task CheckThatHangsGivesUpAtTheLimit()
    {
        var service = Installed(new FakeSource { FeedNeverAnswers = true }, checkTimeout: TimeSpan.FromMilliseconds(50));

        Assert.Null(await service.CheckAsync(CancellationToken.None));
        Assert.Contains("update_check_error reason=timeout", Logged());
    }

    [Fact]
    public async Task CallerCancellationIsPassedOn()
    {
        var service = Installed(new FakeSource { FeedNeverAnswers = true });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync(cancel.Token));
    }

    [Fact]
    public async Task DownloadReportsProgressAndLeavesThePackageReady()
    {
        byte[] package = [1, 2, 3, 4, 5, 6, 7, 8];
        var release = Full("1.1.0", payload: package);
        var service = Installed(new FakeSource { Feed = [release], Payload = package });
        var update = await service.CheckAsync(CancellationToken.None);
        var progress = new RecordingProgress();

        Assert.True(await service.DownloadAsync(update!, progress, CancellationToken.None));
        Assert.Contains(100, progress.Values);
        Assert.True(File.Exists(Path.Combine(PackagesDir, release.FileName)));
        Assert.Contains("update_download outcome=ready version=1.1.0", Logged());
    }

    [Fact]
    public async Task FailedDownloadReturnsFalseAndLeavesNoPartialFile()
    {
        var failure = new HttpRequestException("connection reset", null, HttpStatusCode.BadGateway);
        var service = Installed(new FakeSource { Feed = [Full("1.1.0")], DownloadError = failure });
        var update = await service.CheckAsync(CancellationToken.None);

        Assert.False(await service.DownloadAsync(update!, null, CancellationToken.None));
        Assert.Contains("update_download_error status=502 error=HttpRequestException", Logged());
        Assert.Empty(Directory.EnumerateFiles(PackagesDir, "*.nupkg*"));
    }

    [Fact]
    public async Task DownloadThatStallsGivesUpAtTheLimit()
    {
        var service = Installed(new FakeSource { Feed = [Full("1.1.0")], DownloadStalls = true },
            downloadTimeout: TimeSpan.FromMilliseconds(100));
        var update = await service.CheckAsync(CancellationToken.None);

        Assert.False(await service.DownloadAsync(update!, null, CancellationToken.None));
        Assert.Contains("update_download_error reason=timeout", Logged());
    }

    [Fact]
    public async Task RestartWithoutADownloadedUpdateKeepsTheAppRunning()
    {
        var exits = 0;
        var service = Installed(new FakeSource { Feed = [Full("1.1.0")] }, exitApp: () => exits++);
        var update = await service.CheckAsync(CancellationToken.None);

        service.ApplyAndRestart(update!);

        Assert.Equal(0, exits);
        Assert.Contains("update_apply_skipped reason=not_downloaded", Logged());
    }

    // An installed copy of version 1.0.0, as Velopack's test locator describes one. Nothing is started for real.
    private UpdateService Installed(IUpdateSource source, Action? exitApp = null, TimeSpan? checkTimeout = null,
        TimeSpan? downloadTimeout = null)
    {
        var locator = new TestVelopackLocator(AppId, "1.0.0", PackagesDir);
        return new UpdateService(new UpdateManager(source, null, locator), exitApp ?? (() => { }),
            checkTimeout ?? Generous, downloadTimeout ?? Generous);
    }

    private string Logged() => Directory.Exists(LogDir)
        ? string.Concat(Directory.EnumerateFiles(LogDir, "smartpaste-*.log").Select(File.ReadAllText))
        : "";

    private static VelopackAsset Full(string version, string? notes = null, byte[]? payload = null)
    {
        payload ??= [];
        return new VelopackAsset
        {
            PackageId = AppId,
            Version = SemanticVersion.Parse(version),
            Type = VelopackAssetType.Full,
            FileName = $"{AppId}-{version}-win-x64-full.nupkg",
            Size = payload.Length,
            SHA256 = Convert.ToHexString(SHA256.HashData(payload)),
            NotesMarkdown = notes
        };
    }

    private sealed class FakeSource : IUpdateSource
    {
        public VelopackAsset[] Feed { get; init; } = [];

        public bool FeedNeverAnswers { get; init; }

        public byte[] Payload { get; init; } = [];

        public Exception? DownloadError { get; init; }

        public bool DownloadStalls { get; init; }

        public int FeedRequests { get; private set; }

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
            Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            FeedRequests++;
            return FeedNeverAnswers
                ? new TaskCompletionSource<VelopackAssetFeed>().Task
                : Task.FromResult(new VelopackAssetFeed { Assets = Feed });
        }

        public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress, CancellationToken cancelToken = default)
        {
            if (DownloadStalls) await Task.Delay(Timeout.Infinite, cancelToken);
            if (DownloadError is not null) throw DownloadError;
            await File.WriteAllBytesAsync(localFile, Payload, cancelToken);
            progress(50);
            progress(100);
        }
    }

    // Every HTTP call fails the way GitHub answers for a private repository or one without releases.
    private sealed class FailingDownloader(HttpStatusCode status) : IFileDownloader
    {
        public Task DownloadFile(string url, string targetFile, Action<int> progress,
            IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default) =>
            Task.FromException(Failure(url));

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            Task.FromException<byte[]>(Failure(url));

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            Task.FromException<string>(Failure(url));

        private HttpRequestException Failure(string url) => new($"GET {url} failed", null, status);
    }

    // What VelopackApp finds when Smart Paste runs from a build folder: no manifest, so no version.
    private sealed class NotInstalledLocator : VelopackLocator
    {
        public override string? AppId => null;

        public override string? RootAppDir => null;

        public override string? PackagesDir => null;

        public override string? UpdateExePath => null;

        public override string? AppContentDir => null;

        public override string? Channel => null;

        public override SemanticVersion? CurrentlyInstalledVersion => null;

        public override IProcessImpl Process => throw new NotSupportedException("Nothing is started in these tests.");
    }

    private sealed class RecordingProgress : IProgress<int>
    {
        private readonly List<int> _values = [];

        public IReadOnlyList<int> Values
        {
            get { lock (_values) return [.. _values]; }
        }

        public void Report(int value)
        {
            lock (_values) _values.Add(value);
        }
    }
}
