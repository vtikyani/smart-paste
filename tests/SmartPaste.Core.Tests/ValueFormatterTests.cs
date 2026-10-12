using SmartPaste.Core;

public class ValueFormatterTests
{
    private static FieldContext Field(string? name, int? max = null, string? control = null, string? placeholder = null, string? help = null) =>
        new(name, help, null, null, control, false, [], null, max) { Placeholder = placeholder };

    [Theory]
    [InlineData("Phone digits only", null, null, "2015550100")]
    [InlineData("Phone xxx-xxx-xxxx", null, null, "201-555-0100")]
    [InlineData("International phone", null, null, "+12015550100")]
    [InlineData("Phone", "(555) 555-5555", null, "(201) 555-0100")]
    [InlineData("Phone", "555-555-5555", null, "201-555-0100")]
    [InlineData("Phone", "555.555.5555", null, "201.555.0100")]
    [InlineData("Phone", "+1 555 555 5555", null, "+1 201 555 0100")]
    [InlineData("Phone", "+1 (###) ###-####", null, "+1 (201) 555-0100")]
    [InlineData("Phone", null, "Numbers only, no dashes", "2015550100")]
    public void PhonePatterns(string label, string? placeholder, string? help, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Phone, "+1 (201) 555-0100", Field(label, placeholder: placeholder, help: help)));

    [Theory]
    [InlineData(10, "2015550100")]
    [InlineData(11, "2015550100")]
    [InlineData(12, "201-555-0100")]
    [InlineData(14, "(201) 555-0100")]
    [InlineData(20, "+1 (201) 555-0100")]
    public void PhoneMaxLength(int max, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Phone, "+1 (201) 555-0100", Field("Phone", max)));

    [Fact]
    public void PhoneLeftAloneWithoutHints()
    {
        Assert.Equal("201-555-0100", ValueFormatter.Format(FieldKeys.Phone, "201-555-0100", Field("Phone", 14)));
        Assert.Equal("+44 20 7946 0958", ValueFormatter.Format(FieldKeys.Phone, "+44 20 7946 0958", Field("Phone", placeholder: "(555) 555-5555")));
    }

    [Theory]
    [InlineData("MM/DD/YYYY", "03/04/1990")]
    [InlineData("DD/MM/YYYY", "04/03/1990")]
    [InlineData("YYYY-MM-DD", "1990-03-04")]
    [InlineData("dd.mm.yyyy", "04.03.1990")]
    [InlineData("MM/DD/YY", "03/04/90")]
    [InlineData("MM/YY", "03/90")]
    [InlineData("M/D/YYYY", "3/4/1990")]
    [InlineData("Date of birth (mm dd yyyy)", "03/04/1990")]
    [InlineData("Month DD, YYYY", "March 4, 1990")]
    [InlineData("DD Month YYYY", "4 March 1990")]
    public void DatePatterns(string hint, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field(hint)));

    [Theory]
    [InlineData("03/04/1990")]
    [InlineData("1990-03-04")]
    [InlineData("4 March 1990")]
    [InlineData("Mar 4, 1990")]
    public void DateInputsAreRead(string value) =>
        Assert.Equal("1990-03-04", ValueFormatter.Format(FieldKeys.DateOfBirth, value, Field("Birth date", placeholder: "YYYY-MM-DD")));

    [Fact]
    public void DateFromPlaceholderAndUnparseableDate()
    {
        Assert.Equal("03/04/1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "1990-03-04", Field("Date of birth", placeholder: "MM/DD/YYYY")));
        Assert.Equal("tomorrow", ValueFormatter.Format(FieldKeys.DateOfBirth, "tomorrow", Field("MM/DD/YYYY")));
        Assert.Equal("March 4, 1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field("Date of birth")));
    }

    [Theory]
    [InlineData("07081-1234", "ZIP", 5, "07081")]
    [InlineData("07081-1234", "ZIP (5 digits)", null, "07081")]
    [InlineData("07081-1234", "ZIP", 9, "070811234")]
    [InlineData("07081-1234", "ZIP", 10, "07081-1234")]
    [InlineData("070811234", "ZIP+4", null, "07081-1234")]
    [InlineData("07081-1234", "ZIP", 7, "07081")]
    [InlineData("07081", "ZIP+4", 10, "07081")]
    [InlineData("k1a0b1", "Postal code", null, "K1A 0B1")]
    [InlineData("K1A 0B1", "Postal code", 6, "K1A0B1")]
    public void Zip(string value, string label, int? max, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Zip, value, Field(label, max)));

    [Theory]
    [InlineData("New Jersey", "State code", null, null, null, "NJ")]
    [InlineData("NJ", "Full state name", null, null, null, "New Jersey")]
    [InlineData("New Jersey", "State", 2, null, null, "NJ")]
    [InlineData("NJ", "State", null, "ComboBox", null, "New Jersey")]
    [InlineData("NJ", "State", 2, "ComboBox", null, "NJ")]
    [InlineData("New Jersey", "State", null, null, "e.g. CA", "NJ")]
    [InlineData("NJ", "State", null, null, "California", "New Jersey")]
    [InlineData("NJ", "State", null, null, null, "NJ")]
    [InlineData("N.J.", "State abbreviation", null, null, null, "NJ")]
    [InlineData("Ontario", "Province code", null, null, null, "ON")]
    public void State(string value, string label, int? max, string? control, string? placeholder, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.State, value, Field(label, max, control, placeholder)));

    [Theory]
    [InlineData("United States", "Country ISO code", null, null, "US")]
    [InlineData("USA", "Country", null, null, "United States")]
    [InlineData("U.S.A.", "Country", null, null, "United States")]
    [InlineData("Canada", "Country", 2, null, "CA")]
    [InlineData("GB", "Country", null, "ComboBox", "United Kingdom")]
    [InlineData("United Kingdom", "Country", null, null, "United Kingdom")]
    [InlineData("Narnia", "Country code", null, null, "Narnia")]
    public void Country(string value, string label, int? max, string? control, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Country, value, Field(label, max, control)));

    [Fact]
    public void OtherKeysAndOddInputsPassThrough()
    {
        Assert.Equal("Jordan", ValueFormatter.Format(FieldKeys.FirstName, "Jordan", Field("First name", 2)));
        Assert.Equal("", ValueFormatter.Format(FieldKeys.Phone, "", Field("Phone")));
        Assert.Equal("ext. 12", ValueFormatter.Format(FieldKeys.Phone, "ext. 12", Field("Phone", 4)));
        Assert.Equal("1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "1990", Field("\\MM/DD/YYYY\\")));
    }
}
