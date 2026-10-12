using SmartPaste.Core;

public class AddressPatternsTests
{
    private static AddressPatterns.Address Read(string text) => AddressPatterns.Read(LabelLines.SplitLines(text));

    // Town names that start like a unit word ("Fl", "Ste", "Apt") are towns, not units.
    [Theory]
    [InlineData("123 Main St, Flushing, NY 11354", "Flushing", "NY", "11354")]
    [InlineData("123 Main St, Stevens Point, WI 54481", "Stevens Point", "WI", "54481")]
    [InlineData("123 Main St, Floral Park, NY 11001", "Floral Park", "NY", "11001")]
    [InlineData("123 Main St, Sterling, VA 20164", "Sterling", "VA", "20164")]
    [InlineData("123 Main St, Aptos, CA 95003", "Aptos", "CA", "95003")]
    [InlineData("123 Main St, Flagstaff, AZ 86001", "Flagstaff", "AZ", "86001")]
    [InlineData("123 Main St, Ste. Genevieve, MO 63670", "Ste. Genevieve", "MO", "63670")]
    public void TownsAreNotUnits(string text, string city, string state, string zip)
    {
        var address = Read(text);
        Assert.Equal("123 Main St", address.Street!.Street);
        Assert.Null(address.Unit);
        Assert.Equal(city, address.Place!.City);
        Assert.Equal(state, address.Place.State);
        Assert.Equal(zip, address.Place.Zip);
    }

    [Fact]
    public void AUkTownLineIsNotAUnit() => Assert.Null(Read("10 High St\nStevenage\nSG1 1AA").Unit);

    [Theory]
    [InlineData("123 Main St, Floor 3, Springfield, NJ 07081", "Floor 3")]
    [InlineData("123 Main St Apt 4B", "Apt 4B")]
    [InlineData("123 Main St, Apt. 4-B", "Apt. 4-B")]
    [InlineData("123 Main St Suite 200", "Suite 200")]
    [InlineData("123 Main St #12", "#12")]
    [InlineData("123 Main St Unit A", "Unit A")]
    [InlineData("500 Howard St Ste. PH", "Ste. PH")]
    [InlineData("1 Infinite Loop Bldg 2", "Bldg 2")]
    [InlineData("Apt 4B 123 Main St", "Apt 4B")]
    [InlineData("Suite 200, 500 Howard St", "Suite 200")]
    [InlineData("123 Main St\nApartment 12", "Apartment 12")]
    public void UnitsHaveANumber(string text, string unit) => Assert.Equal(unit, Read(text).Unit);

    [Theory]
    [InlineData("123 Main St Floor Plan")]
    [InlineData("123 Main St Suite Life")]
    [InlineData("123 Main St, Apartments available")]
    public void UnitWordsWithoutANumberAreNotUnits(string text) => Assert.Null(Read(text).Unit);

    [Theory]
    [InlineData("123 1/2 Main St", "123 1/2 Main St")]
    [InlineData("Apt 4B 123 Main St", "123 Main St")]
    [InlineData("4 Embarcadero Center San Francisco CA 94111", "4 Embarcadero Center")]
    [InlineData("12 St. Marks Pl", "12 St. Marks Pl")]
    [InlineData("123 St Johns Wood Rd", "123 St Johns Wood Rd")]
    [InlineData("1600 Pennsylvania Ave NW, Washington, DC 20500", "1600 Pennsylvania Ave NW")]
    [InlineData("221B Baker St", "221B Baker St")]
    [InlineData("350 5th Ave", "350 5th Ave")]
    [InlineData("123 Main St Crescent City CA 95531", "123 Main St")]
    [InlineData("PO Box 12", "PO Box 12")]
    [InlineData("P.O. Box 4321", "P.O. Box 4321")]
    public void Streets(string text, string street) => Assert.Equal(street, Read(text).Street!.Street);

    [Theory]
    [InlineData("Class of 2015 St. Mary's")]
    [InlineData("12345 is on its way")]
    [InlineData("Order 2015 St")]
    [InlineData("Ships in 3 Business Days")]
    public void ProseIsNotAStreet(string text) => Assert.Null(Read(text).Street);

    [Theory]
    [InlineData("4 Embarcadero Center San Francisco CA 94111", "San Francisco")]
    [InlineData("123 Main St Crescent City CA 95531", "Crescent City")]
    [InlineData("123 Main St Rockville Centre NY 11570", "Rockville Centre")]
    public void TheCityStartsAfterTheStreet(string text, string city) => Assert.Equal(city, Read(text).Place!.City);

    [Theory]
    [InlineData("Rose Cottage, Main Road", true)]
    [InlineData("Flat 2, Rose Court", true)]
    [InlineData("Same as billing", false)]
    [InlineData("N/A", false)]
    public void StreetLikeValues(string value, bool expected) => Assert.Equal(expected, AddressPatterns.IsStreetLike(value));
}
