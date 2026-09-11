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
        var packages = new List<SourceSelector.PackageSource>();
        var seen = new HashSet<PackageIdentity>(PackageIdentityComparer.Instance);
        var otherSources = new List<SourceSelector>();

        foreach (SourceSelector selector in intent.Selectors)
        {
            switch (selector)
            {
                case SourceSelector.PackageSource package:
                    AddPackage(package);
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
                AddPackage(new SourceSelector.Package(coordinate));
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

        void AddPackage(SourceSelector.PackageSource package)
        {
            if (seen.Add(GetIdentity(package)))
                packages.Add(package);
        }
    }

    private static PackageIdentity GetIdentity(SourceSelector.PackageSource source) => source switch
    {
        SourceSelector.Package package => new(
            false, package.Coordinate.PackageId, package.Coordinate.Version,
            package.Coordinate.Framework, package.Coordinate.RuntimeIdentifier),
        SourceSelector.PackageReference reference => new(
            false, reference.PackageId, reference.Version, null, null),
        SourceSelector.PackageArchive archive => new(true, archive.Path, null, null, null),
        _ => throw new InvalidOperationException("Unknown package source."),
    };

    private readonly record struct PackageIdentity(
        bool IsArchive, string Name, string? Version, string? Framework, string? RuntimeIdentifier);

    private sealed class PackageIdentityComparer : IEqualityComparer<PackageIdentity>
    {
        public static PackageIdentityComparer Instance { get; } = new();

        public bool Equals(PackageIdentity x, PackageIdentity y) =>
            x.IsArchive == y.IsArchive
            && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Version, y.Version)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Framework, y.Framework)
            && StringComparer.OrdinalIgnoreCase.Equals(x.RuntimeIdentifier, y.RuntimeIdentifier);

        public int GetHashCode(PackageIdentity obj)
        {
            var hash = new HashCode();
            hash.Add(obj.IsArchive);
            hash.Add(obj.Name, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Version, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Framework, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.RuntimeIdentifier, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
