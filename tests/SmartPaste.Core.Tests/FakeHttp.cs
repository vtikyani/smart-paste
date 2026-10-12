using System.Net;
using System.Text;

// Stands in for the network: records each request and answers it from a function. Tests never make real calls.
internal sealed class FakeHttp(Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public sealed record Request(HttpMethod Method, string Url, string? Body, string? Authorization, IReadOnlyDictionary<string, string> Headers);

    public List<Request> Requests { get; } = [];

    public static FakeHttp Reply(HttpStatusCode status, string body = "{}") =>
        new((_, _, _) => Task.FromResult(Response(status, body)));

    public static FakeHttp Reply(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new((request, _, _) => Task.FromResult(respond(request)));

    public static HttpResponseMessage Response(HttpStatusCode status, string body = "{}") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // A 200 whose Content-Type names a charset .NET cannot decode, as a misconfigured server might send.
    public static HttpResponseMessage ResponseWithCharset(string body, string charset)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.TryAddWithoutValidation("Content-Type", $"application/json; charset={charset}");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    public HttpClient Client() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new Request(request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString(), headers));
        return await respond(request, body, cancellationToken);
    }
}
