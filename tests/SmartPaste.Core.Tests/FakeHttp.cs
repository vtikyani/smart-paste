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

    public HttpClient Client() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new Request(request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString(), headers));
        return await respond(request, body, cancellationToken);
    }
}
