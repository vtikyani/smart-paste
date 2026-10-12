using SmartPaste.Core;

public class ProfileGroundingTests
{
    private const string Source = """
        Rivera, Jordan
        123 Main St., Apt 4B
        Springfield, New Jersey 07081-1234
        Phone: (201) 555-0100
        Email: V@Example.com
        Web: https://www.jrivera.dev/
        Born March 4, 1990 in the USA
        Ottawa office: K1A 0B1
        """;

    private static readonly ProfileGrounding Grounding = new(Source);

    [Theory]
    [InlineData(FieldKeys.FullName, "Jordan Rivera")]
    [InlineData(FieldKeys.FirstName, "Jordan")]
    [InlineData(FieldKeys.Street, "123 Main St")]
    [InlineData(FieldKeys.Unit, "Apt 4B")]
    [InlineData(FieldKeys.City, "springfield")]
    [InlineData(FieldKeys.State, "New Jersey")]
    [InlineData(FieldKeys.State, "NJ")]
    [InlineData(FieldKeys.Zip, "07081")]
    [InlineData(FieldKeys.Zip, "070811234")]
    [InlineData(FieldKeys.Zip, "K1A0B1")]
    [InlineData(FieldKeys.Phone, "201-555-0100")]
    [InlineData(FieldKeys.Phone, "+1 201 555 0100")]
    [InlineData(FieldKeys.Email, "v@example.com")]
    [InlineData(FieldKeys.Website, "jrivera.dev")]
    [InlineData(FieldKeys.Website, "http://jrivera.dev")]
    [InlineData(FieldKeys.DateOfBirth, "1990-03-04")]
    [InlineData(FieldKeys.Country, "US")]
    [InlineData(FieldKeys.Country, "United States")]
    [InlineData("office", "Ottawa office")]
    public void ValuesFromTheTextAreGrounded(string key, string value) => Assert.True(Grounding.IsGrounded(key, value), $"{key}={value}");

    [Theory]
    [InlineData(FieldKeys.FullName, "Jordan Smith")]
    [InlineData(FieldKeys.Street, "125 Main St")]
    [InlineData(FieldKeys.City, "Newark")]
    [InlineData(FieldKeys.State, "NY")]
    [InlineData(FieldKeys.State, "IN")]
    [InlineData(FieldKeys.Zip, "07082")]
    [InlineData(FieldKeys.Phone, "201-555-0199")]
    [InlineData(FieldKeys.Email, "jordan@example.com")]
    [InlineData(FieldKeys.Website, "https://jordan.dev")]
    [InlineData(FieldKeys.DateOfBirth, "1990-04-03")]
    [InlineData(FieldKeys.Country, "Canada")]
    [InlineData(FieldKeys.Company, "Acme")]
    [InlineData(FieldKeys.Title, "")]
    public void InventedValuesAreNot(string key, string value) =>
        Assert.False(Grounding.IsGrounded(key, value), $"{key}={value}");

    [Fact]
    public void WordsMustMatchWhole()
    {
        var grounding = new ProfileGrounding("Maine Street Partners");
        Assert.False(grounding.IsGrounded(FieldKeys.City, "Main"));
        Assert.True(grounding.IsGrounded(FieldKeys.Company, "maine street partners"));
    }

    [Fact]
    public void GroundKeepsOnlyGroundedFields()
    {
        var entity = new Entity("person", new Dictionary<string, string>
        {
            [FieldKeys.FirstName] = "Jordan", [FieldKeys.LastName] = "Rivera", [FieldKeys.Title] = "Chief Pilot"
        });
        var grounded = Grounding.Ground(entity);
        Assert.Equal(["first_name", "last_name"], grounded.Fields.Keys.Order());
        Assert.Equal("person", grounded.Label);
    }
}
