using System.Collections.Immutable;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspect.Cli.Sections;

/// <summary>The evidence phases requested by one resolved section plan.</summary>
internal sealed record DependsAssetRequestPlan(
    bool Declarations,
    bool RestoredRelationships,
    bool Traversal,
    bool Pruning)
{
    internal static DependsAssetRequestPlan FromSections(
        IReadOnlySet<string> sections)
    {
        bool failures = sections.Contains(DependsAssetSections.Failures);
        bool dependencies =
            sections.Contains(DependsAssetSections.Dependencies);
        bool pruning =
            sections.Contains(DependsAssetSections.Pruning);
        return new DependsAssetRequestPlan(
            Declarations: dependencies
                || sections.Contains(DependsAssetSections.DependencyGroups)
                || failures
                || pruning,
            RestoredRelationships: dependencies
                || sections.Contains(DependsAssetSections.RestoredEdges)
                || sections.Contains(DependsAssetSections.RestoredPackages)
                || failures,
            Traversal:
                sections.Contains(DependsAssetSections.DependencyGraph),
            Pruning: pruning);
    }
}

/// <summary>
/// CLI root presentation over one host-neutral dependency root.
/// </summary>
internal sealed record DependsRootRow
{
    internal DependsRootRow(
        int occurrence,
        DependencyInspectionRootKind kind,
        InertString input,
        string source,
        DependencyInspectionRootState state,
        string? identityKind,
        InertString? identity,
        DependencyInspectionTraversalCompletion traversal,
        DependencyInspectionEvidenceAvailability declarationState,
        DependencyInspectionEvidencePhaseCompletion declarationCompletion,
        DependencyInspectionSelectionStatus selection,
        DependencyInspectionEvidenceAvailability restoredRelationshipState,
        DependencyInspectionEvidencePhaseCompletion
            restoredRelationshipCompletion)
    {
        Content = new DependencyInspectionRoot(
            new DependencyRootOccurrenceIdentity(occurrence),
            kind,
            input,
            state,
            GraphIdentity: null,
            traversal,
            declarationState,
            declarationCompletion,
            selection,
            restoredRelationshipState,
            restoredRelationshipCompletion);
        IdentityKind = identityKind;
        Identity = identity;
        Source = source;
    }

    internal DependencyInspectionRoot Content { get; private init; }

    internal int Occurrence => Content.Identity.Value;

    internal DependencyInspectionRootKind Kind => Content.Kind;

    internal InertString Input => Content.Input;

    internal string Source { get; }

    internal DependencyInspectionRootState State => Content.State;

    internal string? IdentityKind { get; }

    internal InertString? Identity { get; }

    internal DependencyGraphNodeIdentity? GraphIdentity
    {
        get => Content.GraphIdentity;
        init => Content = Content with { GraphIdentity = value };
    }

    internal DependencyInspectionTraversalCompletion Traversal =>
        Content.Traversal;

    internal DependencyInspectionEvidenceAvailability DeclarationState =>
        Content.DeclarationState;

    internal DependencyInspectionEvidencePhaseCompletion
        DeclarationCompletion => Content.DeclarationCompletion;

    internal DependencyInspectionSelectionStatus Selection =>
        Content.Selection;

    internal DependencyInspectionEvidenceAvailability
        RestoredRelationshipState => Content.RestoredRelationshipState;

    internal DependencyInspectionEvidencePhaseCompletion
        RestoredRelationshipCompletion =>
            Content.RestoredRelationshipCompletion;

    internal DependencyEvidenceRootRow? Evidence { get; init; }

    internal PackageDependencyEvidenceGroupIdentity? SelectedGroup
    { get; init; }

    internal int? SelectedGroupIndex { get; init; }

    internal PackageDependencyEvidenceGroupOccurrence?
        SelectedSourceOccurrence
    { get; init; }

    internal InertString? RequestedFramework
    {
        get => Content.RequestedFramework;
        init => Content = Content with { RequestedFramework = value };
    }

    internal InertString? SelectedFramework
    {
        get => Content.SelectedFramework;
        init => Content = Content with { SelectedFramework = value };
    }
}

