namespace NuGetFetch;

/// <summary>
/// Configures resource limits for NuGet metadata requests.
/// </summary>
public sealed record NuGetFetchOptions
{
    private static readonly TimeSpan MaximumBodyTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    /// <summary>
    /// Default maximum size of a service-index, version-index, or search-response body.
    /// </summary>
    public const long DefaultMaxMetadataResponseBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Default time allowed to consume and parse one metadata response body.
    /// </summary>
    public static TimeSpan DefaultMetadataBodyTimeout { get; } =
        TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the maximum accepted metadata response size in bytes.
    /// </summary>
    public long MaxMetadataResponseBytes { get; init; } =
        DefaultMaxMetadataResponseBytes;

    /// <summary>
    /// Gets the time allowed to consume and parse one metadata response body after its
    /// headers arrive.
    /// </summary>
    public TimeSpan MetadataBodyTimeout { get; init; } =
        DefaultMetadataBodyTimeout;

    internal static NuGetFetchOptions Validate(NuGetFetchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxMetadataResponseBytes);
        if (options.MetadataBodyTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MetadataBodyTimeout),
                options.MetadataBodyTimeout,
                "The metadata body timeout must be positive.");
        }

        if (options.MetadataBodyTimeout > MaximumBodyTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MetadataBodyTimeout),
                options.MetadataBodyTimeout,
                $"The metadata body timeout cannot exceed {MaximumBodyTimeout}.");
        }

        return options;
    }

    internal static NuGetFetchOptions ForClient(
        NuGetFetchOptions options,
        TimeSpan clientTimeout)
    {
        options = Validate(options);
        return clientTimeout != Timeout.InfiniteTimeSpan
            && clientTimeout < options.MetadataBodyTimeout
                ? options with { MetadataBodyTimeout = clientTimeout }
                : options;
    }
}
