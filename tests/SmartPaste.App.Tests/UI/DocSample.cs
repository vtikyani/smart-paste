using SmartPaste.App.Services;
using SmartPaste.Core;

// The README's own example block ("Jordan Rivera, 123 Main St Apt 4B, Springfield NJ 07081, jordan@example.com,
// 201-555-0100"), as the parser would arm it. Documentation and test data only; never shipped in the app.
internal static class DocSample
{
    public static Profile Profile(DateTimeOffset createdAt) => new(
    [
        new Entity("person", new Dictionary<string, string>
        {
            [FieldKeys.FirstName] = "Jordan",
            [FieldKeys.LastName] = "Rivera",
            [FieldKeys.FullName] = "Jordan Rivera",
            [FieldKeys.Email] = "jordan@example.com",
            [FieldKeys.Phone] = "201-555-0100"
        }),
        new Entity("address", new Dictionary<string, string>
        {
            [FieldKeys.Street] = "123 Main St",
            [FieldKeys.Unit] = "Apt 4B",
            [FieldKeys.City] = "Springfield",
            [FieldKeys.State] = "NJ",
            [FieldKeys.Zip] = "07081"
        })
    ], createdAt);

    public static ArmedProfile Armed(DateTimeOffset now) =>
        new(Profile(now.AddMinutes(-3)), now.AddMinutes(-3), now.AddMinutes(12), ProfileOrigin.CopiedText, ParsedByAi: true);

    // What a few smart pastes into a web form leave in the Recent list: labels and kinds of value only.
    public static IEnumerable<ActivityEntry> Activity(DateTimeOffset now) =>
    [
        new(now.AddMinutes(-6), "Email address", "Email", ActivityOutcome.Pasted, DecisionMaker.BuiltIn, "chrome.exe"),
        new(now.AddMinutes(-2), "Address line 1", "Street", ActivityOutcome.Pasted, DecisionMaker.Jev, "chrome.exe"),
        new(now.AddSeconds(-20), "Given name", "First name", ActivityOutcome.Pasted, DecisionMaker.Jev, "chrome.exe")
    ];

    // Jev could not tell which name a field labeled "Name" wants.
    public static PickRequest NamePick(ScreenRect anchor) => new(
    [
        new Candidate(FieldKeys.FullName, "person", "Jordan Rivera"),
        new Candidate(FieldKeys.FirstName, "person", "Jordan"),
        new Candidate(FieldKeys.LastName, "person", "Rivera")
    ], PickMode.Ambiguous, anchor, "Name");
}
