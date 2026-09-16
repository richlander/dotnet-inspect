using System.Collections.Immutable;
using DotnetInspector.Packages;

namespace DotnetInspector.Ecosystems;

/// <summary>Immutable metadata and membership for one package set.</summary>
public sealed class PackageSetDescriptor
{
    internal PackageSetDescriptor(
        PackageSetId id,
        string title,
        string summary,
        int order,
        IEnumerable<PackageCoordinate> members)
    {
        Id = id;
        Title = title;
        Summary = summary;
        Order = order;
        Members = [.. members];
    }

    public PackageSetId Id { get; }

    public string Title { get; }

    public string Summary { get; }

    public int Order { get; }

    public ImmutableArray<PackageCoordinate> Members { get; }
}

/// <summary>The result of exact package-set lookup.</summary>
public abstract record PackageSetLookupResult
{
    private protected PackageSetLookupResult()
    {
    }

    public sealed record Known : PackageSetLookupResult
    {
        internal Known(PackageSetDescriptor descriptor) =>
            Descriptor = descriptor;

        public PackageSetDescriptor Descriptor { get; }
    }

    public sealed record Unknown : PackageSetLookupResult
    {
        internal Unknown(PackageSetId id) => Id = id;

        public PackageSetId Id { get; }
    }
}
