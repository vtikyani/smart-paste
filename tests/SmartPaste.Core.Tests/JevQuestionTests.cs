using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SmartPaste.Core;

public class JevQuestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private static Entity Entity(string label, params (string Key, string Value)[] fields) =>
        new(label, fields.ToDictionary(f => f.Key, f => f.Value));

    [Fact]
    public void OneOptionPerDistinctValueNamedForMapping()
    {
        var profile = new Profile(
        [
            Entity("person", (FieldKeys.FirstName, "Jordan"), (FieldKeys.Email, "v@example.com")),
            Entity("billing address", (FieldKeys.Street, "1 Main St"), (FieldKeys.City, "Springfield")),
            Entity("shipping address", (FieldKeys.Street, "2 Oak Ave"), (FieldKeys.City, " springfield "))
        ], Now);

        var options = JevQuestion.BuildOptions(profile);
        Assert.Equal(["first_name", "street__billing_address", "street__shipping_address", "city", "email", "none"],
            options.Select(o => o.Name));
        // The same city in two entities is one option.
        Assert.Equal("Springfield", options.Single(o => o.Name == "city").Candidate!.Value);
        Assert.True(options[^1].IsNone);

        var shipping = options.Single(o => o.Name == "street__shipping_address");
        Assert.Equal(new Candidate(FieldKeys.Street, "shipping address", "2 Oak Ave"), shipping.Candidate);
        // A plain string: name, owner, value, then the definition that tells it from its neighbours.
        Assert.Equal("Street (shipping address): 2 Oak Ave — " + FieldKeys.Describe(FieldKeys.Street), shipping.DescriptionText);
        Assert.Equal("Street (shipping address): 2 Oak Ave — " + FieldKeys.Describe(FieldKeys.Street), (string?)shipping.Description);
    }

    [Fact]
    public void SameEntityLabelGetsNumberedNames()
    {
        var profile = new Profile([Entity("person", (FieldKeys.Phone, "201-555-0100")), Entity("person", (FieldKeys.Phone, "201-555-0199"))], Now);
        Assert.Equal(["phone__person", "phone__person_2", "none"], JevQuestion.BuildOptions(profile).Select(o => o.Name));
    }

    [Fact]
    public void SingleEntityDescriptionsLeaveOutTheOwner()
    {
        var profile = new Profile([Entity("person", (FieldKeys.Zip, "07081"), ("favorite_color", "Teal"))], Now);
        var options = JevQuestion.BuildOptions(profile);
        Assert.Equal("zip", options[0].Name);
        Assert.Equal("ZIP / postal code: 07081 — ZIP or postal code", options[0].DescriptionText);
        // An extra key's definition would only repeat its name.
        Assert.Equal("Favorite color: Teal", options[1].DescriptionText);
    }

    [Fact]
    public void ValuesAreTrimmedBeforeLeavingThePc()
    {
        var profile = new Profile([Entity("person", ("summary", "  word " + new string('a', 200)))], Now);
        var description = JevQuestion.BuildOptions(profile)[0].DescriptionText;
        var value = description["Summary: ".Length..];
        Assert.Equal(JevQuestion.ValueLimit, value.Length);
        Assert.StartsWith("word a", value);
    }

    [Fact]
    public void OptionNamesUseOnlyLowercaseLettersDigitsAndUnderscores()
    {
        var profile = new Profile(
        [
            Entity("person", ("work_phone_#", "201-555-0100"), ("número", "42"), ("電話", "x"), ("Shoe Size (EU)", "44")),
            Entity("Billing / Shipping", (FieldKeys.Street, "1 Main St")),
            Entity("Billing — Shipping", (FieldKeys.Street, "2 Oak Ave")),
            Entity("Société Générale", (FieldKeys.Company, "Acme"))
        ], Now);

        var options = JevQuestion.BuildOptions(profile);
        Assert.All(options, option => Assert.Matches(new Regex("^[a-z0-9_]+$"), option.Name));
        // Standard keys first, then extra keys in ordinal order ("Shoe Size (EU)", "número", "work_phone_#", "電話").
        Assert.Equal(
            ["street__billing_shipping", "street__billing_shipping_2", "company", "shoe_size_eu", "numero", "work_phone", "value", "none"],
            options.Select(o => o.Name));
        // Every name maps back to its value, including a duplicate that only differs by its number.
        Assert.Equal("2 Oak Ave", JevQuestion.ForField(new FieldContext("Street", null, null, null, "Edit", false, []), profile)
            .Option("street__billing_shipping_2")!.Candidate!.Value);
    }

    [Fact]
    public void AnswersThatDifferOnlyInCaseOrSpacesMapBack()
    {
        var profile = new Profile([Entity("billing address", (FieldKeys.Street, "1 Main St")), Entity("shipping address", (FieldKeys.Street, "2 Oak Ave"))], Now);
        var question = JevQuestion.ForField(new FieldContext("Street", null, null, null, "Edit", false, []), profile);
        Assert.Equal("2 Oak Ave", question.Option(" Street__Shipping_Address ")!.Candidate!.Value);
        Assert.Null(question.Option("street.shipping_address"));
        Assert.Null(question.Option(""));
    }

    [Fact]
    public void OptionsAreCappedWithNoneKept()
    {
        var fields = Enumerable.Range(0, 400).Select(i => ($"extra_{i:D3}", $"value {i}")).ToArray();
        var options = JevQuestion.BuildOptions(new Profile([Entity("person", fields)], Now));
        Assert.Equal(JevQuestion.MaxOptions, options.Count);
        Assert.Equal("none", options[^1].Name);
    }

    [Fact]
    public void ExtraKeyNamedNoneDoesNotCollide()
    {
        var options = JevQuestion.BuildOptions(new Profile([Entity("person", ("none", "x"))], Now));
        Assert.Equal(["none_2", "none"], options.Select(o => o.Name));
    }

    [Fact]
    public void RequestBodyHasModelStateAndPlainStringCriteria()
    {
        var field = new FieldContext("ZIP code", null, null, null, "Edit", false, []);
        var question = JevQuestion.ForField(field, new Profile([Entity("person", (FieldKeys.Zip, "07081"))], Now));
        var body = question.ToRequestBody("jev-latest", "field_value");

        Assert.Equal("jev-latest", (string?)body["model"]);
        // The state stays an object.
        Assert.Equal("ZIP code", (string?)body["state"]!["label"]);
        var asked = body["questions"]!["field_value"]!.AsObject();
        Assert.Equal("choice", (string?)asked["type"]);
        Assert.Equal(JevQuestion.FieldInstructions, (string?)asked["instructions"]);
        var criteria = asked["criteria"]!.AsObject();
        Assert.Equal(["zip", "none"], criteria.Select(p => p.Key));
        Assert.All(criteria, p => Assert.Equal(System.Text.Json.JsonValueKind.String, p.Value!.GetValueKind()));
        Assert.Equal("ZIP / postal code: 07081 — ZIP or postal code", (string?)criteria["zip"]);
        Assert.Equal("None of these values belongs in this field", (string?)criteria["none"]);
        // A second body for another route is independent of the first.
        Assert.NotSame(body["state"], question.ToRequestBody("typesafe/jev-1.13", "field_value")["state"]);
    }

    [Fact]
    public void ADescriptionGivenAsAnObjectIsStillSentAsAString()
    {
        var option = new JevOption("zip", new JsonObject { ["field"] = "ZIP", ["value"] = "07081" }, null);
        var body = new JevQuestion(new JsonObject(), "Which?", [option]).ToRequestBody("jev-latest", "q");
        Assert.Equal("{\"field\":\"ZIP\",\"value\":\"07081\"}", (string?)body["questions"]!["q"]!["criteria"]!["zip"]);
    }

    [Fact]
    public void QuestionRejectsBadOptionSets()
    {
        var state = new JsonObject();
        Assert.Throws<ArgumentOutOfRangeException>(() => new JevQuestion(state, "Which?", []));
        var duplicate = new JevOption("a", JsonValue.Create("x"), null);
        Assert.Throws<ArgumentException>(() => new JevQuestion(state, "Which?", [duplicate, duplicate]));
        foreach (var name in new[] { "street.billing", "Zip", "número", "", "work phone", new string('a', 101) })
            Assert.Throws<ArgumentException>(() => new JevQuestion(state, "Which?", [new JevOption(name, JsonValue.Create("x"), null)]));
    }
}
