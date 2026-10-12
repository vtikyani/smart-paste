using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmartPaste.App.Services;
using SmartPaste.App.UI.Navigation;
using SmartPaste.App.UI.Onboarding.Steps;
using SmartPaste.App.UI.Settings;
using Wpf.Ui.Controls;

namespace SmartPaste.App.UI.Onboarding;

// The welcome tour window. Steps slide in the direction of travel. Closing it by hand marks the tour as seen; Smart
// Paste exiting (a restart, a sign-out, an update) does not, so the tour comes back at the next start.
internal partial class OnboardingWindow : FluentWindow
{
    private static readonly IReadOnlyDictionary<OnboardingStep, string> Titles = new Dictionary<OnboardingStep, string>
    {
        [OnboardingStep.Welcome] = "Welcome",
        [OnboardingStep.HowItWorks] = "How it works",
        [OnboardingStep.Connect] = "Connect Jev",
        [OnboardingStep.Privacy] = "What leaves your PC",
        [OnboardingStep.Practice] = "Try it",
        [OnboardingStep.Ready] = "You're all set"
    };

    private readonly Dictionary<OnboardingStep, FrameworkElement> _views = [];
    private int _transition;
    private bool _closingForExit;

    public OnboardingWindow(UiServices services, IWindowNavigator navigator)
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        var context = new SettingsContext(services, navigator, new ContentDialogPrompts(DialogHost), Dispatcher);
        ViewModel = new OnboardingViewModel(context);
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.CloseRequested += (_, _) => Close();
        // Enter or Space moves on right away; Tab reaches everything else.
        Loaded += (_, _) => NextButton.Focus();
        Closing += (_, _) =>
        {
            if (!_closingForExit) ViewModel.Complete(finished: false);
        };
        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            ViewModel.Dispose();
        };
        ShowStep(animate: false);
    }

    public OnboardingViewModel ViewModel { get; }

    // Closes the tour because Smart Paste is exiting. The user did not dismiss it, so it is not marked as seen.
    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnboardingViewModel.Step)) ShowStep(animate: IsLoaded);
    }

    // Swaps in the current step: the old one leaves quickly, the new one settles in from the side it came from.
    // With animation effects off, it only cross-fades.
    private void ShowStep(bool animate)
    {
        var view = ViewFor(ViewModel.Step);
        var version = ++_transition;
        AutomationProperties.SetName(StepHost, $"{ViewModel.StepLabel}: {Titles[ViewModel.Step]}");

        if (!animate || StepHost.Content is null)
        {
            StepHost.BeginAnimation(OpacityProperty, null);
            StepOffset.BeginAnimation(TranslateTransform.XProperty, null);
            StepHost.Content = view;
            return;
        }

        var slide = !Motion.IsReduced;
        var direction = ViewModel.MovedForward ? 1 : -1;
        var exit = new DoubleAnimation(0, Motion.Exit) { EasingFunction = Motion.EaseIn };
        exit.Completed += (_, _) =>
        {
            if (version != _transition) return;
            StepHost.Content = view;
            if (slide)
                StepOffset.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(18 * direction, 0, Motion.Enter) { EasingFunction = Motion.EaseOut });
            StepHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Enter) { EasingFunction = Motion.EaseOut });
            AnnounceStep();
        };
        if (slide)
            StepOffset.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-10 * direction, Motion.Exit) { EasingFunction = Motion.EaseIn });
        StepHost.BeginAnimation(OpacityProperty, exit);
    }

    // Screen readers hear "Step 3 of 6: Connect Jev" when the step changes.
    private void AnnounceStep()
    {
        AutomationProperties.SetLiveSetting(StepHost, AutomationLiveSetting.Polite);
        UIElementAutomationPeer.CreatePeerForElement(StepHost)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private FrameworkElement ViewFor(OnboardingStep step)
    {
        if (_views.TryGetValue(step, out var view)) return view;
        view = step switch
        {
            OnboardingStep.Welcome => new WelcomeStep(),
            OnboardingStep.HowItWorks => new HowItWorksStep(),
            OnboardingStep.Connect => new ConnectStep(),
            OnboardingStep.Privacy => new PrivacyStep(),
            OnboardingStep.Practice => new PracticeStep(),
            _ => new ReadyStep()
        };
        view.DataContext = ViewModel;
        _views[step] = view;
        return view;
    }
}
