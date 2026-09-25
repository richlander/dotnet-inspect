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

public enum DependencyInspectionLicenseCompletion
{
    NotRequested,
    Complete,
    Partial,
}

public enum DependencyInspectionLicenseState
{
    Available,
    Unavailable,
}

public enum DependencyInspectionLicenseIdentityKind
{
    None,
    Expression,
    RecognizedFile,
    Unknown,
}

public enum DependencyInspectionLicenseDeclarationKind
{
    Expression,
    File,
    Url,
}

public sealed record DependencyInspectionLicense(
    string PackageId,
    string PackageVersion,
    DependencyInspectionLicenseState State,
    InertString License,
    DependencyInspectionLicenseIdentityKind? IdentityKind,
    DependencyInspectionLicenseDeclarationKind? DeclarationKind,
    InertString? DeclarationValue,
    PackageLicenseInventoryFailureReason? FailureReason,
    [property: JsonConverter(typeof(ProseInertStringJsonConverter))]
    InertString? FailureMessage)
{
    public static DependencyInspectionLicense Create(
        PackageLicenseInventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.License is { } license)
        {
            return new(
                item.Coordinate.PackageId,
                item.Coordinate.Version,
                DependencyInspectionLicenseState.Available,
                new InertString(TextPolicy.Field, license.Value),
                license.Kind switch
                {
                    PackageLicenseIdentityKind.None =>
                        DependencyInspectionLicenseIdentityKind.None,
                    PackageLicenseIdentityKind.Expression =>
                        DependencyInspectionLicenseIdentityKind.Expression,
                    PackageLicenseIdentityKind.RecognizedFile =>
                        DependencyInspectionLicenseIdentityKind.RecognizedFile,
                    PackageLicenseIdentityKind.Unknown =>
                        DependencyInspectionLicenseIdentityKind.Unknown,
                    _ => throw new InvalidOperationException(
                        "Unknown package license identity kind."),
                },
                item.Declaration?.Kind switch
                {
                    DotnetInspector.Packages.PackageLicenseDeclarationKind
                        .Expression =>
                            DependencyInspectionLicenseDeclarationKind
                                .Expression,
                    DotnetInspector.Packages.PackageLicenseDeclarationKind
                        .File =>
                            DependencyInspectionLicenseDeclarationKind.File,
                    DotnetInspector.Packages.PackageLicenseDeclarationKind
                        .Url =>
                            DependencyInspectionLicenseDeclarationKind.Url,
                    null => null,
                    _ => throw new InvalidOperationException(
                        "Unknown package license declaration kind."),
                },
                item.Declaration is { } declaration
                    ? new InertString(TextPolicy.Field, declaration.Value)
                    : null,
                FailureReason: null,
                FailureMessage: null);
        }

        PackageLicenseInventoryFailure failure =
            item.Failure
            ?? throw new InvalidOperationException(
                "An unavailable license item requires failure evidence.");
        return new(
            item.Coordinate.PackageId,
            item.Coordinate.Version,
            DependencyInspectionLicenseState.Unavailable,
            new InertString(TextPolicy.Field, "unavailable"),
            IdentityKind: null,
            DeclarationKind: null,
            DeclarationValue: null,
            failure.Reason,
            new InertString(TextPolicy.Prose, failure.Message));
    }
}

