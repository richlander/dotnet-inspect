namespace BinaryFetch;

/// <summary>The random-access operation that issued an HTTP range request.</summary>
public enum RangeRequestKind
{
    Tail,
    Exact,
}

/// <summary>
/// Sends one ranged request and returns its response with headers read. The
/// consumer owns what happens around the send: credentials and host request
/// options on the message, retry, and the per-request deadline. The source
/// calls it once per ranged request and lets its exceptions pass through.
/// </summary>
public delegate Task<HttpResponseMessage> RangeRequestSender(
    HttpRequestMessage request,
    CancellationToken cancellationToken);

/// <summary>
/// Sends one ranged request with the random-access operation that issued it.
/// </summary>
public delegate Task<HttpResponseMessage> ClassifiedRangeRequestSender(
    HttpRequestMessage request,
    RangeRequestKind kind,
    CancellationToken cancellationToken);
