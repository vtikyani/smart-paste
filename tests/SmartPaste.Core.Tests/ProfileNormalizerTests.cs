using SmartPaste.Core;

public class ProfileNormalizerTests
{
    private static IReadOnlyDictionary<string, string> Normalize(params (string Key, string Value)[] fields) =>
        ProfileNormalizer.Normalize(new Entity("person", fields.ToDictionary(f => f.Key, f => f.Value))).Fields;

    [Theory]
    [InlineData("Rivera, Jordan", "Jordan", null, "Rivera")]
    [InlineData("Rivera, Jordan A.", "Jordan", "A.", "Rivera")]
    [InlineData("van der Berg, Anna", "Anna", null, "van der Berg")]
    [InlineData("Le, Minh", "Minh", null, "Le")]
    [InlineData("Dr. Jane Smith", "Jane", null, "Smith")]
    [InlineData("Mx Sam Lee", "Sam", null, "Lee")]
    [InlineData("Martin Luther King Jr.", "Martin", "Luther", "King")]
    [InlineData("Martin Luther King, Jr.", "Martin", "Luther", "King")]
    [InlineData("Jane Smith, PhD", "Jane", null, "Smith")]
    [InlineData("John Smith III", "John", null, "Smith")]
    [InlineData("Anna van der Berg", "Anna", null, "van der Berg")]
    [InlineData("Juan Carlos de la Cruz", "Juan", "Carlos", "de la Cruz")]
    [InlineData("Ludwig van Beethoven", "Ludwig", null, "van Beethoven")]
    [InlineData("Jordan Le", "Jordan", null, "Le")]
    [InlineData("Mary St. John", "Mary", null, "St. John")]
    [InlineData("Jordan Rivera (they/them)", "Jordan", null, "Rivera")]
    [InlineData("Robert \"Bob\" Smith", "Robert", null, "Smith")]
    [InlineData("Siobhan J. O'Brien", "Siobhan", "J.", "O'Brien")]
    [InlineData("José Martínez", "José", null, "Martínez")]
    [InlineData("JORDAN RIVERA", "JORDAN", null, "RIVERA")]
    public void FullNamesSplitIntoTheirParts(string fullName, string first, string? middle, string last)
    {
        var fields = Normalize((FieldKeys.FullName, fullName));
        Assert.Equal(first, fields[FieldKeys.FirstName]);
        Assert.Equal(middle, fields.GetValueOrDefault(FieldKeys.MiddleName));
        Assert.Equal(last, fields[FieldKeys.LastName]);
    }

    [Theory]
    [InlineData("Jordan")]
    [InlineData("Dr. Smith")]
    [InlineData("Jordan Rivera, Springfield")]
    [InlineData("Rivera, Jordan, Springfield")]
    [InlineData("Jordan Rivera Lee Ann Smith")]
    [InlineData("Jordan R1vera")]
    [InlineData("jordan@example.com")]
    [InlineData("(they/them)")]
    public void UnclearNamesDeriveNothing(string fullName)
    {
        var fields = Normalize((FieldKeys.FullName, fullName));
        Assert.False(fields.ContainsKey(FieldKeys.FirstName), fullName);
        Assert.False(fields.ContainsKey(FieldKeys.LastName), fullName);
    }

    [Fact]
    public void ANoteInBracketsLeavesTheFullName() =>
        Assert.Equal("Jordan Rivera", Normalize((FieldKeys.FullName, "Jordan Rivera (they/them)"))[FieldKeys.FullName]);

    [Fact]
    public void GivenPartsAreKept()
    {
        var fields = Normalize((FieldKeys.FullName, "Jordan Rivera"), (FieldKeys.FirstName, "Jordy"));
        Assert.Equal("Jordy", fields[FieldKeys.FirstName]);
        Assert.False(fields.ContainsKey(FieldKeys.LastName));

        var built = Normalize((FieldKeys.FirstName, "Jordan"), (FieldKeys.MiddleName, "Lee"), (FieldKeys.LastName, "Rivera"));
        Assert.Equal("Jordan Lee Rivera", built[FieldKeys.FullName]);
    }

    [Theory]
    [InlineData("First Name", "first_name")]
    [InlineData("first-name", "first_name")]
    [InlineData("  Date of  Birth ", "date_of_birth")]
    public void KeysAreSnakeCase(string key, string expected) => Assert.Equal(expected, ProfileNormalizer.NormalizeKey(key));
}
