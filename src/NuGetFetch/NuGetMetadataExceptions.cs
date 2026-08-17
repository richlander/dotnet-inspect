namespace NuGetFetch;

/// <summary>
/// Thrown when a NuGet service-index, version-index, or search-response body exceeds
/// its configured byte limit.
/// </summary>
public sealed class NuGetMetadataResponseTooLargeException : IOException
{
    internal NuGetMetadataResponseTooLargeException(
        long maximumBytes,
        long? advertisedBytes = null)
        : base(
            advertisedBytes is long advertised
                ? $"NuGet metadata response advertised {advertised} bytes, exceeding "
                    + $"the {maximumBytes}-byte limit."
                : $"NuGet metadata response exceeded the {maximumBytes}-byte limit.")
    {
        MaximumBytes = maximumBytes;
        AdvertisedBytes = advertisedBytes;
    }

    /// <summary>
    /// Gets the configured maximum response size.
    /// </summary>
    public long MaximumBytes { get; }

    /// <summary>
    /// Gets the response's advertised size when the rejection was made from headers.
    /// </summary>
    public long? AdvertisedBytes { get; }
}

internal sealed class NuGetRedirectLimitExceededException()
    : IOException("The package source response exceeded the redirect limit.");

/// <summary>
/// Thrown when a NuGet metadata response body does not complete within its configured
/// body-phase timeout.
/// </summary>
public sealed class NuGetMetadataBodyTimeoutException : TimeoutException
{
    internal NuGetMetadataBodyTimeoutException(
        TimeSpan timeout,
        OperationCanceledException innerException)
        : base(
            $"NuGet metadata response body did not complete within {timeout}.",
            innerException)
    {
        Timeout = timeout;
    }

    /// <summary>
    /// Gets the configured body-phase timeout.
    /// </summary>
    public TimeSpan Timeout { get; }
}
