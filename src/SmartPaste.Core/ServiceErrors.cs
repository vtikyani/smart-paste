using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace SmartPaste.Core;

// Why a cloud call (OpenRouter parsing, Jev, a key check) gave no usable answer. The app turns these into the
// service status the user sees, so each one maps to something the user can understand or act on.
public enum ServiceFailure
{
    // No key is saved for the service.
    NoKey,
    // The service rejected the key (HTTP 401 or 403).
    Unauthorized,
    // The account behind the key has no credits left (HTTP 402).
    PaymentRequired,
    // Too many requests for now (HTTP 429).
    RateLimited,
    // No answer before our own deadline.
    Timeout,
    // The service could not be reached (offline, DNS, connection reset).
    Network,
    // The service failed on its side (HTTP 5xx, including 502, 503, 524 and 529).
    ServerError,
    // The service answered, but not with something usable: an unparseable body, or it refused the request as sent.
    BadResponse
}

// A cloud call failed. The message is fixed text chosen by the failure kind: it never holds a response body, a key,
// or anything the user copied, so it is safe to show and safe to log.
public sealed class ServiceException : Exception
{
    public ServiceException(ServiceFailure failure, int? statusCode = null, Exception? innerException = null)
        : base(Describe(failure, statusCode), innerException)
    {
        Failure = failure;
        StatusCode = statusCode;
    }

    public ServiceFailure Failure { get; }

    // The HTTP status code, when the service answered with one.
    public int? StatusCode { get; }

    // Maps an HTTP error status to a failure kind.
    public static ServiceFailure FailureForStatus(int statusCode) => statusCode switch
    {
        401 or 403 => ServiceFailure.Unauthorized,
        402 => ServiceFailure.PaymentRequired,
        408 => ServiceFailure.Timeout,
        429 => ServiceFailure.RateLimited,
        >= 500 => ServiceFailure.ServerError,
        // 400, 404, 413, 422: the service refused the request as sent.
        _ => ServiceFailure.BadResponse
    };

    // Higher means more useful to tell the user: a rejected key beats "offline", which beats "no key".
    // Used to pick one failure to report when several routes failed.
    public static int Actionability(ServiceFailure failure) => failure switch
    {
        ServiceFailure.Unauthorized => 7,
        ServiceFailure.PaymentRequired => 6,
        ServiceFailure.RateLimited => 5,
        ServiceFailure.ServerError => 4,
        ServiceFailure.Timeout => 3,
        ServiceFailure.Network => 2,
        ServiceFailure.BadResponse => 1,
        _ => 0
    };

    private static string Describe(ServiceFailure failure, int? statusCode)
    {
        var text = failure switch
        {
            ServiceFailure.NoKey => "No API key is saved for this service.",
            ServiceFailure.Unauthorized => "The service rejected the API key.",
            ServiceFailure.PaymentRequired => "The account has no credits left.",
            ServiceFailure.RateLimited => "The service is rate limiting requests.",
            ServiceFailure.Timeout => "The service did not answer in time.",
            ServiceFailure.Network => "The service could not be reached.",
            ServiceFailure.ServerError => "The service had an internal error.",
            _ => "The service returned an unusable response."
        };
        return statusCode is { } code ? $"{text} (HTTP {code})" : text;
    }
}

// Sends one HTTP request under a deadline and turns every way it can fail into a ServiceException.
internal static class ServiceHttp
{
    // Returns the body of a successful response. The request token carries the deadline; callerToken is the
    // caller's own token, so cancellation by the caller stays an OperationCanceledException and is never
    // mistaken for a timeout.
    public static async Task<string> SendAsync(
        HttpClient http, HttpRequestMessage request, CancellationToken requestToken, CancellationToken callerToken)
    {
        try
        {
            using var response = await http.SendAsync(request, requestToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new ServiceException(ServiceException.FailureForStatus(status), status);
            }
            return await response.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Our deadline, or HttpClient.Timeout.
            throw new ServiceException(ServiceFailure.Timeout, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ServiceException(ServiceFailure.Network, null, ex);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            throw new ServiceException(ServiceFailure.Network, null, ex);
        }
    }

    // Sets the bearer key. A key with spaces or control characters can never be valid and would make the HTTP
    // stack throw, so it is reported as a rejected key without sending anything.
    public static void Authorize(HttpRequestMessage request, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ServiceException(ServiceFailure.NoKey);
        var trimmed = key.Trim();
        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ServiceException(ServiceFailure.Unauthorized);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trimmed);
    }

    public static StringContent Json(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    // A token that fires at the deadline or when the caller cancels, whichever comes first.
    public static CancellationTokenSource Deadline(TimeSpan deadline, CancellationToken callerToken)
    {
        if (deadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deadline), "The deadline must be positive.");
        var source = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        source.CancelAfter(deadline);
        return source;
    }
}
