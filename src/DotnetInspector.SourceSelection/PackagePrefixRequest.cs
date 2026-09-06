using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection;

/// <summary>Inert, bounded package-prefix intent; construction does not authorize source access.</summary>
public sealed record PackagePrefixRequest
{
    public PackagePrefixRequest(
        string prefix,
        int maxPackages,
        bool includePrerelease = false)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!IsPackagePrefix(prefix))
        {
            throw new ArgumentException(
                "A package prefix must be a nonempty literal beginning of a valid package ID.",
                nameof(prefix));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPackages);
        Prefix = prefix;
        MaxPackages = maxPackages;
        IncludePrerelease = includePrerelease;
    }

    public string Prefix { get; }
    public int MaxPackages { get; }
    public bool IncludePrerelease { get; }

    private static bool IsPackagePrefix(string prefix) =>
        PackageCoordinateResolver.IsCanonicalPackageId(prefix)
        || (prefix.Length is > 1 and < PackageCoordinateResolver.MaxPackageIdLength
            && prefix[^1] is '.' or '-'
            && PackageCoordinateResolver.IsCanonicalPackageId(prefix[..^1]));
}
