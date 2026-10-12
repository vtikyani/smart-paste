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

    [Fact]
    public void LabeledBlockWithTwoKnownLabelsIs() =>
        Assert.True(TextHeuristics.LooksMultiField("Name: Jordan Rivera\nCompany: Acme Corp"));

    [Fact]
    public void LabeledBlockOnSeparatedLineIs() =>
        Assert.True(TextHeuristics.LooksMultiField("First name: Jordan | Job title: Engineer"));

    [Theory]
    [InlineData("Note: call me after five")]
    [InlineData("Password: hunter2\nCard number: 4111 1111 1111 1111")]
    [InlineData("Order 12345 shipped on 3/4/2026, thanks for shopping with us.")]
    [InlineData("See you at 10:30, bring the slides.")]
    public void ProseAndSensitiveLinesAreNot(string text) => Assert.False(TextHeuristics.LooksMultiField(text));

    [Fact]
    public void PostalCodeCountsOnlyInAnAddress()
    {
        Assert.Equal(1, TextHeuristics.CountFieldTypes("Order 12345 is on its way, email v@example.com"));
        Assert.Equal(2, TextHeuristics.CountFieldTypes("Springfield, NJ 07081, email v@example.com"));
    }
}
