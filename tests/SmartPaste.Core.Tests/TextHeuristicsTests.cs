using System.Diagnostics;
using SmartPaste.Core;

public class TextHeuristicsTests
{
    private const string Sample = "Jordan Rivera, 123 Main St Apt 4B, Springfield NJ 07081, v@example.com, 201-555-0100";

    [Fact] public void SampleIsMultiField() => Assert.True(TextHeuristics.LooksMultiField(Sample));
    [Fact] public void OnePhoneIsNot() => Assert.False(TextHeuristics.LooksMultiField("Call me at 201-555-0100"));
    [Fact] public void DateInProseIsNot() => Assert.False(TextHeuristics.LooksMultiField(string.Join(' ', Enumerable.Repeat("Some ordinary prose", 65)) + " on 3/4/1990"));
    [Fact] public void OversizeIsNot() => Assert.False(TextHeuristics.LooksMultiField(new string('x', 25_000) + Sample));
    [Fact] public void ZipIsNotTakenFromPhone() => Assert.Null(TextHeuristics.Extract("201-555-0100").Zip);
    [Fact] public void CanadianPostalWorks() => Assert.Equal("K1A 0B1", TextHeuristics.Extract("Ottawa K1A 0B1").Zip);

    [Theory]
    [InlineData("Jordan Rivera\nSenior Engineer\njordan@example.com | (201) 555-0100 | linkedin.com/in/jrivera")]
    [InlineData("Jordan Rivera\nSenior Engineer, Acme Corp\nM: +1 201 555 0100\njordan@acme.com\nwww.acme.com")]
    [InlineData("Name: Jordan Rivera\nCompany: Acme Corp")]
    [InlineData("First name: Jordan\nLast name: Rivera")]
    [InlineData("First name: Jordan | Email: jordan@example.com")]
    [InlineData("Jordan Rivera\n123 Main St\nSpringfield, NJ 07081")]
    [InlineData("12345 Main St\nSpringfield 07081")]
    [InlineData("Address: 123 Main St, Springfield, NJ 07081")]
    [InlineData("Phone: 2015550100\nEmail: v@example.com")]
    [InlineData("Mobile 2015550100\njordan@example.com")]
    [InlineData("Jane Doe\n+44 20 7946 0958\njane@example.co.uk")]
    [InlineData("Sam Tremblay\n24 Sussex Dr\nOttawa, ON K1A 0B1")]
    [InlineData("City: Portland\nState: OR")]
    public void ContactBlocksAre(string text) => Assert.True(TextHeuristics.LooksMultiField(text), text);

    // Each of these used to be uploaded to the parsing model whenever a key was saved.
    [Theory]
    [InlineData("2026-10-12 order 1234567890 failed")]
    [InlineData("See https://example.com/spec before 10/12/2026")]
    [InlineData("Wire to account 4111111111 on 2026-10-01")]
    [InlineData("From: boss@example.com\nSent: 10/12/2026\nPlease keep this confidential")]
    [InlineData("From: boss@example.com / Sent: 10/12/2026 / Please keep this confidential")]
    [InlineData("Title: Q3 plan\nCompany: Contoso")]
    [InlineData("Title: Q3 plan / Company: Contoso")]
    [InlineData("First name: Jordan | Job title: Engineer")]
    [InlineData("Company: Contoso\nWebsite: https://contoso.com\nFounded: 2026-10-12")]
    [InlineData("Order #112-3456789-0123456 placed 2026-10-12, call 201-555-0100 with questions")]
    [InlineData("Account #: 1234567890\nRouting: 021000021")]
    [InlineData("Ticket 12345 opened 3/4/2026 by v@example.com")]
    [InlineData("Version 1.2.3 released 2026-10-12, notes at https://example.com/12345")]
    [InlineData("Meeting in Room 201 at 10:30, dial 201-555-0100")]
    [InlineData("Emergency contact: Sam Lee, 201-555-0199\nReference: bob@example.com")]
    [InlineData("Card number: 4111 1111 1111 1111\nExp: 12/28\nCVV: 123")]
    [InlineData("linkedin.com/in/jane-doe-48213 and https://github.com/jdoe/12345")]
    [InlineData("Build 20261012 passed on 10.0.0.1:8080 at 2026-10-12T10:30:00Z")]
    public void OtherCopiesAreNot(string text) => Assert.False(TextHeuristics.LooksMultiField(text), text);

    [Theory]
    [InlineData("Note: call me after five")]
    [InlineData("Password: hunter2\nCard number: 4111 1111 1111 1111")]
    [InlineData("Order 12345 shipped on 3/4/2026, thanks for shopping with us.")]
    [InlineData("See you at 10:30, bring the slides.")]
    public void ProseAndSensitiveLinesAreNot(string text) => Assert.False(TextHeuristics.LooksMultiField(text));

    [Fact]
    public void PostalCodeCountsOnlyInAnAddress()
    {
        Assert.Equal(1, TextHeuristics.CountContactSignals("Order 12345 is on its way, email v@example.com"));
        Assert.Equal(2, TextHeuristics.CountContactSignals("Springfield, NJ 07081, email v@example.com"));
    }

