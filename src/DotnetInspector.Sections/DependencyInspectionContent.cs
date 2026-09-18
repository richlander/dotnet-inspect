using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum DependencyInspectionRootKind
{
    Package,
    Nuspec,
    Library,
    Project,
}

public enum DependencyInspectionRootSetCompletion
{
    Complete,
    Partial,
    Failed,
}

public enum DependencyInspectionTraversalCompletion
{
    Complete,
    DepthBounded,
    SourceBounded,
    Partial,
    Failed,
    NotRequested,
}

public enum DependencyInspectionPruningCompletion
{
    NotRequested,
    Complete,
    SourceBounded,
    Partial,
    Failed,
}

public enum DependencyInspectionRootState
{
    Admitted,
    Failed,
}

public enum DependencyInspectionEvidencePhaseCompletion
{
    NotRequested,
    NotApplicable,
    Complete,
    Partial,
    Unavailable,
    Failed,
}

public enum DependencyInspectionEvidenceAvailability
{
    NotRequested,
    NotApplicable,
    Available,
    Unavailable,
    Failed,
}

public enum DependencyInspectionSelectionStatus
{
    NotRequested,
    NotApplicable,
    Selected,
    NoDependencyGroups,
    NoMatchingTargetFramework,
    Unavailable,
}

public sealed record DependencyInspectionRoot(
    DependencyRootOccurrenceIdentity Identity,
    DependencyInspectionRootKind Kind,
    InertString Input,
    DependencyInspectionRootState State,
    DependencyGraphNodeIdentity? GraphIdentity,
    DependencyInspectionTraversalCompletion Traversal,
    DependencyInspectionEvidenceAvailability DeclarationState,
    DependencyInspectionEvidencePhaseCompletion DeclarationCompletion,
    DependencyInspectionSelectionStatus Selection,
    DependencyInspectionEvidenceAvailability RestoredRelationshipState,
    DependencyInspectionEvidencePhaseCompletion
        RestoredRelationshipCompletion)
{
    public InertString? RequestedFramework { get; init; }

    public InertString? SelectedFramework { get; init; }
}

public sealed record DependencyInspectionDependency(
    DependencyEvidenceDependencyRow Declaration,
    string? ResolvedVersion,
    RestoredProjectPackageNodeIdentity? ResolvedPackageIdentity,
    RestoredProjectEdgeIdentity? ResolvedRelationshipIdentity);

public enum DependencyInspectionPruningDisposition
{
    PlatformDelegation,
    PackageRetained,
    NotEvaluated,
    SourceBounded,
    CandidateUnavailable,
    InventoryUnavailable,
}

public sealed record DependencyInspectionPruning(
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
    DependencyInspectionPruningDisposition Disposition,
    string Reason,
    PackageHouseDependencyPruningApplicability Applicability,
    PackageDependencyCandidateResult? CandidateOutcome,
    PackageHouseDependencyPruningResult? Result);

public sealed record DependencyInspectionPruningSummary(
    DependencyInspectionPruningCompletion Completion,
    int Roots,
    int Declarations,
    int Evaluated,
    int Delegated,
    int Retained,
    int NotEvaluated,
    int SourceBounded,
    int Failed)
{
    public static DependencyInspectionPruningSummary NotRequested { get; } =
        new(
            DependencyInspectionPruningCompletion.NotRequested,
            Roots: 0,
            Declarations: 0,
            Evaluated: 0,
            Delegated: 0,
            Retained: 0,
            NotEvaluated: 0,
            SourceBounded: 0,
            Failed: 0);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "case")]
[JsonDerivedType(
    typeof(DependencyInspectionRestoredTraversalFailure.Outcome),
    "outcome")]
[JsonDerivedType(
    typeof(DependencyInspectionRestoredTraversalFailure.Graph),
    "graph")]
public abstract record DependencyInspectionRestoredTraversalFailure
{
    private DependencyInspectionRestoredTraversalFailure()
    {
    }

    public sealed record Outcome(
        RestoredProjectDependencyTraversalFailure Value) :
        DependencyInspectionRestoredTraversalFailure;

    public sealed record Graph(RestoredProjectGraphFailure Value) :
        DependencyInspectionRestoredTraversalFailure;
}

public sealed record DependencyInspectionAssemblyBindingFailure(
    AssemblyBindingMissDisposition Disposition,
    AssemblyReferenceIdentity RequestedAssembly);

public sealed record DependencyInspectionTraversalFailure(
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
    DependencyInspectionRestoredTraversalFailure? RestoredFailure,
    ImmutableArray<int> AffectedRootOccurrences,
    DependencyInspectionAssemblyBindingFailure? AssemblyBindingFailure = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "case")]
[JsonDerivedType(typeof(DependencyInspectionFailure.Evidence), "evidence")]
[JsonDerivedType(typeof(DependencyInspectionFailure.Traversal), "traversal")]
[JsonDerivedType(typeof(DependencyInspectionFailure.Pruning), "pruning")]
public abstract record DependencyInspectionFailure
{
    private DependencyInspectionFailure()
    {
    }

    public sealed record Evidence(DependencyEvidenceFailureRow Value) :
        DependencyInspectionFailure;

    public sealed record Traversal(DependencyInspectionTraversalFailure Value) :
        DependencyInspectionFailure;

    public sealed record Pruning(DependencyInspectionPruningFailure Value) :
        DependencyInspectionFailure;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "case")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningFailure.Inventory),
    "inventory")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningFailure.Prerequisite),
    "prerequisite")]
[JsonDerivedType(
    typeof(DependencyInspectionPruningFailure.Candidate),
    "candidate")]
public abstract record DependencyInspectionPruningFailure
{
    private DependencyInspectionPruningFailure()
    {
    }

    public sealed record Inventory(
        string PlatformFamily,
        string TargetFramework,
        InertString Message,
        ImmutableArray<int> AffectedRootOccurrences,
        int AffectedDeclarations) : DependencyInspectionPruningFailure;

    public sealed record Prerequisite(
        int RootOccurrence,
        PackageDependencyEvidenceRootIdentity RootIdentity,
        InertString RootDisplay,
        DependencyEvidenceDeclarationState DeclarationState,
        InertString Message) : DependencyInspectionPruningFailure;

    public sealed record Candidate(
        int RootOccurrence,
        PackageDependencyEvidenceRootIdentity RootIdentity,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint,
        PackageDependencyCandidateResult Outcome) :
        DependencyInspectionPruningFailure;
}

public sealed record DependencyInspectionSummary(
    DependencyInspectionRootSetCompletion RootSetCompletion,
    int RequestedRoots,
    int AdmittedRoots,
    int FailedRoots,
    DependencyInspectionTraversalCompletion TraversalCompletion,
    int? RequestedDepth,
    int GraphNodes,
    int GraphEdges,
    DependencyInspectionEvidencePhaseCompletion DeclarationCompletion,
    DependencyInspectionEvidencePhaseCompletion
        RestoredRelationshipCompletion,
    DependencyInspectionPruningSummary Pruning,
    bool IsPrefixRootSet,
    PackageDependencyEvidencePackagePrefixCompletion? PackagePrefix);

public sealed record DependencyInspectionContent(
    DependencyInspectionSummary Summary,
    DependencyGraphDocument Graph,
    ImmutableArray<DependencyInspectionRoot> Roots,
    ImmutableArray<DependencyInspectionDependency> Dependencies,
    ImmutableArray<DependencyInspectionPruning> Pruning,
    ImmutableArray<DependencyInspectionFailure> Failures);
