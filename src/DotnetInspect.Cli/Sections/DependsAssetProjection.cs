using System.Collections.Immutable;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Sections;

/// <summary>The aggregate root-set state for one asset-mode dependency document.</summary>
internal enum DependsRootSetCompletion
{
    Complete,
    Partial,
    Failed,
}

/// <summary>The requested dependency traversal phase's terminal state.</summary>
internal enum DependsTraversalCompletion
{
    Complete,
    DepthBounded,
    SourceBounded,
    Partial,
    Failed,
    NotRequested,
}

/// <summary>The selected pruning-policy phase's terminal state.</summary>
internal enum DependsPruningCompletion
{
    NotRequested,
    Complete,
    SourceBounded,
    Partial,
    Failed,
}

/// <summary>Whether one explicit root occurrence established semantic identity.</summary>
internal enum DependsRootState
{
    Admitted,
    Failed,
}

/// <summary>The selected-plan state of one evidence phase.</summary>
internal enum DependsEvidencePhaseCompletion
{
    NotRequested,
    NotApplicable,
    Complete,
    Partial,
    Unavailable,
    Failed,
}

/// <summary>Whether an evidence phase produced an applicable value.</summary>
internal enum DependsEvidenceAvailability
{
    NotRequested,
    NotApplicable,
    Available,
    Unavailable,
    Failed,
}

/// <summary>The selected-plan package declaration-group selection state.</summary>
internal enum DependsSelectionStatus
{
    NotRequested,
    NotApplicable,
    Selected,
    NoDependencyGroups,
    NoMatchingTargetFramework,
    Unavailable,
}

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

/// <summary>One explicit root occurrence in the unified dependency document.</summary>
internal sealed record DependsRootRow(
    int Occurrence,
    DependsAssetRootKind Kind,
    InertString Input,
    string Source,
    DependsRootState State,
    string? IdentityKind,
    InertString? Identity,
    DependsTraversalCompletion Traversal,
    DependsEvidenceAvailability DeclarationState,
    DependsEvidencePhaseCompletion DeclarationCompletion,
    DependsSelectionStatus Selection,
    DependsEvidenceAvailability RestoredRelationshipState,
    DependsEvidencePhaseCompletion RestoredRelationshipCompletion)
{
    internal DependencyGraphNodeIdentity? GraphIdentity { get; init; }

    internal DependencyEvidenceRootRow? Evidence { get; init; }

    internal PackageDependencyEvidenceGroupIdentity? SelectedGroup
        { get; init; }

    internal int? SelectedGroupIndex { get; init; }

    internal PackageDependencyEvidenceGroupOccurrence?
        SelectedSourceOccurrence { get; init; }

    internal InertString? RequestedFramework { get; init; }

    internal InertString? SelectedFramework { get; init; }
}

/// <summary>
/// One declaration plus an owner-issued restored association when the explicit
/// restored root established one.
/// </summary>
internal sealed record DependsDependencyRow(
    DependencyEvidenceDependencyRow Declaration,
    string? ResolvedVersion,
    RestoredProjectPackageNodeIdentity? ResolvedPackageIdentity,
    RestoredProjectEdgeIdentity? ResolvedRelationshipIdentity);

/// <summary>How the CLI could answer one selected declaration's pruning question.</summary>
internal enum DependsPruningDisposition
{
    PlatformDelegation,
    PackageRetained,
    NotEvaluated,
    SourceBounded,
    CandidateUnavailable,
    InventoryUnavailable,
}

/// <summary>One selected direct declaration's typed pruning-policy projection.</summary>
internal sealed record DependsPruningRow(
    int RootOccurrence,
    PackageDependencyEvidenceRootIdentity RootIdentity,
    InertString RootDisplay,
    PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
    InertString RequestedFramework,
    InertString SelectedFramework,
    string PackageId,
    InertString PackageIdSpelling,
    string VersionConstraint,
    InertString VersionConstraintSpelling,
    string? CandidateVersion,
    string? PlatformFamily,
    string? PlatformTargetFramework,
    string? PlatformVersion,
    string? PlatformProvidedVersion,
    DependsPruningDisposition Disposition,
    string Reason,
    PackageHouseDependencyPruningApplicability Applicability,
    PackageDependencyCandidateResult? CandidateOutcome,
    PackageHouseDependencyPruningResult? Result);

