using System.Windows;
using System.Windows.Media;
using SmartPaste.App.Services;
using SmartPaste.App.Tests.Settings;
using SmartPaste.App.UI.Onboarding;
using Wpf.Ui.Appearance;

namespace SmartPaste.App.Tests.Onboarding;

// Builds the real welcome tour on the UI test thread and renders every step off-screen, in light and dark.
// Set SMARTPASTE_SNAPSHOT_DIR to keep the images.
public sealed class OnboardingRenderTests
{
    [Fact]
    public void EveryStepRendersInLightAndDark() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            UiTestHost.UseTheme(theme);
            var window = new OnboardingWindow(ui.Services, ui.Navigator);
            try
            {
                UiTestHost.ShowOffScreen(window);
                await UiTestHost.Settle(200);
                foreach (var step in Enum.GetValues<OnboardingStep>())
                {
                    window.ViewModel.Step = step;
                    await UiTestHost.Settle(450);
                    Assert.Equal(step + "Step", window.StepHost.Content?.GetType().Name);
                    // The welcome loop never stops; show its finished frame so the picture is the same every time.
                    var illustrations = Descendants<PasteFlowIllustration>(window).ToList();
                    if (step == OnboardingStep.Welcome) Assert.Single(illustrations);
                    foreach (var illustration in illustrations) illustration.ShowFinished();
                    await UiTestHost.Settle();
                    var image = UiTestHost.Render(window, scale: 2);
                    Assert.True(UiTestHost.HasContent(image));
                    UiTestHost.SaveSnapshot(image, $"onboarding-{(int)step + 1}-{step.ToString().ToLowerInvariant()}-{theme.ToString().ToLowerInvariant()}");
                }
            }
            finally
            {
                window.Close();
            }
        }
        UiTestHost.UseTheme(ApplicationTheme.Dark);
    });

    [Fact]
    public void PracticeFormFillsWithTheHighlighter() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Practice.Results["First name"] = new PracticeResult("Jordan", "First name", nameof(DecisionMaker.Jev));
        ui.Practice.Results["Email address"] = new PracticeResult("jordan.rivera@example.com", "Email", nameof(DecisionMaker.Jev));
        ui.Practice.Results["ZIP code"] = new PracticeResult("07030", "ZIP / postal code", nameof(DecisionMaker.Jev));
        ui.Practice.Results["State name"] = new PracticeResult("New Jersey", "State", nameof(DecisionMaker.Jev));
        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            UiTestHost.UseTheme(theme);
            var window = new OnboardingWindow(ui.Services, ui.Navigator);
            try
            {
                UiTestHost.ShowOffScreen(window);
                window.ViewModel.Step = OnboardingStep.Practice;
                await UiTestHost.Settle(450);
                var practice = window.ViewModel.Practice;
                practice.CopySampleCommand.Execute(null);
                foreach (var field in practice.Fields.Take(3))
                {
                    await practice.PasteAsync(field);
                    await UiTestHost.Settle(1500);
                }
                // Catch the last one with the highlighter still down.
                await practice.PasteAsync(practice.Fields[3]);
                await UiTestHost.Settle(380);
                Assert.True(practice.IsComplete);
                var image = UiTestHost.Render(window, scale: 2);
                Assert.True(UiTestHost.HasContent(image));
                UiTestHost.SaveSnapshot(image, $"onboarding-practice-filled-{theme.ToString().ToLowerInvariant()}");
            }
            finally
            {
                window.Close();
            }
        }
        UiTestHost.UseTheme(ApplicationTheme.Dark);
    });

    [Fact]
    public void ConnectShowsTheCheckResult() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Connections.Result = new ConnectionCheck(true, "Connected. Jev is ready to decide your pastes.");
        UiTestHost.UseTheme(ApplicationTheme.Dark);
        var window = new OnboardingWindow(ui.Services, ui.Navigator);
        try
        {
            UiTestHost.ShowOffScreen(window);
            window.ViewModel.Step = OnboardingStep.Connect;
            await UiTestHost.Settle(450);
            window.ViewModel.OpenRouterKey.KeyText = "sk-or-v1-example";
            await window.ViewModel.OpenRouterKey.SaveCommand.ExecuteAsync(null);
            await UiTestHost.Settle();

            Assert.Equal("Next", window.ViewModel.NextText);
            var image = UiTestHost.Render(window, scale: 2);
            Assert.True(UiTestHost.HasContent(image));
            UiTestHost.SaveSnapshot(image, "onboarding-3-connect-saved-dark");
        }
        finally
        {
            window.Close();
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
