using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal sealed record TypeInspectionPolicy(
    ApiSurfaceExtractionBounds ExtractionBounds,
    AssemblyContextLibraryMaterializationLimits MaterializationLimits);

internal static class TypeInspectionPolicies
{
    internal static TypeInspectionPolicy CompactOverview { get; } =
        new(
            new ApiSurfaceExtractionBounds(
                maxTypes: 250_000,
                maxMembers: 2_000_000,
                maxInspectionFailures: 4_096,
                maxTypeForwarders: 250_000,
                maxMetadataRows: 5_000_000,
                maxRetainedTextCharacters: 20_000_000),
            new AssemblyContextLibraryMaterializationLimits(
                maxCapturedImageBytes: 512L * 1024 * 1024,
                maxRetainedArtifactBytes: 512L * 1024 * 1024));
}
