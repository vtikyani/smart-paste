using SmartPaste.Core;
using System.Text.Json;

public class LabelCacheTests
{
    [Fact] public void SeedIsBroadAndStandard()
    {
        var cache = new LabelCache(null);
        Assert.True(cache.Count >= 180, $"Only {cache.Count} labels");
        Assert.True(cache.TryGet(LabelNormalizer.Normalize("Legal First Name*"), out var key));
        Assert.Equal(FieldKeys.FirstName, key);
        using var stream = typeof(LabelCache).Assembly.GetManifestResourceStream("SmartPaste.Core.seed-labels.json")!;
        var seed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        Assert.All(seed.Values, value => Assert.Contains(value, FieldKeys.Standard));
    }
    [Fact] public void EmptyFactoryHasNoSeed() => Assert.Equal(0, LabelCache.Empty().Count);

    [Fact]
    public void LookupSaysWhetherTheLabelWasLearned()
    {
        var cache = new LabelCache(null);
        Assert.True(cache.TryGet("email address", out var builtIn, out var learned));
        Assert.Equal(FieldKeys.Email, builtIn);
        Assert.False(learned);

        cache.Set("Contact me at", FieldKeys.Email);
        Assert.True(cache.TryGet("contact me at", out var taught, out learned));
        Assert.Equal(FieldKeys.Email, taught);
        Assert.True(learned);
        Assert.True(cache.Forget("Contact me at"));
        Assert.False(cache.TryGet("contact me at", out _));
    }

    [Theory]
    // A built-in label: a learned copy would shadow later fixes to the built-in list.
    [InlineData("Email address", FieldKeys.Phone)]
    [InlineData("First name", FieldKeys.FirstName)]
    [InlineData("Name", FieldKeys.Company)]
    // Vague single words: the right key depends on the form.
    [InlineData("Number", FieldKeys.Phone)]
    [InlineData("Code", FieldKeys.Zip)]
    [InlineData("Value", FieldKeys.City)]
    [InlineData("Text", FieldKeys.City)]
    [InlineData("Field", FieldKeys.City)]
    [InlineData("Date", FieldKeys.DateOfBirth)]
    [InlineData("Details", FieldKeys.Street)]
    [InlineData("12", FieldKeys.Unit)]
    // Someone else's field.
    [InlineData("Spouse first name", FieldKeys.FirstName)]
    [InlineData("Guardian email", FieldKeys.Email)]
    public void LabelsThatMustNotBeLearned(string label, string key)
    {
        var cache = new LabelCache(null);
        cache.Set(label, key);
        Assert.Empty(cache.Learned);
    }

    [Theory]
    [InlineData("Card number")]
    [InlineData("Password")]
    [InlineData("Security question")]
    [InlineData("Driver's license")]
    [InlineData("Account #")]
    public void SensitiveLabelsAreNeverLearned(string label)
    {
        var cache = LabelCache.Empty();
        cache.Set(label, FieldKeys.FullName);
        Assert.Empty(cache.Learned);
    }

    [Fact]
    public void OldEntriesThatMayNoLongerBeLearnedAreIgnoredAndDropped()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["name"] = FieldKeys.Company,          // a built-in label, learned from one company form
                ["first name"] = FieldKeys.FirstName,  // a copy of the built-in list
                ["number"] = FieldKeys.Phone,          // a vague word
                ["mother s maiden name"] = FieldKeys.LastName,
                ["preferred contact"] = FieldKeys.Email
            }));
            var cache = new LabelCache(path);
            Assert.Equal([new LearnedLabel("preferred contact", FieldKeys.Email)], cache.Learned);
            Assert.True(cache.TryGet("name", out var key, out var learned) && !learned);
            Assert.Equal(FieldKeys.FullName, key);

            cache.Set("Contact me at", FieldKeys.Email);
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            Assert.Equal(["contact me at", "preferred contact"], saved.Keys.Order());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SetNeverThrowsAndPersistsOnlyLearnedLabels()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "label-cache.json");
            var cache = new LabelCache(path);
            cache.Set("Favourite zip thing", "zip");
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            Assert.Single(saved);
            Assert.True(new LabelCache(path).TryGet("favourite zip thing", out var key) && key == "zip");
            File.WriteAllText(Path.Combine(dir, "blocker"), "x");
            // The directory is a file: saving fails quietly.
            new LabelCache(Path.Combine(dir, "blocker", "label-cache.json")).Set("x", "zip");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
    [Fact] public void SetPersistsAndCorruptFileIsIgnored()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            File.WriteAllText(path, "not json");
            var cache = new LabelCache(path);
            Assert.True(cache.Count >= 180);
            cache.Set("custom label", FieldKeys.City);
            Assert.True(new LabelCache(path).TryGet("custom label", out var key));
            Assert.Equal(FieldKeys.City, key);
        } finally { File.Delete(path); }
    }
}
