using DotnetInspector.Packages;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// The package a package Root declares for an assembly it forwards a Type to
/// (docs/design/package-endpoint-scope.md, adoption step 4 part 5): the
/// dependency named for that assembly, in the manifest's dependency group for
/// the Root's framework, exact or framework-neutral, at its version range's
/// lower bound.
/// </summary>
public static class PackageDeclaredForwardTarget
{
    /// <summary>
    /// Selects the declared coordinate, or returns <see langword="null"/>
    /// with the reason in <paramref name="failure"/>.
    /// </summary>
    public static PackageSourceCoordinate? Select(
        ReadOnlyMemory<byte> manifest,
        string packageId,
        string packageVersion,
        string targetFramework,
        string assemblyName,
        out string? failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        PackageDependencyGroupsResult groups = PackageDependencyGroupsQuery.Execute(
            manifest,
            packageId,
            packageVersion,
            targetFramework,
            allowCompatibleFallbackForRequestedTfm: false);
        if (groups is not PackageDependencyGroupsResult.Available
            {
                Value.SelectedGroup: { } group,
            })
        {
            failure = groups switch
            {
                PackageDependencyGroupsResult.Failed failed =>
                    $"its manifest could not be read: {failed.Error.Message}",
                PackageDependencyGroupsResult.NoManifest =>
                    "it has no manifest",
                _ => $"its manifest declares no dependency group for {targetFramework}",
            };
            return null;
        }

        string[] versions =
        [
            .. group.Dependencies
                .Where(dependency => dependency.Id.Equals(
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static dependency => VersionRange.TryParse(
                        dependency.VersionRange,
                        out VersionRange? range)
                    ? range.MinVersion?.ToNormalizedString()
                    : null)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        if (versions.Length != 1)
        {
            failure = versions.Length == 0
                ? $"it declares no dependency named {assemblyName} for {targetFramework}"
                : $"it declares more than one version of {assemblyName} for {targetFramework}";
            return null;
        }

        failure = null;
        return PackageSourceCoordinate.Create(assemblyName, versions[0]);
    }
}
