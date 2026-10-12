using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Text;

namespace SmartPaste.App.UI.Tray;

// Everything the tray flyout shows, derived from AppState and settings, plus the actions it offers. Lives on the
// UI thread. While the flyout is open, a one-second clock keeps countdowns and "2 min ago" labels current.
internal sealed partial class FlyoutViewModel : ObservableObject, IDisposable
{
    public const int RecentShown = 5;

    private readonly AppState _state;
    private readonly AppController _controller;
    private readonly SettingsStore _settings;
    private readonly IWindowNavigator _navigator;
    private readonly TimeProvider _clock;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _ticker;
    private bool _syncing;

    public FlyoutViewModel(AppState state, AppController controller, SettingsStore settings, IWindowNavigator navigator, TimeProvider clock)
    {
        _state = state;
        _controller = controller;
        _settings = settings;
        _navigator = navigator;
        _clock = clock;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), _dispatcher) { IsEnabled = false };
        PauseOptions = PauseOption.All.Select(o => new PauseChoice(o.Label, new RelayCommand(() => Change(c => c.Pause(o.Length(_clock.GetLocalNow())))))).ToArray();

        _state.PropertyChanged += OnStateChanged;
        _state.Recent.CollectionChanged += OnRecentChanged;
        _settings.Changed += OnSettingsChanged;
        Refresh();
        RefreshRecent();
        PickerKeys = DisplayText.Keys(_settings.Current.PickerHotkey);
    }

    // Asks the window to close, before an action opens another window.
    public event EventHandler? CloseRequested;

    // The master switch in the header.
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    // The state in a few words, under the name in the header.
    [ObservableProperty]
    public partial string StatusLine { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsOff { get; private set; }

    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    [ObservableProperty]
    public partial bool IsParsing { get; private set; }

    [ObservableProperty]
    public partial bool IsArmed { get; private set; }

    // On, not paused, and waiting for a copy.
    [ObservableProperty]
    public partial bool IsWaiting { get; private set; }

    [ObservableProperty]
    public partial ArmedBlockViewModel? Armed { get; private set; }

    // Show the armed values instead of dots. Always starts hidden when the flyout opens.
    [ObservableProperty]
    public partial bool RevealValues { get; set; }

    [ObservableProperty]
    public partial ServiceSummary Services { get; private set; } = new("", Tone.Neutral);

    [ObservableProperty]
    public partial string? UpdateVersion { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> PickerKeys { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasRecent { get; private set; }

    public ObservableCollection<RecentEntryViewModel> Recent { get; } = [];

    public IReadOnlyList<PauseChoice> PauseOptions { get; }

    public bool CanPause => !IsOff && !IsPaused;

    // Called when the flyout opens: values start hidden and the clock runs while it is visible.
    public void Opened()
    {
        RevealValues = false;
        Refresh();
        Tick();
        _ticker.Start();
    }

    public void Closed() => _ticker.Stop();

    [RelayCommand]
    private void TurnOn() => Change(c => c.SetEnabled(true));

    [RelayCommand]
    private void Resume() => Change(c => c.Resume());

    [RelayCommand]
    private void Clear() => _controller.ClearProfile();

    [RelayCommand]
    private void ToggleReveal() => RevealValues = !RevealValues;

    [RelayCommand]
    private void OpenSettings() => Navigate(null);

    [RelayCommand]
    private void OpenServiceSettings() => Navigate(SettingsPages.Jev);

    [RelayCommand]
    private void OpenUpdate() => Navigate(SettingsPages.About);

    private void Navigate(string? page)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        _navigator.ShowSettings(page);
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing) Change(c => c.SetEnabled(value));
    }

    // The flyout has nowhere to show a failed save; the tray says it with a notification.
    private void Change(Action<AppController> change) => _controller.TryChange(change, "flyout");

    partial void OnRevealValuesChanged(bool value) => Armed?.SetReveal(value);

    partial void OnIsOffChanged(bool value) => OnPropertyChanged(nameof(CanPause));

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(CanPause));

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.Update) || e.PropertyName == nameof(AppState.Armed) || e.PropertyName == nameof(AppState.Status)
            || e.PropertyName == nameof(AppState.IsEnabled) || e.PropertyName == nameof(AppState.PausedUntil) || e.PropertyName == nameof(AppState.IsParsing)
            || e.PropertyName == nameof(AppState.JevStatus) || e.PropertyName == nameof(AppState.ParsingStatus))
            Refresh();
    }

    private void OnRecentChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshRecent();

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.PickerHotkey == current.PickerHotkey) return;
        if (_dispatcher.CheckAccess()) PickerKeys = DisplayText.Keys(current.PickerHotkey);
        else _dispatcher.BeginInvoke(() => PickerKeys = DisplayText.Keys(current.PickerHotkey));
    }

    private void Refresh()
    {
        var now = _clock.GetLocalNow();
        _syncing = true;
        IsOn = _state.IsEnabled;
        _syncing = false;

        var paused = _state.IsEnabled && _state.PausedUntil is { } until && until > now;
        IsOff = !_state.IsEnabled;
        IsPaused = paused;
        IsParsing = _state.IsEnabled && !paused && _state.IsParsing && _state.Armed is null;
        IsArmed = _state.IsEnabled && !paused && _state.Armed is not null;
        IsWaiting = _state.IsEnabled && !paused && !IsParsing && !IsArmed;
        StatusLine = StatusText.FlyoutStatus(_state, now, CultureInfo.CurrentCulture);
        Services = StatusText.Services(_state.JevStatus, _state.ParsingStatus);
        UpdateVersion = _state.Update?.Version;

        if (_state.Armed is { } armed && IsArmed)
        {
            if (!ReferenceEquals(Armed?.Source, armed)) Armed = new ArmedBlockViewModel(armed, RevealValues, now);
        }
        else
        {
            Armed = null;
        }
    }

    private void RefreshRecent()
    {
        var now = _clock.GetLocalNow();
        Recent.Clear();
        foreach (var entry in _state.Recent.Take(RecentShown)) Recent.Add(new RecentEntryViewModel(entry, now));
        HasRecent = Recent.Count > 0;
    }

    private void Tick()
    {
        var now = _clock.GetLocalNow();
        Armed?.Tick(now);
        foreach (var entry in Recent) entry.Tick(now);
        // A pause that ends "tomorrow at 9:00 AM" reads "9:00 AM" once midnight passes.
        StatusLine = StatusText.FlyoutStatus(_state, now, CultureInfo.CurrentCulture);
    }

    public void Dispose()
    {
        _ticker.Stop();
        _state.PropertyChanged -= OnStateChanged;
        _state.Recent.CollectionChanged -= OnRecentChanged;
        _settings.Changed -= OnSettingsChanged;
    }
}

// One pause length in the flyout's pause menu.
internal sealed record PauseChoice(string Label, IRelayCommand Command);
