using SmartPaste.App.Services;
using SmartPaste.App.UI.Popups;
using SmartPaste.Core;

// The picker's keyboard and pointer rules, driven the way the keyboard hook drives them, and its list logic.
public sealed class PickerTests
{
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkEscape = 0x1B;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;
    private const int VkBack = 0x08;

    // Near the top-left corner of the primary screen, where the picker opens below it.
    private static readonly ScreenRect Anchor = new(40, 40, 220, 26);

    private static PickRequest AllValues() => new(
        DocSample.Profile(DateTimeOffset.Now).Entities.SelectMany(e => e.Fields.Select(f => new Candidate(f.Key, e.Label, f.Value))).ToArray(),
        PickMode.AllValues, Anchor, null);

    private static Task<PickResult> Pick(PickRequest request, Func<ManualKeyCapture, PasteFeedback, Task>? act = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var keys = new ManualKeyCapture();
        return WpfHost.RunAsync(async () =>
        {
            using var feedback = new PasteFeedback(keys, timeout);
            feedback.Prepare();
            var pick = feedback.PickAsync(request, ct);
            if (act is not null) await act(keys, feedback);
            var result = await pick;
            Assert.Null(keys.Sink); // the keyboard is always handed back
            return result;
        });
    }

    private static Task Press(ManualKeyCapture keys, int vk, char? c = null, KeyModifiers mods = KeyModifiers.None, bool accepted = true)
    {
        Assert.Equal(accepted, keys.Press(vk, c, mods));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task NumberKeyChoosesThatRow()
    {
        var result = await Pick(DocSample.NamePick(Anchor), (keys, feedback) =>
        {
            Assert.False(feedback.Picker.IsActive);
            return Press(keys, 0x32, '2');
        });
        Assert.Equal(PickOutcome.Chosen, result.Outcome);
        Assert.Equal("Jordan", result.Candidate!.Value);
    }

    [Fact]
    public async Task ArrowsAndEnterChoose()
    {
        var result = await Pick(DocSample.NamePick(Anchor), async (keys, _) =>
        {
            await Press(keys, VkDown);
            await Press(keys, VkDown);
            await Press(keys, VkDown); // stays on the last row
            await Press(keys, VkUp);
            await Press(keys, VkReturn);
        });
        Assert.Equal("Jordan", result.Candidate!.Value);
    }

    [Fact]
    public async Task NumpadDigitsChooseToo()
    {
        var result = await Pick(DocSample.NamePick(Anchor), (keys, _) => Press(keys, 0x63));
        Assert.Equal("Rivera", result.Candidate!.Value);
    }

    [Fact]
    public async Task EscapePastesTheOriginal()
    {
        var result = await Pick(DocSample.NamePick(Anchor), (keys, _) => Press(keys, VkEscape));
        Assert.Equal(PickOutcome.PasteOriginal, result.Outcome);
        Assert.Null(result.Candidate);
    }

    [Fact]
    public async Task AnyOtherKeyGoesToTheAppAndDismisses()
    {
        var result = await Pick(DocSample.NamePick(Anchor), (keys, _) => Press(keys, VkTab, accepted: false));
        Assert.Equal(PickOutcome.Dismissed, result.Outcome);
    }

    [Fact]
    public async Task ShortcutsGoToTheAppAndDismiss()
    {
        var result = await Pick(AllValues(), (keys, _) => Press(keys, 0x43, 'c', KeyModifiers.Control, accepted: false));
        Assert.Equal(PickOutcome.Dismissed, result.Outcome);
    }

    [Fact]
    public async Task ShiftAloneKeepsThePickerOpen()
    {
        var result = await Pick(DocSample.NamePick(Anchor), async (keys, _) =>
        {
            await Press(keys, VkShift, accepted: false);
            await Press(keys, 0x31, '1');
        });
        Assert.Equal("Jordan Rivera", result.Candidate!.Value);
    }

    [Fact]
    public async Task NumberWithoutARowIsIgnored()
    {
        var result = await Pick(DocSample.NamePick(Anchor), async (keys, _) =>
        {
            await Press(keys, 0x37, '7');
            await Press(keys, VkEscape);
        });
        Assert.Equal(PickOutcome.PasteOriginal, result.Outcome);
    }

    [Fact]
    public async Task TypingFiltersAllValuesAndNumbersFollowTheFilter()
    {
        var result = await Pick(AllValues(), async (keys, feedback) =>
        {
            await Press(keys, 0x53, 's');
            await Press(keys, 0x50, 'p');
            await Press(keys, 0x52, 'r');
            await Press(keys, 0x58, 'x');
            Assert.True(feedback.Picker.ViewModel.HasNoMatch);
            await Press(keys, VkBack);
            Assert.Equal("Springfield", Assert.Single(feedback.Picker.ViewModel.Rows).Value);
            await Press(keys, 0x31, '1');
        });
        Assert.Equal("Springfield", result.Candidate!.Value);
    }

    [Fact]
    public async Task TypingIntoAShortListGoesToTheApp()
    {
        var result = await Pick(DocSample.NamePick(Anchor), (keys, _) => Press(keys, 0x58, 'x', accepted: false));
        Assert.Equal(PickOutcome.Dismissed, result.Outcome);
    }

    [Fact]
    public async Task ClickingElsewhereDismissesButClickingThePickerDoesNot()
    {
        var result = await Pick(DocSample.NamePick(Anchor), (keys, feedback) =>
        {
            var bounds = feedback.Picker.SurfaceBounds();
            keys.Sink!.OnPointerDown(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            Assert.NotNull(keys.Sink);
            keys.Sink!.OnPointerDown(bounds.Right + 400, bounds.Bottom + 400);
            return Task.CompletedTask;
        });
        Assert.Equal(PickOutcome.Dismissed, result.Outcome);
    }

    [Fact]
    public async Task CancellationDismisses()
    {
        using var cancel = new CancellationTokenSource();
        var result = await Pick(DocSample.NamePick(Anchor), (_, _) =>
        {
            cancel.Cancel();
            return Task.CompletedTask;
        }, ct: cancel.Token);
        Assert.Equal(PickOutcome.Dismissed, result.Outcome);
    }

    [Fact]
    public async Task UnansweredPickerTimesOut()
    {
        var result = await Pick(DocSample.NamePick(Anchor), timeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(PickOutcome.Dismissed, result.Outcome);
    }

    [Fact]
    public async Task ANewRequestReplacesTheOpenOne()
    {
        var keys = new ManualKeyCapture();
        var (first, second) = await WpfHost.RunAsync(async () =>
        {
            using var feedback = new PasteFeedback(keys);
            var firstPick = feedback.PickAsync(DocSample.NamePick(Anchor), CancellationToken.None);
            var secondPick = feedback.PickAsync(AllValues(), CancellationToken.None);
            var firstResult = await firstPick;
            keys.Press(VkEscape);
            return (firstResult, await secondPick);
        });
        Assert.Equal(PickOutcome.Dismissed, first.Outcome);
        Assert.Equal(PickOutcome.PasteOriginal, second.Outcome);
    }

    [Fact]
    public async Task NothingToChooseIsDismissedAtOnce()
    {
        var result = await Pick(new PickRequest([], PickMode.AllValues, Anchor, null));
        Assert.Equal(PickOutcome.Dismissed, result.Outcome);
    }

    [Fact]
    public void ShortListsKeepNineRowsAndNameTheField()
    {
        var model = new PickerViewModel();
        var many = Enumerable.Range(1, 12).Select(i => new Candidate(FieldKeys.Street, "person", $"{i} Main St")).ToArray();
        model.Load(new PickRequest(many, PickMode.Ambiguous, Anchor, "Street address"));
        Assert.Equal(9, model.Rows.Count);
        Assert.Equal("Which one for “Street address”?", model.Title);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9"], model.Rows.Select(r => r.Number));
        Assert.True(model.Rows[0].IsSelected);
    }

    [Fact]
    public void AllValuesNumbersOnlyTheFirstNine()
    {
        var model = new PickerViewModel();
        model.Load(AllValues());
        Assert.Equal(10, model.Rows.Count);
        Assert.Equal("Paste a value", model.Title);
        Assert.True(model.Rows.Take(9).All(r => r.HasNumber));
        Assert.False(model.Rows[9].HasNumber);
        Assert.Null(model.RowForNumber(10));
    }

    [Fact]
    public void EntityChipsAppearOnlyWhenTheyTellRowsApart()
    {
        var model = new PickerViewModel();
        model.Load(DocSample.NamePick(Anchor));
        Assert.All(model.Rows, r => Assert.Null(r.Entity));

        model.Load(new PickRequest(
        [
            new Candidate(FieldKeys.Street, "billing address", "1 Bill St"),
            new Candidate(FieldKeys.Street, "shipping address", "2 Ship Rd")
        ], PickMode.Ambiguous, Anchor, "Street"));
        Assert.Equal(["Billing address", "Shipping address"], model.Rows.Select(r => r.Entity));
    }
}
