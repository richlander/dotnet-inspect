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
    bool Licenses,
    bool Pruning,
    bool SupplementalEvidence)
{
    internal bool PackageTraversal => Traversal || Licenses;

    internal static DependsAssetRequestPlan FromSections(
        IReadOnlySet<string> sections)
    {
        bool failures = sections.Contains(DependsAssetSections.Failures);
        bool dependencies =
            sections.Contains(DependsAssetSections.Dependencies);
        bool pruning =
            sections.Contains(DependsAssetSections.Pruning);
        bool licenses =
            sections.Contains(DependsAssetSections.Licenses);
        return new DependsAssetRequestPlan(
            Declarations: dependencies
                || sections.Contains(DependsAssetSections.DependencyGroups)
                || failures
                || pruning
                || licenses,
            RestoredRelationships: dependencies
                || sections.Contains(DependsAssetSections.RestoredEdges)
                || sections.Contains(DependsAssetSections.RestoredPackages)
                || failures
                || licenses,
            Traversal:
                sections.Contains(DependsAssetSections.DependencyHierarchy),
            Licenses: licenses,
            Pruning: pruning,
            SupplementalEvidence:
                sections.Contains(DependsAssetSections.Roots)
                || sections.Contains(DependsAssetSections.RestoredEdges)
                || sections.Contains(DependsAssetSections.DependencyGroups)
                || sections.Contains(DependsAssetSections.RestoredPackages));
    }
}

/// <summary>
/// CLI root presentation over one host-neutral dependency root.
/// </summary>
internal sealed record DependsRootRow
{
    internal DependsRootRow(
        DependencyInspectionRoot content,
        string source,
        string? identityKind,
        InertString? identity)
    {
        Content = content
            ?? throw new ArgumentNullException(nameof(content));
        IdentityKind = identityKind;
        Identity = identity;
        Source = source;
    }

    internal DependencyInspectionRoot Content { get; }

    internal int Occurrence => Content.Identity.Value;

    internal DependencyInspectionRootKind Kind => Content.Kind;

    internal InertString Input => Content.Input;

    internal string Source { get; }

    internal DependencyInspectionRootState State => Content.State;

    internal string? IdentityKind { get; }

    internal InertString? Identity { get; }

    internal DependencyGraphNodeIdentity? DependencyIdentity =>
        Content.DependencyIdentity;

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

    internal InertString? RequestedFramework => Content.RequestedFramework;

    internal InertString? SelectedFramework => Content.SelectedFramework;
}

/// <summary>
/// CLI presentation values plus the reusable Content and complete Evidence
/// settled by one dependency inspection.
/// </summary>
internal sealed record DependsAssetProjection(
    InspectionEnvelope<DependencyInspectionContent> Inspection,
    DependencyInspectionSummary Summary,
    DependencyGraphDocument Graph,
    DependencyHierarchyDocument Hierarchy,
    ImmutableArray<DependencyHierarchyOccurrenceRow> HierarchyRows,
    ImmutableArray<DependsRootRow> Roots,
    ImmutableArray<DependencyInspectionDependency> Dependencies,
    ImmutableArray<DependencyInspectionPruning> Pruning,
    ImmutableArray<DependencyEvidenceRestoredEdgeRow> RestoredEdges,
    ImmutableArray<DependencyInspectionFailure> Failures,
    ImmutableArray<DependencyEvidenceGroupRow> DependencyGroups,
    ImmutableArray<DependencyEvidenceRestoredPackageRow> RestoredPackages,
    EvidenceInspectionEnvelope<
        DependencyInspectionContent,
        DependencyInspectionEvidenceDocument>? Enriched)
{
    internal DependencyInspectionContent Content => Inspection.Content;

    internal ImmutableArray<DependencyInspectionLicense> Licenses =>
        Content.Licenses;

    internal DependencyInspectionEvidenceDocument? Evidence =>
        Enriched?.Evidence;
}
