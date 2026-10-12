using SmartPaste.App.UI.Settings.Pages;
using SmartPaste.Core;

namespace SmartPaste.App.Tests.Settings;

// Labels here must be ones the cache may learn: not in the built-in list, not vague, not about someone else.
public sealed class LearnedLabelsViewModelTests
{
    [Fact]
    public void ListsWhatThisPcLearned() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Labels.Set("Preferred given name", FieldKeys.FirstName);
        ui.Labels.Set("Postal / ZIP", FieldKeys.Zip);
        using var vm = new LearnedLabelsViewModel(ui.Context);

        Assert.Equal(
            [new LearnedLabelRow("postal zip", "ZIP / postal code"), new LearnedLabelRow("preferred given name", "First name")],
            vm.Labels);
        Assert.Equal("2 labels", vm.CountText);
        Assert.True(vm.HasLabels);
        Assert.False(vm.IsEmpty);
    });

    [Fact]
    public void EmptyWhenNothingWasLearned() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        using var vm = new LearnedLabelsViewModel(ui.Context);

        Assert.Empty(vm.Labels);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.ForgetAllCommand.CanExecute(null));
    });

    [Fact]
    public void UpdatesWhenTheAppLearnsSomething() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        using var vm = new LearnedLabelsViewModel(ui.Context);

        // The paste pipeline learns labels on its own thread.
        await Task.Run(() => ui.Labels.Set("Daytime mobile", FieldKeys.Phone));
        await UiTestHost.Settle();

        Assert.Equal([new LearnedLabelRow("daytime mobile", "Phone")], vm.Labels);
        Assert.Equal("1 label", vm.CountText);
    });

    [Fact]
    public void ForgetRemovesOneLabel() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Labels.Set("Preferred given name", FieldKeys.FirstName);
        ui.Labels.Set("Daytime mobile", FieldKeys.Phone);
        using var vm = new LearnedLabelsViewModel(ui.Context);

        vm.ForgetCommand.Execute(vm.Labels.Single(l => l.Label == "preferred given name"));

        Assert.Equal(["daytime mobile"], ui.Labels.Learned.Select(l => l.Label));
        Assert.Equal(["daytime mobile"], vm.Labels.Select(l => l.Label));
    });

    [Fact]
    public void ForgetAllAsksFirst() => UiTestHost.Run(async () =>
    {
        using var ui = new TestUi();
        ui.Labels.Set("Preferred given name", FieldKeys.FirstName);
        ui.Labels.Set("Daytime mobile", FieldKeys.Phone);
        using var vm = new LearnedLabelsViewModel(ui.Context);

        ui.Prompts.Answer = false;
        await vm.ForgetAllCommand.ExecuteAsync(null);
        Assert.Equal(2, ui.Labels.Learned.Count);
        var question = Assert.Single(ui.Prompts.Asked);
        Assert.Equal("Forget all learned labels?", question.Title);
        Assert.Equal("Forget all", question.ConfirmText);
        Assert.Contains("2 labels", question.Message);

        ui.Prompts.Answer = true;
        await vm.ForgetAllCommand.ExecuteAsync(null);
        Assert.Empty(ui.Labels.Learned);
        Assert.True(vm.IsEmpty);
    });

    [Fact]
    public void FilterMatchesLabelOrKind() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        ui.Labels.Set("Preferred given name", FieldKeys.FirstName);
        ui.Labels.Set("Daytime mobile", FieldKeys.Phone);
        using var vm = new LearnedLabelsViewModel(ui.Context);

        vm.Filter = "PHONE";
        Assert.Equal(["daytime mobile"], vm.Labels.Select(l => l.Label));

        vm.Filter = "given";
        Assert.Equal(["preferred given name"], vm.Labels.Select(l => l.Label));

        vm.Filter = "fax";
        Assert.Empty(vm.Labels);
        Assert.True(vm.HasNoMatches);

        vm.Filter = "";
        Assert.Equal(2, vm.Labels.Count);
        Assert.False(vm.HasNoMatches);
    });

    [Fact]
    public void BuiltInLabelsAreNeverListedAsLearned() => UiTestHost.Run(() =>
    {
        using var ui = new TestUi();
        // Already in the built-in list: learning it again would only shadow future updates of that list.
        ui.Labels.Set("Mobile number", FieldKeys.Phone);
        using var vm = new LearnedLabelsViewModel(ui.Context);

        Assert.True(vm.IsEmpty);
    });
}
