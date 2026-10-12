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

    private static readonly ProfileGrounding Card = new(
        "Jordan Rivera, jordan@example.com, 201-555-0100, https://jrivera.dev/about, " +
        "https://www.linkedin.com/in/jrivera?trk=abc, Springfield, N.J. 07081, Montréal, José Martínez");

    [Theory]
    [InlineData(FieldKeys.Email, "jordan@example.co")]
    [InlineData(FieldKeys.Email, "ordan@example.com")]
    [InlineData(FieldKeys.Email, "example.com")]
    [InlineData(FieldKeys.Phone, "201-555-010")]
    [InlineData(FieldKeys.Phone, "01-555-0100")]
    [InlineData(FieldKeys.Phone, "201 555")]
    [InlineData(FieldKeys.Zip, "55501")]
    [InlineData(FieldKeys.Zip, "0708")]
    [InlineData(FieldKeys.Website, "jrivera.de")]
    [InlineData(FieldKeys.Website, "rivera.dev")]
    [InlineData(FieldKeys.Website, "example.com")]
    [InlineData(FieldKeys.Linkedin, "linkedin.com/in/jriver")]
    [InlineData(FieldKeys.City, "Montr")]
    [InlineData(FieldKeys.LastName, "Rive")]
    public void PiecesOfAValueAreNotGrounded(string key, string value) =>
        Assert.False(Card.IsGrounded(key, value), $"{key}={value}");

    [Theory]
    [InlineData(FieldKeys.Email, "Jordan@Example.com")]
    [InlineData(FieldKeys.Phone, "(201) 555-0100")]
    [InlineData(FieldKeys.Phone, "+1 201 555 0100")]
    [InlineData(FieldKeys.Zip, "07081")]
    [InlineData(FieldKeys.State, "NJ")]
    [InlineData(FieldKeys.State, "N.J.")]
    [InlineData(FieldKeys.State, "New Jersey")]
    [InlineData(FieldKeys.City, "Montreal")]
    [InlineData(FieldKeys.City, "Montréal")]
    [InlineData(FieldKeys.FirstName, "Jose")]
    [InlineData(FieldKeys.LastName, "Martínez")]
    [InlineData(FieldKeys.Website, "jrivera.dev")]
    [InlineData(FieldKeys.Website, "https://jrivera.dev/about")]
    [InlineData(FieldKeys.Linkedin, "linkedin.com/in/jrivera")]
    [InlineData(FieldKeys.Linkedin, "https://www.linkedin.com/in/jrivera")]
    public void WholeValuesAreGrounded(string key, string value) =>
        Assert.True(Card.IsGrounded(key, value), $"{key}={value}");

    [Fact]
    public void NumbersSideBySideAreEachGrounded()
    {
        var grounding = new ProfileGrounding("Phone: 201-555-0100 201-555-0199, ZIP 07081-1234");
        Assert.True(grounding.IsGrounded(FieldKeys.Phone, "201-555-0100"));
        Assert.True(grounding.IsGrounded(FieldKeys.Phone, "201.555.0199"));
        Assert.True(grounding.IsGrounded(FieldKeys.Zip, "07081-1234"));
        Assert.False(grounding.IsGrounded(FieldKeys.Phone, "0100201-5550"));
    }

    [Fact]
    public void BrokenUnicodeStillGrounds()
    {
        var grounding = new ProfileGrounding("\uD800 Jordan Rivera, jordan@example.com");
        Assert.True(grounding.IsGrounded(FieldKeys.FullName, "Jordan Rivera"));
        Assert.True(grounding.IsGrounded(FieldKeys.Email, "jordan@example.com"));
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
