using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.QueriesConsumer;

public sealed record WorkspaceEcosystemRegistrationObservation(
    WorkspaceEcosystemRegistrationId Id,
    ImmutableArray<string> NamespaceRoots,
    ImmutableArray<PackageCoordinate> CorePackages,
    ImmutableArray<ExactLibrarySourceCoordinate> ExactLibraries,
    ImmutableArray<PlatformLibraryPopulationDeclaration> PlatformPopulations,
    ImmutableArray<PackagePrefixDeclaration> PackagePrefixes,
    EcosystemIntegrationScannerBinding? IntegrationScanner);

public static class WorkspaceEcosystemRegistrationConsumer
{
    public static WorkspaceEcosystemRegistrationObservation Observe(
        WorkspaceEcosystemRegistrationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var exactLibraries =
            ImmutableArray.CreateBuilder<ExactLibrarySourceCoordinate>();
        var platformPopulations =
            ImmutableArray.CreateBuilder<PlatformLibraryPopulationDeclaration>();
        var packagePrefixes =
            ImmutableArray.CreateBuilder<PackagePrefixDeclaration>();

        foreach (WorkspaceEcosystemPopulationDeclaration population
            in declaration.Populations)
        {
            switch (population)
            {
                case WorkspaceEcosystemPopulationDeclaration.ExactLibrary exact:
                    exactLibraries.Add(exact.Coordinate);
                    break;
                case WorkspaceEcosystemPopulationDeclaration.Platform platform:
                    platformPopulations.Add(platform.Population);
                    break;
                case WorkspaceEcosystemPopulationDeclaration.PackagePrefix prefix:
                    packagePrefixes.Add(prefix.Prefix);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Workspace ecosystem population declaration.");
            }
        }

        return new(
            declaration.Id,
            declaration.NamespaceRoots,
            declaration.CorePackages,
            exactLibraries.ToImmutable(),
            platformPopulations.ToImmutable(),
            packagePrefixes.ToImmutable(),
            declaration.IntegrationScanner);
    }
}
