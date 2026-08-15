namespace NuGetFetch;

/// <summary>
/// Configures resource limits and deadlines for NuGet requests.
/// </summary>
public sealed record NuGetFetchOptions
{
    private static readonly TimeSpan MaximumCancellationTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    /// <summary>
    /// Default maximum size of a service-index, version-index, or search-response body.
    /// </summary>
    public const long DefaultMaxMetadataResponseBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Default deadline for one HTTP request, including response-body consumption.
    /// </summary>
    public static TimeSpan DefaultRequestTimeout { get; } =
        TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default ceiling for one logical NuGet operation.
    /// </summary>
    public static TimeSpan DefaultOperationTimeout { get; } =
        TimeSpan.FromSeconds(120);

    /// <summary>
    /// Legacy name for the default request deadline.
    /// </summary>
    public static TimeSpan DefaultMetadataBodyTimeout =>
        DefaultRequestTimeout;

    /// <summary>
    /// Gets the maximum accepted metadata response size in bytes.
    /// </summary>
    public long MaxMetadataResponseBytes { get; init; } =
        DefaultMaxMetadataResponseBytes;

    /// <summary>
    /// Gets the deadline for one HTTP request, including response-body consumption.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = DefaultRequestTimeout;

    /// <summary>
    /// Gets the ceiling for one logical operation across requests and pages.
    /// </summary>
    public TimeSpan OperationTimeout { get; init; } =
        DefaultOperationTimeout;

    /// <summary>
    /// Gets an optional stricter timeout for consuming and parsing metadata.
    /// <see cref="Timeout.InfiniteTimeSpan"/> means <see cref="RequestTimeout"/> applies
    /// without a separate body clamp.
    /// </summary>
    public TimeSpan MetadataBodyTimeout { get; init; } =
        Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Creates the CLI timeout policy whose operation ceiling is four request deadlines.
    /// </summary>
    public static NuGetFetchOptions FromRequestTimeout(
        TimeSpan requestTimeout)
    {
        ValidateTimeout(requestTimeout, nameof(requestTimeout));
        if (requestTimeout > MaximumCancellationTimeout / 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                requestTimeout,
                "The derived operation timeout exceeds the supported cancellation range.");
        }

        return new NuGetFetchOptions
        {
            RequestTimeout = requestTimeout,
            OperationTimeout = requestTimeout * 4,
        };
    }

    internal static NuGetFetchOptions Validate(NuGetFetchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxMetadataResponseBytes);
        ValidateTimeout(options.RequestTimeout, nameof(RequestTimeout));
        ValidateTimeout(options.OperationTimeout, nameof(OperationTimeout));
        if (options.MetadataBodyTimeout != Timeout.InfiniteTimeSpan)
        {
            ValidateTimeout(
                options.MetadataBodyTimeout,
                nameof(MetadataBodyTimeout));
        }

        return options;
    }

    internal static NuGetFetchOptions ForClient(
        NuGetFetchOptions options,
        TimeSpan clientTimeout)
    {
        options = Validate(options);
        TimeSpan requestTimeout = RequestTimeoutForClient(
            options,
            clientTimeout);
        return options.MetadataBodyTimeout != Timeout.InfiniteTimeSpan
            && options.MetadataBodyTimeout < requestTimeout
                ? options
                : options with
                {
                    MetadataBodyTimeout = Timeout.InfiniteTimeSpan,
                };
    }

    internal static NuGetFetchOptions ForStream(NuGetFetchOptions options)
    {
        options = Validate(options);
        TimeSpan bodyTimeout =
            options.MetadataBodyTimeout == Timeout.InfiniteTimeSpan
                || options.MetadataBodyTimeout > options.RequestTimeout
                    ? options.RequestTimeout
                    : options.MetadataBodyTimeout;
        return options with { MetadataBodyTimeout = bodyTimeout };
    }

    internal static TimeSpan RequestTimeoutForClient(
        NuGetFetchOptions options,
        TimeSpan clientTimeout)
    {
        options = Validate(options);
        return clientTimeout != Timeout.InfiniteTimeSpan
            && clientTimeout < options.RequestTimeout
                ? clientTimeout
                : options.RequestTimeout;
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                "The timeout must be positive.");
        }

        if (timeout > MaximumCancellationTimeout)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                timeout,
                $"The timeout cannot exceed {MaximumCancellationTimeout}.");
        }
    }
}
