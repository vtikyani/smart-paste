using SmartPaste.Core;

public class ProfileParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Live = "```json\n{\n  \"entities\": [\n    {\n      \"label\": \"person\",\n      \"fields\": {\n        \"first_name\": \"Jordan\",\n        \"last_name\": \"Rivera\",\n        \"street\": \"123 Main St\",\n        \"unit\": \"Apt 4B\",\n        \"city\": \"Springfield\",\n        \"state\": \"NJ\",\n        \"zip\": \"07081\",\n        \"email\": \"v@example.com\",\n        \"phone\": \"201-555-0100\"\n      }\n    }\n  ]\n}\n```";
    [Fact] public void ParsesCapturedFenceAndDerivesName()
    {
        var p = ProfileParser.Parse(Live, Now);
        Assert.Equal("Jordan Rivera", p.ValuesFor(FieldKeys.FullName).Single().Value);
        Assert.True(p.IsMultiField);
        Assert.True(p.IsFresh(Now.AddMinutes(14)));
        Assert.False(p.IsFresh(Now.AddMinutes(15)));
    }
    [Fact] public void ParsesProseWrapperNumbersAndNulls()
    {
        var p = ProfileParser.Parse("Here is the result: {\"entities\":[{\"label\":\"person\",\"fields\":{\"age\":36,\"nothing\":null,\"bad\":[],\"full_name\":\"Ada Lovelace\"}}]} Thanks", Now);
        Assert.Equal("36", p.ValuesFor("age").Single().Value);
        Assert.Empty(p.ValuesFor("nothing"));
        Assert.Empty(p.ValuesFor("bad"));
        Assert.Equal("Ada", p.ValuesFor(FieldKeys.FirstName).Single().Value);
        Assert.Equal("Lovelace", p.ValuesFor(FieldKeys.LastName).Single().Value);
    }
    [Theory]
    [InlineData("garbage")]
    [InlineData("{\"entities\":{}}")]
    [InlineData("{\"entities\":[{\"label\":\"person\"}]}")]
    public void InvalidThrows(string input) => Assert.Throws<FormatException>(() => ProfileParser.Parse(input, Now));
    [Fact] public void MergeOnlyFillsAbsentKeys()
    {
        var p = ProfileParser.Parse("{\"entities\":[{\"label\":\"person\",\"fields\":{\"email\":\"a@b.com\"}},{\"label\":\"work\",\"fields\":{\"zip\":\"12345\"}}]}", Now);
        var merged = ProfileParser.Merge(p, new FastFields("other@b.com", "201-555-0100", "99999", null, null));
        Assert.Equal("a@b.com", merged.ValuesFor(FieldKeys.Email).Single().Value);
        Assert.Equal("12345", merged.ValuesFor(FieldKeys.Zip).Single().Value);
        Assert.Equal("201-555-0100", merged.ValuesFor(FieldKeys.Phone).Single().Value);
    }

    [Fact]
    public void ParsesStructuredOutputPairs()
    {
        const string reply = "{\"entities\":[{\"label\":\"person\",\"fields\":[{\"key\":\"First Name\",\"value\":\" Ada \"},{\"key\":\"last_name\",\"value\":\"Lovelace\"},{\"key\":\"first_name\",\"value\":\"Duplicate\"},{\"key\":\"empty\",\"value\":\"\"}]}]}";
        var p = ProfileParser.Parse(reply, Now);
        Assert.Equal("Ada", p.ValuesFor(FieldKeys.FirstName).Single().Value);
        Assert.Equal("Ada Lovelace", p.ValuesFor(FieldKeys.FullName).Single().Value);
        Assert.Empty(p.ValuesFor("empty"));
    }

    [Fact]
    public void GroundedParseDropsInventedValuesButKeepsDerivedNames()
    {
        const string reply = "{\"entities\":[{\"label\":\"person\",\"fields\":[{\"key\":\"first_name\",\"value\":\"Jordan\"},{\"key\":\"last_name\",\"value\":\"Rivera\"},{\"key\":\"phone\",\"value\":\"201-555-0199\"},{\"key\":\"company\",\"value\":\"Acme\"}]}]}";
        var p = ProfileParser.ParseGrounded(reply, "Rivera, Jordan - 201-555-0100", Now);
        Assert.Equal("Jordan Rivera", p.ValuesFor(FieldKeys.FullName).Single().Value);
        Assert.Empty(p.ValuesFor(FieldKeys.Phone));
        Assert.Empty(p.ValuesFor(FieldKeys.Company));
    }

    [Fact]
    public void MergeFillsGapsFromTheLocalProfile()
    {
        var llm = ProfileParser.Parse("{\"entities\":[{\"label\":\"person\",\"fields\":{\"email\":\"a@b.com\",\"city\":\"Springfield\"}},{\"label\":\"shipping address\",\"fields\":{\"street\":\"2 Oak Ave\"}}]}", Now);
        var local = new Profile(
        [
            new Entity("person", new Dictionary<string, string> { [FieldKeys.Email] = "other@b.com", [FieldKeys.Phone] = "201-555-0100", [FieldKeys.FullName] = "Jordan Rivera" }),
            new Entity("billing address", new Dictionary<string, string> { [FieldKeys.Unit] = "Apt 4B", [FieldKeys.City] = "Newark" })
        ], Now.AddSeconds(-1));

        var merged = ProfileParser.Merge(llm, local);
        Assert.Equal(Now, merged.CreatedAt);
        Assert.Equal("a@b.com", merged.ValuesFor(FieldKeys.Email).Single().Value);
        Assert.Equal("Springfield", merged.ValuesFor(FieldKeys.City).Single().Value);
        var person = merged.Entities.Single(e => e.Label == "person");
        Assert.Equal("201-555-0100", person.Fields[FieldKeys.Phone]);
        Assert.Equal("Jordan", person.Fields[FieldKeys.FirstName]);
        Assert.Equal("Apt 4B", merged.Entities.Single(e => e.Label == "billing address").Fields[FieldKeys.Unit]);
    }
}
