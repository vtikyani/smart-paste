using System.Diagnostics;
using System.Net.Http;
using System.Windows.Threading;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Shell;
using SmartPaste.Core;

namespace SmartPaste.App.Services;

// What happens on a copy. Any new copy disarms the previous block at once. Text that looks like several contact
// details is read on this PC first (LocalExtractor), so Ctrl+V works within milliseconds even offline; then, with an
// OpenRouter key, the parsing model reads it and the richer result replaces the local one. A copied screenshot (only
// with "Read screenshots" on) is turned into text by Windows OCR on this PC and continues the same way.
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

    // The copy being handled: set on the UI thread by each clipboard change.
    private int _generation;
    private CancellationToken _token;

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

    private void OnTextCopied(string text, string? ownerExe) =>
        _ = HandleAsync(text, ProfileOrigin.CopiedText, _generation, _token);

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
                if (_guard.Succeeded()) Publish();
            }
            catch (ServiceException ex)
            {
                // The local result stays armed.
                Log.Event("parse_error", ("reason", ex.Failure.ToString()), ("status", ex.StatusCode ?? 0), ("ms", clock.ElapsedMilliseconds));
                if (_guard.Failed(ex.Failure, DateTimeOffset.Now)) Publish();
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

    private void SetParsing(int generation, bool parsing) => _dispatcher.BeginInvoke(() =>
    {
        if (_profiles.IsCurrent(generation)) _state.IsParsing = parsing;
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
        _watcher.Changed -= OnChanged;
        _watcher.TextCopied -= OnTextCopied;
        _watcher.ImageCopied -= OnImageCopied;
        CredentialStore.Changed -= OnKeysChanged;
    }
}
