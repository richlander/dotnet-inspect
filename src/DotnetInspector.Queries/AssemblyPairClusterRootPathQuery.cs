using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Analysis = ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspector.Services;

namespace DotnetInspector.Queries;

/// <summary>
/// The selected cluster cannot be joined to the supplied assembly group.
/// </summary>
public sealed class AssemblyPairClusterRootPathRequestException
    : ArgumentException
{
    internal AssemblyPairClusterRootPathRequestException(
        string message,
        string parameterName)
        : base(message, parameterName)
    {
    }
}

/// <summary>Owner-specific limits for one cluster root-path composition.</summary>
public sealed record AssemblyPairClusterRootPathLimits(
    PublicMethodRootInventoryLimits PublicRoots,
    Analysis.LibraryBodyRootPathLimits Paths);

public enum AssemblyPairClusterRootPathFailureKind
{
    ParticipantRejected,
    InvalidImage,
}

/// <summary>One failure to inspect the selected consumer participant.</summary>
public sealed record AssemblyPairClusterRootPathFailure(
    AssemblyContextSubject Source,
    AssemblyPairClusterRootPathFailureKind Kind,
    string Detail,
    CandidateOpenFailure? AcquisitionFailure = null);

/// <summary>
/// Exact cluster selection, public roots, local path evidence, and every
/// contributing completion boundary.
/// </summary>
public sealed record AssemblyPairClusterRootPathResult(
    AssemblyPairDirectUseClusterProjection Selection,
    PublicMethodRootInventory? PublicRoots,
    Analysis.LibraryBodyRootPathResult? Paths,
    ImmutableArray<AssemblyPairClusterRootPathFailure> Failures)
{
    public AssemblyPairDirectUseCluster Cluster =>
        Selection.Clusters[0];

    public bool IsComplete =>
        Selection.IsComplete
        && Failures.IsEmpty
        && PublicRoots is { IsComplete: true } roots
        && (roots.Roots.IsEmpty
            || Paths is { IsComplete: true });
}

/// <summary>
/// Composes one exact direct-use cluster with Metadata public roots and
/// Analysis local path witnesses.
/// </summary>
public static class AssemblyPairClusterRootPathQuery
{
    public static InspectionQuery<AssemblyPairClusterRootPathResult>
        Definition { get; } =
            new(
                "Assembly pair cluster public root paths",
                InspectionCost.Unbounded);

