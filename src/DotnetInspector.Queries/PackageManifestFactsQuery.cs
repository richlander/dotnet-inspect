using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Query facade over package-owned bounded manifest-fact projection.
/// </summary>
public static class PackageManifestFactsQuery
{
    public const int MaxManifestBytes =
        PackageManifestFactsProjection.MaxManifestBytes;
    public const int MaxManifestCharacters =
        PackageManifestFactsProjection.MaxManifestCharacters;
    public const int MaxScalarCharacters =
        PackageManifestFactsProjection.MaxScalarCharacters;
    public const int MaxPackageTypes =
        PackageManifestFactsProjection.MaxPackageTypes;
    public const int MaxDependencyGroups =
        PackageManifestFactsProjection.MaxDependencyGroups;
    public const int MaxDependencies =
        PackageManifestFactsProjection.MaxDependencies;
    public const int MaxFrameworkReferenceGroups =
        PackageManifestFactsProjection.MaxFrameworkReferenceGroups;
    public const int MaxFrameworkReferences =
        PackageManifestFactsProjection.MaxFrameworkReferences;

    public static InspectionQuery<PackageManifestFactsResult> Definition { get; } =
        new("Package manifest facts", InspectionCost.NetworkFree);

    public static PackageManifestFactsResult Execute(
        ReadOnlyMemory<byte> manifestBytes,
        PackageSourceCoordinate expectedCoordinate) =>
        PackageManifestFactsProjection.Execute(
            manifestBytes,
            expectedCoordinate);

    public static PackageManifestFactsResult ExecuteSelfAttested(
        ReadOnlyMemory<byte> manifestBytes) =>
        PackageManifestFactsProjection.ExecuteSelfAttested(manifestBytes);
}
