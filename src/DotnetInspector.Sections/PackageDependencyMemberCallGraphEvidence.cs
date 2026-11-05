using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Sections;

public enum PackageDependencyMemberCallGraphEvidenceStatus
{
    InitialPreparationUnavailable,
    InitialRealizationsCaptured,
}

public enum PackageDependencyMemberCallGraphUnobservedStage
{
    RootLoading,
    DependencyDiscovery,
    AssemblyReferenceContinuation,
}

public sealed record PackageDependencyMemberCallGraphRealizationEvidence(
    int RootOccurrenceIndex,
    int EdgeIndex,
    PackageSourceCoordinate Coordinate,
    string? RequestedFramework,
    string? RuntimeIdentifier,
    PackageHouseAssetSelectionKind? AssetSelection,
    PackageAssetDemand AssetDemand,
    ImmutableArray<string> ImplementationNames,
    PackageAcquisitionEvidenceEntry? Acquisition);

public sealed record PackageDependencyMemberCallGraphEvidenceDocument(
    string RootPackageId,
    string RootPackageVersion,
    string? RootFramework,
    PackageDependencyMemberCallGraphInspectionFocus Focus,
    TraversalTargetFrameworkPolicy TraversalTargetPolicy,
    PackageDependencyMemberCallGraphEvidenceStatus Status,
    ImmutableArray<PackageDependencyMemberCallGraphRealizationEvidence>
        Realizations,
    ImmutableArray<PackageDependencyMemberCallGraphUnobservedStage>
        UnobservedStages);

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PackageDependencyMemberCallGraphEvidenceDocument))]
public partial class PackageDependencyMemberCallGraphEvidenceJsonContext
    : JsonSerializerContext;

internal sealed class PackageDependencyMemberCallGraphEvidenceCapture(
    PackageDependencyMemberCallGraphInspectionRequest request)
{
    ImmutableArray<PackageDependencyMemberCallGraphRealizationEvidence>?
        _realizations;

    public void Record(
        PackageDependencyTraversalOutcome traversal,
        PackageDependencyMemberCallGraphPreparation preparation)
    {
        if (_realizations is not null)
            throw new InvalidOperationException(
                "Initial call-graph realization evidence was already captured.");

        _realizations =
        [
            .. preparation.GetRealizationEvidence(traversal).Select(
                static realization =>
                {
                    PackageHouseRequest demand =
                        realization.Execution.Request;
                    return new PackageDependencyMemberCallGraphRealizationEvidence(
                        realization.Subject.RootOccurrenceIndex,
                        realization.Subject.EdgeIndex,
                        realization.Subject.Candidate.Coordinate,
                        demand.TargetContext?.RequestedFramework,
                        demand.TargetContext?.RuntimeIdentifier,
                        demand.AssetSelection,
                        demand.AssetDemand,
                        demand.ImplementationNames is { } names
                            ? [.. names.Names]
                            : [],
                        realization.Result.Evidence.Acquisition is { } acquisition
                            ? PackageAcquisitionEvidenceEntry.From(acquisition)
                            : null);
                }),
        ];
    }

    public PackageDependencyMemberCallGraphEvidenceDocument Complete() =>
        new(
            request.Root.Coordinate.PackageId,
            request.Root.Coordinate.Version,
            request.Root.Coordinate.Framework,
            request.Focus,
            request.TraversalTargetPolicy,
            _realizations is null
                ? PackageDependencyMemberCallGraphEvidenceStatus
                    .InitialPreparationUnavailable
                : PackageDependencyMemberCallGraphEvidenceStatus
                    .InitialRealizationsCaptured,
            _realizations ?? [],
            [
                PackageDependencyMemberCallGraphUnobservedStage.RootLoading,
                PackageDependencyMemberCallGraphUnobservedStage.DependencyDiscovery,
                PackageDependencyMemberCallGraphUnobservedStage.AssemblyReferenceContinuation,
            ]);
}
