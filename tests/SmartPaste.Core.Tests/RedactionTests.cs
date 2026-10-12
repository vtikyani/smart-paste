using System.Diagnostics;
using SmartPaste.Core;

public class RedactionTests
{
    [Theory]
    [InlineData("Jordan Rivera, 123 Main St Apt 4B, Springfield NJ 07081, v@example.com, 201-555-0100")]
    [InlineData("Jordan Rivera\r\n123 Main St\r\nSpringfield, NJ 07081\r\n(201) 555-0100\r\n")]
    [InlineData("Name: Jordan Rivera | Email: v@example.com\nName on card: Jordan Rivera\nAccount email: v@example.com")]
    [InlineData("Taylor Swift\ntaylor@example.com\n+1 415 555 0123")]
    [InlineData("Apt 4B\nSuite 200\nPO Box 12\nOrder #12345\nTel 201 555 0100\nCard holder: Jordan Rivera")]
    [InlineData("Born 1990-03-04, ZIP 07081-1234 201-555-0100, +86 138 0013 8000, +44 20 7946 0958")]
    public void OrdinaryTextIsUnchanged(string text) => Assert.Equal(text, Redaction.Redact(text));

    [Theory]
    [InlineData("Password: hunter2")]
    [InlineData("Card #: 4111 1111 1111 1111")]
    [InlineData("Card number (Visa): 4111111111111111")]
    [InlineData("Acct: 000123456789")]
    [InlineData("Acct. No.: 000123456789")]
    [InlineData("Account #: 1234567890")]
    [InlineData("Account number: 123456789")]
    [InlineData("Bank account\t123456789")]
    [InlineData("Routing: 021000021")]
    [InlineData("Routing number: 021000021")]
    [InlineData("IBAN: DE89 3704 0044 0532 0130 00")]
    [InlineData("SWIFT/BIC: DEUTDEFF")]
    [InlineData("Sort code: 12-34-56")]
    [InlineData("SSN: 123-45-6789")]
    [InlineData("PIN: 1234")]
    [InlineData("Security code: 737")]
    [InlineData("CVV: 123")]
    [InlineData("Exp: 12/28")]
    [InlineData("Driver’s license: D1234567")]
    [InlineData("Passport no.: X1234567")]
    [InlineData("One-time code: 482913")]
    [InlineData("2FA code: 482913")]
    [InlineData("Security answer: Fluffy")]
    [InlineData("Mother's maiden name: Smith")]
    [InlineData("Gate code: #1234")]
    [InlineData("API key: sk-test-abc123")]
    public void LabeledSecretsAreRemoved(string line)
    {
        var redacted = Redaction.Redact("Jordan Rivera\n" + line + "\nv@example.com");
        Assert.Equal("Jordan Rivera\n\nv@example.com", redacted);
    }

    [Fact]
    public void OnlyTheSecretPartOfASeparatedLineIsRemoved()
    {
        Assert.Equal("Name: Jordan Rivera | Email: v@example.com",
            Redaction.Redact("Name: Jordan Rivera | SSN: 123-45-6789 | Email: v@example.com"));
        Assert.Equal("Name: Jordan Rivera", Redaction.Redact("Name: Jordan Rivera • Acct # 12345678"));
    }