public sealed record DependencyInspectionLicenseSummary(
    DependencyInspectionLicenseCompletion Completion,
    int Packages,
    int Available,
    int Unavailable)
{
    public static DependencyInspectionLicenseSummary NotRequested { get; } =
        new(
            DependencyInspectionLicenseCompletion.NotRequested,
            Packages: 0,
            Available: 0,
            Unavailable: 0);
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
    DependencyGraphNodeIdentity? DependencyIdentity,
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
    DependencyInspectionPackageCandidateOutcome? CandidateOutcome,
    DependencyInspectionPruningResult? Result)
{
    [JsonIgnore]
    public PackageDependencyCandidateResult? RuntimeCandidateOutcome
    { get; init; }

    [JsonIgnore]
    public PackageHouseDependencyPruningResult? RuntimeResult { get; init; }
}

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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
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
        DependencyInspectionRestoredTraversalOutcomeFailure Value) :
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
    DependencyInspectionPackageCandidateOutcome? CandidateOutcome,
    DependencyInspectionPackageManifestFailure? ManifestFailure,
    PackageDependencyTraversalWorkBudgetKind? BudgetKind,
    int? BudgetLimit,
    DependencyInspectionRestoredTraversalFailure? RestoredFailure,
    ImmutableArray<int> AffectedRootOccurrences,
    DependencyInspectionAssemblyBindingFailure? AssemblyBindingFailure = null)
{
    public ImmutableArray<int> AffectedRootOccurrences { get; init; } =
        AffectedRootOccurrences.IsDefault ? [] : AffectedRootOccurrences;

    [JsonIgnore]
    public PackageDependencyTraversalCandidateResult? RuntimeCandidateOutcome
    { get; init; }

    [JsonIgnore]
    public PackageDependencyTraversalManifestFailureDetail?
        RuntimeManifestFailure
    { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
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
        [property: JsonConverter(typeof(ProseInertStringJsonConverter))]
        InertString Message,
        ImmutableArray<int> AffectedRootOccurrences,
        int AffectedDeclarations) : DependencyInspectionPruningFailure
    {
        public ImmutableArray<int> AffectedRootOccurrences { get; init; } =
            AffectedRootOccurrences.IsDefault ? [] : AffectedRootOccurrences;
    }

    public sealed record Prerequisite(
        int RootOccurrence,
        PackageDependencyEvidenceRootIdentity RootIdentity,
        InertString RootDisplay,
        DependencyEvidenceDeclarationState DeclarationState,
        [property: JsonConverter(typeof(ProseInertStringJsonConverter))]
        InertString Message) : DependencyInspectionPruningFailure;

    public sealed record Candidate(
        int RootOccurrence,
        PackageDependencyEvidenceRootIdentity RootIdentity,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint,
        DependencyInspectionPackageCandidateOutcome Outcome) :
        DependencyInspectionPruningFailure
    {
        [JsonIgnore]
        public PackageDependencyCandidateResult? RuntimeOutcome { get; init; }
    }
}

public sealed record DependencyInspectionSummary(
    DependencyInspectionRootSetCompletion RootSetCompletion,
    int RequestedRoots,
    int AdmittedRoots,
    int FailedRoots,
    DependencyInspectionTraversalCompletion TraversalCompletion,
    int? RequestedDepth,
    int HierarchyOccurrences,
    int CanonicalNodes,
    int Relationships,
    DependencyInspectionEvidencePhaseCompletion DeclarationCompletion,
    DependencyInspectionEvidencePhaseCompletion
        RestoredRelationshipCompletion,
    DependencyInspectionPruningSummary Pruning,
    bool IsPrefixRootSet,
    PackageDependencyEvidencePackagePrefixCompletion? PackagePrefix)
{
    public DependencyInspectionLicenseSummary Licenses { get; init; } =
        DependencyInspectionLicenseSummary.NotRequested;
}

public sealed record DependencyInspectionContent(
    DependencyInspectionSummary Summary,
    DependencyHierarchyDocument Hierarchy,
    ImmutableArray<DependencyInspectionRoot> Roots,
    ImmutableArray<DependencyInspectionDependency> Dependencies,
    ImmutableArray<DependencyInspectionPruning> Pruning,
    ImmutableArray<DependencyInspectionFailure> Failures)
{
    public ImmutableArray<DependencyInspectionRoot> Roots { get; init; } =
        Roots.IsDefault ? [] : Roots;

    public ImmutableArray<DependencyInspectionDependency> Dependencies
    { get; init; } = Dependencies.IsDefault ? [] : Dependencies;

    public ImmutableArray<DependencyInspectionPruning> Pruning { get; init; } =
        Pruning.IsDefault ? [] : Pruning;

    public ImmutableArray<DependencyInspectionFailure> Failures { get; init; } =
        Failures.IsDefault ? [] : Failures;

    public ImmutableArray<DependencyInspectionLicense> Licenses { get; init; } =
        [];

    public bool Equals(DependencyInspectionContent? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Summary == other.Summary
        && Hierarchy == other.Hierarchy
        && DependencyValueEquality.SequenceEqual(Roots, other.Roots)
        && DependencyValueEquality.SequenceEqual(
            Dependencies,
            other.Dependencies)
        && DependencyValueEquality.SequenceEqual(Pruning, other.Pruning)
        && DependencyValueEquality.SequenceEqual(Licenses, other.Licenses)
        && DependencyValueEquality.SequenceEqual(Failures, other.Failures);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Summary);
        hash.Add(Hierarchy);
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
