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

    [Theory]
    [InlineData("123 Main St, Flushing, NY 11354", "Flushing", "NY", "11354")]
    [InlineData("123 Main St, Stevens Point, WI 54481", "Stevens Point", "WI", "54481")]
    [InlineData("123 Main St, Floral Park, NY 11001", "Floral Park", "NY", "11001")]
    [InlineData("123 Main St, Sterling, VA 20164", "Sterling", "VA", "20164")]
    [InlineData("123 Main St, Aptos, CA 95003", "Aptos", "CA", "95003")]
    [InlineData("123 Main St, Flagstaff, AZ 86001", "Flagstaff", "AZ", "86001")]
    public void TownNamesAreNotUnits(string text, string city, string state, string zip)
    {
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal("123 Main St", Value(profile, FieldKeys.Street));
        Assert.Null(Value(profile, FieldKeys.Unit));
        Assert.Equal(city, Value(profile, FieldKeys.City));
        Assert.Equal(state, Value(profile, FieldKeys.State));
        Assert.Equal(zip, Value(profile, FieldKeys.Zip));
    }

    [Fact]
    public void AFloorIsOneUnit()
    {
        var profile = LocalExtractor.Extract("123 Main St, Floor 3, Springfield, NJ 07081", Now);
        Assert.Equal("Floor 3", Value(profile, FieldKeys.Unit));
        Assert.Equal("Springfield", Value(profile, FieldKeys.City));
    }

    [Fact]
    public void AUkTownLineIsNotAUnit()
    {
        var profile = LocalExtractor.Extract("10 High St\nStevenage\nSG1 1AA", Now);
        Assert.Equal("10 High St", Value(profile, FieldKeys.Street));
        Assert.Null(Value(profile, FieldKeys.Unit));
    }

    [Theory]
    [InlineData("Order #112-3456789-0123456")]
    [InlineData("Account #: 1234567890")]
    [InlineData("Order 12345")]
    [InlineData("2026-10-12 order 1234567890 failed")]
    public void OrderAndAccountNumbersAreNothing(string text) => Assert.Empty(LocalExtractor.Extract(text, Now).Entities);

    [Theory]
    [InlineData("12345 Main St\nSpringfield 07081")]
    [InlineData("12345 Main St / Springfield 07081")]
    public void TheHouseNumberIsNotTheZip(string text)
    {
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal("12345 Main St", Value(profile, FieldKeys.Street));
        Assert.Equal("Springfield", Value(profile, FieldKeys.City));
        Assert.Equal("07081", Value(profile, FieldKeys.Zip));
    }

    [Fact]
    public void ALinksNumberIsNotAZip()
    {
        var profile = LocalExtractor.Extract("linkedin.com/in/jane-doe-48213", Now);
        Assert.Null(Value(profile, FieldKeys.Zip));
        Assert.Equal("linkedin.com/in/jane-doe-48213", Value(profile, FieldKeys.Linkedin));
    }

    [Theory]
    [InlineData("Name: Rivera, Jordan", "Jordan", null, "Rivera")]
    [InlineData("Name: Dr. Jane Smith", "Jane", null, "Smith")]
    [InlineData("Dr. Jane Smith\nv@example.com", "Jane", null, "Smith")]
    [InlineData("Name: Martin Luther King Jr.", "Martin", "Luther", "King")]
    [InlineData("Martin Luther King, Jr.\nmlk@example.com", "Martin", "Luther", "King")]
    [InlineData("Name: Anna van der Berg", "Anna", null, "van der Berg")]
    [InlineData("Anna van der Berg\nanna@example.nl", "Anna", null, "van der Berg")]
    [InlineData("Name: Jordan Rivera (they/them)", "Jordan", null, "Rivera")]
    [InlineData("Jordan Rivera (they/them)\njordan@example.com", "Jordan", null, "Rivera")]
    public void NamesSplitIntoTheirParts(string text, string first, string? middle, string last)
    {
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal(first, Value(profile, FieldKeys.FirstName));
        Assert.Equal(middle, Value(profile, FieldKeys.MiddleName));
        Assert.Equal(last, Value(profile, FieldKeys.LastName));
    }

    [Fact]
    public void ANoteInBracketsIsNotPartOfTheFullName() =>
        Assert.Equal("Jordan Rivera", Value(LocalExtractor.Extract("Name: Jordan Rivera (they/them)", Now), FieldKeys.FullName));

    [Theory]
    [InlineData("Ship To\n2 Oak Ave\nPortland, OR 97201")]
    [InlineData("Bill To:\n123 Main St\nSpringfield, NJ 07081")]
    [InlineData("Senior Engineer\nv@example.com\n201-555-0100")]
    [InlineData("Software Engineer, Acme Corp\nv@example.com")]
    [InlineData("Contact\nv@example.com\n201-555-0100")]
    [InlineData("Resume\nv@example.com\n201-555-0100")]
    [InlineData("Contact Us\nv@example.com")]
    [InlineData("Product Manager\nv@example.com")]
    public void HeadingsAndJobTitlesAreNotNames(string text)
    {
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Empty(profile.ValuesFor(FieldKeys.FullName));
        Assert.Empty(profile.ValuesFor(FieldKeys.FirstName));
    }

    [Theory]
    [InlineData("Contact Information\nJordan Rivera\nv@example.com")]
    [InlineData("Resume\nJordan Rivera\nv@example.com")]
    [InlineData("Best regards,\nJordan Rivera\nSenior Engineer | Acme Corp\nM: +1 201 555 0100")]
    public void TheNameUnderAHeadingIsFound(string text) =>
        Assert.Equal("Jordan Rivera", Value(LocalExtractor.Extract(text, Now), FieldKeys.FullName));

    [Theory]
    [InlineData("+1 415 555 0123\n500 Howard St", "+1 415 555 0123", "500 Howard St")]
    [InlineData("+44 20 7946 0958\n221B Baker St", "+44 20 7946 0958", "221B Baker St")]
    public void APhoneNumberEndsAtTheLineBreak(string text, string phone, string street)
    {
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal(phone, Value(profile, FieldKeys.Phone));
        Assert.Equal(street, Value(profile, FieldKeys.Street));
    }

    [Theory]
    [InlineData("Shipping Address\nJordan Rivera\n2 Oak Ave\nPortland, OR 97201\n\nBilling Address\nJordan Rivera\n123 Main St Apt 4B\nSpringfield, NJ 07081")]
    [InlineData("SHIPPING ADDRESS:\nJordan Rivera\n2 Oak Ave\nPortland, OR 97201\nBILLING ADDRESS:\nJordan Rivera\n123 Main St Apt 4B\nSpringfield, NJ 07081")]
    [InlineData("Ship to: Jordan Rivera, 2 Oak Ave, Portland, OR 97201\nBill to: Jordan Rivera, 123 Main St Apt 4B, Springfield, NJ 07081")]
    public void ShippingAndBillingBlocksKeepBothAddresses(string text)
    {
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal("2 Oak Ave", Value(profile, FieldKeys.Street, "shipping address"));
        Assert.Equal("Portland", Value(profile, FieldKeys.City, "shipping address"));
        Assert.Equal("OR", Value(profile, FieldKeys.State, "shipping address"));
        Assert.Equal("97201", Value(profile, FieldKeys.Zip, "shipping address"));
        Assert.Equal("123 Main St", Value(profile, FieldKeys.Street, "billing address"));
        Assert.Equal("Apt 4B", Value(profile, FieldKeys.Unit, "billing address"));
        Assert.Equal("Springfield", Value(profile, FieldKeys.City, "billing address"));
        Assert.Equal("07081", Value(profile, FieldKeys.Zip, "billing address"));
        Assert.Equal("Jordan Rivera", Value(profile, FieldKeys.FullName, "shipping address"));
        Assert.Equal("Jordan Rivera", Value(profile, FieldKeys.FullName, "billing address"));
    }

    [Fact]
    public void ThePersonsOwnDetailsStayWithThePerson()
    {
        const string text = """
            Jordan Rivera
            jordan@example.com

            Shipping Address
            2 Oak Ave
            Portland, OR 97201

            Phone: 201-555-0100
            """;
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal("Jordan Rivera", Value(profile, FieldKeys.FullName, "person"));
        Assert.Equal("jordan@example.com", Value(profile, FieldKeys.Email, "person"));
        Assert.Equal("201-555-0100", Value(profile, FieldKeys.Phone, "person"));
        Assert.Equal("2 Oak Ave", Value(profile, FieldKeys.Street, "shipping address"));
        Assert.Null(Value(profile, FieldKeys.Street, "person"));
    }

    [Fact]
    public void AnInlineAddressLabelTakesTheLinesUnderIt()
    {
        var profile = LocalExtractor.Extract("Billing Address: 123 Main St\nSpringfield, NJ 07081\nEmail: jordan@example.com", Now);
        Assert.Equal("123 Main St", Value(profile, FieldKeys.Street, "billing address"));
        Assert.Equal("Springfield", Value(profile, FieldKeys.City, "billing address"));
        Assert.Equal("jordan@example.com", Value(profile, FieldKeys.Email, "person"));
    }

    [Fact]
    public void LabeledLinesUnderAHeadingBelongToItsAddress()
    {
        var profile = LocalExtractor.Extract("Shipping Address\nStreet: 2 Oak Ave\nCity: Portland\nZIP: 97201", Now);
        Assert.Equal("2 Oak Ave", Value(profile, FieldKeys.Street, "shipping address"));
        Assert.Equal("Portland", Value(profile, FieldKeys.City, "shipping address"));
        Assert.Equal("97201", Value(profile, FieldKeys.Zip, "shipping address"));
    }

    [Fact]
    public void ALabeledAddressThatIsNotOneIsSkipped() =>
        Assert.Empty(LocalExtractor.Extract("Shipping address: Same as billing", Now).Entities);

    [Theory]
    [InlineData("123 1/2 Main St, Springfield, NJ 07081", "123 1/2 Main St", null)]
    [InlineData("Apt 4B 123 Main St, Springfield, NJ 07081", "123 Main St", "Apt 4B")]
    [InlineData("4 Embarcadero Center San Francisco CA 94111", "4 Embarcadero Center", null)]
    public void StreetShapes(string text, string street, string? unit)
    {
        var profile = LocalExtractor.Extract(text, Now);
        Assert.Equal(street, Value(profile, FieldKeys.Street));
        Assert.Equal(unit, Value(profile, FieldKeys.Unit));
    }

    [Fact]
    public void TheCityAfterACenterIsOnlyTheCity() =>
        Assert.Equal("San Francisco", Value(LocalExtractor.Extract("4 Embarcadero Center San Francisco CA 94111", Now), FieldKeys.City));

    [Fact]
    public void AClassYearIsNotAStreet() =>
        Assert.Null(Value(LocalExtractor.Extract("Class of 2015 St. Mary's\njordan@example.com", Now), FieldKeys.Street));

    [Fact]
    public void ALinkInAngleBracketsLeavesThemOut() =>
        Assert.Equal("https://janedoe.dev", Value(LocalExtractor.Extract("Jane Doe <https://janedoe.dev>", Now), FieldKeys.Website));

    [Fact]
    public void SecretNumbersNeverBecomeValues()
    {
        const string text = "Jordan Rivera\njordan@example.com\nPaid with 4111 1111 1111 1111\nSSN 123-45-6789\nNote: IBAN DE89 3704 0044 0532 0130 00";
        var values = LocalExtractor.Extract(text, Now).Entities.SelectMany(e => e.Fields.Values).ToArray();
        Assert.DoesNotContain(values, v => v.Contains("4111") || v.Contains("6789") || v.Contains("DE89") || v.Contains("3704"));
        Assert.Contains("jordan@example.com", values);
    }

    [Fact]
    public void AdversarialInputReturns()
    {
        string[] texts =
        [
            new string('1', TextHeuristics.MaxChars),
            string.Concat(Enumerable.Repeat("1 ", TextHeuristics.MaxChars / 2)),
            string.Concat(Enumerable.Repeat("A ", TextHeuristics.MaxChars / 2)),
            string.Concat(Enumerable.Repeat("123 Main ", TextHeuristics.MaxChars / 9)),
            string.Concat(Enumerable.Repeat("Apt 1 ", TextHeuristics.MaxChars / 6)),
            string.Concat(Enumerable.Repeat("Name: Jordan\n", TextHeuristics.MaxChars / 13)),
            string.Concat(Enumerable.Repeat("Shipping Address\n", TextHeuristics.MaxChars / 17)),
            string.Concat(Enumerable.Repeat("Springfield NJ ", TextHeuristics.MaxChars / 15)),
            string.Concat(Enumerable.Repeat("+1 (", TextHeuristics.MaxChars / 4)),
            string.Concat(Enumerable.Repeat("a@b.", TextHeuristics.MaxChars / 4)),
            string.Concat(Enumerable.Repeat("4111 ", TextHeuristics.MaxChars / 5)),
            "Label: a" + new string(' ', TextHeuristics.MaxChars - 10) + "b"
        ];
        foreach (var text in texts)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var profile = LocalExtractor.Extract(text, Now);
            Assert.NotNull(profile);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{clock.Elapsed} for {text[..20]}");
        }
    }
}
