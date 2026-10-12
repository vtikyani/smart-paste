using SmartPaste.Core;

public class ValueFormatterTests
{
    private static FieldContext Field(string? name, int? max = null, string? control = null, string? placeholder = null, string? help = null,
        string? labeledBy = null) =>
        new(name, help, null, labeledBy, control, false, [], null, max) { Placeholder = placeholder };

    // ---- Phone ----

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
    // Another country's number is never forced into a US pattern.
    [InlineData("+45 12 34 56 78", "Phone", "(555) 555-5555", "+45 12 34 56 78")]
    [InlineData("+64 9 123 4567", "Phone", "(555) 555-5555", "+64 9 123 4567")]
    [InlineData("+64 9 123 4567", "Phone xxx-xxx-xxxx", null, "+64 9 123 4567")]
    [InlineData("138 1234 5678", "Phone", "(555) 555-5555", "138 1234 5678")]
    // The international form keeps the number's own country code.
    [InlineData("+45 12 34 56 78", "International phone", null, "+4512345678")]
    [InlineData("+64 9 123 4567", "Phone (E.164)", null, "+6491234567")]
    // A number with no country code cannot be made international.
    [InlineData("138 1234 5678", "International phone", null, "138 1234 5678")]
    [InlineData("+45 12 34 56 78", "Phone digits only", null, "4512345678")]
    // US and Canada numbers still take the pattern, written any common way.
    [InlineData("1-201-555-0100", "Phone", "(555) 555-5555", "(201) 555-0100")]
    [InlineData("2015550100", "Phone", "555-555-5555", "201-555-0100")]
    [InlineData("(201)555-0100", "International phone", null, "+12015550100")]
    public void PhonesFromOtherCountriesKeepTheirShape(string value, string label, string? placeholder, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Phone, value, Field(label, placeholder: placeholder)));

    [Theory]
    [InlineData("Phone digits only", null, "2015550100")]
    [InlineData("Phone", "(555) 555-5555", "(201) 555-0100")]
    [InlineData("International phone", null, "+12015550100")]
    // With no pattern to follow, the number stays as written, extension and all.
    [InlineData("Phone", null, "201-555-0100 ext. 12")]
    public void ExtensionIsLeftOutOfReshapedNumbers(string label, string? placeholder, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Phone, "201-555-0100 ext. 12", Field(label, placeholder: placeholder)));

    [Fact]
    public void OtherExtensionSpellings()
    {
        Assert.Equal("2015550100", ValueFormatter.Format(FieldKeys.Phone, "(201) 555-0100 x12", Field("Phone digits only")));
        Assert.Equal("2015550100", ValueFormatter.Format(FieldKeys.Phone, "201.555.0100, extension 7", Field("Phone digits only")));
        Assert.Equal("2015550100", ValueFormatter.Format(FieldKeys.Phone, "201-555-0100 #34", Field("Phone digits only")));
    }

    [Fact]
    public void PhoneNeverLongerThanTheField()
    {
        // The mask asks for 17 characters; the field holds 14, so the number as written (12) is used.
        Assert.Equal("201-555-0100", ValueFormatter.Format(FieldKeys.Phone, "201-555-0100", Field("Phone", 14, placeholder: "+1 (###) ###-####")));
        Assert.Equal("(201) 555-0100", ValueFormatter.Format(FieldKeys.Phone, "+1 201 555 0100", Field("Phone", 14, placeholder: "+1 (###) ###-####")));
        // Another country's number is shortened without losing its country code.
        Assert.Equal("+442079460958", ValueFormatter.Format(FieldKeys.Phone, "+44 20 7946 0958", Field("Phone", 13)));
        Assert.Equal("442079460958", ValueFormatter.Format(FieldKeys.Phone, "+44 20 7946 0958", Field("Phone", 12)));
    }

    // ---- Dates ----

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
    [InlineData("DD-MMM-YYYY", "04-Mar-1990")]
    public void DatePatterns(string hint, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field(hint)));

    [Theory]
    [InlineData("Fecha de nacimiento (dd/mm/aaaa)", "04/03/1990")]
    [InlineData("Date de naissance (jj/mm/aaaa)", "04/03/1990")]
    [InlineData("Geburtsdatum (TT.MM.JJJJ)", "04.03.1990")]
    [InlineData("Data di nascita (gg/mm/aaaa)", "04/03/1990")]
    [InlineData("Födelsedatum (ÅÅÅÅ-MM-DD)", "1990-03-04")]
    [InlineData("Data urodzenia (dd.mm.rrrr)", "04.03.1990")]
    [InlineData("Fecha (dd/mm/aa)", "04/03/90")]
    // Two letters nothing names ("TT" and "JJ" could each be the day or the year): left as written.
    [InlineData("Geburtsdatum (TT.MM.JJ)", "March 4, 1990")]
    public void DatePatternsInOtherLanguagesKeepTheYear(string hint, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field(hint)));

    [Theory]
    [InlineData("1990-03-04")]
    [InlineData("4 March 1990")]
    [InlineData("Mar 4, 1990")]
    [InlineData("4.3.1990")]
    public void DateInputsAreRead(string value) =>
        Assert.Equal("1990-03-04", ValueFormatter.Format(FieldKeys.DateOfBirth, value, Field("Birth date", placeholder: "YYYY-MM-DD")));

    [Theory]
    // Both parts 12 or less: 3 April or 4 March? Never reordered; pasted as written.
    [InlineData("04/03/1990", "DD/MM/YYYY", "04/03/1990")]
    [InlineData("04/03/1990", "MM/DD/YYYY", "04/03/1990")]
    [InlineData("04/03/1990", "YYYY-MM-DD", "04/03/1990")]
    [InlineData("4-3-1990", "YYYY-MM-DD", "4-3-1990")]
    // A part over 12 can only be the day, so the order is known.
    [InlineData("13/04/1990", "MM/DD/YYYY", "04/13/1990")]
    [InlineData("04/13/1990", "DD/MM/YYYY", "13/04/1990")]
    [InlineData("13-04-1990", "YYYY-MM-DD", "1990-04-13")]
    // The same number twice reads the same either way.
    [InlineData("05/05/1990", "YYYY-MM-DD", "1990-05-05")]
    public void AmbiguousDatesAreNeverReordered(string value, string hint, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.DateOfBirth, value, Field("Date of birth", placeholder: hint)));

    [Fact]
    public void TwoDigitBirthYearsAreInThePast()
    {
        Assert.Equal("03/14/1945", ValueFormatter.Format(FieldKeys.DateOfBirth, "3/14/45", Field("Date of birth", placeholder: "MM/DD/YYYY")));
        Assert.Equal("1945", ValueFormatter.Format(FieldKeys.DateOfBirth, "3/4/45", Field("Year")));
        Assert.Equal("1945-03-04", ValueFormatter.Format(FieldKeys.DateOfBirth, "4.3.45", Field("Date of birth", placeholder: "YYYY-MM-DD")));
        // Ambiguous, so as written: never "2045".
        Assert.Equal("3/4/45", ValueFormatter.Format(FieldKeys.DateOfBirth, "3/4/45", Field("Date of birth", placeholder: "YYYY-MM-DD")));
        Assert.Equal("1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "3/4/90", Field("Year")));
    }

    [Theory]
    [InlineData("Year", null, null, null, "1990")]
    [InlineData("Birth year", null, null, null, "1990")]
    [InlineData("Year of birth", null, null, null, "1990")]
    [InlineData(null, "YYYY", null, null, "1990")]
    [InlineData("Year", "YY", null, null, "90")]
    [InlineData("Year", null, 2, null, "90")]
    [InlineData("Month", "MM", null, null, "03")]
    [InlineData("Birth month", null, null, null, "03")]
    [InlineData("Month", null, null, "ComboBox", "March")]
    [InlineData("Month", null, 2, "ComboBox", "03")]
    [InlineData("Day", "DD", null, null, "04")]
    [InlineData("Day of birth", null, null, null, "04")]
    [InlineData("Day", null, null, "ComboBox", "4")]
    public void BoxesForOnePartGetOnlyThatPart(string? name, string? placeholder, int? max, string? control, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field(name, max, control, placeholder)));

    [Fact]
    public void ABoxsOwnNameWinsOverALabelSharedWithTheOtherBoxes()
    {
        var year = Field("Year", placeholder: "YYYY", labeledBy: "Date of birth (MM/DD/YYYY)");
        Assert.Equal("1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", year));
        // With no part of its own, the box follows the shared label's pattern.
        Assert.Equal("03/04/1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field(null, labeledBy: "Date of birth (MM/DD/YYYY)")));
        // "Birth day" is the whole birthday, not the day.
        Assert.Equal("March 4, 1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field("Birth day")));
    }

    [Fact]
    public void DayAndMonthOfAnAmbiguousDateAreNotGuessed()
    {
        Assert.Equal("1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "04/03/1990", Field("Year")));
        Assert.Equal("04/03/1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "04/03/1990", Field("Month", placeholder: "MM")));
        Assert.Equal("04/03/1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "04/03/1990", Field("Day", placeholder: "DD")));
        Assert.Equal("13", ValueFormatter.Format(FieldKeys.DateOfBirth, "13/04/1990", Field("Day", placeholder: "DD")));
    }

    [Fact]
    public void DateFromPlaceholderAndUnparseableDate()
    {
        Assert.Equal("03/04/1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "1990-03-04", Field("Date of birth", placeholder: "MM/DD/YYYY")));
        Assert.Equal("tomorrow", ValueFormatter.Format(FieldKeys.DateOfBirth, "tomorrow", Field("MM/DD/YYYY")));
        Assert.Equal("March 4, 1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "March 4, 1990", Field("Date of birth")));
        Assert.Equal("02/30/1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "02/30/1990", Field("Date of birth", placeholder: "YYYY-MM-DD")));
        Assert.Equal("14/13/1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "14/13/1990", Field("Date of birth", placeholder: "YYYY-MM-DD")));
    }

    [Fact]
    public void DateNeverLongerThanTheField()
    {
        // "March 4, 1990" would not fit in 10 characters; the date as written does.
        Assert.Equal("1990-03-04", ValueFormatter.Format(FieldKeys.DateOfBirth, "1990-03-04", Field("Date of birth", 10, placeholder: "Month DD, YYYY")));
        // A year box with an example in its placeholder is still a year box.
        Assert.Equal("1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "1990-03-04", Field("Year", placeholder: "e.g. 1985")));
    }

    // ---- ZIP and postal codes ----

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
    [InlineData("ZIP+4", 4, "1234")]
    [InlineData("+4", 4, "1234")]
    [InlineData("Plus 4", 4, "1234")]
    [InlineData("+4", null, "1234")]
    [InlineData("Plus four", null, "1234")]
    [InlineData("ZIP code", 4, "1234")]
    public void SecondBoxOfASplitZipGetsTheExtension(string label, int? max, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Zip, "07081-1234", Field(label, max)));

    [Fact]
    public void ZipNeverLongerThanTheField()
    {
        // A UK postcode reads the same without its space.
        Assert.Equal("SW1A1AA", ValueFormatter.Format(FieldKeys.Zip, "SW1A 1AA", Field("Postcode", 7)));
        // Nothing shorter is equivalent: cutting "07081" to "0708" would be wrong, so it is left as it is.
        Assert.Equal("07081", ValueFormatter.Format(FieldKeys.Zip, "07081", Field("+4", 4)));
        Assert.Equal("K1A 0B1", ValueFormatter.Format(FieldKeys.Zip, "K1A 0B1", Field("Postal code", 5)));
    }

    // ---- States and countries ----

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
    // A drop-down that holds 5 characters gets the code, not a name it would cut.
    [InlineData("NJ", "State", 5, "ComboBox", null, "NJ")]
    [InlineData("New Jersey", "State", 5, "ComboBox", null, "NJ")]
    [InlineData("New Jersey", "Full state name", 8, null, null, "NJ")]
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
    // The name does not fit: the value as written, then the code.
    [InlineData("USA", "Country", 3, null, "USA")]
    [InlineData("United States", "Country", 10, "ComboBox", "US")]
    public void Country(string value, string label, int? max, string? control, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(FieldKeys.Country, value, Field(label, max, control)));

    // ---- Everything else ----

    [Fact]
    public void OtherKeysAndOddInputsPassThrough()
    {
        Assert.Equal("Jordan", ValueFormatter.Format(FieldKeys.FirstName, "Jordan", Field("First name", 2)));
        Assert.Equal("", ValueFormatter.Format(FieldKeys.Phone, "", Field("Phone")));
        Assert.Equal("ext. 12", ValueFormatter.Format(FieldKeys.Phone, "ext. 12", Field("Phone", 4)));
        Assert.Equal("1990", ValueFormatter.Format(FieldKeys.DateOfBirth, "1990", Field("\\MM/DD/YYYY\\")));
    }

    [Theory]
    [InlineData("99/99/9999")]
    [InlineData("0/0/00")]
    [InlineData("12/31/9999")]
    [InlineData("1/1/0001")]
    [InlineData("+")]
    [InlineData("((((((((((")]
    [InlineData("x 1 x 2 x 3 x 4 x 5 x 6 x 7 x 8")]
    [InlineData("\ud800 broken surrogate")]
    public void NeverThrows(string value)
    {
        string[] hints = ["MM/DD/YYYY", "Year", "Month", "Day", "ZIP+4", "(555) 555-5555", "International phone", "dd/mm/aaaa", "a/b/c"];
        foreach (var key in FieldKeys.Standard)
            foreach (var hint in hints)
                foreach (var max in new int?[] { null, 1, 2, 4, 10 })
                    Assert.NotNull(ValueFormatter.Format(key, value, Field(hint, max, "ComboBox", hint)));
    }
}
