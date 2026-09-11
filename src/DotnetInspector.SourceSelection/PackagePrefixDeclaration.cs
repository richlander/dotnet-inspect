using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection;

/// <summary>A validated literal package prefix, independent of consumer request policy.</summary>
public sealed record PackagePrefixDeclaration
{
    public PackagePrefixDeclaration(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!IsPackagePrefix(prefix))
        {
            throw new ArgumentException(
                "A package prefix must be a nonempty literal beginning of a valid package ID.",
                nameof(prefix));
        }

        Prefix = prefix;
    }

    public string Prefix { get; }

    private static bool IsPackagePrefix(string prefix) =>
        PackageCoordinateResolver.IsCanonicalPackageId(prefix)
        || (prefix.Length is > 1 and < PackageCoordinateResolver.MaxPackageIdLength
            && prefix[^1] is '.' or '-'
            && PackageCoordinateResolver.IsCanonicalPackageId(prefix[..^1]));
}
