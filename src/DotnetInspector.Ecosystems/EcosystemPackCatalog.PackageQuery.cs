using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Ecosystems;

public static partial class EcosystemPackCatalog
{
    public static PackageQueryEcosystemMembershipCatalog
        PackageQueryMemberships { get; } = CreatePackageQueryMemberships();

    private static PackageQueryEcosystemMembershipCatalog
        CreatePackageQueryMemberships() =>
        new(
            Discover().Select(pack =>
                new PackageQueryEcosystemMembershipDeclaration(
                    WorkspaceEcosystemRegistrationId.Create(pack.Id.Value),
                    ExactPackages(pack),
                    PackagePrefixes(pack))));

    private static IEnumerable<PackageCoordinate> ExactPackages(
        EcosystemPackDescriptor pack) =>
        pack.PackageSet is not { } packageSetId
            ? []
            : PackageSetCatalog.Lookup(packageSetId) switch
            {
                PackageSetLookupResult.Known known =>
                    known.Descriptor.Members,
                PackageSetLookupResult.Unknown =>
                    throw new InvalidOperationException(
                        $"Ecosystem '{pack.Id}' references unknown package set"
                        + $" '{packageSetId}'."),
                _ => throw new InvalidOperationException(
                    "Package-set lookup returned an unknown result."),
            };

    private static IEnumerable<PackagePrefixDeclaration> PackagePrefixes(
        EcosystemPackDescriptor pack) =>
        SelectWorkspaceRegistration(pack.Id) switch
        {
            EcosystemWorkspaceRegistrationSelectionResult.Known known =>
                known.Declaration.Populations
                    .OfType<
                        WorkspaceEcosystemPopulationDeclaration.PackagePrefix>()
                    .Select(population => population.Prefix),
            EcosystemWorkspaceRegistrationSelectionResult.Unavailable => [],
            EcosystemWorkspaceRegistrationSelectionResult.Unknown =>
                throw new InvalidOperationException(
                    $"Discovered ecosystem '{pack.Id}' was not registered."),
            _ => throw new InvalidOperationException(
                "Workspace ecosystem lookup returned an unknown result."),
        };
}
