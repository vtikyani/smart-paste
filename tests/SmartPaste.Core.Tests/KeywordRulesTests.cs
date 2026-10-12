using SmartPaste.Core;

public class KeywordRulesTests
{
    [Theory]
    // A specific kind of value wins over a broader word in the same label.
    [InlineData("Website address", FieldKeys.Website)]
    [InlineData("Web address", FieldKeys.Website)]
    [InlineData("Company website", FieldKeys.Website)]
    [InlineData("Postal address", FieldKeys.Street)]
    [InlineData("Postal", FieldKeys.Zip)]
    [InlineData("Postal / ZIP", FieldKeys.Zip)]
    [InlineData("Pincode", FieldKeys.Zip)]
    [InlineData("Company email", FieldKeys.Email)]
    [InlineData("Company phone", FieldKeys.Phone)]
    [InlineData("Employer", FieldKeys.Company)]
    [InlineData("Employer name", FieldKeys.Company)]
    [InlineData("Organisation", FieldKeys.Company)]
    [InlineData("Email address", FieldKeys.Email)]
    [InlineData("LinkedIn URL", FieldKeys.Linkedin)]
    [InlineData("Birth year", FieldKeys.DateOfBirth)]
    [InlineData("Preferred email", FieldKeys.Email)]
    [InlineData("Preferred phone number", FieldKeys.Phone)]
    [InlineData("Card holder name", FieldKeys.FullName)]
    public void SpecificLabelsMatchTheirKey(string label, string expected) => Assert.Equal(expected, KeywordRules.Match(label));

    [Theory]
    // A birthplace is neither the user's city nor their birth date.
    [InlineData("Place of birth")]
    [InlineData("City of birth")]
    [InlineData("Country of birth")]
    // An employer's details are the employer's.
    [InlineData("Employer phone")]
    [InlineData("Employer address")]
    [InlineData("Employer email")]
    // Someone else's details.
    [InlineData("Spouse first name")]
    [InlineData("Emergency phone")]
    [InlineData("Guardian email")]
    [InlineData("Referral source")]
    [InlineData("Referrer name")]
    [InlineData("Referred by")]
    [InlineData("References")]
    [InlineData("Supervisor phone")]
    // Secrets.
    [InlineData("Card number")]
    [InlineData("Account #")]
    public void LabelsWithNoValueInTheBlockMatchNothing(string label) => Assert.Null(KeywordRules.Match(label));

    [Theory]
    [InlineData("Spouse", true)]
    [InlineData("Partner's email", true)]
    [InlineData("Emergency contact", true)]
    [InlineData("Parent / guardian", true)]
    [InlineData("Next of kin", true)]
    [InlineData("Reference", true)]
    [InlineData("Referee", true)]
    [InlineData("Referral", true)]
    [InlineData("Referrer", true)]
    [InlineData("Manager", true)]
    [InlineData("Supervisor", true)]
    [InlineData("Recruiter", true)]
    [InlineData("Beneficiary", true)]
    [InlineData("Children", true)]
    [InlineData("Child's name", true)]
    [InlineData("Dependents", true)]
    [InlineData("Co-applicant", true)]
    // "preferred" holds the letters of "referr" but is the user's own choice.
    [InlineData("Preferred email:", false)]
    [InlineData("Preferred contact", false)]
    [InlineData("Preferred name", false)]
    [InlineData("Contact information", false)]
    [InlineData("Personal details", false)]
    public void SomeoneElseIsMatchedAsWholeWords(string text, bool expected) =>
        Assert.Equal(expected, KeywordRules.IsAboutSomeoneElse(text));

    [Theory]
    [InlineData("Account email", false)]
    [InlineData("Account name", false)]
    [InlineData("Account type", false)]
    [InlineData("Account", true)]
    [InlineData("Acct #", true)]
    [InlineData("Routing", true)]
    [InlineData("ABA routing number", true)]
    [InlineData("Company", false)]
    [InlineData("Shipping", false)]
    [InlineData("Pincode", false)]
    [InlineData("PIN code", true)]
    [InlineData("Name on credit card", false)]
    [InlineData("Credit card", true)]
    [InlineData("Gift card", true)]
    [InlineData("Valid thru", true)]
    [InlineData("Last 4 of SSN", true)]
    [InlineData("Social Insurance Number", true)]
    [InlineData("One-time password", true)]
    [InlineData("Two-factor code", true)]
    [InlineData("Authenticator app code", true)]
    [InlineData("Memorable word", true)]
    [InlineData("Date of birth (MM/DD/YYYY)", false)]
    [InlineData("Expiration (MM/YY)", true)]
    public void SensitiveLabels(string label, bool expected) => Assert.Equal(expected, KeywordRules.IsSensitive(label));

    [Fact]
    public void FieldLevelCheckReadsEveryTextSource()
    {
        static FieldContext Field(string? name = null, string? help = null, string? id = null, string[]? siblings = null,
            string? ocr = null, string[]? sections = null, string? placeholder = null) =>
            new(name, help, id, null, "Edit", false, siblings ?? [], ocr) { Sections = sections ?? [], Placeholder = placeholder };

        Assert.True(KeywordRules.IsSensitive(Field(name: "Code", help: "Enter the 6-digit code we texted you")));
        Assert.True(KeywordRules.IsSensitive(Field(name: "Number", siblings: ["Card number"])));
        Assert.True(KeywordRules.IsSensitive(Field(name: "Value", ocr: "IBAN")));
        Assert.True(KeywordRules.IsSensitive(Field(name: "Number", sections: ["Credit card", "Payment"])));
        Assert.True(KeywordRules.IsSensitive(Field(name: "Enter code", sections: ["Two-factor authentication"])));
        Assert.True(KeywordRules.IsSensitive(Field(name: "Answer", sections: ["Security questions"])));
        Assert.True(KeywordRules.IsSensitive(Field(name: "Number", id: "ccnum")));
        Assert.True(KeywordRules.IsSensitive(Field(placeholder: "1234 1234 1234 1234")));
        Assert.True(KeywordRules.IsSensitive(Field(placeholder: "3782 822463 10005")));

        Assert.False(KeywordRules.IsSensitive(Field(name: "Phone", help: "We'll text a verification code to this number")));
        Assert.False(KeywordRules.IsSensitive(Field(name: "Name", sections: ["Credit card"])));
        Assert.False(KeywordRules.IsSensitive(Field(name: "Billing ZIP", sections: ["Payment"])));
        Assert.False(KeywordRules.IsSensitive(Field(name: "Number", sections: ["Contact details"])));
        Assert.False(KeywordRules.IsSensitive(Field(sections: ["Shipping address"])));
        Assert.False(KeywordRules.IsSensitive(Field(placeholder: "(555) 555-5555")));
        Assert.False(KeywordRules.IsSensitive(Field(placeholder: "12345-6789")));
    }
}
