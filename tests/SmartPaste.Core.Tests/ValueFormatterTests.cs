using SmartPaste.Core;

public class ValueFormatterTests
{
    private static FieldContext Field(string name, int? max = null, string? control = null) => new(name, null, null, null, control, false, [], null, max);
    [Theory]
    [InlineData("New Jersey", "State code", "NJ")]
    [InlineData("NJ", "Full state name", "New Jersey")]
    public void State(string value, string hint, string expected) => Assert.Equal(expected, ValueFormatter.Format(FieldKeys.State, value, Field(hint)));
    [Theory]
    [InlineData("Phone digits only", "2015550100")]
    [InlineData("Phone xxx-xxx-xxxx", "201-555-0100")]
    [InlineData("International phone", "+12015550100")]
    public void Phone(string hint, string expected) => Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Phone, "(201) 555-0100", Field(hint)));
    [Theory]
    [InlineData("MM/DD/YYYY", "03/04/1990")]
    [InlineData("DD/MM/YYYY", "04/03/1990")]
    [InlineData("YYYY-MM-DD", "1990-03-04")]
    public void Date(string hint, string expected) => Assert.Equal(expected, ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field(hint)));
    [Fact] public void CountryBothWays()
    {
        Assert.Equal("US", ValueFormatter.Format(FieldKeys.Country, "United States", Field("Country ISO code")));
        Assert.Equal("United States", ValueFormatter.Format(FieldKeys.Country, "USA", Field("Country")));
    }
    [Fact] public void ZipAndUnparseableDate()
    {
        Assert.Equal("07081", ValueFormatter.Format(FieldKeys.Zip, "07081-1234", Field("ZIP", 5)));
        Assert.Equal("tomorrow", ValueFormatter.Format(FieldKeys.DateOfBirth, "tomorrow", Field("MM/DD/YYYY")));
    }
}
