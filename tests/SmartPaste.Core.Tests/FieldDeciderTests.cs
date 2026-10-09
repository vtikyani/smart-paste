using SmartPaste.Core;

public class FieldDeciderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static FieldContext Field(string? label, bool password = false) => new(label, null, null, null, "Edit", password, []);
    private static Profile Profile(params Entity[] entities) => new(entities, Now);
    private static Entity Person => new("person", new Dictionary<string,string> { [FieldKeys.FirstName] = "Jordan", [FieldKeys.LastName] = "Rivera", [FieldKeys.Email] = "v@example.com", [FieldKeys.Zip] = "07081" });
    [Fact] public async Task CacheAndRules()
    {
        var decider = new FieldDecider(new LabelCache(null), null);
        Assert.Equal("cache", (await decider.DecideAsync(Field("First Name"), Profile(Person), CancellationToken.None)).Source);
        Assert.Equal("rules", (await decider.DecideAsync(Field("given applicant name"), Profile(Person), CancellationToken.None)).Source);
    }
    [Fact] public async Task PasswordAndMissingLabelPlainPaste()
    {
        var d = new FieldDecider(new LabelCache(null), null);
        Assert.Equal(DecisionKind.PlainPaste, (await d.DecideAsync(Field("Email", true), Profile(Person), CancellationToken.None)).Kind);
        Assert.Equal(DecisionKind.PlainPaste, (await d.DecideAsync(Field(null), Profile(Person), CancellationToken.None)).Kind);
    }
    [Fact] public async Task JevConfidentCaches()
    {
        var cache = LabelCache.Empty();
        var d = new FieldDecider(cache, new Fake(new JevChoice(FieldKeys.Zip, .99, new Dictionary<string,double>{{FieldKeys.Zip,.99}}, "test")), useLocalRules:false);
        var result = await d.DecideAsync(Field("Mystery postal entry"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.Value, result.Kind);
        Assert.Equal("jev", result.Source);
        Assert.True(cache.TryGet("mystery postal entry", out var key));
        Assert.Equal(FieldKeys.Zip, key);
    }
    [Fact] public async Task LowConfidencePicksTopThree()
    {
        var choice = new JevChoice(FieldKeys.Zip, .5, new Dictionary<string,double>{{FieldKeys.Zip,.5},{FieldKeys.Email,.3},{FieldKeys.FirstName,.1},{FieldKeys.LastName,.05},{"none",.05}}, "test");
        var d = new FieldDecider(LabelCache.Empty(), new Fake(choice), useLocalRules:false);
        var result = await d.DecideAsync(Field("Unclear field"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.Picker, result.Kind);
        Assert.Equal([FieldKeys.Zip, FieldKeys.Email, FieldKeys.FirstName], result.Candidates.Select(x => x.Key).ToArray());
    }
    [Fact] public async Task JevNoneAndErrorPlainPaste()
    {
        var none = new FieldDecider(LabelCache.Empty(), new Fake(new JevChoice("none", .9, new Dictionary<string,double>{{"none",.9}}, "test")), useLocalRules:false);
        Assert.Equal(DecisionKind.PlainPaste, (await none.DecideAsync(Field("Odd field"), Profile(Person), CancellationToken.None)).Kind);
        var error = new FieldDecider(LabelCache.Empty(), new Fake(null, true), useLocalRules:false);
        Assert.Equal(DecisionKind.PlainPaste, (await error.DecideAsync(Field("Odd field"), Profile(Person), CancellationToken.None)).Kind);
    }
    [Fact] public async Task EntityWordResolvesAndAmbiguityPicks()
    {
        var profile = Profile(new Entity("billing address", new Dictionary<string,string>{{FieldKeys.Street,"1 Main St"}}), new Entity("shipping address", new Dictionary<string,string>{{FieldKeys.Street,"2 Main St"}}));
        var d = new FieldDecider(new LabelCache(null), null);
        Assert.Equal("1 Main St", (await d.DecideAsync(Field("Billing address"), profile, CancellationToken.None)).Value);
        Assert.Equal(DecisionKind.Picker, (await d.DecideAsync(Field("Street address"), profile, CancellationToken.None)).Kind);
    }
    [Fact] public async Task AbsentProfileKeyPlainPastes()
    {
        var d = new FieldDecider(new LabelCache(null), null);
        Assert.Equal(DecisionKind.PlainPaste, (await d.DecideAsync(Field("Date of birth"), Profile(Person), CancellationToken.None)).Kind);
    }
    private sealed class Fake(JevChoice? answer, bool fail = false) : IJevChooser
    {
        public Task<JevChoice> ChooseAsync(string state, string instructions, IReadOnlyDictionary<string,string> criteria, CancellationToken ct)
            => fail ? throw new JevException("test failure") : Task.FromResult(answer!);
    }
}

public class ReviewFixTests
{
    private static Profile Sample() => new([new Entity("person", new Dictionary<string, string> {
        ["first_name"] = "Jordan", ["zip"] = "07081", ["state"] = "NJ" })], DateTimeOffset.UtcNow);

    private sealed class ThrowingJev : IJevChooser
    {
        public int Calls;
        public Task<JevChoice> ChooseAsync(string state, string instructions, IReadOnlyDictionary<string, string> criteria, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("must not be called"); }
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Card number")]
    [InlineData("Security code (CVV)")]
    [InlineData("SSN")]
    public async Task SensitiveLabelsPlainPasteWithoutCallingJev(string label)
    {
        var jev = new ThrowingJev();
        var field = new FieldContext(label, null, null, null, "Edit", false, []);
        var d = await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(field, Sample(), CancellationToken.None);
        Assert.Equal(DecisionKind.PlainPaste, d.Kind);
        Assert.Equal("sensitive label", d.Reason);
        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public void CacheSetNeverThrowsAndPersistsOnlyLearnedLabels()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sp-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "label-cache.json");
        var cache = new LabelCache(path);
        cache.Set("Favourite zip thing", "zip");
        var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        Assert.Single(saved);
        Assert.True(new LabelCache(path).TryGet("favourite zip thing", out var key) && key == "zip");
        File.WriteAllText(Path.Combine(dir, "blocker"), "x");
        new LabelCache(Path.Combine(dir, "blocker", "label-cache.json")).Set("x", "zip"); // directory is a file: must not throw
    }

    [Fact]
    public void StateComboBoxWithoutMaxLengthGetsFullName()
    {
        var field = new FieldContext("State", null, null, null, "ComboBox", false, []);
        Assert.Equal("New Jersey", ValueFormatter.Format("state", "NJ", field));
    }
}
