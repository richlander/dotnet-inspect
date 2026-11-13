using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation;

public enum DirectUseClusterPresentationStatus
{
    Available,
    ClusterNotFound,
    Rejected,
}

public readonly record struct DirectUseLibraryPresentation(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken,
    Guid ModuleVersionId);

public readonly record struct DirectUseClusterPresentation(
    int Ordinal,
    DirectUseLibraryPresentation Source,
    DirectUseLibraryPresentation Target,
    int AnchorSourceToken,
    int AnchorTargetToken,
    int SourceMembers,
    int ProviderTypes,
    int TargetMembers,
    int ExtensionMethods,
    int CallSites);

public readonly record struct DirectUseCallSitePresentation(
    DirectUseLibraryPresentation Source,
    string SourceMember,
    int SourceToken,
    DirectUseLibraryPresentation Target,
    string TargetMember,
    int TargetToken,
    string CallKind,
    string EvidenceMethod,
    Guid EvidenceModuleVersionId,
    int EvidenceToken,
    int IlOffset);

public sealed record DirectUseClusterPresentationResult(
    DirectUseClusterPresentationStatus Status,
    bool IsComplete,
    ImmutableArray<DirectUseClusterPresentation> Clusters,
    int? SelectedCluster,
    ImmutableArray<DirectUseCallSitePresentation> CallSites,
    string? Failure);

/// <summary>
/// Projects one completed pairwise Library Direct-Use Cluster inspection into
/// detached presentation values shared by product hosts.
/// </summary>
public static class AssemblyPairDirectUseClusterPresentation
{
    public static DirectUseClusterPresentationResult Project(
        InspectionEnvelope<AssemblyPairDirectUseClusterInspectionOutcome>
            inspection,
        int? selectedCluster)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (selectedCluster is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedCluster),
                "A selected Direct-Use Cluster ordinal must be positive.");
        }

        if (inspection.Content
            is AssemblyPairDirectUseClusterInspectionOutcome.Rejected rejected)
        {
            return new(
                DirectUseClusterPresentationStatus.Rejected,
                IsComplete: false,
                [],
                selectedCluster,
                [],
                rejected.Pair.Detail);
        }

        var available =
            (AssemblyPairDirectUseClusterInspectionOutcome.Available)
                inspection.Content;
        AssemblyPairDirectUseClusterProjection projection =
            available.Projection;
        AssemblyPairDirectUseClusterProjection? scoped =
            selectedCluster is int ordinal
                ? projection.ScopeToObservedCluster(ordinal)
                : null;
        ImmutableArray<DirectUseClusterPresentation> clusters =
        [
            .. projection.Clusters.Select(Project),
        ];
        if (selectedCluster is not null && scoped is null)
        {
            return new(
                DirectUseClusterPresentationStatus.ClusterNotFound,
                projection.IsComplete,
                clusters,
                selectedCluster,
                [],
                projection.Clusters.IsEmpty
                    ? "No Direct-Use Clusters were observed for this Library pair."
                    : "The selected Direct-Use Cluster ordinal was not observed.");
        }

        return new(
            DirectUseClusterPresentationStatus.Available,
            projection.IsComplete,
            clusters,
            selectedCluster,
            scoped is null
                ? []
                : [.. scoped.Pair.Occurrences.Select(Project)],
            Failure: null);
    }

    static DirectUseClusterPresentation Project(
        AssemblyPairDirectUseCluster cluster) =>
        new(
            cluster.Ordinal,
            Project(
                cluster.Identity.Source,
                cluster.Identity.SourceModuleVersionId),
            Project(
                cluster.Identity.Target,
                cluster.Identity.TargetModuleVersionId),
            cluster.Identity.AnchorSourceMethodToken,
            cluster.Identity.AnchorTargetMethodToken,
            cluster.SourceMethods.Length,
            cluster.TargetTypes.Length,
            cluster.TargetMethods.Length,
            cluster.ExtensionMethodCount,
            cluster.CallSiteCount);

    static DirectUseCallSitePresentation Project(
        AssemblyPairCallUseOccurrence occurrence) =>
        new(
            Project(occurrence.Source, occurrence.SourceModuleVersionId),
            FormatMethod(occurrence.SourceMethod),
            occurrence.SourceMethod.MetadataToken,
            Project(occurrence.Target, occurrence.TargetModuleVersionId),
            FormatMethod(occurrence.TargetMethod),
            occurrence.TargetMethod.MetadataToken,
            FormatCallKind(occurrence.Call.Kind),
            FormatMethod(occurrence.Call.EvidenceMethod),
            occurrence.Call.EvidenceMethod.ModuleVersionId,
            occurrence.Call.EvidenceMethod.MetadataToken,
            occurrence.Call.ILOffset);

    static DirectUseLibraryPresentation Project(
        AssemblyContextSubject subject,
        Guid moduleVersionId) =>
        new(
            subject.Identity.Name,
            subject.Identity.Version?.ToString(),
            subject.Identity.Culture,
            subject.Identity.PublicKeyToken,
            moduleVersionId);

    static string FormatMethod(MethodIdentity method) =>
        $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}("
            + $"{string.Join(", ", method.ParameterTypes.Select(
                parameter => parameter.ToQualifiedDisplayString()))})";

    static string FormatCallKind(CallKind kind) =>
        kind switch
        {
            CallKind.Call => "call",
            CallKind.CallVirtual => "callvirt",
            CallKind.NewObject => "newobj",
            _ => throw new InvalidOperationException(
                $"Call kind '{kind}' is not admitted by pairwise Library call-use."),
        };
}
