using System.Collections.Immutable;
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
    ImmutableArray<DependencyInspectionFailure> Failures)
{
    public bool Equals(DependencyInspectionContent? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Summary == other.Summary
        && Graph == other.Graph
        && DependencyValueEquality.SequenceEqual(Roots, other.Roots)
        && DependencyValueEquality.SequenceEqual(
            Dependencies,
            other.Dependencies)
        && DependencyValueEquality.SequenceEqual(Pruning, other.Pruning)
        && DependencyValueEquality.SequenceEqual(Failures, other.Failures);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Summary);
        hash.Add(Graph);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Roots);
        DependencyValueEquality.AddSequenceHashCode(
            ref hash,
            Dependencies);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Pruning);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Failures);
        return hash.ToHashCode();
    }
}

internal static class DependencyValueEquality
{
    internal static bool SequenceEqual<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
    {
        if (left.IsDefault || right.IsDefault)
            return left.IsDefault && right.IsDefault;

        return left.SequenceEqual(right, EqualityComparer<T>.Default);
    }

    internal static void AddSequenceHashCode<T>(
        ref HashCode hash,
        ImmutableArray<T> values)
    {
        if (values.IsDefault)
        {
            hash.Add(0);
            return;
        }

        hash.Add(values.Length);
        foreach (T value in values)
            hash.Add(value, EqualityComparer<T>.Default);
    }
}
