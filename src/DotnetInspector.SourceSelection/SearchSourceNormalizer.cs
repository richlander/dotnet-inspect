using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection;

public static class SearchSourceNormalizer
{
    public static SearchSourceSelection Normalize(SourceIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        bool usesImplicitPlatform = intent.Selectors.Count == 0;
        bool hasPlatform = usesImplicitPlatform
            || intent.Selectors.Any(selector => selector is SourceSelector.PlatformGroup);
        var packages = new List<PackageCoordinate>();
        var seen = new HashSet<PackageCoordinate>(CoordinateComparer.Instance);
        var otherSources = new List<SourceSelector>();

        foreach (SourceSelector selector in intent.Selectors)
        {
            switch (selector)
            {
                case SourceSelector.Package package:
                    AddPackage(package.Coordinate);
                    break;
                case SourceSelector.PackageGroup:
                case SourceSelector.PlatformGroup:
                    break;
                case SourceSelector.PackagePrefix:
                case SourceSelector.Library:
                case SourceSelector.PlatformLibrary:
                case SourceSelector.Project:
                case SourceSelector.BinaryDirectory:
                    otherSources.Add(selector);
                    break;
                default:
                    throw new InvalidOperationException("Unknown source selector.");
            }
        }

        // Direct package requests precede group membership regardless of selector order.
        foreach (var group in intent.Selectors.OfType<SourceSelector.PackageGroup>())
        {
            foreach (PackageCoordinate coordinate in group.Coordinates)
                AddPackage(coordinate);
        }

        return new(
            intent,
            usesImplicitPlatform,
            hasPlatform
                ? [SearchPlatformFramework.Runtime, SearchPlatformFramework.AspNetCore,
                    SearchPlatformFramework.NetStandard]
                : [],
            [.. packages],
            [.. otherSources]);

        void AddPackage(PackageCoordinate coordinate)
        {
            if (seen.Add(coordinate))
                packages.Add(coordinate);
        }
    }

    private sealed class CoordinateComparer : IEqualityComparer<PackageCoordinate>
    {
        public static CoordinateComparer Instance { get; } = new();

        public bool Equals(PackageCoordinate? x, PackageCoordinate? y) =>
            ReferenceEquals(x, y)
            || (x is not null && y is not null
                && StringComparer.OrdinalIgnoreCase.Equals(x.PackageId, y.PackageId)
                && StringComparer.OrdinalIgnoreCase.Equals(x.Version, y.Version)
                && StringComparer.OrdinalIgnoreCase.Equals(x.Framework, y.Framework)
                && StringComparer.OrdinalIgnoreCase.Equals(x.RuntimeIdentifier, y.RuntimeIdentifier));

        public int GetHashCode(PackageCoordinate obj)
        {
            var hash = new HashCode();
            hash.Add(obj.PackageId, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Version, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Framework, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.RuntimeIdentifier, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
