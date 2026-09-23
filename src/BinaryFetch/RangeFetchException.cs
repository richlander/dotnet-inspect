using System.Net;

namespace BinaryFetch;

/// <summary>The ways a random-access read can fail for reasons the source owns.</summary>
public enum RangeFetchFailure
{
    /// <summary>The source answered a ranged request with the whole representation (<c>200</c>).</summary>
    RangeIgnored,

    /// <summary>The representation changed between requests (validator or total differs, or <c>200</c> to <c>If-Range</c>).</summary>
    RepresentationChanged,

    /// <summary>The response does not describe the bytes it carries (range, length, or body disagree).</summary>
    InvalidResponse,

    /// <summary>The source refused or could not serve the request; <see cref="RangeFetchException.StatusCode"/> carries the status when there was one.</summary>
    Transport,
}

/// <summary>
/// A failure of a random-access read, classified by what the source did.
/// Transport-level exceptions the underlying client raises (connection
/// failures, cancellation, timeouts) are not wrapped; they propagate as they
/// are so a consumer keeps its own deadline and failure attribution.
/// </summary>
public sealed class RangeFetchException : IOException
{
    public RangeFetchException(
        RangeFetchFailure failure,
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        StatusCode = statusCode;
    }

    public RangeFetchFailure Failure { get; }

    public HttpStatusCode? StatusCode { get; }
}
