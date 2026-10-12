using System.Diagnostics;
using System.Net.Http;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Shell;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// What happens on a copy. Any new copy disarms the previous block at once. Text that looks like several contact
// details is read on this PC first (LocalExtractor), so Ctrl+V works within milliseconds even offline; then, with an
// OpenRouter key, the parsing model reads it (secrets taken out first) and the richer result replaces the local one. A
// copied screenshot (only with "Read text from screenshots" on) is turned into text by Windows OCR on this PC and
// continues the same way.
internal sealed class CopyPipeline : IDisposable
{
    private readonly ClipboardWatcher _watcher;
    private readonly ProfileStore _profiles;
    private readonly SettingsStore _settings;
    private readonly AppState _state;
    private readonly HttpClient _http;
    private readonly WindowsOcr _ocr;
    private readonly Dispatcher _dispatcher;
    private readonly ServiceGuard _guard;

    // A single slow parse is normal; this many in a row means OpenRouter is effectively unreachable.
    private const int TimeoutsBeforeUnreachable = 3;

    // The copy being handled: set on the UI thread by each clipboard change.
    private int _generation;
    private CancellationToken _token;

    // The copy whose parse turned the "reading" indicator on (UI thread).
    private int _parsingGeneration;
    private int _timeoutsInARow;

    public CopyPipeline(ClipboardWatcher watcher, ProfileStore profiles, SettingsStore settings, AppState state,
        HttpClient http, WindowsOcr ocr, Dispatcher dispatcher)
    {
        _watcher = watcher;
        _profiles = profiles;
        _settings = settings;
        _state = state;
        _http = http;
        _ocr = ocr;
        _dispatcher = dispatcher;
        _guard = new ServiceGuard(HasKey ? ServiceStatus.Ready : ServiceStatus.NotConfigured);
        // Copies made in apps Smart Paste never touches (Remote Desktop, Hyper-V, password managers, and the apps the
        // user excluded) are never read, text or screenshot: the watcher asks before reading anything.
        watcher.IsSkippedOwner = IsSkippedApp;
        watcher.Changed += OnChanged;
        watcher.TextCopied += OnTextCopied;
        watcher.ImageCopied += OnImageCopied;
        CredentialStore.Changed += OnKeysChanged;
        Publish();
    }

    private static bool HasKey => CredentialStore.Exists(CredentialStore.OpenRouter);

    // UI thread: something new is on the clipboard. Whatever was armed belongs to the old copy.
    private void OnChanged()
    {
        (_generation, _token) = _profiles.BeginCopy();
        _state.IsParsing = false;
    }

    private void OnTextCopied(string text) => _ = HandleAsync(text, ProfileOrigin.CopiedText, _generation, _token);

    // UI thread, asked by the watcher with the clipboard open.
    private bool IsSkippedApp(string exe) =>
        InterceptGate.BuiltInBlockedApps.Contains(exe, StringComparer.OrdinalIgnoreCase) ||
        _settings.Current.ExcludedApps.Contains(exe, StringComparer.OrdinalIgnoreCase);

    private void OnImageCopied(byte[] png)
    {
        if (!_settings.Current.ReadScreenshots) return;
        var (generation, token) = (_generation, _token);
        _ = ReadScreenshotAsync(png, generation, token);
    }

    private async Task ReadScreenshotAsync(byte[] png, int generation, CancellationToken token)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var text = await _ocr.ReadTextAsync(png, token);
            Log.Event("screenshot_read", ("ms", clock.ElapsedMilliseconds), ("length", text?.Length ?? 0));
            if (!string.IsNullOrWhiteSpace(text)) await HandleAsync(text, ProfileOrigin.Screenshot, generation, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(string text, ProfileOrigin origin, int generation, CancellationToken token)
    {
        try
        {
            if (!_settings.Current.IsActiveAt(DateTimeOffset.Now) || !TextHeuristics.LooksMultiField(text)) return;

            // Local first: regex work on up to 20,000 characters stays off the UI thread.
            var local = await Task.Run(() => LocalExtractor.Extract(text, DateTimeOffset.UtcNow), token);
            if (_profiles.Arm(generation, local, origin, parsedByAi: false))
                Log.Event("copy_armed", ("source", "local"), ("count", local.Keys.Count), ("kind", origin));

            if (CredentialStore.Get(CredentialStore.OpenRouter) is not { } key || !_guard.CanCall(DateTimeOffset.Now)) return;
            SetParsing(generation, true);
            var clock = Stopwatch.StartNew();
            try
            {
                var parser = new OpenRouterParser(_http, key, _settings.Current.ParsingModel);
                var parsed = await parser.ParseTextAsync(text, DateTimeOffset.UtcNow, token);
                var merged = ProfileParser.Merge(parsed, local);
                if (_profiles.Arm(generation, merged, origin, parsedByAi: true))
                    Log.Event("copy_armed", ("source", "ai"), ("count", merged.Keys.Count), ("ms", clock.ElapsedMilliseconds));
                Interlocked.Exchange(ref _timeoutsInARow, 0);
                if (_guard.Succeeded()) Publish();
            }
            catch (ServiceException ex)
            {
                // The local result stays armed.
                Log.Event("parse_error", ("reason", ex.Failure.ToString()), ("status", ex.StatusCode ?? 0), ("ms", clock.ElapsedMilliseconds));
                var changed = ex.Failure == ServiceFailure.Timeout
                    ? Interlocked.Increment(ref _timeoutsInARow) >= TimeoutsBeforeUnreachable && _guard.Failed(ServiceFailure.Network, DateTimeOffset.Now)
                    : _guard.Failed(ex.Failure, DateTimeOffset.Now);
                if (ex.Failure != ServiceFailure.Timeout) Interlocked.Exchange(ref _timeoutsInARow, 0);
                if (changed) Publish();
            }
            finally
            {
                SetParsing(generation, false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer copy replaced this one.
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Log.Error("copy_error", ex);
        }
    }

    // The indicator belongs to one copy's parse: a newer copy's parse takes it over, and a parse that ends turns it off
    // only if it is still the one that turned it on (even when its copy was cleared meanwhile).
    private void SetParsing(int generation, bool parsing) => _dispatcher.BeginInvoke(() =>
    {
        if (parsing)
        {
            if (!_profiles.IsCurrent(generation)) return;
            _parsingGeneration = generation;
            _state.IsParsing = true;
        }
        else if (_parsingGeneration == generation)
        {
            _state.IsParsing = false;
        }
    });

    private void OnKeysChanged(string target)
    {
        if (target == CredentialStore.OpenRouter && _guard.Reset(HasKey)) Publish();
    }

    private void Publish()
    {
        if (_dispatcher.CheckAccess()) _state.ParsingStatus = _guard.Status;
        else _dispatcher.BeginInvoke(() => _state.ParsingStatus = _guard.Status);
    }

    public void Dispose()
    {
        _watcher.IsSkippedOwner = null;
        _watcher.Changed -= OnChanged;
        _watcher.TextCopied -= OnTextCopied;
        _watcher.ImageCopied -= OnImageCopied;
        CredentialStore.Changed -= OnKeysChanged;
    }
}
