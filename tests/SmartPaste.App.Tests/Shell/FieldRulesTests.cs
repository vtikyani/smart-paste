using SmartPaste.App.Services;
using SmartPaste.App.Shell;
using static SmartPaste.App.Shell.UiaControlTypes;

namespace SmartPaste.App.Tests.Shell;

// What UI Automation elements count as fields, and which nearby text and containers describe them. The node shapes
// mirror what Edge exposes for real forms (labels and inputs as siblings once generic divs are pruned).
public sealed class FieldRulesTests
{
    private static UiaNode TextNode(string? name) => new(Text, name);

    private static UiaNode Heading(string name, int level = 2) => new(Text, name, HeadingLevel: level);

    private static UiaNode Input(string? name = null, int type = Edit) => new(type, name);

    private static SiblingScan Scan(params UiaNode[] nearestFirst) => FieldRules.ScanPreviousSiblings(nearestFirst, groupCaption: null);

    // ---- Which elements are fields ----

    [Theory]
    [InlineData(Edit, true, false, true, true)]
    [InlineData(Edit, true, false, false, true)]
    [InlineData(Edit, true, true, true, false)]
    [InlineData(Edit, false, false, true, false)]
    [InlineData(ComboBox, true, false, true, true)]
    [InlineData(ComboBox, true, false, false, false)]
    [InlineData(ComboBox, true, true, true, false)]
    [InlineData(CheckBox, true, false, false, false)]
    [InlineData(Document, true, false, true, false)]
    [InlineData(Group, true, true, true, false)]
    public void OnlyEditableTextBoxesAndComboBoxesQualify(int controlType, bool value, bool readOnly, bool text, bool expected)
    {
        Assert.Equal(expected, FieldRules.Qualifies(controlType, value, readOnly, text));
    }

    [Fact]
    public void ChromiumSelectIsNotAFieldButADatalistInputIs()
    {
        // As Edge reports them: <select> has a value that is not read-only but no text pattern.
        Assert.False(FieldRules.Qualifies(ComboBox, hasValuePattern: true, isReadOnly: false, hasTextPattern: false));
        Assert.True(FieldRules.Qualifies(ComboBox, hasValuePattern: true, isReadOnly: false, hasTextPattern: true));
    }

    [Fact]
    public void ControlTypeNamesMatchFieldContext()
    {
        Assert.Equal("Edit", FieldRules.ControlTypeName(Edit));
        Assert.Equal("ComboBox", FieldRules.ControlTypeName(ComboBox));
        Assert.Null(FieldRules.ControlTypeName(Button));
    }

    // ---- Nearby text ----

    [Fact]
    public void TheTextRightBeforeAFieldIsItsLabel()
    {
        var scan = Scan(TextNode("Phone"));
        Assert.Equal(new[] { "Phone" }, scan.NearbyText);
    }

    [Fact]
    public void UpToThreeTextsAreKeptNearestLast()
    {
        var scan = Scan(TextNode("(5 digits)"), TextNode("ZIP"), TextNode("Postal"), TextNode("Too far"));
        Assert.Equal(new[] { "Postal", "ZIP", "(5 digits)" }, scan.NearbyText);
    }

    [Fact]
    public void AnUnlabeledFieldDoesNotBorrowTheLabelOfTheFieldAbove()
    {
        // Edge, for an <input> with no label after "Card number": the previous sibling is that row's input.
        var scan = Scan(Input("Card number"), TextNode("Card number"), Input("Order id"), TextNode("Order id"));
        Assert.Empty(scan.NearbyText);
    }

    [Fact]
    public void OnlyTextBetweenTheFieldAndThePreviousControlCounts()
    {
        var scan = Scan(TextNode("Mobile"), Input(), TextNode("Phone"), Input("you@example.com"));
        Assert.Equal(new[] { "Mobile" }, scan.NearbyText);
    }

    [Theory]
    [InlineData(Edit)]
    [InlineData(ComboBox)]
    [InlineData(CheckBox)]
    [InlineData(RadioButton)]
    [InlineData(Button)]
    [InlineData(SplitButton)]
    [InlineData(List)]
    [InlineData(ListItem)]
    [InlineData(Slider)]
    [InlineData(Spinner)]
    [InlineData(Tab)]
    [InlineData(Tree)]
    [InlineData(DataGrid)]
    [InlineData(Table)]
    [InlineData(Group)]
    [InlineData(Pane)]
    [InlineData(Document)]
    public void EveryKindOfControlEndsTheLabelSearch(int controlType)
    {
        var scan = Scan(Input(type: controlType), TextNode("Belongs to the control above"));
        Assert.Empty(scan.NearbyText);
    }

