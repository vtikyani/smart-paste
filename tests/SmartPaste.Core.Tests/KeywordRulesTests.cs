using SmartPaste.Core;

public class KeywordRulesTests
{
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
