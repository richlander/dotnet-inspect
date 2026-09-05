namespace NuGetFetch;

/// <summary>A Gallery source order, not a semantic row-order or Top operation.</summary>
public enum NuGetGalleryDiscoveryOrder
{
    /// <summary>Gallery download-ranked candidates, sorted within the response.</summary>
    MostDownloaded,

    /// <summary>The Gallery's relevance order for the supplied search text.</summary>
    Relevance,
}

/// <summary>
/// Immutable intent for one bounded Gallery response. Construction does not
/// authorize or execute source access.
/// </summary>
public sealed record NuGetGalleryDiscoveryRequest
{
    /// <summary>The largest supported capacity of one Gallery response.</summary>
    public const int MaximumCapacity = 1000;

    public NuGetGalleryDiscoveryRequest(
        PackageSourceDescriptor source,
        int capacity,
        string? text = null,
        NuGetGalleryPackageType? packageType = null,
        NuGetGalleryDiscoveryOrder? order = null,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind != PackageSourceKind.NuGetGallery)
        {
            throw new ArgumentException(
                "Gallery discovery requires a Gallery source.",
                nameof(source));
        }

        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                $"Gallery response capacity must be between 1 and {MaximumCapacity}.");
        }

        Source = source;
        Capacity = capacity;
        Text = string.IsNullOrWhiteSpace(text) ? null : text;
        PackageType = packageType;
        Order = order is { } explicitOrder
            ? NuGetGalleryDiscoveryCatalog.GetOrder(explicitOrder).Order
            : IsBrowse
                ? NuGetGalleryDiscoveryOrder.MostDownloaded
                : NuGetGalleryDiscoveryOrder.Relevance;
        IncludePrerelease = includePrerelease;
    }

    /// <summary>Gets the source configuration; eligibility remains caller-owned.</summary>
    public PackageSourceDescriptor Source { get; }

    /// <summary>
    /// Gets the requested input capacity, independent of semantic row selection.
    /// </summary>
    public int Capacity { get; }

    /// <summary>Gets the original nonempty search text, or null for browse.</summary>
    public string? Text { get; }

    /// <summary>Gets whether this request browses without search text.</summary>
    public bool IsBrowse => Text is null;

    /// <summary>Gets the optional type selector; null includes all package types.</summary>
    public NuGetGalleryPackageType? PackageType { get; }

    /// <summary>Gets the resolved source order, including the browse/search default.</summary>
    public NuGetGalleryDiscoveryOrder Order { get; }

    /// <summary>Gets whether prerelease package versions are eligible.</summary>
    public bool IncludePrerelease { get; }
}
