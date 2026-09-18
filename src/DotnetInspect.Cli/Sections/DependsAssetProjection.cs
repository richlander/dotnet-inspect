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
    internal DependencyInspectionResult Result { get; } =
        new(
            new DependencyInspectionContent(
                Summary,
                Graph,
                [.. Roots.Select(static root => root.Content)],
                Dependencies,
                Pruning,
                Failures),
            Evidence);

    internal DependencyInspectionContent Content => Result.Content;
}
