using System.Net;
using System.Net.Http;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;

namespace SmartPaste.App.Tests.Onboarding;

// The practice engine works on the tour's own sample and nothing else, and not after the tour has closed.
public sealed class PracticeEngineTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "sp-practice-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    // No decision service: anything that got past the sample check would fail here.
    private PracticeEngine Engine(HttpMessageHandler handler) =>
        new(new HttpClient(handler), decisions: null!, new SettingsStore(_settingsPath));

    [Fact]
    public async Task OtherTextIsNeverParsedOrSent()
    {
        var network = new CountingHandler();
        using var engine = Engine(network);

        Assert.Null(await engine.PasteIntoAsync("Email address", "Jane Doe\njane@corp.example\n(555) 010-0199", CancellationToken.None));
        Assert.Null(await engine.PasteIntoAsync("API key", "sk-or-v1-5f2b8c0e9d7a41f3b6c2e8d0a9f1b3c5", CancellationToken.None));
        Assert.Null(await engine.PasteIntoAsync("First name", "", CancellationToken.None));

        Assert.Equal(0, network.Requests);
    }

    [Fact]
    public async Task AClosedTourIsNotPractisedOn()
    {
        var engine = Engine(new CountingHandler());
        engine.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.PasteIntoAsync("First name", PracticeSample.Text, CancellationToken.None));
    }

    [Theory]
    [InlineData(PracticeSample.Text, true)]
    [InlineData("  Jordan Rivera\r\njordan.rivera@example.com\r\n(201) 555-0143\r\n48 Maple Avenue\r\nHoboken, NJ 07030\r\n", true)]
    [InlineData("Jordan Rivera", false)]
    [InlineData(PracticeSample.Text + "\nmore", false)]
    [InlineData(null, false)]
    public void OnlyTheSampleCounts(string? text, bool isSample) => Assert.Equal(isSample, PracticeSample.Is(text));

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