    [Theory]
    [InlineData(Hyperlink)]
    [InlineData(50006)] // Image
    [InlineData(50038)] // Separator
    public void LinksAndDecorationsDoNotEndTheLabelSearch(int controlType)
    {
        var scan = Scan(new UiaNode(controlType, "Forgot your password?"), TextNode("Password"));
        Assert.Equal(new[] { "Password" }, scan.NearbyText);
    }

    [Fact]
    public void LongSentencesAreContextNotLabels()
    {
        var instruction = "Enter the address that appears on your card statement exactly as the bank has it on file please.";
        var scan = Scan(TextNode("Street"), TextNode(instruction));

        Assert.Equal(new[] { "Street" }, scan.NearbyText);
        Assert.NotNull(scan.Context);
        Assert.True(scan.Context.Length <= FieldRules.MaxContextLength);
        Assert.StartsWith("Enter the address", scan.Context);
    }

    [Fact]
    public void ALongSentenceIsSkippedButShorterTextBeyondItStillCounts()
    {
        var scan = Scan(TextNode(new string('x', 81)), TextNode("Street"));
        Assert.Equal(new[] { "Street" }, scan.NearbyText);
        Assert.NotNull(scan.Context);
    }

    [Fact]
    public void ContextBeyondThePreviousControlBelongsToThatControl()
    {
        var scan = Scan(Input(), TextNode(new string('x', 100)));
        Assert.Null(scan.Context);
    }

    [Fact]
    public void TheFieldsetCaptionIsNotALabel()
    {
        // Edge exposes a fieldset's legend as the group's name and as its first text child.
        var scan = FieldRules.ScanPreviousSiblings([TextNode("First name"), TextNode("Contact details")], groupCaption: "Contact details");
        Assert.Equal(new[] { "First name" }, scan.NearbyText);
    }

    [Fact]
    public void BlankTextIsIgnoredAndWhitespaceTidied()
    {
        var scan = Scan(TextNode(" "), TextNode("Mobile "), TextNode(null), TextNode("  Home\n phone "));
        Assert.Equal(new[] { "Home phone", "Mobile" }, scan.NearbyText);
    }

    // ---- Headings and sections ----

    [Fact]
    public void TheNearestHeadingIsFoundPastLabelsAndContext()
    {
        var scan = Scan(TextNode("Street"), TextNode(new string('y', 90)), Heading("Billing address", 3), TextNode("Earlier"));
        Assert.Equal("Billing address", scan.Heading);
        Assert.Equal(new[] { "Street" }, scan.NearbyText);
    }

    [Fact]
    public void TheHeadingSearchContinuesPastOtherFields()
    {
        var scan = Scan(Input("Last name"), TextNode("Last name"), Input("First name"), Heading("Shipping address"));
        Assert.Equal("Shipping address", scan.Heading);
        Assert.Empty(scan.NearbyText);
    }

    [Fact]
    public void AHeadingRightAboveAnUnlabeledFieldIsItsLabel()
    {
        var scan = Scan(Heading("Email"));
        Assert.Equal(new[] { "Email" }, scan.NearbyText);
        Assert.Equal("Email", scan.Heading);
    }

    [Fact]
    public void AGroupBoxDrawnAroundTheFieldNamesItsSection()
    {
        var field = new ScreenRect(120, 100, 200, 24);
        UiaNode[] siblings =
        [
            TextNode("City"),
            new(Group, "Mailing address", Bounds: new ScreenRect(100, 60, 400, 200)),
            new(Group, "Elsewhere", Bounds: new ScreenRect(600, 60, 100, 100))
        ];

        var scan = FieldRules.ScanPreviousSiblings(siblings, groupCaption: null, field);

        Assert.Equal("Mailing address", scan.EnclosingGroup);
        Assert.Equal(new[] { "City" }, scan.NearbyText);
    }

    [Fact]
    public void FindHeadingTakesTheNearestOne()
    {
        Assert.Equal("Contact", FieldRules.FindHeading([TextNode("x"), Heading("Contact"), Heading("Older")]));
        Assert.Null(FieldRules.FindHeading([TextNode("x"), Input("y")]));
    }

