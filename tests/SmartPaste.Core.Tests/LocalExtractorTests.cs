using SmartPaste.Core;

public class LocalExtractorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private static string? Value(Profile profile, string key, string? entity = null) =>
        profile.ValuesFor(key).Where(v => entity is null || v.Entity.Label == entity).Select(v => v.Value).SingleOrDefault();

    [Fact]
    public void SampleLine()
    {
        var profile = LocalExtractor.Extract("Jordan Rivera, 123 Main St Apt 4B, Springfield NJ 07081, v@example.com, 201-555-0100", Now);

        Assert.Equal("Jordan Rivera", Value(profile, FieldKeys.FullName));
        Assert.Equal("Jordan", Value(profile, FieldKeys.FirstName));
        Assert.Equal("Rivera", Value(profile, FieldKeys.LastName));
        Assert.Equal("123 Main St", Value(profile, FieldKeys.Street));
        Assert.Equal("Apt 4B", Value(profile, FieldKeys.Unit));
        Assert.Equal("Springfield", Value(profile, FieldKeys.City));
        Assert.Equal("NJ", Value(profile, FieldKeys.State));
        Assert.Equal("07081", Value(profile, FieldKeys.Zip));
        Assert.Equal("v@example.com", Value(profile, FieldKeys.Email));
        Assert.Equal("201-555-0100", Value(profile, FieldKeys.Phone));
        Assert.Equal(Now, profile.CreatedAt);
        Assert.Single(profile.Entities);
    }

    [Fact]
    public void MultiLineAddressBlock()
    {
        const string text = "Jordan Rivera\n123 Main St\nApt 4B\nSpringfield, NJ 07081\njordan@example.com\n(201) 555-0100\nhttps://linkedin.com/in/jrivera";
        var profile = LocalExtractor.Extract(text, Now);

        Assert.Equal("Jordan Rivera", Value(profile, FieldKeys.FullName));
        Assert.Equal("123 Main St", Value(profile, FieldKeys.Street));
        Assert.Equal("Apt 4B", Value(profile, FieldKeys.Unit));
        Assert.Equal("Springfield", Value(profile, FieldKeys.City));
        Assert.Equal("(201) 555-0100", Value(profile, FieldKeys.Phone));
        Assert.Equal("https://linkedin.com/in/jrivera", Value(profile, FieldKeys.Linkedin));
        Assert.Null(Value(profile, FieldKeys.Website));
    }

    [Fact]
    public void CanadianAddress()
    {
        var profile = LocalExtractor.Extract("Sam Tremblay\n24 Sussex Dr\nOttawa, ON K1A 0B1\nsam@example.ca", Now);
        Assert.Equal("24 Sussex Dr", Value(profile, FieldKeys.Street));
        Assert.Equal("Ottawa", Value(profile, FieldKeys.City));
        Assert.Equal("ON", Value(profile, FieldKeys.State));
        Assert.Equal("K1A 0B1", Value(profile, FieldKeys.Zip));
    }

    [Fact]
    public void LabeledLinesWithBillingAndShipping()
    {
        const string text = """
            Name: Jordan Rivera
            Email: jordan@example.com
            Mobile: 201-555-0100
            Job title: Engineer
            Company: Acme Corp
            Billing Address: 123 Main St Apt 4B, Springfield, NJ 07081
            Shipping street: 2 Oak Ave
            Shipping city: Portland
            Card number: 4111 1111 1111 1111
            Password: hunter2
            Emergency contact phone: 201-555-0199
            Favourite colour: teal
            """;
        var profile = LocalExtractor.Extract(text, Now);

        Assert.Equal("Jordan Rivera", Value(profile, FieldKeys.FullName, "person"));
        Assert.Equal("201-555-0100", Value(profile, FieldKeys.Phone));
        Assert.Equal("Engineer", Value(profile, FieldKeys.Title));
        Assert.Equal("Acme Corp", Value(profile, FieldKeys.Company));
        Assert.Equal("123 Main St", Value(profile, FieldKeys.Street, "billing address"));
        Assert.Equal("Apt 4B", Value(profile, FieldKeys.Unit, "billing address"));
        Assert.Equal("Springfield", Value(profile, FieldKeys.City, "billing address"));
        Assert.Equal("07081", Value(profile, FieldKeys.Zip, "billing address"));
        Assert.Equal("2 Oak Ave", Value(profile, FieldKeys.Street, "shipping address"));
        Assert.Equal("Portland", Value(profile, FieldKeys.City, "shipping address"));
        // Sensitive and someone else's details are never taken; unknown labels are skipped.
        Assert.DoesNotContain(profile.Entities.SelectMany(e => e.Fields.Values), v => v.Contains("4111") || v.Contains("hunter2") || v.Contains("0199") || v == "teal");
    }

    [Theory]
    [InlineData("Contact Information\nv@example.com\n201-555-0100")]
    [InlineData("Acme Corp, 123 Main St, Springfield NJ 07081")]
    [InlineData("JORDAN RIVERA, v@example.com, 201-555-0100")]
    [InlineData("Shipping Address\n2 Oak Ave\nPortland, OR 97201")]
    [InlineData("Jordan Rivera Lee Ann Smith, v@example.com")]
    public void NameGuessIsConservative(string text) =>
        Assert.Null(Value(LocalExtractor.Extract(text, Now), FieldKeys.FullName));

    [Fact]
    public void SomeoneElsesLinesAreNeverReadAsTheUsers()
    {
        const string text = """
            Jordan Rivera
            jordan@example.com
            Emergency contact: Sam Lee, 201-555-0199
            Reference address: 9 Elm St, Trenton, NJ 08608
            Password: Tr0ub4dor 5 Main St
            """;
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal("Jordan Rivera", Value(profile, FieldKeys.FullName));
        Assert.Null(Value(profile, FieldKeys.Phone));
        Assert.Null(Value(profile, FieldKeys.Street));
        Assert.Null(Value(profile, FieldKeys.City));
        Assert.Null(Value(profile, FieldKeys.Zip));
    }

    [Fact]
    public void NameWithApostropheAndInitial() =>
        Assert.Equal("Siobhan J. O'Brien", Value(LocalExtractor.Extract("Siobhan J. O'Brien\nv@example.com\n201-555-0100", Now), FieldKeys.FullName));

    [Fact]
    public void EveryValueComesFromTheText()
    {
        const string text = "Jordan Rivera, 123 Main St Apt 4B, Springfield NJ 07081, v@example.com, 201-555-0100";
        var profile = LocalExtractor.Extract(text, Now);
        foreach (var value in profile.Entities.SelectMany(e => e.Fields.Values))
            Assert.Contains(value, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Just a sentence with nothing in it.")]
    public void NothingFoundIsAnEmptyProfile(string text) => Assert.Empty(LocalExtractor.Extract(text, Now).Entities);

    [Fact]
    public void OversizeTextIsNotRead() =>
        Assert.Empty(LocalExtractor.Extract(new string('x', TextHeuristics.MaxChars) + " v@example.com", Now).Entities);
}