/// <summary>Unfiltered accounting for the selected pruning-policy phase.</summary>
internal sealed record DependsPruningSummary(
    DependsPruningCompletion Completion,
    int Roots,
    int Declarations,
    int Evaluated,
    int Delegated,
    int Retained,
    int NotEvaluated,
    int SourceBounded,
    int Failed)
{
    internal static DependsPruningSummary NotRequested { get; } =
        new(
            DependsPruningCompletion.NotRequested,
            Roots: 0,
            Declarations: 0,
            Evaluated: 0,
            Delegated: 0,
            Retained: 0,
            NotEvaluated: 0,
            SourceBounded: 0,
            Failed: 0);
}

/// <summary>Exact restored-project failure detail retained by the traversal phase.</summary>
internal abstract record DependsRestoredTraversalFailure
{
    private DependsRestoredTraversalFailure()
    {
    }

    internal sealed record Outcome(
        RestoredProjectDependencyTraversalFailure Value) :
        DependsRestoredTraversalFailure;

    internal sealed record Graph(RestoredProjectGraphFailure Value) :
        DependsRestoredTraversalFailure;
}

/// <summary>One unresolved assembly binding retained by library traversal.</summary>
internal sealed record DependsAssemblyBindingFailure(
    AssemblyBindingMissDisposition Disposition,
    AssemblyReferenceIdentity RequestedAssembly);

/// <summary>One traversal failure with its complete owner-issued detail.</summary>
internal sealed record DependsTraversalFailureRow(
    string Reason,
    int? SourceProjectionIndex,
    int? NodeIndex,
    int? ProjectionIndex,
    PackageDependencyEvidenceDeclarationIdentity? DeclarationIdentity,
    string? PackageId,
    string? VersionConstraint,
    PackageDependencyTraversalCandidateResult? CandidateOutcome,
    PackageDependencyTraversalManifestFailureDetail? ManifestFailure,
    PackageDependencyTraversalWorkBudgetKind? BudgetKind,
    int? BudgetLimit,
    DependsRestoredTraversalFailure? RestoredFailure,
    ImmutableArray<int> AffectedRootOccurrences,
    DependsAssemblyBindingFailure? AssemblyBindingFailure = null);

/// <summary>A root/evidence failure or a package-traversal failure.</summary>
internal abstract record DependsFailureRow
{
    private DependsFailureRow()
    {
    }

    internal sealed record Evidence(DependencyEvidenceFailureRow Value) :
        DependsFailureRow;

    internal sealed record Traversal(DependsTraversalFailureRow Value) :
        DependsFailureRow;

    internal sealed record Pruning(DependsPruningFailure Value) :
        DependsFailureRow;
}

/// <summary>A producer failure encountered only by the selected pruning phase.</summary>
internal abstract record DependsPruningFailure
{
    private DependsPruningFailure()
    {
    }

    internal sealed record Inventory(
        string PlatformFamily,
        string TargetFramework,
        InertString Message,
        ImmutableArray<int> AffectedRootOccurrences,
        int AffectedDeclarations) : DependsPruningFailure;

    internal sealed record Candidate(
        int RootOccurrence,
        PackageDependencyEvidenceRootIdentity RootIdentity,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint,
        PackageDependencyCandidateResult Outcome) : DependsPruningFailure;
}

/// <summary>Mandatory document fields for root-set and requested-phase completion.</summary>
internal sealed record DependsAssetSummary(
    DependsRootSetCompletion RootSetCompletion,
    int RequestedRoots,
    int AdmittedRoots,
    int FailedRoots,
    DependsTraversalCompletion TraversalCompletion,
    int? RequestedDepth,
    int GraphNodes,
    int GraphEdges,
    DependsEvidencePhaseCompletion DeclarationCompletion,
    DependsEvidencePhaseCompletion RestoredRelationshipCompletion,
    DependsPruningSummary Pruning,
    bool IsPrefixRootSet,
    PackageDependencyEvidencePackagePrefixCompletion? PackagePrefix);

/// <summary>
/// One immutable CLI projection over ordered roots, graph traversal, and direct evidence.
/// </summary>
internal sealed record DependsAssetProjection(
    DependsAssetSummary Summary,
    DependencyGraphDocument Graph,
    ImmutableArray<DependencyGraphEdgeRow> GraphRows,
    ImmutableArray<DependsRootRow> Roots,
    ImmutableArray<DependsDependencyRow> Dependencies,
    ImmutableArray<DependsPruningRow> Pruning,
    ImmutableArray<DependencyEvidenceRestoredEdgeRow> RestoredEdges,
    ImmutableArray<DependsFailureRow> Failures,
    ImmutableArray<DependencyEvidenceGroupRow> DependencyGroups,
    ImmutableArray<DependencyEvidenceRestoredPackageRow> RestoredPackages);
