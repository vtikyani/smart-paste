using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using SmartPaste.App.Tests.Settings;
using SmartPaste.App.UI.Onboarding;

namespace SmartPaste.App.Tests.Onboarding;

public sealed class PracticeTests
{
    private static PracticeResult ByJev(string value, string kind) => new(value, kind, nameof(DecisionMaker.Jev));

    [Fact]
    public void CopyPutsTheSampleOnTheClipboard() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new PracticeViewModel(ui.Context);
        Assert.Equal("Copy the sample, click a field, and press Ctrl+V.", vm.Guidance);

        vm.CopySampleCommand.Execute(null);

        Assert.Equal(PracticeViewModel.SampleBlock, ui.Clipboard.Text);
        Assert.True(vm.IsCopied);
        Assert.Equal("Now click a field on the right and press Ctrl+V.", vm.Guidance);
        Assert.Equal(PracticeViewModel.SampleBlock.Split('\n'), vm.SampleLines);
    });

    [Fact]
    public void ABusyClipboardIsExplained() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Clipboard.Busy = true;
        using var vm = new PracticeViewModel(ui.Context);

        vm.CopySampleCommand.Execute(null);
        Assert.False(vm.IsCopied);
        Assert.Equal("Another app is using the clipboard. Try again in a moment.", vm.CopyProblem);

        await vm.PasteAsync(vm.Fields[0]);
        Assert.Equal(PracticeFieldState.Problem, vm.Fields[0].State);
    });

    [Fact]
    public void PasteAsksThePipelineWithTheFieldLabelAndClipboardText() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Practice.Results["Email address"] = ByJev("jordan.rivera@example.com", "Email");
        using var vm = new PracticeViewModel(ui.Context);
        vm.CopySampleCommand.Execute(null);
        var email = vm.Fields.Single(f => f.Label == "Email address");

        await vm.PasteAsync(email);

        Assert.Equal([("Email address", PracticeViewModel.SampleBlock)], ui.Practice.Calls);
        Assert.Equal("jordan.rivera@example.com", email.Value);
        Assert.Equal(PracticeFieldState.Filled, email.State);
        Assert.True(email.DecidedByJev);
        Assert.Equal("Jev picked this.", email.Caption);
        Assert.Equal(1, email.FillCount);
        Assert.Equal("1 of 4 filled. Click the next field and press Ctrl+V.", vm.Guidance);
    });

    [Fact]
    public void AReshapedValueSaysSo() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Practice.Results["State name"] = ByJev("New Jersey", "State");
        using var vm = new PracticeViewModel(ui.Context);
        vm.CopySampleCommand.Execute(null);

        await vm.PasteAsync(vm.Fields.Single(f => f.Label == "State name"));

        Assert.Equal("Jev picked this and formatted it for the field.", vm.Fields[3].Caption);
    });

    [Theory]
    [InlineData("BuiltIn", false, "Built-in labels picked this. Add a key so Jev can decide.")]
    [InlineData("rules", false, "Built-in labels picked this. Add a key so Jev can decide.")]
    [InlineData("BuiltIn", true, "Built-in labels picked this, because Jev couldn't be reached.")]
    [InlineData("Learned", false, "A label this PC learned picked this. Add a key so Jev can decide.")]
    [InlineData("cache", true, "A label this PC learned picked this, because Jev couldn't be reached.")]
    public void CaptionsSayWhoDecided(string decidedBy, bool keySaved, string caption) => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        if (keySaved) ui.Keys.Set(CredentialStore.OpenRouter, "sk-or");
        ui.Practice.Results["First name"] = new PracticeResult("Jordan", "First name", decidedBy);
        using var vm = new PracticeViewModel(ui.Context);
        vm.CopySampleCommand.Execute(null);

        await vm.PasteAsync(vm.Fields[0]);

        Assert.False(vm.Fields[0].DecidedByJev);
        Assert.Equal(caption, vm.Fields[0].Caption);
    });

    [Fact]
    public void NothingToPasteIsExplained() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new PracticeViewModel(ui.Context);
        var field = vm.Fields[0];

        await vm.PasteAsync(field);
        Assert.Equal(PracticeFieldState.Problem, field.State);
        Assert.Equal("Your clipboard is empty. Copy the sample first.", field.Caption);
        Assert.Empty(ui.Practice.Calls);

        ui.Clipboard.Text = "Some unrelated text";
        await vm.PasteAsync(field);
        Assert.Equal("Nothing you copied fits this field. Copy the sample, then try again.", field.Caption);
        Assert.Equal("", field.Value);
    });

    [Fact]
    public void FillingEveryFieldCompletesThePractice() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Practice.Results["First name"] = ByJev("Jordan", "First name");
        ui.Practice.Results["Email address"] = ByJev("jordan.rivera@example.com", "Email");
        ui.Practice.Results["ZIP code"] = ByJev("07030", "ZIP / postal code");
        ui.Practice.Results["State name"] = ByJev("New Jersey", "State");
        using var vm = new PracticeViewModel(ui.Context);
        vm.CopySampleCommand.Execute(null);

        foreach (var field in vm.Fields) await vm.PasteAsync(field);

        Assert.True(vm.IsComplete);
        Assert.Equal("That's Smart Paste. Each field got just the part it asked for.", vm.Guidance);
    });

    [Fact]
    public void TypingOverASmartPasteMakesTheValueYourOwn() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Practice.Results["First name"] = ByJev("Jordan", "First name");
        using var vm = new PracticeViewModel(ui.Context);
        vm.CopySampleCommand.Execute(null);
        var field = vm.Fields[0];
        await vm.PasteAsync(field);

        field.Value = "Jordy";

        Assert.Equal(PracticeFieldState.Empty, field.State);
        Assert.Equal("", field.Caption);
    });

    [Fact]
    public void AnEngineFailureIsReportedPlainly() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new PracticeViewModel(new UI.Settings.SettingsContext(
            ui.Services with { Practice = new ThrowingEngine() }, ui.Navigator, ui.Prompts, ui.Context.Dispatcher));
        ui.Clipboard.Text = PracticeViewModel.SampleBlock;

        await vm.PasteAsync(vm.Fields[0]);

        Assert.Equal(PracticeFieldState.Problem, vm.Fields[0].State);
        Assert.Equal("Something went wrong. Try again.", vm.Fields[0].Caption);
    });

    [Fact]
    public void CtrlVInAPracticeFieldNeverDoesANormalPaste() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Practice.Results["ZIP code"] = ByJev("07030", "ZIP / postal code");
        ui.Clipboard.Text = PracticeViewModel.SampleBlock;
        var window = new OnboardingWindow(ui.Services, ui.Navigator);
        try
        {
            UiTestHost.ShowOffScreen(window);
            window.ViewModel.Step = OnboardingStep.Practice;
            await UiTestHost.Settle(450);
            var zipBox = Descendants<TextBox>(window).Single(b => b.DataContext is PracticeFieldViewModel { Label: "ZIP code" });

            // The paste command, as Ctrl+V, Shift+Insert, or the context menu would send it.
            Assert.True(ApplicationCommands.Paste.CanExecute(null, zipBox));
            ApplicationCommands.Paste.Execute(null, zipBox);
            await UiTestHost.Settle(100);

            Assert.Equal("07030", zipBox.Text);
            Assert.Equal([("ZIP code", PracticeViewModel.SampleBlock)], ui.Practice.Calls);
        }
        finally
        {
            window.Close();
        }
    });

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private sealed class ThrowingEngine : IPracticeEngine
    {
        public Task<PracticeResult?> PasteIntoAsync(string fieldLabel, string clipboardText, CancellationToken ct) =>
            throw new InvalidOperationException("engine bug");
    }
}