    public static AssemblyPairClusterRootPathResult Execute(
        AssemblyContextGroup group,
        AssemblyPairDirectUseClusterProjection selection,
        AssemblyPairClusterRootPathLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(limits.PublicRoots);
        ArgumentNullException.ThrowIfNull(limits.Paths);

        AssemblyPairDirectUseCluster cluster =
            RequireScopedCluster(selection);
        AssemblyContextParticipant participant =
            RequirePairParticipants(group, cluster);
        AssemblyContextSubject source =
            new(participant.Assembly);

        AssemblyImageAccessResult<SnapshotResult> access =
            group.UseSnapshot(
                participant,
                cancellationToken,
                snapshot => InspectSnapshot(
                    snapshot,
                    selection,
                    cluster,
                    limits));

        return access switch
        {
            AssemblyImageAccessResult<SnapshotResult>.Available available =>
                available.Value switch
                {
                    SnapshotResult.Available result =>
                        result.Result,
                    SnapshotResult.InvalidImage invalid =>
                        Unavailable(
                            selection,
                            source,
                            invalid.Error),
                    _ => throw new InvalidOperationException(
                        "Unknown cluster root-path snapshot result."),
                },
            AssemblyImageAccessResult<SnapshotResult>.Rejected rejected =>
                new(
                    selection,
                    PublicRoots: null,
                    Paths: null,
                    [
                        new(
                            source,
                            AssemblyPairClusterRootPathFailureKind
                                .ParticipantRejected,
                            rejected.Failure.Detail,
                            rejected.Failure),
                    ]),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    static SnapshotResult InspectSnapshot(
        AssemblyImageSnapshot snapshot,
        AssemblyPairDirectUseClusterProjection selection,
        AssemblyPairDirectUseCluster cluster,
        AssemblyPairClusterRootPathLimits limits)
    {
        if (!ReferenceEquals(
                snapshot.Registration,
                cluster.Identity.Source.Registration)
            || snapshot.ModuleVersionId
                != cluster.Identity.SourceModuleVersionId)
        {
            throw new AssemblyPairClusterRootPathRequestException(
                "The selected cluster does not match the live source "
                    + "participant snapshot.",
                nameof(selection));
        }

        try
        {
            using var image = new PEReader(snapshot.Content);
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(image);
            PublicMethodRootInventory roots =
                PublicMethodRootInventoryReader.Read(
                    reader,
                    limits.PublicRoots);
            Analysis.LibraryBodyAnalysisRequest request =
                Analysis.LibraryBodyAnalysisRequest.Create(
                    Analysis.LibraryBodyAnalysisFeatures
                        .MethodEvidence);
            Analysis.LibraryCallGraphAnalysisResult callGraph =
                Analysis.LibraryBodyAnalysisService.ExecuteImage(
                    cluster.Identity.Source.Identity.Name,
                    snapshot.Content,
                    request)
                .CallGraph;
            if (roots.ModuleVersionId
                    != cluster.Identity.SourceModuleVersionId
                || callGraph.ModuleIdentity.ModuleVersionId
                    != cluster.Identity.SourceModuleVersionId)
            {
                throw new AssemblyPairClusterRootPathRequestException(
                    "The selected cluster, Metadata inventory, and "
                        + "Analysis index do not identify the same "
                        + "source module.",
                    nameof(selection));
            }

            Analysis.LibraryBodyRootPathResult? paths =
                roots.Roots.IsEmpty
                    ? null
                    : Analysis.LibraryBodyRootPathAnalysis
                        .FindShortestPaths(
                            callGraph,
                            roots.Roots,
                            Destinations(cluster),
                            limits.Paths);
            return new SnapshotResult.Available(
                new(
                    selection,
                    roots,
                    paths,
                    []));
        }
        catch (Exception exception)
            when (MemberCallGraphSession
                .IsInvalidImageException(exception))
        {
            return new SnapshotResult.InvalidImage(exception);
        }
    }

    static ImmutableArray<MetadataMethodAddress> Destinations(
        AssemblyPairDirectUseCluster cluster) =>
    [
        .. cluster.SourceMethods
            .Select(method =>
            {
                Handle handle =
                    MetadataTokens.Handle(
                        method.MetadataToken);
                if (handle.Kind
                    != HandleKind.MethodDefinition)
                {
                    throw new AssemblyPairClusterRootPathRequestException(
                        "The selected cluster contains a source "
                            + "method that is not a MethodDef.",
                        nameof(cluster));
                }

                return new MetadataMethodAddress(
                    cluster.Identity.SourceModuleVersionId,
                    (MethodDefinitionHandle)handle);
            })
            .Distinct()
            .OrderBy(static address => address.Token),
    ];

    static AssemblyPairDirectUseCluster RequireScopedCluster(
        AssemblyPairDirectUseClusterProjection selection)
    {
        if (selection.Clusters.Length != 1)
        {
            throw new AssemblyPairClusterRootPathRequestException(
                "Cluster root-path composition requires exactly one "
                    + "scoped direct-use cluster.",
                nameof(selection));
        }

        AssemblyPairDirectUseCluster cluster =
            selection.Clusters[0];
        if (cluster.SourceMethods.IsEmpty
            || selection.Pair.Occurrences.IsEmpty)
        {
            throw new AssemblyPairClusterRootPathRequestException(
                "The selected cluster must retain exact source "
                    + "methods and pair occurrences.",
                nameof(selection));
        }

        AssemblyPairDirectUseClusterProjection derived =
            AssemblyPairDirectUseClusterProjection.Create(
                selection.Pair);
        if (derived.Clusters.Length != 1
            || !MatchesScopedCluster(
                cluster,
                derived.Clusters[0]))
        {
            throw new AssemblyPairClusterRootPathRequestException(
                "The selected cluster does not match its exact "
                    + "scoped pair occurrences.",
                nameof(selection));
        }

        return cluster;
    }

    static bool MatchesScopedCluster(
        AssemblyPairDirectUseCluster selected,
        AssemblyPairDirectUseCluster derived)
    {
        AssemblyPairDirectUseClusterIdentity selectedIdentity =
            selected.Identity;
        AssemblyPairDirectUseClusterIdentity derivedIdentity =
            derived.Identity;
        return selected.Ordinal > 0
            && selected.Derivation == derived.Derivation
            && ReferenceEquals(
                selectedIdentity.Source.Registration,
                derivedIdentity.Source.Registration)
            && selectedIdentity.SourceModuleVersionId
                == derivedIdentity.SourceModuleVersionId
            && selectedIdentity.AnchorSourceMethodToken
                == derivedIdentity.AnchorSourceMethodToken
            && ReferenceEquals(
                selectedIdentity.Target.Registration,
                derivedIdentity.Target.Registration)
            && selectedIdentity.TargetModuleVersionId
                == derivedIdentity.TargetModuleVersionId
            && selectedIdentity.AnchorTargetMethodToken
                == derivedIdentity.AnchorTargetMethodToken
            && selected.SourceMethods.SequenceEqual(
                derived.SourceMethods)
            && selected.TargetTypes.SequenceEqual(
                derived.TargetTypes)
            && selected.TargetMethods.SequenceEqual(
                derived.TargetMethods)
            && selected.OccurrenceIndexes.SequenceEqual(
                derived.OccurrenceIndexes);
    }

    static AssemblyContextParticipant RequirePairParticipants(
        AssemblyContextGroup group,
        AssemblyPairDirectUseCluster cluster)
    {
        AssemblyContextParticipant source =
            group.Participants.SingleOrDefault(
                participant => ReferenceEquals(
                    participant.Assembly.Registration,
                    cluster.Identity.Source.Registration))
            ?? throw new AssemblyPairClusterRootPathRequestException(
                "The selected cluster source does not belong to the "
                    + "supplied assembly group.",
                nameof(cluster));
        if (!group.Participants.Any(
                participant => ReferenceEquals(
                    participant.Assembly.Registration,
                    cluster.Identity.Target.Registration)))
        {
            throw new AssemblyPairClusterRootPathRequestException(
                "The selected cluster target does not belong to the "
                    + "supplied assembly group.",
                nameof(cluster));
        }

        return source;
    }

    static AssemblyPairClusterRootPathResult Unavailable(
        AssemblyPairDirectUseClusterProjection selection,
        AssemblyContextSubject source,
        Exception error) =>
        new(
            selection,
            PublicRoots: null,
            Paths: null,
            [
                new(
                    source,
                    AssemblyPairClusterRootPathFailureKind
                        .InvalidImage,
                    error.Message),
            ]);

    abstract record SnapshotResult
    {
        internal sealed record Available(
            AssemblyPairClusterRootPathResult Result)
            : SnapshotResult;

        internal sealed record InvalidImage(Exception Error)
            : SnapshotResult;
    }
}
