using SmartPaste.Core;

public class FieldContextTests
{
    private static FieldContext Field(string? name = null, string? id = null, string? labeledBy = null, string? help = null,
        string[]? siblings = null, string? ocr = null) =>
        new(name, help, id, labeledBy, "Edit", false, siblings ?? [], ocr);

    [Theory]
    [InlineData("input-15")]
    [InlineData(":r3:")]
    [InlineData("mat-input-2")]
    [InlineData("field_8f3a2c")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("deadbeef-1234")]
    [InlineData("ctl00$main$txt1")]
    [InlineData("react-select-3-input")]
    [InlineData("textBox1")]
    [InlineData("q12")]
    public void GeneratedIdsAreNotLabels(string id)
    {
        Assert.Null(LabelNormalizer.MeaningfulId(id));
        // The visible text next to the field wins over the id.
        Assert.Equal("ZIP code", Field(id: id, siblings: ["ZIP code"]).BestLabel());
    }

    [Theory]
    [InlineData("shippingZipCode", "shipping zip code")]
    [InlineData("firstName", "first name")]
    [InlineData("email", "email")]
    [InlineData("addressLine2", "address line 2")]
    [InlineData("ctl00$MainContent$txtFirstName", "ctl 00 main content txt first name")]
    [InlineData("dob", "dob")]
    public void ReadableIdsAreLabels(string id, string expected)
    {
        Assert.Equal(expected, LabelNormalizer.MeaningfulId(id));
        Assert.Equal(expected, Field(id: id, siblings: ["Something nearby"]).BestLabel());
    }

    [Fact]
    public void FieldWithOnlyAGeneratedIdHasNoLabel()
    {
        var field = Field(id: "input-15");
        Assert.Equal("", field.BestLabel());
        Assert.False(field.HasUsableLabel);
        Assert.False(field.HasContext);
        Assert.True((field with { Sections = ["Shipping address"] }).HasContext);
    }

    [Fact]
    public void LabelPriority()
    {
        Assert.Equal("Labeled", new FieldContext("Name", "Help", "postalCode", "Labeled", "Edit", false, ["Near"]).BestLabel());
        Assert.Equal("Name", new FieldContext("Name", "Help", "postalCode", null, "Edit", false, ["Near"]).BestLabel());
        Assert.Equal("Hint", (new FieldContext(null, "Help", "postalCode", null, "Edit", false, ["Near"]) with { Placeholder = "Hint" }).BestLabel());
        Assert.Equal("Help", new FieldContext(null, "Help", "postalCode", null, "Edit", false, ["Near"]).BestLabel());
        Assert.Equal("postal code", new FieldContext(null, null, "postalCode", null, "Edit", false, ["Near"]).BestLabel());
        Assert.Equal("Near", new FieldContext(null, null, "input-3", null, "Edit", false, ["Far", "Near"]).BestLabel());
        Assert.Equal("From OCR", new FieldContext(null, null, null, null, "Edit", false, [], "From OCR").BestLabel());
    }

    [Fact]
    public void OnlyReadableTextIsLearnable()
    {
        Assert.Equal("ZIP", Field(labeledBy: "ZIP").LearnableLabel());
        Assert.Equal("ZIP", Field(name: "ZIP").LearnableLabel());
        Assert.Equal("ZIP", (Field() with { Placeholder = "ZIP" }).LearnableLabel());
        Assert.Equal("ZIP", Field(siblings: ["ZIP"]).LearnableLabel());
        Assert.Equal("ZIP", Field(ocr: "ZIP").LearnableLabel());
        // An id alone, or help text alone, is never learned.
        Assert.Null(Field(id: "shippingZip").LearnableLabel());
        Assert.Null(Field(help: "Five digits").LearnableLabel());
        Assert.Null(Field(id: "input-15").LearnableLabel());
    }

    [Fact]
    public void JevStateNamesEachPartTrimsAndSkipsEmpties()
    {
        var field = new FieldContext("Street address", "Street address", "shippingStreet", null, "Edit", false,
            ["Far away text", "", "Street address"], null, 60)
        {
            Placeholder = "123 Main St",
            Sections = ["Shipping address", "Checkout"],
            AppName = "chrome.exe"
        };
        var state = field.ToJevState();
        Assert.Equal("Street address", (string?)state["label"]);
        Assert.Equal("123 Main St", (string?)state["placeholder"]);
        Assert.Null(state["help_text"]); // same words as the label
        Assert.Equal("shipping street", (string?)state["field_id"]);
        Assert.Equal("text box", (string?)state["control"]);
        Assert.Equal(60, (int?)state["max_length"]);
        Assert.Equal(["Far away text"], state["nearby_text"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(["Shipping address", "Checkout"], state["sections"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("chrome", (string?)state["app"]);
        Assert.Null(state["ocr_text"]);
    }

    [Fact]
    public void JevStateCapsTextAndLists()
    {
        var field = new FieldContext(new string('x', 500), null, "input-9", null, "Edit", false,
            Enumerable.Range(1, 10).Select(i => $"Nearby {i}").ToArray());
        var state = field.ToJevState();
        Assert.Equal(120, ((string?)state["label"])!.Length);
        Assert.Null(state["field_id"]); // generated ids are not sent
        var nearby = state["nearby_text"]!.AsArray().Select(n => (string?)n).ToArray();
        Assert.Equal(6, nearby.Length);
        Assert.Equal("Nearby 10", nearby[^1]); // the nearest are kept
    }
}
