using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.UI.Settings;

internal enum ApiKeyService { OpenRouter, TypeSafe }

internal enum KeyCheckState { None, Checking, Passed, Failed }

// One API key: whether it's saved, entering a new one, checking it, and removing it. Used on the Jev & AI page
// and in the welcome tour. The key itself only ever lives in Windows Credential Manager.
internal sealed partial class ApiKeyViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    private readonly SettingsContext _context;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _check;

    public ApiKeyViewModel(ApiKeyService service, SettingsContext context)
    {
        Service = service;
        _context = context;
        Target = service == ApiKeyService.OpenRouter ? CredentialStore.OpenRouter : CredentialStore.TypeSafe;
        IsSaved = context.Services.Keys.Exists(Target);
        context.Services.Keys.Changed += OnKeysChanged;
    }

    public ApiKeyService Service { get; }

    public string Target { get; }

    public string ServiceName => Service == ApiKeyService.OpenRouter ? "OpenRouter" : "TypeSafe";

    public string Title => ServiceName + " key";

    public string Description => Service == ApiKeyService.OpenRouter
        ? "Reads what you copy and lets Jev decide each paste. One key does both."
        : "Optional. A direct line to Jev. Without it, Jev runs through your OpenRouter key.";

    public bool IsOptional => Service == ApiKeyService.TypeSafe;

    public string GetKeyUrl => Service == ApiKeyService.OpenRouter ? "https://openrouter.ai/keys" : "https://typesafe.ai";

    public string GetKeyText => Service == ApiKeyService.OpenRouter ? "Get an OpenRouter key" : "Get a TypeSafe key";

    // Shown in the empty key box.
    public string Placeholder => IsSaved ? "Paste a new key to replace the saved one" : $"Paste your {ServiceName} key";

    // "Saved", "Not set", or "Optional".
    public string StateText => IsSaved ? "Saved" : IsOptional ? "Optional" : "Not set";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(Placeholder))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand), nameof(RemoveCommand))]
    public partial bool IsSaved { get; set; }

    // What the user typed or pasted into the key box. Cleared as soon as it's saved.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestCommand))]
    public partial string KeyText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecking), nameof(HasCheckMessage))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(TestCommand), nameof(RemoveCommand))]
    public partial KeyCheckState CheckState { get; set; }

    [ObservableProperty]
    public partial string CheckMessage { get; set; } = "";

    public bool IsChecking => CheckState == KeyCheckState.Checking;

    public bool HasCheckMessage => CheckState != KeyCheckState.None;

    private bool CanSave() => !string.IsNullOrWhiteSpace(KeyText) && !IsChecking;

    private bool CanTest() => (IsSaved || !string.IsNullOrWhiteSpace(KeyText)) && !IsChecking;

    private bool CanRemove() => IsSaved && !IsChecking;

    // Saves the key, then checks it right away so the user knows it works.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var key = KeyText.Trim();
        try
        {
            _context.Services.Keys.Set(Target, key);
        }
        catch (Win32Exception ex)
        {
            Log.Error("key_save_error", ex, ("key", ServiceName));
            Show(KeyCheckState.Failed, "Windows couldn't save the key. Try again in a moment.");
            return;
        }
        Log.Event("key_saved", ("key", ServiceName));
        KeyText = "";
        IsSaved = true;
        await CheckAsync(key);
    }

    // Checks the key in the box, or the saved key when the box is empty.
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        var key = string.IsNullOrWhiteSpace(KeyText) ? _context.Services.Keys.Get(Target) : KeyText.Trim();
        if (key is null)
        {
            IsSaved = false;
            return;
        }
        await CheckAsync(key);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        if (!await _context.Prompts.ConfirmAsync(new Confirmation($"Remove your {ServiceName} key?", WhatRemovingMeans(), "Remove key")))
            return;
        try
        {
            _context.Services.Keys.Delete(Target);
        }
        catch (Win32Exception ex)
        {
            Log.Error("key_delete_error", ex, ("key", ServiceName));
            Show(KeyCheckState.Failed, "Windows couldn't remove the key. Try again in a moment.");
            return;
        }
        Log.Event("key_removed", ("key", ServiceName));
        IsSaved = false;
        Show(KeyCheckState.None, "");
    }

    [RelayCommand]
    private void OpenGetKey() => _context.OpenUrl(GetKeyUrl);

    public void Dispose()
    {
        _context.Services.Keys.Changed -= OnKeysChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task CheckAsync(string key)
    {
        _check?.Cancel();
        using var check = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        check.CancelAfter(CheckTimeout);
        _check = check;
        Show(KeyCheckState.Checking, $"Checking with {ServiceName}…");
        try
        {
            var testers = _context.Services.Connections;
            var result = Service == ApiKeyService.OpenRouter
                ? await testers.CheckOpenRouterAsync(key, check.Token)
                : await testers.CheckTypeSafeAsync(key, check.Token);
            Show(result.Ok ? KeyCheckState.Passed : KeyCheckState.Failed,
                string.IsNullOrWhiteSpace(result.Message)
                    ? result.Ok ? $"{ServiceName} accepted the key." : $"{ServiceName} didn't accept the key."
                    : result.Message);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed while checking; nobody is waiting for the answer.
        }
        catch (OperationCanceledException) when (ReferenceEquals(_check, check))
        {
            Show(KeyCheckState.Failed, $"{ServiceName} didn't answer in time. Check your internet connection, then select Test.");
        }
        catch (OperationCanceledException)
        {
            // A newer check replaced this one.
        }
        catch (Exception ex)
        {
            // The tester reports expected failures in its result; anything thrown is unexpected, so log the type.
            Log.Error("key_check_error", ex, ("key", ServiceName));
            Show(KeyCheckState.Failed, $"Couldn't reach {ServiceName}. Check your internet connection, then select Test.");
        }
        finally
        {
            if (ReferenceEquals(_check, check)) _check = null;
        }
    }

    private void Show(KeyCheckState state, string message)
    {
        CheckMessage = message;
        CheckState = state;
    }

    private string WhatRemovingMeans()
    {
        var keys = _context.Services.Keys;
        if (Service == ApiKeyService.OpenRouter)
            return keys.Exists(CredentialStore.TypeSafe)
                ? "Copied text will be read on this PC only. Jev keeps deciding through your TypeSafe key."
                : "Copied text will be read on this PC only, and built-in labels will pick values until you add a key again.";
        return keys.Exists(CredentialStore.OpenRouter)
            ? "Jev will run through your OpenRouter key instead."
            : "Built-in labels will pick values until you add a key again.";
    }

    // Another window (the welcome tour, say) saved or removed this key.
    private void OnKeysChanged(string target)
    {
        if (target == Target) _context.Post(() => IsSaved = _context.Services.Keys.Exists(Target));
    }
}
