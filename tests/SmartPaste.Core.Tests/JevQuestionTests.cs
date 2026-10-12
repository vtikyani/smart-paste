using System.Text.Json.Nodes;
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
        Assert.Equal(["first_name", "street.billing_address", "street.shipping_address", "city", "email", "none"],
            options.Select(o => o.Name));
        // The same city in two entities is one option.
        Assert.Equal("Springfield", options.Single(o => o.Name == "city").Candidate!.Value);
        Assert.True(options[^1].IsNone);

        var shipping = options.Single(o => o.Name == "street.shipping_address");
        Assert.Equal(new Candidate(FieldKeys.Street, "shipping address", "2 Oak Ave"), shipping.Candidate);
        var description = shipping.Description.AsObject();
        Assert.Equal(FieldKeys.Describe(FieldKeys.Street), (string?)description["field"]);
        Assert.Equal("2 Oak Ave", (string?)description["value"]);
        Assert.Equal("shipping address", (string?)description["belongs_to"]);
    }

    [Fact]
    public void SameEntityLabelGetsNumberedNames()
    {
        var profile = new Profile([Entity("person", (FieldKeys.Phone, "201-555-0100")), Entity("person", (FieldKeys.Phone, "201-555-0199"))], Now);
        Assert.Equal(["phone.person", "phone.person_2", "none"], JevQuestion.BuildOptions(profile).Select(o => o.Name));
    }

    [Fact]
    public void SingleEntityDescriptionsLeaveOutBelongsTo()
    {
        var profile = new Profile([Entity("person", (FieldKeys.Zip, "07081"))], Now);
        var zip = JevQuestion.BuildOptions(profile)[0];
        Assert.Equal("zip", zip.Name);
        Assert.Null(zip.Description.AsObject()["belongs_to"]);
    }

    [Fact]
    public void ValuesAreTrimmedBeforeLeavingThePc()
    {
        var profile = new Profile([Entity("person", ("summary", "  word " + new string('a', 200)))], Now);
        var value = (string?)JevQuestion.BuildOptions(profile)[0].Description.AsObject()["value"];
        Assert.Equal(JevQuestion.ValueLimit, value!.Length);
        Assert.StartsWith("word a", value);
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
    public void RequestBodyHasModelStateAndChoiceQuestion()
    {
        var field = new FieldContext("ZIP code", null, null, null, "Edit", false, []);
        var question = JevQuestion.ForField(field, new Profile([Entity("person", (FieldKeys.Zip, "07081"))], Now));
        var body = question.ToRequestBody("jev-latest", "field_value");

        Assert.Equal("jev-latest", (string?)body["model"]);
        Assert.Equal("ZIP code", (string?)body["state"]!["label"]);
        var asked = body["questions"]!["field_value"]!.AsObject();
        Assert.Equal("choice", (string?)asked["type"]);
        Assert.Equal(JevQuestion.FieldInstructions, (string?)asked["instructions"]);
        Assert.Equal(["zip", "none"], asked["criteria"]!.AsObject().Select(p => p.Key));
        Assert.Equal("None of these values belongs in this field", (string?)asked["criteria"]!["none"]);
        // A second body for another route is independent of the first.
        Assert.NotSame(body["state"], question.ToRequestBody("typesafe/jev-1.13", "field_value")["state"]);
    }

    [Fact]
    public void QuestionRejectsBadOptionSets()
    {
        var state = new JsonObject();
        Assert.Throws<ArgumentOutOfRangeException>(() => new JevQuestion(state, "Which?", []));
        var duplicate = new JevOption("a", JsonValue.Create("x"), null);
        Assert.Throws<ArgumentException>(() => new JevQuestion(state, "Which?", [duplicate, duplicate]));
    }
}