    [Fact]
    public void DatesAndLinksNeverCount() =>
        Assert.Equal(0, TextHeuristics.CountContactSignals("2026-10-12\nhttps://example.com\nwww.example.org\n10/12/2026"));

    [Fact]
    public void ACompanyCountsOnlyNextToAnotherSignal()
    {
        Assert.Equal(0, TextHeuristics.CountContactSignals("Company: Acme Corp"));
        Assert.Equal(2, TextHeuristics.CountContactSignals("Company: Acme Corp\nv@example.com"));
    }

    [Theory]
    [InlineData("201-555-0100", "201-555-0100")]
    [InlineData("(201) 555-0100", "(201) 555-0100")]
    [InlineData("201.555.0100", "201.555.0100")]
    [InlineData("call +1 201 555 0100 today", "+1 201 555 0100")]
    [InlineData("+1 415 555 0123\n500 Howard St", "+1 415 555 0123")]
    [InlineData("+44 20 7946 0958\n221B Baker St", "+44 20 7946 0958")]
    [InlineData("Mobile 2015550100", "2015550100")]
    [InlineData("Tel.: 020 7946 0958", "020 7946 0958")]
    [InlineData("Phone number: (201)555-0100 ext. 12", "(201)555-0100")]
    public void PhonesWrittenLikePhones(string text, string phone) => Assert.Equal(phone, TextHeuristics.Extract(text).Phone);

    [Theory]
    [InlineData("Order #112-3456789-0123456")]
    [InlineData("Account #: 1234567890")]
    [InlineData("2026-10-12 order 1234567890 failed")]
    [InlineData("SSN 123-45-6789")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("https://example.com/201-555-0100")]
    [InlineData("Build 5-201-555-0100")]
    public void OtherNumbersAreNotPhones(string text) => Assert.Null(TextHeuristics.Extract(text).Phone);

    [Theory]
    [InlineData("12345 Main St\nSpringfield 07081", "07081")]
    [InlineData("12345 Main St / Springfield 07081", "07081")]
    [InlineData("123 Main St\nApt 4B\nPortland 97201", "97201")]
    [InlineData("Springfield, NJ 07081-1234", "07081-1234")]
    [InlineData("Springfield, N.J. 07081", "07081")]
    public void ZipCodesNextToTheirCityOrStreet(string text, string zip) => Assert.Equal(zip, TextHeuristics.Extract(text).Zip);

    [Theory]
    [InlineData("linkedin.com/in/jane-doe-48213")]
    [InlineData("Order 12345")]
    [InlineData("12345 Main St")]
    [InlineData("jordan.07081@example.com")]
    [InlineData("https://example.com/Springfield/07081")]
    [InlineData("123 Main St\nOrder 12345")]
    public void OtherNumbersAreNotZipCodes(string text) => Assert.Null(TextHeuristics.Extract(text).Zip);

    [Theory]
    [InlineData("<https://janedoe.dev>", "https://janedoe.dev")]
    [InlineData("(see https://janedoe.dev).", "https://janedoe.dev")]
    [InlineData("Site: \"www.janedoe.dev\"", "www.janedoe.dev")]
    [InlineData("https://en.wikipedia.org/wiki/Mercury_(planet)", "https://en.wikipedia.org/wiki/Mercury_(planet)")]
    public void LinksEndBeforeTheirPunctuation(string text, string link) => Assert.Equal(link, TextHeuristics.Extract(text).Website);

    [Fact]
    public void LinkedinLinks()
    {
        Assert.Equal("linkedin.com/in/jrivera", TextHeuristics.Extract("jordan@example.com, linkedin.com/in/jrivera, NJ").Linkedin);
        Assert.Equal("https://uk.linkedin.com/in/jane", TextHeuristics.Extract("<https://uk.linkedin.com/in/jane>").Linkedin);
    }

    [Fact]
    public void AdversarialInputNeverThrows()
    {
        string[] texts =
        [
            new string('1', TextHeuristics.MaxChars),
            new string('a', TextHeuristics.MaxChars - 1) + "@",
            string.Concat(Enumerable.Repeat("a@", TextHeuristics.MaxChars / 2)),
            string.Concat(Enumerable.Repeat("1 ", TextHeuristics.MaxChars / 2)),
            string.Concat(Enumerable.Repeat("+1 ", TextHeuristics.MaxChars / 3)),
            string.Concat(Enumerable.Repeat("A ", TextHeuristics.MaxChars / 2)),
            string.Concat(Enumerable.Repeat("123 Main ", TextHeuristics.MaxChars / 9)),
            string.Concat(Enumerable.Repeat("Name: ", TextHeuristics.MaxChars / 6)),
            "Label: a" + new string(' ', TextHeuristics.MaxChars - 10) + "b",
            string.Concat(Enumerable.Repeat("https://x.", TextHeuristics.MaxChars / 10)),
            string.Concat(Enumerable.Repeat("Springfield NJ ", TextHeuristics.MaxChars / 15)),
            string.Concat(Enumerable.Repeat("\n", TextHeuristics.MaxChars))
        ];
        foreach (var text in texts)
        {
            var clock = Stopwatch.StartNew();
            TextHeuristics.LooksMultiField(text);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{clock.Elapsed} for {text[..20]}");
        }
    }
}
