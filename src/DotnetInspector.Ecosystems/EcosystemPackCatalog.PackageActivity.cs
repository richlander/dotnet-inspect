using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Ecosystems;

public static partial class EcosystemPackCatalog
{
    /// <summary>
    /// Selects package activity for an Ecosystem by its recorded package
    /// prefixes, never its core packages (package-set-retirement.md, slice 4).
    /// Returns <see langword="null"/> when the Ecosystem records no prefix.
    /// </summary>
    public static EcosystemChangePackageSelection.PackagePrefix?
        SelectPackageActivity(EcosystemPackDescriptor pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        PackagePrefixDeclaration[] prefixes = [.. PackagePrefixes(pack)];
        return prefixes.Length == 0
            ? null
            : new EcosystemChangePackageSelection.PackagePrefix(
                pack.Id.Value,
                prefixes);
    }

    /// <summary>Lists the Ecosystems that package activity can select, in product order.</summary>
    public static IEnumerable<EcosystemPackDescriptor> DiscoverPackageActivity() =>
        Discover().Where(pack => PackagePrefixes(pack).Any());
}
