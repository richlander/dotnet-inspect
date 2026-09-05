using System.Collections.Immutable;

namespace NuGetFetch;

/// <summary>A suggested value for the Gallery package-type selector.</summary>
public sealed record NuGetGalleryPackageTypeSuggestion(
    NuGetGalleryPackageType Value,
    string Label);

/// <summary>
/// The Gallery package-type facet. Its typed value domain includes custom
/// package types, not only the suggested values.
/// </summary>
public sealed class NuGetGalleryPackageTypeFacetDescriptor
{
    internal NuGetGalleryPackageTypeFacetDescriptor()
    {
    }

    public string Id => "nuget.gallery.package-type";
    public string Label => "Package type";
    public string Summary =>
        "Select the package type reported by the Gallery for its selected version.";
    public PackageSourceKind SourceKind => PackageSourceKind.NuGetGallery;
    public int MinimumSelections => 0;
    public int MaximumSelections => 1;

    public ImmutableArray<NuGetGalleryPackageTypeSuggestion> Suggestions { get; } =
    [
        new(NuGetGalleryPackageType.DotnetTool, ".NET tools"),
        new(NuGetGalleryPackageType.Template, "Templates"),
        new(NuGetGalleryPackageType.Dependency, "Dependency packages"),
    ];

    /// <summary>Admits one value using the source-owned package-type domain.</summary>
    public NuGetGalleryPackageType Select(string value) =>
        NuGetGalleryPackageType.Create(value);
}

/// <summary>A source-owned discovery order with an opaque lookup identity.</summary>
public sealed class NuGetGalleryOrderDescriptor
{
    internal NuGetGalleryOrderDescriptor(
        string id,
        string label,
        string summary,
        NuGetGalleryDiscoveryOrder order)
    {
        Id = id;
        Label = label;
        Summary = summary;
        Order = order;
    }

    public string Id { get; }
    public string Label { get; }
    public string Summary { get; }
    public NuGetGalleryDiscoveryOrder Order { get; }
    public PackageSourceKind SourceKind => PackageSourceKind.NuGetGallery;
}

/// <summary>
/// Inert, immutable discovery of Gallery search selectors and source orders.
/// Catalog entries describe intent, not current transport execution capability.
/// </summary>
public static class NuGetGalleryDiscoveryCatalog
{
    public static NuGetGalleryPackageTypeFacetDescriptor PackageType { get; } = new();

    public static NuGetGalleryOrderDescriptor MostDownloaded { get; } = new(
        "nuget.gallery.most-downloaded",
        "Most downloaded",
        "Gallery download-ranked candidates, ordered by lifetime downloads within the response; not global top-N.",
        NuGetGalleryDiscoveryOrder.MostDownloaded);

    public static NuGetGalleryOrderDescriptor Relevance { get; } = new(
        "nuget.gallery.relevance",
        "Relevance",
        "The Gallery's relevance order for this search.",
        NuGetGalleryDiscoveryOrder.Relevance);

    public static ImmutableArray<NuGetGalleryPackageTypeFacetDescriptor> Facets { get; } =
        [PackageType];

    public static ImmutableArray<NuGetGalleryOrderDescriptor> Orders { get; } =
        [MostDownloaded, Relevance];

    /// <summary>Looks up a facet by its opaque identity, not its display label.</summary>
    public static NuGetGalleryPackageTypeFacetDescriptor GetFacet(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        foreach (NuGetGalleryPackageTypeFacetDescriptor facet in Facets)
        {
            if (string.Equals(facet.Id, id, StringComparison.Ordinal))
                return facet;
        }

        throw new ArgumentException("The Gallery search facet is unknown.", nameof(id));
    }

    /// <summary>Looks up a source order by its opaque identity.</summary>
    public static NuGetGalleryOrderDescriptor GetOrder(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        foreach (NuGetGalleryOrderDescriptor descriptor in Orders)
        {
            if (string.Equals(descriptor.Id, id, StringComparison.Ordinal))
                return descriptor;
        }

        throw new ArgumentException("The Gallery source order is unknown.", nameof(id));
    }

    /// <summary>Looks up a typed source order, rejecting undefined enum values.</summary>
    public static NuGetGalleryOrderDescriptor GetOrder(NuGetGalleryDiscoveryOrder order)
    {
        foreach (NuGetGalleryOrderDescriptor descriptor in Orders)
        {
            if (descriptor.Order == order)
                return descriptor;
        }

        throw new ArgumentOutOfRangeException(
            nameof(order),
            order,
            "The Gallery source order is unknown.");
    }
}
