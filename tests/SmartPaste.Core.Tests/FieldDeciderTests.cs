using System.Net;
using SmartPaste.Core;

public class FieldDeciderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private static FieldContext Field(string? label, bool password = false, string control = "Edit") =>
        new(label, null, null, null, control, password, []);

    private static Profile Profile(params Entity[] entities) => new(entities, Now);

    private static Entity Person => new("person", new Dictionary<string, string>
    {
        [FieldKeys.FirstName] = "Jordan", [FieldKeys.LastName] = "Rivera", [FieldKeys.Email] = "v@example.com",
        [FieldKeys.Zip] = "07081", [FieldKeys.State] = "NJ"
    });

    private static Profile Addresses => Profile(
        new Entity("billing address", new Dictionary<string, string> { [FieldKeys.Street] = "1 Main St" }),
        new Entity("shipping address", new Dictionary<string, string> { [FieldKeys.Street] = "2 Oak Ave" }));

    private static JevChoice Pick(string choice, double confidence, params (string Option, double P)[] probabilities) =>
        new(choice, confidence, probabilities.ToDictionary(p => p.Option, p => p.P), JevEndpoint.TypeSafe);

    // ---- Jev decides ----

    [Fact]
    public async Task ConfidentValueIsPastedFormattedAndLearned()
    {
        var cache = LabelCache.Empty();
        var jev = new FakeJev(_ => Pick(FieldKeys.State, 0.95, (FieldKeys.State, 0.97)));
        var decision = await new FieldDecider(cache, jev).DecideAsync(Field("Your region", control: "ComboBox"), Profile(Person), CancellationToken.None);

        Assert.Equal(DecisionKind.Value, decision.Kind);
        Assert.Equal("New Jersey", decision.Value); // formatted for a combo box
        Assert.Equal(FieldKeys.State, decision.Key);
        Assert.Equal(DecisionSource.Jev, decision.DecidedBy);
        Assert.Equal("jev", decision.Source);
        Assert.Equal(DecisionReasons.Matched, decision.Reason);
        Assert.Equal(0.95, decision.Confidence);
        Assert.Null(decision.JevFailure);
        Assert.True(cache.TryGet("your region", out var learned, out var isLearned) && isLearned);
        Assert.Equal(FieldKeys.State, learned);
    }

    [Fact]
    public async Task JevDecidesEvenWhenTheLocalListKnowsTheLabel()
    {
        var jev = new FakeJev(_ => Pick(FieldKeys.Email, 0.9, (FieldKeys.Email, 0.92)));
        var decision = await new FieldDecider(new LabelCache(null), jev).DecideAsync(Field("First name"), Profile(Person), CancellationToken.None);
        Assert.Equal(1, jev.Calls);
        Assert.Equal(DecisionSource.Jev, decision.DecidedBy);
        Assert.Equal("v@example.com", decision.Value);
    }

    [Fact]
    public async Task JevSeesTheFieldAndTrimmedCandidateValues()
    {
        var jev = new FakeJev(_ => Pick("none", 0.9));
        await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field("Street address") with { Sections = ["Shipping address"] }, Addresses, CancellationToken.None);

        var question = jev.LastQuestion!;
        Assert.Equal("Street address", (string?)question.State["label"]);
        Assert.Equal("Shipping address", (string?)question.State["sections"]![0]);
        Assert.Equal(["street__billing_address", "street__shipping_address", "none"], question.Options.Select(o => o.Name));
        Assert.StartsWith("Street (shipping address): 2 Oak Ave", question.Options[1].DescriptionText);
        Assert.Equal(FieldDecider.PasteDeadline, jev.LastDeadline);
    }

    [Fact]
    public async Task JevPicksBetweenBillingAndShipping()
    {
        var jev = new FakeJev(_ => Pick("street__shipping_address", 0.92, ("street__shipping_address", 0.95), ("street__billing_address", 0.05)));
        var decision = await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field("Street"), Addresses, CancellationToken.None);
        Assert.Equal("2 Oak Ave", decision.Value);
        Assert.Equal(new Candidate(FieldKeys.Street, "shipping address", "2 Oak Ave"), Assert.Single(decision.Candidates));
    }

    [Fact]
    public async Task ConfidentNoneIsAPlainPaste()
    {
        var decision = await new FieldDecider(LabelCache.Empty(), new FakeJev(_ => Pick("none", 0.85)))
            .DecideAsync(Field("Emergency contact phone"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.PlainPaste, decision.Kind);
        Assert.Equal(DecisionReasons.NoneSelected, decision.Reason);
        Assert.Equal(DecisionSource.Jev, decision.DecidedBy);
        Assert.Equal(0.85, decision.Confidence);
    }

    [Fact]
    public async Task UnsureShowsTopThreeByProbabilityWithoutNone()
    {
        var jev = new FakeJev(_ => Pick(FieldKeys.Zip, 0.5,
            (FieldKeys.Zip, 0.4), ("none", 0.3), (FieldKeys.Email, 0.15), (FieldKeys.FirstName, 0.1), (FieldKeys.LastName, 0.05)));
        var cache = LabelCache.Empty();
        var decision = await new FieldDecider(cache, jev).DecideAsync(Field("Unclear field"), Profile(Person), CancellationToken.None);

        Assert.Equal(DecisionKind.Picker, decision.Kind);
        Assert.Equal(DecisionReasons.LowConfidence, decision.Reason);
        Assert.Equal([FieldKeys.Zip, FieldKeys.Email, FieldKeys.FirstName], decision.Candidates.Select(c => c.Key));
        Assert.Equal(0.5, decision.Confidence);
        Assert.Empty(cache.Learned); // nothing is learned from an unsure answer
    }

    [Fact]
    public async Task NoneMostLikelyIsAPlainPasteEvenWhenUnsure()
    {
        var decision = await new FieldDecider(LabelCache.Empty(), new FakeJev(_ => Pick("none", 0.3, ("none", 0.6))))
            .DecideAsync(Field("Odd field"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.PlainPaste, decision.Kind);
        Assert.Equal(DecisionReasons.NoneSelected, decision.Reason);
        Assert.Equal(DecisionSource.Jev, decision.DecidedBy);
    }

    // ---- Confidence is concentration, not the chosen value's probability ----

    [Fact]
    public async Task ConfidentButSplitBetweenTwoValuesShowsThePicker()
    {
        // Confidence 0.85 yet only 0.52 for the full name: "Jordan Rivera" must not land in "First name" unasked.
        var profile = Profile(new Entity("person", new Dictionary<string, string>
        {
            [FieldKeys.FirstName] = "Jordan", [FieldKeys.FullName] = "Jordan Rivera", [FieldKeys.Email] = "v@example.com"
        }));
        var cache = LabelCache.Empty();
        var jev = new FakeJev(_ => Pick(FieldKeys.FullName, 0.85, (FieldKeys.FullName, 0.52), (FieldKeys.FirstName, 0.48), ("none", 0)));
        var decision = await new FieldDecider(cache, jev).DecideAsync(Field("First name"), profile, CancellationToken.None);

        Assert.Equal(DecisionKind.Picker, decision.Kind);
        Assert.Equal(DecisionReasons.LowConfidence, decision.Reason);
        Assert.Equal(["Jordan Rivera", "Jordan"], decision.Candidates.Select(c => c.Value));
        Assert.Empty(cache.Learned);
    }

    [Theory]
    // Above 0.8 and at least 0.7 for the chosen value: pasted.
    [InlineData(0.81, 0.7, DecisionKind.Value)]
    [InlineData(0.95, 0.97, DecisionKind.Value)]
    // Exactly 0.8 is not above it.
    [InlineData(0.8, 0.95, DecisionKind.Picker)]
    [InlineData(0.81, 0.69, DecisionKind.Picker)]
    [InlineData(0.5, 0.9, DecisionKind.Picker)]
    public async Task PastedOnlyAboveTheConfidenceFloorWithAProbableChoice(double confidence, double probability, DecisionKind expected)
    {
        var jev = new FakeJev(_ => Pick(FieldKeys.Zip, confidence, (FieldKeys.Zip, probability), (FieldKeys.Email, 1 - probability)));
        var decision = await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field("Postal"), Profile(Person), CancellationToken.None);
        Assert.Equal(expected, decision.Kind);
        Assert.Equal(FieldKeys.Zip, decision.Candidates[0].Key);
        Assert.Equal(0.8, FieldDecider.ConfidenceFloor);
        Assert.Equal(0.7, FieldDecider.ChoiceProbabilityFloor);
    }

    [Fact]
    public async Task AnswerWithoutProbabilitiesIsNeverPastedUnasked()
    {
        var decision = await new FieldDecider(LabelCache.Empty(), new FakeJev(_ => Pick(FieldKeys.Zip, 0.97)))
            .DecideAsync(Field("Postal"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.Picker, decision.Kind);
        Assert.Equal("07081", Assert.Single(decision.Candidates).Value);
    }

    [Fact]
    public async Task NoneMostLikelyWithLowConfidenceNeverOffersTheUsersValues()
    {
        // Jev's choice is the email, but "none" is more probable: an emergency contact's email is not the user's.
        var jev = new FakeJev(_ => Pick(FieldKeys.Email, 0.4, ("none", 0.45), (FieldKeys.Email, 0.4), (FieldKeys.Zip, 0.15)));
        var decision = await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field("Emergency contact email"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.PlainPaste, decision.Kind);
        Assert.Equal(DecisionReasons.NoneSelected, decision.Reason);
        Assert.Empty(decision.Candidates);
        Assert.Equal(0.4, decision.Confidence);

        // A tie with "none" goes to Jev's own choice.
        var tie = await new FieldDecider(LabelCache.Empty(), new FakeJev(_ => Pick(FieldKeys.Email, 0.3, ("none", 0.45), (FieldKeys.Email, 0.45))))
            .DecideAsync(Field("Email"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.Picker, tie.Kind);
    }

    [Fact]
    public async Task ExplicitDeadlineReachesJev()
    {
        var jev = new FakeJev(_ => Pick("none", 0.9));
        await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field("Email"), Profile(Person), TimeSpan.FromSeconds(4), CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(4), jev.LastDeadline);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var jev = new FakeJev(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FieldDecider(new LabelCache(null), jev).DecideAsync(Field("Email"), Profile(Person), CancellationToken.None));
    }

    // ---- Never asked ----

    [Theory]
    [InlineData("Password", null, null)]
    [InlineData("Card number", null, null)]
    [InlineData("Security code (CVV)", null, null)]
    [InlineData("SSN", null, null)]
    [InlineData("One-time code", null, null)]
    [InlineData("Field", "Enter your password", null)]
    [InlineData("Field", null, "cardNumber")]
    public async Task SensitiveFieldsPlainPasteWithoutAskingAnyone(string label, string? placeholder, string? id)
    {
        var field = new FieldContext(label, null, id, null, "Edit", false, []) { Placeholder = placeholder };
        await AssertNeverAsked(field);
    }

    [Theory]
    [InlineData("Card #")]
    [InlineData("Card no.")]
    [InlineData("Exp. date")]
    [InlineData("MM / YY")]
    [InlineData("Expires")]
    [InlineData("CID")]
    [InlineData("CVC2")]
    [InlineData("Card verification number")]
    [InlineData("PIN")]
    [InlineData("TIN")]
    [InlineData("EIN")]
    [InlineData("Taxpayer identification number")]
    [InlineData("Bank account")]
    [InlineData("Account #")]
    [InlineData("Routing #")]
    [InlineData("Sort code")]
    [InlineData("SWIFT/BIC")]
    [InlineData("IBAN")]
    [InlineData("National Insurance number")]
    [InlineData("Driver's license")]
    [InlineData("Driver’s license number")]
    [InlineData("Passport")]
    [InlineData("Enter the 6-digit code")]
    [InlineData("Confirmation code")]
    [InlineData("Security questions")]
    [InlineData("Security question 1")]
    [InlineData("What was the name of your first pet?")]
    [InlineData("In what city were you born?")]
    [InlineData("Mother's maiden name")]
    public async Task SensitiveLabelsAreNeverAsked(string label)
    {
        Assert.True(KeywordRules.IsSensitive(label));
        await AssertNeverAsked(Field(label));
    }

    [Theory]
    [InlineData("ccNumber")]
    [InlineData("cardnumber")]
    [InlineData("cc-exp")]
    [InlineData("cvv2")]
    [InlineData("pin")]
    [InlineData("dlNumber")]
    [InlineData("ssn")]
    [InlineData("iban")]
    [InlineData("billingcardnumber")]
    [InlineData("newPassword2")]
    public async Task SensitiveIdsAreNeverAsked(string id)
    {
        // Even with an ordinary visible label, a telling id is enough.
        await AssertNeverAsked(new FieldContext("Number", null, id, null, "Edit", false, []));
        await AssertNeverAsked(new FieldContext(null, null, id, null, "Edit", false, []));
    }

    [Fact]
    public async Task HelpTextNearbyTextAndSectionsCountForSecrets()
    {
        // A vague label with one-time-code help.
        await AssertNeverAsked(new FieldContext("Code", "Enter the 6-digit code we texted you", null, null, "Edit", false, []));
        // An accessible name of its own, but next to a "Card number" label.
        await AssertNeverAsked(new FieldContext("Number", null, null, null, "Edit", false, ["Card number"]));
        // A CVV hint, as nearby text or as help.
        await AssertNeverAsked(new FieldContext("Code", null, null, null, "Edit", false, ["3 digits on the back of your card"]));
        await AssertNeverAsked(new FieldContext("Code", "3 digits on the back of your card", null, null, "Edit", false, []));
        // Vague labels under a card, payment, bank, or verification heading.
        await AssertNeverAsked(Field("Number") with { Sections = ["Credit card"] });
        await AssertNeverAsked(Field("Code") with { Sections = ["Payment"] });
        await AssertNeverAsked(Field("Date") with { Sections = ["Card details", "Checkout"] });
        await AssertNeverAsked(Field("Number") with { Sections = ["Bank details"] });
        await AssertNeverAsked(Field("Code") with { Sections = ["Verify your phone"] });
        await AssertNeverAsked(new FieldContext(null, null, "input-7", null, "Edit", false, []) { Sections = ["Payment method"] });
        // A card-number pattern as the only hint.
        await AssertNeverAsked(new FieldContext(null, null, null, null, "Edit", false, []) { Placeholder = "1234 1234 1234 1234" });
        await AssertNeverAsked(new FieldContext(null, null, null, null, "Edit", false, []) { Placeholder = "XXX-XX-XXXX" });
        // Under a security-questions heading every answer is secret, whatever its label.
        await AssertNeverAsked(Field("Name") with { Sections = ["Security questions"] });
        await AssertNeverAsked(OcrOnly("Card number"));
    }

    [Theory]
    [InlineData("Account email")]
    [InlineData("Account name")]
    [InlineData("Company")]
    [InlineData("Name on card")]
    [InlineData("Cardholder name")]
    [InlineData("Card holder name")]
    [InlineData("Billing ZIP")]
    [InlineData("Preferred email")]
    [InlineData("Shipping address")]
    [InlineData("Date of birth")]
    public async Task OrdinaryLabelsAreStillAsked(string label)
    {
        Assert.False(KeywordRules.IsSensitive(label));
        var jev = new FakeJev(_ => Pick("none", 0.9));
        await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field(label), Profile(Person), CancellationToken.None);
        Assert.Equal(1, jev.Calls);
    }

    [Fact]
    public async Task AKnownLabelIsNotMadeSecretByItsHelpOrSection()
    {
        var jev = new FakeJev(_ => Pick("none", 0.9));
        var decider = new FieldDecider(LabelCache.Empty(), jev);
        await decider.DecideAsync(new FieldContext("Phone number", "We'll text a verification code to this number", null, null, "Edit", false, []), Profile(Person), CancellationToken.None);
        await decider.DecideAsync(Field("Name") with { Sections = ["Credit card"] }, Profile(Person), CancellationToken.None);
        await decider.DecideAsync(Field("Number") with { Sections = ["Contact details"] }, Profile(Person), CancellationToken.None);
        Assert.Equal(3, jev.Calls);
    }

    [Fact]
    public async Task PasswordFieldPlainPasteWithoutAsking()
    {
        var jev = new FakeJev(_ => Pick(FieldKeys.Email, 1));
        var decision = await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field("Email", password: true), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionReasons.Password, decision.Reason);
        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public async Task FieldWithNoLabelAndNoContextPlainPastes()
    {
        var jev = new FakeJev(_ => Pick("none", 0.9));
        var decider = new FieldDecider(LabelCache.Empty(), jev);
        var blank = new FieldContext(null, null, "input-15", null, "Edit", false, []);
        Assert.Equal(DecisionReasons.NoLabel, (await decider.DecideAsync(blank, Profile(Person), CancellationToken.None)).Reason);
        Assert.Equal(0, jev.Calls);

        // Sections alone are enough context to ask.
        await decider.DecideAsync(blank with { Sections = ["Shipping address"] }, Profile(Person), CancellationToken.None);
        Assert.Equal(1, jev.Calls);
    }

    [Fact]
    public async Task EmptyProfileIsAPlainPaste()
    {
        var jev = new FakeJev(_ => Pick("none", 0.9));
        var decision = await new FieldDecider(LabelCache.Empty(), jev).DecideAsync(Field("Email"), Profile(), CancellationToken.None);
        Assert.Equal(DecisionReasons.KeyAbsent, decision.Reason);
        Assert.Equal(0, jev.Calls);
    }

    // ---- Fallback when Jev is unavailable ----

    [Theory]
    [InlineData(ServiceFailure.Network)]
    [InlineData(ServiceFailure.Timeout)]
    [InlineData(ServiceFailure.Unauthorized)]
    [InlineData(ServiceFailure.BadResponse)]
    public async Task JevFailureFallsBackToTheBuiltInList(ServiceFailure failure)
    {
        var jev = new FakeJev(_ => throw new ServiceException(failure));
        var decision = await new FieldDecider(new LabelCache(null), jev).DecideAsync(Field("First Name"), Profile(Person), CancellationToken.None);

        Assert.Equal(DecisionKind.Value, decision.Kind);
        Assert.Equal("Jordan", decision.Value);
        Assert.Equal(DecisionSource.BuiltIn, decision.DecidedBy);
        Assert.Equal("builtin", decision.Source);
        Assert.Equal(failure, decision.JevFailure);
        Assert.Null(decision.Confidence);
    }

    [Theory]
    [InlineData("{\"choices\":[\"x\"]}", ServiceFailure.BadResponse)]
    [InlineData("[]", ServiceFailure.BadResponse)]
    [InlineData("{\"error\":{\"code\":402,\"message\":\"Insufficient credits\"}}", ServiceFailure.PaymentRequired)]
    public async Task MalformedOrRefusedJevAnswersStillFallBack(string body, ServiceFailure failure)
    {
        var jev = new JevClient(FakeHttp.Reply(HttpStatusCode.OK, body).Client(), new JevCredentials("ts", null));
        var decision = await new FieldDecider(new LabelCache(null), jev).DecideAsync(Field("First Name"), Profile(Person), CancellationToken.None);
        Assert.Equal("Jordan", decision.Value);
        Assert.Equal(DecisionSource.BuiltIn, decision.DecidedBy);
        Assert.Equal(failure, decision.JevFailure);
    }

    [Fact]
    public async Task UnreadableCharsetStillFallsBack()
    {
        var http = FakeHttp.Reply(_ => FakeHttp.ResponseWithCharset("{}", "no-such-charset"));
        var decision = await new FieldDecider(new LabelCache(null), new JevClient(http.Client(), new JevCredentials("ts", null)))
            .DecideAsync(Field("First Name"), Profile(Person), CancellationToken.None);
        Assert.Equal("Jordan", decision.Value);
        Assert.Equal(ServiceFailure.BadResponse, decision.JevFailure);
    }

    [Fact]
    public async Task NoKeyUsesLearnedThenRules()
    {
        var cache = LabelCache.Empty();
        cache.Set("mystery postal entry", FieldKeys.Zip);
        var decider = new FieldDecider(cache, null);

        var learned = await decider.DecideAsync(Field("Mystery postal entry"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionSource.Learned, learned.DecidedBy);
        Assert.Equal("07081", learned.Value);
        Assert.Equal(ServiceFailure.NoKey, learned.JevFailure);

        var rules = await decider.DecideAsync(Field("given applicant name"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionSource.Rules, rules.DecidedBy);
        Assert.Equal("rules", rules.Source);
        Assert.Equal("Jordan", rules.Value);
    }

    [Fact]
    public async Task FallbackNamesTheEntityFromLabelOrSection()
    {
        var decider = new FieldDecider(new LabelCache(null), null);
        var byLabel = await decider.DecideAsync(Field("Billing address"), Addresses, CancellationToken.None);
        Assert.Equal("1 Main St", byLabel.Value);
        Assert.Equal(DecisionReasons.EntityMatched, byLabel.Reason);

        var bySection = await decider.DecideAsync(Field("Street address") with { Sections = ["Shipping details"] }, Addresses, CancellationToken.None);
        Assert.Equal("2 Oak Ave", bySection.Value);

        var byDelivery = await decider.DecideAsync(Field("Street address") with { Sections = ["Delivery"] }, Addresses, CancellationToken.None);
        Assert.Equal("2 Oak Ave", byDelivery.Value);

        var ambiguous = await decider.DecideAsync(Field("Street address"), Addresses, CancellationToken.None);
        Assert.Equal(DecisionKind.Picker, ambiguous.Kind);
        Assert.Equal(DecisionReasons.MultipleValues, ambiguous.Reason);
        Assert.Equal(2, ambiguous.Candidates.Count);
    }

    [Fact]
    public async Task FallbackOutcomesForMissingAndUnknown()
    {
        var decider = new FieldDecider(new LabelCache(null), null);
        Assert.Equal(DecisionReasons.KeyAbsent, (await decider.DecideAsync(Field("Date of birth"), Profile(Person), CancellationToken.None)).Reason);
        Assert.Equal(DecisionReasons.NoMatch, (await decider.DecideAsync(Field("Favourite colour"), Profile(Person), CancellationToken.None)).Reason);
        var someoneElse = Field("Phone") with { Sections = ["Emergency contact"] };
        Assert.Equal(DecisionReasons.NoMatch, (await decider.DecideAsync(someoneElse, Profile(Person), CancellationToken.None)).Reason);
    }

    [Theory]
    [InlineData("Spouse first name")]
    [InlineData("Emergency phone")]
    [InlineData("Guardian email")]
    [InlineData("Partner's email")]
    [InlineData("Parent / guardian phone")]
    [InlineData("Next of kin email")]
    [InlineData("Referee email")]
    [InlineData("Manager email")]
    [InlineData("Child's first name")]
    [InlineData("Dependent ZIP code")]
    [InlineData("Beneficiary state")]
    public async Task FallbackNeverFillsSomeoneElsesFieldWithTheUsersDetails(string label)
    {
        var cache = new LabelCache(null);
        var decision = await new FieldDecider(cache, null).DecideAsync(Field(label), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.PlainPaste, decision.Kind);
        Assert.Equal(DecisionReasons.NoMatch, decision.Reason);
    }

    [Fact]
    public async Task PreferredIsNotAReferral()
    {
        var decider = new FieldDecider(new LabelCache(null), null);
        var preferred = await decider.DecideAsync(Field("Preferred email:"), Profile(Person), CancellationToken.None);
        Assert.Equal("v@example.com", preferred.Value);
        var inSection = await decider.DecideAsync(Field("Email") with { Sections = ["Preferred contact"] }, Profile(Person), CancellationToken.None);
        Assert.Equal("v@example.com", inSection.Value);
        var referral = await decider.DecideAsync(Field("Email") with { Sections = ["Referral"] }, Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionReasons.NoMatch, referral.Reason);
    }

    [Fact]
    public async Task FallbackCanBeTurnedOff()
    {
        var jev = new FakeJev(_ => throw new ServiceException(ServiceFailure.Network));
        var decision = await new FieldDecider(new LabelCache(null), jev, useLocalRules: false).DecideAsync(Field("First Name"), Profile(Person), CancellationToken.None);
        Assert.Equal(DecisionKind.PlainPaste, decision.Kind);
        Assert.Equal(DecisionReasons.JevUnavailable, decision.Reason);
        Assert.Equal(ServiceFailure.Network, decision.JevFailure);
    }

    // ---- Learning ----

    [Fact]
    public async Task IdOnlyFieldsTeachNothing()
    {
        var cache = LabelCache.Empty();
        var jev = new FakeJev(_ => Pick(FieldKeys.Zip, 0.99, (FieldKeys.Zip, 0.99)));
        var field = new FieldContext(null, null, "shippingPostal", null, "Edit", false, []);
        var decision = await new FieldDecider(cache, jev).DecideAsync(field, Profile(Person), CancellationToken.None);
        Assert.Equal("07081", decision.Value);
        Assert.Empty(cache.Learned);

        new FieldDecider(cache, jev).LearnChoice(field, new Candidate(FieldKeys.Zip, "person", "07081"));
        Assert.Empty(cache.Learned);
    }

    [Fact]
    public void PickerChoicesTeachReadableLabels()
    {
        var cache = LabelCache.Empty();
        var field = new FieldContext(null, null, "input-15", null, "Edit", false, ["Postal / ZIP"]);
        new FieldDecider(cache, null).LearnChoice(field, new Candidate(FieldKeys.Zip, "person", "07081"));
        Assert.Equal([new LearnedLabel("postal zip", FieldKeys.Zip)], cache.Learned);

        // The overload with the profile learns the same way.
        var other = new FieldContext(null, null, "input-16", null, "Edit", false, ["Your postal area"]);
        new FieldDecider(cache, null).LearnChoice(other, new Candidate(FieldKeys.Zip, "person", "07081"), Profile(Person));
        Assert.Equal(FieldKeys.Zip, cache.Learned.Single(l => l.Label == "your postal area").Key);
    }

    [Fact]
    public async Task OcrLabelsAreNeverLearned()
    {
        // OCR read "Jordan", another field's value printed left of this unlabeled field.
        var cache = LabelCache.Empty();
        var field = new FieldContext(null, null, null, null, "Edit", false, [], OcrText: "Jordan");
        var jev = new FakeJev(_ => Pick(FieldKeys.FirstName, 0.95, (FieldKeys.FirstName, 0.96)));
        var decision = await new FieldDecider(cache, jev).DecideAsync(field, Profile(Person), CancellationToken.None);
        Assert.Equal("Jordan", decision.Value);
        new FieldDecider(cache, null).LearnChoice(field, new Candidate(FieldKeys.FirstName, "person", "Jordan"), Profile(Person));
        Assert.Empty(cache.Learned);
    }

    [Fact]
    public async Task LabelsThatAreTheBlocksValuesAreNeverLearned()
    {
        var cache = LabelCache.Empty();
        // Nearby text that is the user's own last name and email, next to unlabeled fields.
        var nearName = new FieldContext(null, null, null, null, "Edit", false, ["Rivera"]);
        var nearEmail = new FieldContext(null, null, null, null, "Edit", false, ["Signed in as v@example.com"]);
        new FieldDecider(cache, null).LearnChoice(nearName, new Candidate(FieldKeys.LastName, "person", "Rivera"), Profile(Person));
        new FieldDecider(cache, null).LearnChoice(nearEmail, new Candidate(FieldKeys.FirstName, "person", "Jordan"), Profile(Person));
        // Without the profile, the chosen value itself is still kept out.
        new FieldDecider(cache, null).LearnChoice(nearName, new Candidate(FieldKeys.LastName, "person", "Rivera"));
        // Jev's confident answers too.
        var jev = new FakeJev(_ => Pick(FieldKeys.LastName, 0.95, (FieldKeys.LastName, 0.96)));
        await new FieldDecider(cache, jev).DecideAsync(nearName, Profile(Person), CancellationToken.None);
        Assert.Empty(cache.Learned);
    }

    [Fact]
    public void AFieldThatIsSensitiveByItsContextTeachesNothing()
    {
        // The picker shortcut works in any field; a pick in a card field must not teach its label.
        var cache = LabelCache.Empty();
        var field = new FieldContext("Holder", null, null, null, "Edit", false, []) { Sections = ["Credit card"] };
        Assert.True(LabelCache.IsLearnable("Holder")); // the label alone would be learned
        new FieldDecider(cache, null).LearnChoice(field, new Candidate(FieldKeys.FullName, "person", "Jordan Rivera"), Profile(Person));
        Assert.Empty(cache.Learned);
    }

    [Fact]
    public async Task JevAnswersThatDependOnTheFormOrAgreeWithTheBuiltInListAreNotLearned()
    {
        var company = Profile(Person, new Entity("company", new Dictionary<string, string> { [FieldKeys.Company] = "Acme Corp" }));
        var cache = new LabelCache(null);
        var jev = new FakeJev(question => Pick(question.Options.First(o => o.Candidate?.Key == FieldKeys.Company).Name, 0.95,
            (question.Options.First(o => o.Candidate?.Key == FieldKeys.Company).Name, 0.96)));
        // "Name" under "Company details" is the company's name: right here, wrong on the next form.
        var decision = await new FieldDecider(cache, jev).DecideAsync(Field("Name") with { Sections = ["Company details"] }, company, CancellationToken.None);
        Assert.Equal("Acme Corp", decision.Value);
        Assert.Empty(cache.Learned);
        // A later offline "Name" is still the person's full name, from the built-in list.
        Assert.True(cache.TryGet("name", out var key, out var learned) && !learned && key == FieldKeys.FullName);

        // An answer that agrees with the built-in list adds nothing.
        var agrees = new FakeJev(_ => Pick(FieldKeys.FirstName, 0.95, (FieldKeys.FirstName, 0.96)));
        await new FieldDecider(cache, agrees).DecideAsync(Field("First name"), Profile(Person), CancellationToken.None);
        // A vague one-word label could mean anything on the next form.
        var vague = new FakeJev(_ => Pick(FieldKeys.Zip, 0.95, (FieldKeys.Zip, 0.96)));
        await new FieldDecider(cache, vague).DecideAsync(Field("Code"), Profile(Person), CancellationToken.None);
        Assert.Empty(cache.Learned);
    }

    // ---- Picker shortcut ----

    [Fact]
    public void AllCandidatesListsEveryDistinctValueStandardKeysFirst()
    {
        var profile = Profile(
            new Entity("person", new Dictionary<string, string> { ["favorite_color"] = "Teal", [FieldKeys.Email] = "v@example.com", [FieldKeys.State] = "NJ" }),
            new Entity("work", new Dictionary<string, string> { [FieldKeys.Email] = "V@example.com", [FieldKeys.FirstName] = "Jordan" }));

        var raw = FieldDecider.AllCandidates(profile, null);
        Assert.Equal([FieldKeys.FirstName, FieldKeys.State, FieldKeys.Email, "favorite_color"], raw.Select(c => c.Key));
        Assert.Equal("NJ", raw[1].Value);

        var formatted = FieldDecider.AllCandidates(profile, Field("State", control: "ComboBox"));
        Assert.Equal("New Jersey", formatted[1].Value);
    }

    private static FieldContext OcrOnly(string text) => new(null, null, null, null, "Edit", false, [], OcrText: text);

    private static async Task AssertNeverAsked(FieldContext field)
    {
        var jev = new FakeJev(_ => throw new InvalidOperationException("must not be called"));
        var cache = LabelCache.Empty();
        var decision = await new FieldDecider(cache, jev).DecideAsync(field, Profile(Person), CancellationToken.None);

        Assert.Equal(DecisionKind.PlainPaste, decision.Kind);
        Assert.Equal(DecisionReasons.SensitiveLabel, decision.Reason);
        Assert.Equal(DecisionSource.None, decision.DecidedBy);
        Assert.Equal(0, jev.Calls);
        Assert.Empty(cache.Learned);
    }

    private sealed class FakeJev(Func<JevQuestion, JevChoice> answer) : IJevChooser
    {
        public int Calls { get; private set; }
        public JevQuestion? LastQuestion { get; private set; }
        public TimeSpan LastDeadline { get; private set; }

        public Task<JevChoice> ChooseAsync(JevQuestion question, TimeSpan deadline, CancellationToken ct)
        {
            Calls++;
            LastQuestion = question;
            LastDeadline = deadline;
            return Task.FromResult(answer(question));
        }
    }
}