/// <summary>
/// CLI presentation values plus the reusable Content and complete Evidence
/// settled by one dependency inspection.
/// </summary>
internal sealed record DependsAssetProjection(
    DependencyInspectionSummary Summary,
    DependencyGraphDocument Graph,
    ImmutableArray<DependencyGraphEdgeRow> GraphRows,
    ImmutableArray<DependsRootRow> Roots,
    ImmutableArray<DependencyInspectionDependency> Dependencies,
    ImmutableArray<DependencyInspectionPruning> Pruning,
    ImmutableArray<DependencyEvidenceRestoredEdgeRow> RestoredEdges,
    ImmutableArray<DependencyInspectionFailure> Failures,
    ImmutableArray<DependencyEvidenceGroupRow> DependencyGroups,
    ImmutableArray<DependencyEvidenceRestoredPackageRow> RestoredPackages,
    DependencyInspectionEvidenceDocument Evidence)
{
    internal DependencyInspectionContent Content { get; } =
        new(
            ContentSummary(Summary),
            ContentGraph(Graph),
            [.. Roots.Select(static root => root.Content)],
            Dependencies,
            [.. Pruning.Select(ContentPruning)],
            [.. Failures.Select(ContentFailure)]);

    private static DependencyInspectionSummary ContentSummary(
        DependencyInspectionSummary summary) =>
        summary.PackagePrefix is { } prefix
            ? summary with
            {
                PackagePrefix =
                    new PackageDependencyEvidencePackagePrefixCompletion(
                        prefix.Prefix,
                        prefix.Source.WithoutRuntimeAssociation(),
                        prefix.Candidates,
                        prefix.Matches,
                        prefix.Failures,
                        prefix.TruncationReason),
            }
            : summary;

    private static DependencyGraphDocument ContentGraph(
        DependencyGraphDocument graph)
    {
        if (!graph.PackageProjections.Any(static projection =>
                projection.RuntimeCandidate is not null
                || !projection.RuntimeDiagnostics.IsEmpty
                || projection.Evidence is
                {
                    Provenance:
                        PackageDependencyEvidenceRootProvenance.Package
                        {
                            Source: not null,
                        },
                })
            && !graph.Edges.Any(static edge =>
                !edge.RuntimePackageDiagnostics.IsEmpty))
        {
            return graph;
        }

        return graph with
        {
            PackageProjections =
            [
                .. graph.PackageProjections.Select(static projection =>
                    projection with
                    {
                        Evidence = projection.Evidence is { } evidence
                            ? ContentRoot(evidence)
                            : null,
                        RuntimeCandidate = null,
                        RuntimeDiagnostics = [],
                    }),
            ],
            Edges =
            [
                .. graph.Edges.Select(static edge =>
                    edge with { RuntimePackageDiagnostics = [] }),
            ],
        };
    }

    private static DependencyInspectionPruning ContentPruning(
        DependencyInspectionPruning pruning) =>
        pruning with
        {
            Applicability = pruning.Applicability with
            {
                Root = ContentRoot(pruning.Applicability.Root),
            },
            RuntimeCandidateOutcome = null,
            RuntimeResult = null,
        };

    private static DependencyInspectionFailure ContentFailure(
        DependencyInspectionFailure failure) =>
        failure switch
        {
            DependencyInspectionFailure.Evidence evidence =>
                new DependencyInspectionFailure.Evidence(
                    evidence.Value with
                    {
                        Source = evidence.Value.Source?
                            .WithoutRuntimeAssociation(),
                    }),
            DependencyInspectionFailure.Traversal traversal =>
                new DependencyInspectionFailure.Traversal(
                    traversal.Value with
                    {
                        RuntimeCandidateOutcome = null,
                        RuntimeManifestFailure = null,
                    }),
            DependencyInspectionFailure.Pruning
            {
                Value: DependencyInspectionPruningFailure.Candidate candidate,
            } => new DependencyInspectionFailure.Pruning(
                candidate with { RuntimeOutcome = null }),
            _ => failure,
        };

    private static PackageDependencyEvidenceRoot ContentRoot(
        PackageDependencyEvidenceRoot root)
    {
        if (root.Provenance is not
            PackageDependencyEvidenceRootProvenance.Package
            {
                Source: { } source,
            } package)
        {
            return root;
        }

        return new PackageDependencyEvidenceRoot(
            root.Identity,
            package with
            {
                Source = source.WithoutRuntimeAssociation(),
            },
            root.Display,
            root.Declaration,
            root.Selection,
            root.RestoredTarget,
            root.Relationships,
            root.Processing,
            root.RuntimeTarget);
    }
}
