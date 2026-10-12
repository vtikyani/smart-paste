using SmartPaste.Core;

public class JevResponseTests
{
    // Captured from both routes (TypeSafe direct, then OpenRouter), with the question id renamed to ours.
    private const string TypeSafe = "{\"model\":\"jev-1.13.0\",\"answers\":{\"field_value\":{\"type\":\"choice\",\"choice\":\"zip\",\"confidence\":1.0,\"probabilities\":{\"city\":0.0,\"none\":0.0,\"zip\":1.0,\"state\":0.0}}},\"usage\":{\"input_tokens\":374,\"output_tokens\":45}}";
    private const string OpenRouter = "{\"model\":\"typesafe/jev-1.13-20260917\",\"answers\":{\"field_value\":{\"type\":\"choice\",\"choice\":\"zip\",\"probabilities\":{\"none\":0,\"city\":0,\"zip\":1,\"state\":0},\"confidence\":1}},\"usage\":{\"input_tokens\":374,\"output_tokens\":45,\"cost\":0.000015708},\"id\":\"gen-dec-1\",\"provider\":\"TypeSafe\"}";

    [Fact]
    public void CapturedResponsesParse()
    {
        var direct = JevClient.ParseResponse(TypeSafe, JevEndpoint.TypeSafe);
        Assert.Equal("zip", direct.Choice);
        Assert.Equal(1.0, direct.Confidence);
        Assert.Equal(JevEndpoint.TypeSafe, direct.Route);
        Assert.Equal(4, direct.Probabilities.Count);
        Assert.Equal("zip", JevClient.ParseResponse(OpenRouter, JevEndpoint.OpenRouter).Choice);
    }

    [Fact]
    public void MissingChoiceUsesTheMostProbableOption()
    {
        var result = JevClient.ParseResponse("{\"answers\":{\"field_value\":{\"probabilities\":{\"city\":0.2,\"zip\":0.7,\"none\":0.1},\"confidence\":0.55}}}", JevEndpoint.TypeSafe);
        Assert.Equal("zip", result.Choice);
        Assert.Equal(0.55, result.Confidence);
    }

    [Fact]
    public void MissingConfidenceUsesTypeSafesFormulaNotTheRawProbability()
    {
        // Two options at 0.8 / 0.2: an even spread would be 0.5, so confidence is (0.8 - 0.5) / (1 - 0.5) = 0.6.
        var result = JevClient.ParseResponse("{\"answers\":{\"field_value\":{\"probabilities\":{\"city\":0.2,\"zip\":0.8}}}}", JevEndpoint.TypeSafe);
        Assert.Equal("zip", result.Choice);
        Assert.Equal(0.6, result.Confidence, 6);
        Assert.Equal(1.0, JevClient.ChoiceConfidence(1.0, 4));
        Assert.Equal(0.0, JevClient.ChoiceConfidence(0.25, 4));
        Assert.Equal(0.4, JevClient.ChoiceConfidence(0.6, 3), 6);
    }

    [Fact]
    public void ChoiceMustBeOneOfTheOptionsSent()
    {
        const string json = "{\"answers\":{\"field_value\":{\"choice\":\"phone\",\"confidence\":0.9,\"probabilities\":{\"phone\":0.95,\"zip\":0.05}}}}";
        var error = Assert.Throws<ServiceException>(() => JevClient.ParseResponse(json, JevEndpoint.OpenRouter, ["zip", "none"]));
        Assert.Equal(ServiceFailure.BadResponse, error.Failure);
        Assert.Equal("phone", JevClient.ParseResponse(json, JevEndpoint.OpenRouter, ["phone", "zip", "none"]).Choice);
    }

    [Fact]
    public void ProbabilitiesForUnknownOptionsAreIgnored()
    {
        const string json = "{\"answers\":{\"field_value\":{\"choice\":\"zip\",\"confidence\":0.9,\"probabilities\":{\"zip\":0.9,\"ghost\":0.1}}}}";
        var result = JevClient.ParseResponse(json, JevEndpoint.TypeSafe, ["zip", "none"]);
        Assert.Equal(["zip"], result.Probabilities.Keys);
    }

    [Fact]
    public void SingleAnswerUnderAnotherIdIsAccepted() =>
        Assert.Equal("zip", JevClient.ParseResponse("{\"answers\":{\"pick\":{\"choice\":\"zip\",\"confidence\":0.9}}}", JevEndpoint.TypeSafe).Choice);

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"answers\":{}}")]
    [InlineData("{\"answers\":{\"a\":{\"choice\":\"x\",\"confidence\":1},\"b\":{\"choice\":\"y\",\"confidence\":1}}}")]
    [InlineData("{\"answers\":{\"field_value\":{\"confidence\":0.9}}}")]
    [InlineData("{\"answers\":{\"field_value\":{\"choice\":\"zip\"}}}")]
    [InlineData("{\"error\":{\"code\":400,\"message\":\"bad\"}}")]
    public void UnusableBodiesAreBadResponses(string json)
    {
        var error = Assert.Throws<ServiceException>(() => JevClient.ParseResponse(json, JevEndpoint.TypeSafe));
        Assert.Equal(ServiceFailure.BadResponse, error.Failure);
        Assert.DoesNotContain("bad", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutOfRangeNumbersAreClamped()
    {
        var result = JevClient.ParseResponse("{\"answers\":{\"field_value\":{\"choice\":\"zip\",\"confidence\":1.7,\"probabilities\":{\"zip\":1.2}}}}", JevEndpoint.TypeSafe);
        Assert.Equal(1.0, result.Confidence);
        Assert.Equal(1.0, result.Probabilities["zip"]);
    }
}
