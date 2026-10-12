using System.ComponentModel;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Settings;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Onboarding;

internal enum OnboardingStep { Welcome, HowItWorks, Connect, Privacy, Practice, Ready }

// One dot of the progress indicator.
internal sealed partial class StepDot : ObservableObject
{
    public StepDot(OnboardingStep step)
    {
        Step = step;
    }

    public OnboardingStep Step { get; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }
}

// The welcome tour: six short steps, Back / Next / Skip, and what happens when it ends. Finishing or closing it
// marks the tour as seen, so it doesn't come back on its own.
internal sealed partial class OnboardingViewModel : ObservableObject, IDisposable
{
    public static readonly int StepCount = Enum.GetValues<OnboardingStep>().Length;

    private readonly SettingsContext _context;

    public OnboardingViewModel(SettingsContext context)
    {
        _context = context;
        OpenRouterKey = new ApiKeyViewModel(ApiKeyService.OpenRouter, context);
        Practice = new PracticeViewModel(context);
        Dots = Enum.GetValues<OnboardingStep>().Select(s => new StepDot(s)).ToArray();
        OpenRouterKey.PropertyChanged += OnKeyChanged;
        context.Services.Settings.Changed += OnSettingsChanged;
        BlockLifetime = StatusText.Minutes(context.Settings.ProfileLifetimeMinutes);
        RefreshDots();
    }

    // Asks the window to close (after Finish or Skip).
    public event EventHandler? CloseRequested;

    public ApiKeyViewModel OpenRouterKey { get; }

    public PracticeViewModel Practice { get; }

    public IReadOnlyList<StepDot> Dots { get; }

    public Notice Notice => _context.Notice;

    // True after the tour has been marked as seen.
    public bool IsCompleted { get; private set; }

    // Which way the last move went, so the step transition can slide the right way.
    public bool MovedForward { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepIndex), nameof(StepLabel), nameof(CanGoBack), nameof(IsLastStep), nameof(NextText), nameof(CanSkip))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial OnboardingStep Step { get; set; }

    // Applied when the tour finishes, not before: closing early keeps whatever was set already.
    [ObservableProperty]
    public partial bool StartAtLogin { get; set; } = true;

    // "15 minutes", for the privacy step.
    [ObservableProperty]
    public partial string BlockLifetime { get; set; } = "";

    public int StepIndex => (int)Step;

    public string StepLabel => $"Step {StepIndex + 1} of {StepCount}";

    public bool CanGoBack => Step > OnboardingStep.Welcome;

    public bool IsLastStep => Step == OnboardingStep.Ready;

    public bool CanSkip => !IsLastStep;

    public string NextText => Step switch
    {
        OnboardingStep.Welcome => "Get started",
        OnboardingStep.Connect when !OpenRouterKey.IsSaved => "Skip for now",
        OnboardingStep.Ready => "Finish",
        _ => "Next"
    };

    partial void OnStepChanged(OnboardingStep value) => RefreshDots();

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep)
        {
            Finish();
            return;
        }
        MovedForward = true;
        Step++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        MovedForward = false;
        Step--;
    }

    [RelayCommand]
    private void Skip()
    {
        Complete(finished: false);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Finish()
    {
        Complete(finished: true);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenPrivacySettings() => _context.Navigator.ShowSettings(SettingsPages.Privacy);

    // Marks the tour as seen. Finishing also applies "Start when I sign in". Safe to call more than once.
    public void Complete(bool finished)
    {
        if (IsCompleted) return;
        IsCompleted = true;
        Log.Event("onboarding_done", ("outcome", finished ? "finished" : "closed"), ("stage", Step));
        if (!finished)
        {
            _context.TrySave(s => s with { OnboardingCompleted = true });
            return;
        }
        var startAtLogin = StartAtLogin;
        if (!_context.TrySave(s => s with { OnboardingCompleted = true, StartAtLogin = startAtLogin })) return;
        try
        {
            StartupRegistration.Apply(startAtLogin);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error("startup_apply_error", ex);
            _context.Notice.Show(InfoBarSeverity.Warning, "Couldn't change how Smart Paste starts",
                "Windows didn't allow it. You can change it in Settings > Apps > Startup.");
        }
    }

    public void Dispose()
    {
        OpenRouterKey.PropertyChanged -= OnKeyChanged;
        _context.Services.Settings.Changed -= OnSettingsChanged;
        OpenRouterKey.Dispose();
        Practice.Dispose();
    }

    private void RefreshDots()
    {
        foreach (var dot in Dots)
        {
            dot.IsCurrent = dot.Step == Step;
            dot.IsDone = dot.Step < Step;
        }
    }

    private void OnKeyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApiKeyViewModel.IsSaved)) OnPropertyChanged(nameof(NextText));
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current) =>
        _context.Post(() => BlockLifetime = StatusText.Minutes(current.ProfileLifetimeMinutes));
}
