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

        cache.Set("email address", FieldKeys.Phone);
        Assert.True(cache.TryGet("Email address", out var overridden, out learned));
        Assert.Equal(FieldKeys.Phone, overridden);
        Assert.True(learned);
        Assert.True(cache.Forget("email address"));
        Assert.True(cache.TryGet("email address", out var restored, out learned) && !learned && restored == FieldKeys.Email);
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