    [Theory]
    [InlineData("Account number\n123456789")]
    [InlineData("Routing number:\n021000021")]
    [InlineData("Bank Account Number\n\n1234 5678 9012")]
    [InlineData("Password\nhunter2")]
    [InlineData("Security code*\n737")]
    public void ALoneLabelTakesTheValueBelowWithIt(string pair)
    {
        var redacted = Redaction.Redact("v@example.com\n" + pair + "\n201-555-0100");
        Assert.DoesNotContain(pair.Split('\n')[^1], redacted, StringComparison.Ordinal);
        Assert.StartsWith("v@example.com\n", redacted, StringComparison.Ordinal);
        Assert.EndsWith("\n201-555-0100", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(pair.Split('\n')[0], redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoneLabelLeavesALabeledLineBelowIt() =>
        Assert.Equal("\nEmail: v@example.com", Redaction.Redact("Password\nEmail: v@example.com"));

    [Theory]
    [InlineData("Routing 021000021")]
    [InlineData("Acct # 1234-5678")]
    [InlineData("PIN 1234")]
    [InlineData("CVV2 123")]
    [InlineData("Account No 000123456789")]
    public void ALabelAndANumberWithoutAColonAreRemoved(string line) =>
        Assert.Equal("Jordan Rivera\n", Redaction.Redact("Jordan Rivera\n" + line));

    [Theory]
    [InlineData("My card is 4111 1111 1111 1111, thanks", "My card is [removed], thanks")]
    [InlineData("4111-1111-1111-1111", "[removed]")]
    [InlineData("4111111111111111 exp 12/28", "[removed] exp 12/28")]
    [InlineData("Amex 3782 822463 10005 please", "Amex [removed] please")]
    [InlineData("Diners 3056 930902 5904", "Diners [removed]")]
    [InlineData("4111 1111 1111 1111 123", "[removed] 123")]
    [InlineData("Wire 6221 2600 0000 0000 001 now", "Wire [removed] now")]
    [InlineData("SSN 123-45-6789 on file", "SSN [removed] on file")]
    [InlineData("Pay DE89 3704 0044 0532 0130 00 today", "Pay [removed] today")]
    [InlineData("Pay GB82WEST12345698765432.", "Pay [removed].")]
    [InlineData("iban de89370400440532013000", "iban [removed]")]
    public void SecretNumbersAreMaskedAnywhere(string text, string expected) =>
        Assert.Equal(expected, Redaction.MaskNumbers(text));

    [Theory]
    [InlineData("4111 1111 1111 1112")]
    [InlineData("201-555-0100 201-555-0199")]
    [InlineData("+4111111111111111")]
    [InlineData("Springfield, NJ 07081 201-555-0100")]
    [InlineData("123-456-7890 and 12-345-6789")]
    [InlineData("DE89 3704 0044 0532 0130 02")]
    [InlineData("Order 2026-10-12 #1234567890123")]
    [InlineData("+86 138 0013 8000")]
    public void LookalikesAreNotMasked(string text) => Assert.Equal(text, Redaction.MaskNumbers(text));

    [Fact]
    public void TheMaskCanBeBlank() =>
        Assert.Equal("Paid with \nv@example.com", Redaction.Redact("Paid with 4111 1111 1111 1111\nv@example.com", ""));

    [Theory]
    [InlineData("Card #", true)]
    [InlineData("acct", true)]
    [InlineData("Account no.", true)]
    [InlineData("Routing", true)]
    [InlineData("IBAN", true)]
    [InlineData("SSN", true)]
    [InlineData("PIN", true)]
    [InlineData("security code", true)]
    [InlineData("account number", true)]
    [InlineData("routing number", true)]
    [InlineData("Name on card", false)]
    [InlineData("Cardholder name", false)]
    [InlineData("Account email", false)]
    [InlineData("Account holder", false)]
    [InlineData("Email", false)]
    [InlineData("Phone", false)]
    [InlineData("Order #", false)]
    [InlineData("Experience", false)]
    [InlineData("Apt", false)]
    public void SensitiveLabels(string label, bool sensitive) => Assert.Equal(sensitive, Redaction.IsSensitiveLabel(label));

    [Fact]
    public void SensitiveNumbersInValues()
    {
        Assert.True(Redaction.HasSensitiveNumber("4111 1111 1111 1111"));
        Assert.True(Redaction.HasSensitiveNumber("SSN 123-45-6789", cards: false));
        Assert.True(Redaction.HasSensitiveNumber("DE89370400440532013000", cards: false));
        Assert.False(Redaction.HasSensitiveNumber("4111111111111111", cards: false));
        Assert.False(Redaction.HasSensitiveNumber("201-555-0100"));
        Assert.False(Redaction.HasSensitiveNumber("123 Main St Apt 4B"));
    }

    [Theory]
    [InlineData('1')]
    [InlineData(' ')]
    [InlineData('-')]
    [InlineData('A')]
    public void AdversarialInputReturns(char filler)
    {
        foreach (var text in new[]
                 {
                     new string(filler, TextHeuristics.MaxChars),
                     string.Concat(Enumerable.Repeat(filler + "1 ", TextHeuristics.MaxChars / 3)),
                     string.Concat(Enumerable.Repeat("DE89 " + filler, TextHeuristics.MaxChars / 6)),
                     "Password:" + new string(filler, TextHeuristics.MaxChars - 9)
                 })
        {
            var clock = Stopwatch.StartNew();
            var redacted = Redaction.Redact(text);
            Assert.NotNull(redacted);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{clock.Elapsed} for filler '{filler}'");
        }
    }
}
