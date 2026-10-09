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
    public void HumanizesIds(string input, string expected) => Assert.Equal(expected, LabelNormalizer.Humanize(input));
    [Fact] public void ContextUsesPriorityAndLimitsDescription()
    {
        var field = new FieldContext("Name", "Help", "id", "Labeled", "Edit", false, [new string('x', 100), "second", "third", "fourth", "fifth"]);
        Assert.Equal("Labeled", field.BestLabel());
        Assert.Contains("Field label: Labeled", field.DescribeForJev());
        Assert.DoesNotContain("fifth", field.DescribeForJev());
    }
}
