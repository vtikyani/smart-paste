using SmartPaste.Core;

public class LabelNormalizerTests
{
    [Theory]
    [InlineData("Legal First Name* (required):", "legal first name")]
    [InlineData("Town/City?", "town city")]
    [InlineData("E-mail (optional)", "e mail")]
    public void NormalizesLabels(string input, string expected) => Assert.Equal(expected, LabelNormalizer.Normalize(input));
    [Theory]
    [InlineData("shippingZipCode", "shipping zip code")]
    [InlineData("billing_postal-code", "billing postal code")]
    [InlineData("addressLine1", "address line 1")]
    [InlineData("HTMLEmailInput", "html email input")]
    public void HumanizesIds(string input, string expected) => Assert.Equal(expected, LabelNormalizer.Humanize(input));
}
