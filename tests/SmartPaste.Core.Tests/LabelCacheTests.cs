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