    [Fact]
    public void NamedGroupsLandmarksAndDialogsAreSections()
    {
        Assert.True(FieldRules.IsSection(new UiaNode(Group, "Contact details")));
        Assert.True(FieldRules.IsSection(new UiaNode(Group, "Checkout form", LandmarkType: 80001)));
        Assert.True(FieldRules.IsSection(new UiaNode(Pane, "Billing", LandmarkType: 80000)));
        Assert.True(FieldRules.IsSection(new UiaNode(Pane, "Add address", IsDialog: true)));
        Assert.False(FieldRules.IsSection(new UiaNode(Group, null)));
        Assert.False(FieldRules.IsSection(new UiaNode(Group, "  ")));
        Assert.False(FieldRules.IsSection(new UiaNode(Pane, "Panel1")));
        Assert.False(FieldRules.IsSection(new UiaNode(Text, "Label")));
    }

    [Fact]
    public void TheWalkUpStopsAtThePageOrTheWindow()
    {
        Assert.True(FieldRules.EndsSearch(new UiaNode(Document, "UIA probe form")));
        Assert.True(FieldRules.EndsSearch(new UiaNode(Window, "Notepad")));
        Assert.False(FieldRules.EndsSearch(new UiaNode(Group, "Form")));
    }

    [Theory]
    [InlineData(80050, 0)]
    [InlineData(80051, 1)]
    [InlineData(80053, 3)]
    [InlineData(80059, 9)]
    [InlineData(0, 0)]
    public void MapsUiaHeadingLevels(int uiaValue, int expected)
    {
        Assert.Equal(expected, FieldRules.HeadingLevel(uiaValue));
    }

    [Fact]
    public void SectionsAreNearestFirstWithoutDuplicatesAndAtMostFourGroups()
    {
        var sections = new SectionsBuilder();
        sections.AddContext("Enter the address exactly as printed on your statement");
        sections.AddHeading("Billing address");
        sections.AddGroup("Billing");
        sections.AddGroup("billing");
        sections.AddGroup("Payment");
        sections.AddGroup("Checkout form");
        sections.AddGroup("Main");
        Assert.True(sections.HasAllGroups);
        sections.AddGroup("Fifth");
        sections.AddGroup(null);
        sections.AddHeading("BILLING ADDRESS");

        Assert.Equal(
            new[] { "Enter the address exactly as printed on your statement", "Billing address", "Billing", "Payment", "Checkout form", "Main" },
            sections.Build());
    }

    // ---- Placeholder, limits, classes ----

    [Fact]
    public void ChromiumHelpTextIsThePlaceholder()
    {
        Assert.Equal("Jane", FieldRules.Placeholder("Chrome", "Jane", null));
        Assert.Equal("you@example.com", FieldRules.Placeholder("Chrome", "you@example.com", null));
        // aria-describedby text is a description, not a placeholder.
        Assert.Null(FieldRules.Placeholder("Chrome", "5 digits", "5 digits"));
        Assert.Null(FieldRules.Placeholder("Chrome", null, "5 digits"));
        Assert.Null(FieldRules.Placeholder("Win32", "Jane", null));
        Assert.Null(FieldRules.Placeholder(null, "Jane", null));
    }

    [Theory]
    [InlineData(5L, 5)]
    [InlineData(40L, 40)]
    [InlineData(29999L, 29999)]
    [InlineData(30000L, null)]
    [InlineData(32767L, null)]
    [InlineData(0x7FFFFFFEL, null)]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    public void OnlyRealTextLimitsCount(long limit, int? expected)
    {
        Assert.Equal(expected, FieldRules.NormalizeTextLimit(limit));
    }

    [Theory]
    [InlineData("Edit", true)]
    [InlineData("edit", true)]
    [InlineData("RichEdit20W", true)]
    [InlineData("RICHEDIT50W", true)]
    [InlineData("WindowsForms10.EDIT.app.0.141b42a_r6_ad1", true)]
    [InlineData("WindowsForms10.RichEdit20W.app.0.141b42a_r6_ad1", true)]
    [InlineData("Chrome_RenderWidgetHostHWND", false)]
    [InlineData("EditBox", false)]
    [InlineData("Button", false)]
    [InlineData(null, false)]
    public void RecognizesNativeEditClasses(string? className, bool expected)
    {
        Assert.Equal(expected, FieldRules.IsNativeEditClass(className));
    }

    [Fact]
    public void CutsLongTextAtAWordBoundary()
    {
        Assert.Equal("short", FieldRules.Cut("short", 10));
        Assert.Equal("one two", FieldRules.Cut("one two three", 10));
        Assert.Equal("abcdefghij", FieldRules.Cut("abcdefghijklmnop", 10));
    }
}
