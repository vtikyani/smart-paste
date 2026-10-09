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
}
