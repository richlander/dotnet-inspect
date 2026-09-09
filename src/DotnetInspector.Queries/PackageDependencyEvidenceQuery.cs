using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The semantic altitude of one admitted package input.</summary>
public enum PackageDependencyEvidenceInputKind
{
    PackageManifest = 0,
    RestoredProject = 1,
    AuthoredProject = 2,
    RuntimeDependencyManifest = 3,
}

/// <summary>How one root's already-acquired facts reached this composition query.</summary>
public enum PackageDependencyEvidenceAcquisitionForm
{
    PackageArchive,
    DirectNuspec,
    PackageSourceManifest,
    ProjectAssets,
    ProjectLocator,
}

/// <summary>The fidelity basis claimed by one root's declaration phase.</summary>
public enum PackageDependencyEvidenceDeclarationBasis
{
    PackageManifest,
    AuthoredProjectSyntax,
    EvaluatedProject,
    RestoredProject,
    NotApplicable,
}

/// <summary>Who authored the declaration associated with one evidence row.</summary>
public enum PackageDependencyEvidenceAuthorship
{
    ApplicationAuthored,
    LibraryDeclared,
    Unattributed,
}

/// <summary>Whether every fact exposed by one phase was projected.</summary>
public enum PackageDependencyEvidencePhaseCompletion
{
    Complete,
    Incomplete,
}

/// <summary>Whether the caller supplied an exhaustive root set.</summary>
public enum PackageDependencyEvidenceRootSetCompletion
{
    Complete,
    Incomplete,
}

/// <summary>One already-projected root supplied to the composition query.</summary>
public abstract record PackageDependencyEvidenceInput
{
    private PackageDependencyEvidenceInput()
    {
    }

    /// <summary>Package-manifest facts plus their owner-issued group-selection outcome.</summary>
    public sealed record Package(
        PackageManifestFacts Manifest,
        PackageDependencyGroups Groups,
        PackageDependencyEvidenceAcquisitionForm AcquisitionForm,
        InertString? SourceLabel = null,
        PackageSourceResultIdentity? Source = null) :
        PackageDependencyEvidenceInput;

    /// <summary>Restored-project declaration and graph facts.</summary>
    public sealed record RestoredProject(
        RestoredProjectDependencyFacts Facts,
        PackageDependencyEvidenceAcquisitionForm AcquisitionForm,
        InertString? SourceLabel = null) : PackageDependencyEvidenceInput;
}

/// <summary>One typed upstream failure for a root that could not be admitted.</summary>
public abstract record PackageDependencyEvidenceRootFailure
{
    private PackageDependencyEvidenceRootFailure()
    {
    }

    public sealed record Package(
        PackageDependencyEvidenceAcquisitionForm AcquisitionForm,
        PackageSourceCoordinate? Coordinate,
        PackageManifestFailure Failure,
        InertString? SourceLabel = null) : PackageDependencyEvidenceRootFailure;

    public sealed record RestoredProject(
        PackageDependencyEvidenceAcquisitionForm AcquisitionForm,
        RestoredProjectDependencyFailure Failure,
        InertString? SourceLabel = null) : PackageDependencyEvidenceRootFailure;

    public sealed record PackageProfile(
        PackageSourceResultIdentity Source,
        PackageProfileFailureKind Kind,
        PackageManifestFailureReason? ManifestFailureReason,
        PackageSourceCoordinate? Coordinate,
        InertString? PackageId,
        InertString? Version,
        InertString Message) : PackageDependencyEvidenceRootFailure;

    public sealed record Acquisition(
        PackageDependencyEvidenceAcquisitionForm AcquisitionForm,
        PackageDependencyEvidenceAcquisitionFailureReason Reason,
        PackageSourceCoordinate? Coordinate = null,
        InertString? SourceLabel = null) : PackageDependencyEvidenceRootFailure;
}

/// <summary>Why an input could not supply bytes or typed facts to this query.</summary>
public enum PackageDependencyEvidenceAcquisitionFailureReason
{
    NotFound,
    NotRestored,
    SourceUnavailable,
    ProducerContract,
    AcquisitionFailed,
}

/// <summary>Typed terminal accounting retained from one package-prefix query.</summary>
public sealed record PackageDependencyEvidencePackagePrefixCompletion
{
    public PackageDependencyEvidencePackagePrefixCompletion(
        InertString prefix,
        PackageSourceResultIdentity source,
        int candidates,
        int matches,
        int failures,
        PackageSearchTruncationReason truncationReason)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidates);
        ArgumentOutOfRangeException.ThrowIfNegative(matches);
        ArgumentOutOfRangeException.ThrowIfNegative(failures);
        if (matches > candidates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matches),
                matches,
                "Package-profile matches cannot exceed candidate count.");
        }

        Prefix = prefix;
        Source = source;
        Candidates = candidates;
        Matches = matches;
        Failures = failures;
        TruncationReason = truncationReason;
    }

    public InertString Prefix { get; }

    public PackageSourceResultIdentity Source { get; }

    public int Candidates { get; }

    public int Matches { get; }

    public int Failures { get; }

    public PackageSearchTruncationReason TruncationReason { get; }
}

/// <summary>All admitted and rejected inputs for one normalized dependency-evidence outcome.</summary>
public sealed record PackageDependencyEvidenceRequest
{
    public PackageDependencyEvidenceRequest(
        ImmutableArray<PackageDependencyEvidenceInput> roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure> failedRoots = default,
        int rejectedRootCount = 0,
        bool isTruncated = false,
        PackageDependencyEvidencePackagePrefixCompletion?
            packagePrefixCompletion = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rejectedRootCount);
        if (packagePrefixCompletion is not null
            && isTruncated
                != (packagePrefixCompletion.TruncationReason
                    != PackageSearchTruncationReason.None))
        {
            throw new ArgumentException(
                "Package-prefix truncation must agree with its terminal completion.",
                nameof(isTruncated));
        }

        Roots = roots.IsDefault ? [] : roots;
        FailedRoots = failedRoots.IsDefault ? [] : failedRoots;
        RejectedRootCount = rejectedRootCount;
        IsTruncated = isTruncated;
        PackagePrefixCompletion = packagePrefixCompletion;
    }

    public ImmutableArray<PackageDependencyEvidenceInput> Roots { get; }

    public ImmutableArray<PackageDependencyEvidenceRootFailure> FailedRoots { get; }

    public int RejectedRootCount { get; }

    public bool IsTruncated { get; }

    public PackageDependencyEvidencePackagePrefixCompletion?
        PackagePrefixCompletion { get; }
}

/// <summary>Stable identity for one admitted package or restored-project root.</summary>
public abstract record PackageDependencyEvidenceRootIdentity
{
    private PackageDependencyEvidenceRootIdentity()
    {
    }

    public sealed record Package(PackageSourceCoordinate Coordinate) :
        PackageDependencyEvidenceRootIdentity;

    public sealed record RestoredProject(RestoredProjectRootIdentity Identity) :
        PackageDependencyEvidenceRootIdentity;
}

/// <summary>Identity trust, content provenance, and acquisition form for one admitted root.</summary>
public abstract record PackageDependencyEvidenceRootProvenance
{
    private PackageDependencyEvidenceRootProvenance()
    {
    }

    public abstract PackageDependencyEvidenceAcquisitionForm AcquisitionForm
        { get; init; }

    public abstract InertString? SourceLabel { get; init; }

    public sealed record Package(
        PackageDependencyEvidenceAcquisitionForm AcquisitionForm,
        PackageManifestIdentityProvenance IdentityProvenance,
        InertString? SourceLabel,
        PackageSourceResultIdentity? Source) :
        PackageDependencyEvidenceRootProvenance;

    public sealed record RestoredProject(
        PackageDependencyEvidenceAcquisitionForm AcquisitionForm,
        RestoredProjectContentProvenance ContentProvenance,
        InertString? SourceLabel) : PackageDependencyEvidenceRootProvenance;
}

/// <summary>The semantic framework scope of one logical declaration group.</summary>
public enum PackageDependencyFrameworkScopeKind
{
    AnyFramework,
    ExactFramework,
    UnrecognizedFramework,
}

/// <summary>
/// Framework identity for matching plus inert source spelling for presentation.
/// </summary>
public sealed record PackageDependencyFrameworkScopeIdentity
{
    private readonly string? _opaqueIdentity;

    private PackageDependencyFrameworkScopeIdentity(
        PackageDependencyFrameworkScopeKind kind,
        string? canonicalFramework,
        string? opaqueIdentity,
        InertString sourceSpelling)
    {
        Kind = kind;
        CanonicalFramework = canonicalFramework;
        _opaqueIdentity = opaqueIdentity;
        SourceSpelling = sourceSpelling;
    }

    public PackageDependencyFrameworkScopeKind Kind { get; }

    /// <summary>Canonical NuGet short-folder spelling for an exact framework.</summary>
    public string? CanonicalFramework { get; }

    /// <summary>The retained source spelling; opaque comparison identity is never renderable.</summary>
    public InertString SourceSpelling { get; }

    internal string? OpaqueIdentity => _opaqueIdentity;

    internal static PackageDependencyFrameworkScopeIdentity Any(InertString sourceSpelling) =>
        new(PackageDependencyFrameworkScopeKind.AnyFramework, null, null, sourceSpelling);

    internal static PackageDependencyFrameworkScopeIdentity Exact(
        string canonicalFramework,
        InertString sourceSpelling) =>
        new(
            PackageDependencyFrameworkScopeKind.ExactFramework,
            canonicalFramework,
            null,
            sourceSpelling);

    internal static PackageDependencyFrameworkScopeIdentity Unrecognized(
        string opaqueIdentity,
        InertString sourceSpelling) =>
        new(
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework,
            null,
            opaqueIdentity,
            sourceSpelling);
}

/// <summary>Stable identity for one normalized logical declaration group.</summary>
public abstract record PackageDependencyEvidenceGroupIdentity
{
    private PackageDependencyEvidenceGroupIdentity()
    {
    }

    public sealed record Package(
        PackageDependencyEvidenceRootIdentity.Package Root,
        bool IsImplicitManifestGroup,
        int FirstSourceOccurrence) : PackageDependencyEvidenceGroupIdentity;

    public sealed record RestoredProject(
        RestoredProjectDeclarationGroupIdentity Identity) :
        PackageDependencyEvidenceGroupIdentity;
}

/// <summary>One owner-issued occurrence contributing to a logical declaration group.</summary>
public abstract record PackageDependencyEvidenceGroupOccurrence
{
    private PackageDependencyEvidenceGroupOccurrence()
    {
    }

    public sealed record Package(int SourceIndex) :
        PackageDependencyEvidenceGroupOccurrence;

    public sealed record RestoredProject(
        RestoredProjectDeclarationGroupIdentity Identity) :
        PackageDependencyEvidenceGroupOccurrence;
}

/// <summary>Identity for one successful declaration row.</summary>
public readonly record struct PackageDependencyEvidenceDeclarationIdentity(
    PackageDependencyEvidenceGroupIdentity Group,
    string CanonicalPackageId);

/// <summary>One normalized direct package declaration.</summary>
public sealed record PackageDependencyEvidenceDeclaration(
    PackageDependencyEvidenceDeclarationIdentity Identity,
    string CanonicalPackageId,
    string CanonicalVersionConstraint,
    InertString SourcePackageIdSpelling,
    InertString SourceVersionConstraintSpelling,
    int SourceOccurrenceCount,
    PackageDependencyEvidenceAuthorship Authorship)
{
    public int SourceOccurrenceCount { get; } = SourceOccurrenceCount >= 1
        ? SourceOccurrenceCount
        : throw new ArgumentOutOfRangeException(
            nameof(SourceOccurrenceCount),
            SourceOccurrenceCount,
            "A source occurrence count must be at least one.");
}

/// <summary>One normalized logical declaration group, including a valid empty group.</summary>
public sealed record PackageDependencyEvidenceGroup(
    PackageDependencyEvidenceGroupIdentity Identity,
    PackageDependencyFrameworkScopeIdentity FrameworkScope,
    ImmutableArray<PackageDependencyEvidenceGroupOccurrence> SourceOccurrences,
    string OrderKey,
    ImmutableArray<PackageDependencyEvidenceDeclaration> Declarations);

/// <summary>A typed reason one declaration projection is incomplete or failed.</summary>
public abstract record PackageDependencyEvidenceDeclarationFailure
{
    private PackageDependencyEvidenceDeclarationFailure()
    {
    }

    public sealed record ConflictingPackageDeclaration(
        PackageDependencyEvidenceGroupIdentity Group,
        string CanonicalPackageId,
        int SourceOccurrenceCount) : PackageDependencyEvidenceDeclarationFailure;

    public sealed record InvalidPackageDeclaration(
        PackageDependencyEvidenceGroupIdentity Group,
        int SourceOccurrenceCount) : PackageDependencyEvidenceDeclarationFailure
    {
        public int SourceOccurrenceCount { get; } = SourceOccurrenceCount >= 1
            ? SourceOccurrenceCount
            : throw new ArgumentOutOfRangeException(
                nameof(SourceOccurrenceCount),
                SourceOccurrenceCount,
                "A source occurrence count must be at least one.");
    }

    public sealed record RestoredProject(
        RestoredProjectDeclarationFailure Failure) :
        PackageDependencyEvidenceDeclarationFailure;
}

/// <summary>The closed normalized declaration projection for one root.</summary>
public abstract record PackageDependencyEvidenceDeclarationResult
{
    private PackageDependencyEvidenceDeclarationResult()
    {
    }

    public sealed record Available : PackageDependencyEvidenceDeclarationResult
    {
        public Available(
            ImmutableArray<PackageDependencyEvidenceGroup> groups,
            ImmutableArray<PackageDependencyEvidenceDeclarationFailure> failures,
            PackageDependencyEvidencePhaseCompletion completion)
        {
            if (failures.IsDefaultOrEmpty
                != (completion == PackageDependencyEvidencePhaseCompletion.Complete))
            {
                throw new ArgumentException(
                    "A complete declaration projection carries no failures and an incomplete projection carries at least one.",
                    nameof(completion));
            }

            Groups = groups.IsDefault ? [] : groups;
            Failures = failures.IsDefault ? [] : failures;
            Completion = completion;
        }

        public ImmutableArray<PackageDependencyEvidenceGroup> Groups { get; }

        public ImmutableArray<PackageDependencyEvidenceDeclarationFailure> Failures { get; }

        public PackageDependencyEvidencePhaseCompletion Completion { get; }

        public bool IsComplete =>
            Completion == PackageDependencyEvidencePhaseCompletion.Complete;
    }

    public sealed record NotApplicable :
        PackageDependencyEvidenceDeclarationResult;

    public sealed record Unavailable : PackageDependencyEvidenceDeclarationResult;

    public sealed record Failed(
        PackageDependencyEvidenceDeclarationFailure Failure) :
        PackageDependencyEvidenceDeclarationResult;
}

/// <summary>The owner-issued dependency-group selection status for one root.</summary>
public enum PackageDependencyEvidenceSelectionStatus
{
    Selected,
    NoDependencyGroups,
    NoMatchingTargetFramework,
    Unavailable,
}

/// <summary>Selection evidence retained separately from the complete declaration set.</summary>
public sealed record PackageDependencyEvidenceSelection(
    PackageDependencyEvidenceSelectionStatus Status,
    PackageDependencyEvidenceGroupIdentity? SelectedGroup,
    PackageDependencyEvidenceGroupOccurrence? SelectedSourceOccurrence,
    InertString? RequestedFramework,
    InertString? SelectedFramework);

/// <summary>Stable provider-issued identity for one resolved package node.</summary>
public abstract record PackageDependencyEvidencePackageIdentity
{
    private PackageDependencyEvidencePackageIdentity()
    {
    }

    public sealed record RestoredProject(
        RestoredProjectPackageNodeIdentity Identity) :
        PackageDependencyEvidencePackageIdentity;
}

/// <summary>The closed parent identity of one produced package relationship.</summary>
public abstract record PackageDependencyEvidenceRelationshipParentIdentity
{
    private PackageDependencyEvidenceRelationshipParentIdentity()
    {
    }

    public sealed record Root(PackageDependencyEvidenceRootIdentity Identity) :
        PackageDependencyEvidenceRelationshipParentIdentity;

    public sealed record Package(PackageDependencyEvidencePackageIdentity Identity) :
        PackageDependencyEvidenceRelationshipParentIdentity;

    public sealed record Project(RestoredProjectProjectNodeIdentity Identity) :
        PackageDependencyEvidenceRelationshipParentIdentity;
}

/// <summary>Stable provider-issued identity for one produced relationship.</summary>
public abstract record PackageDependencyEvidenceRelationshipIdentity
{
    private PackageDependencyEvidenceRelationshipIdentity()
    {
    }

    public sealed record RestoredProject(RestoredProjectEdgeIdentity Identity) :
        PackageDependencyEvidenceRelationshipIdentity;
}

/// <summary>A provider-owned direct or transitive role for one produced relationship.</summary>
public enum PackageDependencyEvidenceRelationshipRole
{
    Direct,
    Transitive,
}

/// <summary>One normalized resolved package node retained by a relationship phase.</summary>
public sealed record PackageDependencyEvidenceResolvedPackage(
    PackageDependencyEvidencePackageIdentity Identity,
    PackageSourceCoordinate Coordinate,
    PackageDependencyEvidenceRelationshipRole? Role);

/// <summary>One normalized produced package relationship.</summary>
public sealed record PackageDependencyEvidenceRelationship(
    PackageDependencyEvidenceRelationshipIdentity Identity,
    PackageDependencyEvidenceRelationshipParentIdentity Parent,
    PackageDependencyEvidencePackageIdentity Dependency,
    string? CanonicalRequestedConstraint,
    InertString? SourceRequestedConstraintSpelling,
    PackageSourceCoordinate ResolvedCoordinate,
    PackageDependencyEvidenceRelationshipRole? Role,
    PackageDependencyEvidenceAuthorship Authorship,
    PackageDependencyEvidenceDeclarationIdentity? DeclarationAssociation);

/// <summary>A typed provider failure retained by the relationship phase.</summary>
public abstract record PackageDependencyEvidenceRelationshipFailure
{
    private PackageDependencyEvidenceRelationshipFailure()
    {
    }

    public sealed record RestoredProject(RestoredProjectGraphFailure Failure) :
        PackageDependencyEvidenceRelationshipFailure;
}

/// <summary>The additive produced-relationship state for one normalized root.</summary>
public abstract record PackageDependencyEvidenceRelationshipResult
{
    private PackageDependencyEvidenceRelationshipResult()
    {
    }

    public sealed record NotApplicable :
        PackageDependencyEvidenceRelationshipResult;

    public sealed record Available : PackageDependencyEvidenceRelationshipResult
    {
        public Available(
            ImmutableArray<PackageDependencyEvidenceResolvedPackage> packages,
            ImmutableArray<PackageDependencyEvidenceRelationship> relationships,
            ImmutableArray<PackageDependencyEvidenceRelationshipFailure> failures,
            PackageDependencyEvidencePhaseCompletion completion)
        {
            if (failures.IsDefaultOrEmpty
                != (completion == PackageDependencyEvidencePhaseCompletion.Complete))
            {
                throw new ArgumentException(
                    "A complete graph projection carries no failures and an incomplete projection carries at least one.",
                    nameof(completion));
            }

            Packages = packages.IsDefault ? [] : packages;
            Relationships = relationships.IsDefault ? [] : relationships;
            Failures = failures.IsDefault ? [] : failures;
            Completion = completion;
        }

        public ImmutableArray<PackageDependencyEvidenceResolvedPackage> Packages
            { get; }

        public ImmutableArray<PackageDependencyEvidenceRelationship> Relationships
            { get; }

        public ImmutableArray<PackageDependencyEvidenceRelationshipFailure> Failures
            { get; }

        public PackageDependencyEvidencePhaseCompletion Completion { get; }

        public bool IsComplete =>
            Completion == PackageDependencyEvidencePhaseCompletion.Complete;
    }

    public sealed record Unavailable :
        PackageDependencyEvidenceRelationshipResult;

    public sealed record Failed(PackageDependencyEvidenceRelationshipFailure Failure) :
        PackageDependencyEvidenceRelationshipResult;
}

/// <summary>One positively evidenced upstream processing semantic.</summary>
public enum PackageDependencyEvidenceProcessingObservation
{
    RestoreResolution,
    PackagePruningEvaluation,
    RuntimeDependencyProjection,
}

/// <summary>A typed failure to associate processing evidence with its input and target.</summary>
public abstract record PackageDependencyEvidenceProcessingFailure
{
    private PackageDependencyEvidenceProcessingFailure()
    {
    }

    public sealed record Association(
        PackageDependencyEvidenceProcessingObservation Observation) :
        PackageDependencyEvidenceProcessingFailure;

    public sealed record RestoredProjectPackagePruning(
        RestoredProjectPackagePruningFailure Failure) :
        PackageDependencyEvidenceProcessingFailure;
}

/// <summary>The positive processing-evidence state for one normalized root.</summary>
public abstract record PackageDependencyEvidenceProcessingResult
{
    private PackageDependencyEvidenceProcessingResult()
    {
    }

    public sealed record NotApplicable : PackageDependencyEvidenceProcessingResult;

    public sealed record Available : PackageDependencyEvidenceProcessingResult
    {
        public Available(
            ImmutableArray<PackageDependencyEvidenceProcessingObservation>
                observations,
            ImmutableArray<PackageDependencyEvidenceProcessingFailure> failures,
            PackageDependencyEvidencePhaseCompletion completion)
        {
            if (failures.IsDefaultOrEmpty
                != (completion == PackageDependencyEvidencePhaseCompletion.Complete))
            {
                throw new ArgumentException(
                    "A complete processing projection carries no failures and an incomplete projection carries at least one.",
                    nameof(completion));
            }

            Observations = observations.IsDefault ? [] : observations;
            Failures = failures.IsDefault ? [] : failures;
            Completion = completion;
        }

        public ImmutableArray<PackageDependencyEvidenceProcessingObservation>
            Observations { get; }

        public ImmutableArray<PackageDependencyEvidenceProcessingFailure> Failures
            { get; }

        public PackageDependencyEvidencePhaseCompletion Completion { get; }

        public bool IsComplete =>
            Completion == PackageDependencyEvidencePhaseCompletion.Complete;
    }

    public sealed record Unavailable : PackageDependencyEvidenceProcessingResult;

    public sealed record Failed(PackageDependencyEvidenceProcessingFailure Failure) :
        PackageDependencyEvidenceProcessingResult;
}

/// <summary>One admitted normalized root.</summary>
public sealed record PackageDependencyEvidenceRoot(
    PackageDependencyEvidenceRootIdentity Identity,
    PackageDependencyEvidenceRootProvenance Provenance,
    InertString Display,
    PackageDependencyEvidenceDeclarationResult Declaration,
    PackageDependencyEvidenceSelection Selection,
    RestoredProjectSelectedTarget? RestoredTarget,
    PackageDependencyEvidenceRelationshipResult Relationships,
    PackageDependencyEvidenceProcessingResult Processing)
{
    public PackageDependencyEvidenceRootIdentity Identity { get; } =
        Identity ?? throw new ArgumentNullException(nameof(Identity));

    public PackageDependencyEvidenceRootProvenance Provenance { get; } =
        (Identity, Provenance) switch
        {
            (PackageDependencyEvidenceRootIdentity.Package _,
                PackageDependencyEvidenceRootProvenance.Package _) =>
                    Provenance,
            (PackageDependencyEvidenceRootIdentity.RestoredProject _,
                PackageDependencyEvidenceRootProvenance.RestoredProject _) =>
                    Provenance,
            _ => throw new ArgumentException(
                "Root identity and provenance must describe the same semantic input.",
                nameof(Provenance)),
        };

    public PackageDependencyEvidenceInputKind InputKind { get; } =
        (Identity, Provenance) switch
        {
            (PackageDependencyEvidenceRootIdentity.Package _,
                PackageDependencyEvidenceRootProvenance.Package _) =>
                    PackageDependencyEvidenceInputKind.PackageManifest,
            (PackageDependencyEvidenceRootIdentity.RestoredProject _,
                PackageDependencyEvidenceRootProvenance.RestoredProject _) =>
                    PackageDependencyEvidenceInputKind.RestoredProject,
            _ => throw new ArgumentException(
                "Root identity and provenance must describe the same semantic input kind.",
                nameof(Provenance)),
        };

    public PackageDependencyEvidenceDeclarationBasis DeclarationBasis { get; } =
        (Identity, Provenance) switch
        {
            (PackageDependencyEvidenceRootIdentity.Package _,
                PackageDependencyEvidenceRootProvenance.Package _) =>
                    PackageDependencyEvidenceDeclarationBasis.PackageManifest,
            (PackageDependencyEvidenceRootIdentity.RestoredProject _,
                PackageDependencyEvidenceRootProvenance.RestoredProject _) =>
                    PackageDependencyEvidenceDeclarationBasis.RestoredProject,
            _ => throw new ArgumentException(
                "Root identity and provenance must describe the same declaration basis.",
                nameof(Provenance)),
        };
}

/// <summary>Root-set admission accounting kept independent from per-root phase completion.</summary>
public sealed record PackageDependencyEvidenceRootSetSummary(
    PackageDependencyEvidenceRootSetCompletion Completion,
    int AdmittedRootCount,
    int RejectedRootCount,
    int FailedRootCount,
    bool IsTruncated,
    PackageDependencyEvidencePackagePrefixCompletion?
        PackagePrefixCompletion);

/// <summary>Aggregate counts for one independently completed evidence phase.</summary>
public sealed record PackageDependencyEvidencePhaseCounts(
    int NotApplicable,
    int Complete,
    int Incomplete,
    int Unavailable,
    int Failed);

/// <summary>Aggregate per-root declaration, relationship, and processing states.</summary>
public sealed record PackageDependencyEvidencePhaseSummary(
    PackageDependencyEvidencePhaseCounts Declarations,
    PackageDependencyEvidencePhaseCounts Relationships,
    PackageDependencyEvidencePhaseCounts Processing);

/// <summary>The immutable normalized dependency evidence for one supplied root set.</summary>
public sealed record PackageDependencyEvidenceOutcome(
    ImmutableArray<PackageDependencyEvidenceRoot> Roots,
    ImmutableArray<PackageDependencyEvidenceRootFailure> FailedRoots,
    PackageDependencyEvidenceRootSetSummary RootSet,
    PackageDependencyEvidencePhaseSummary Phases);

/// <summary>Why two declaration projections cannot truthfully be compared.</summary>
public enum PackageDependencyEvidenceNotComparableReason
{
    DeclarationProjectionIncomplete,
    FrameworkScope,
    SelectionStatusUnavailable,
}

/// <summary>The closed result of one semantic declaration comparison.</summary>
public abstract record PackageDependencyEvidenceComparisonResult
{
    private PackageDependencyEvidenceComparisonResult()
    {
    }

    public sealed record Equal : PackageDependencyEvidenceComparisonResult;

    public sealed record Unequal : PackageDependencyEvidenceComparisonResult;

    public sealed record NotComparable(
        PackageDependencyEvidenceNotComparableReason Reason) :
        PackageDependencyEvidenceComparisonResult;
}

/// <summary>Full-set and selected-group comparisons for one explicitly paired root.</summary>
public sealed record PackageDependencyEvidenceComparison(
    PackageDependencyEvidenceComparisonResult Core,
    PackageDependencyEvidenceComparisonResult Scoped,
    PackageDependencyEvidenceComparisonResult SelectedCore,
    PackageDependencyEvidenceComparisonResult SelectedScoped);

/// <summary>
/// Composes existing package-manifest/group and restored-project facts into one host-neutral
/// dependency-evidence result.
/// </summary>
public static class PackageDependencyEvidenceQuery
{
    public static InspectionQuery<PackageDependencyEvidenceOutcome> Definition { get; } =
        new("Package dependency evidence", InspectionCost.NetworkFree);

    /// <summary>
    /// Builds the package-side input by reusing the existing exact-framework group-selection
    /// query over already-projected manifest facts.
    /// </summary>
    public static PackageDependencyEvidenceInput.Package CreatePackageInput(
        PackageManifestFacts manifest,
        PackageDependencyEvidenceAcquisitionForm acquisitionForm,
        string? requestedTargetFramework = null,
        InertString? sourceLabel = null,
        PackageSourceResultIdentity? source = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        RequirePackageAcquisitionForm(acquisitionForm, source);
        string? requested = string.IsNullOrWhiteSpace(requestedTargetFramework)
            ? null
            : requestedTargetFramework;
        return new PackageDependencyEvidenceInput.Package(
            manifest,
            PackageDependencyGroupsQuery.ProjectDependencyGroups(
                manifest,
                requested),
            acquisitionForm,
            sourceLabel,
            source);
    }

    /// <summary>Builds a package-prefix input from one admitted package-profile match.</summary>
    public static PackageDependencyEvidenceInput.Package CreatePackageInput(
        PackageProfileMatch match,
        string? requestedTargetFramework = null,
        InertString? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        PackageSourceCoordinate matchCoordinate =
            PackageSourceCoordinate.Create(match.PackageId, match.Version);
        if (match.Manifest.Coordinate != matchCoordinate)
        {
            throw new ArgumentException(
                "A package-profile match must agree with its manifest coordinate.",
                nameof(match));
        }

        return CreatePackageInput(
            match.Manifest,
            PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
            requestedTargetFramework,
            sourceLabel,
            match.Source);
    }

    /// <summary>Builds a restored-project input from owner-issued facts.</summary>
    public static PackageDependencyEvidenceInput.RestoredProject
        CreateRestoredProjectInput(
            RestoredProjectDependencyFacts facts,
            PackageDependencyEvidenceAcquisitionForm acquisitionForm,
            InertString? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireRestoredAcquisitionForm(acquisitionForm);
        return new PackageDependencyEvidenceInput.RestoredProject(
            facts,
            acquisitionForm,
            sourceLabel);
    }

    /// <summary>Contains package-profile failure text before it reaches a sink.</summary>
    public static PackageDependencyEvidenceRootFailure.PackageProfile
        CreatePackageProfileFailure(PackageProfileFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new PackageDependencyEvidenceRootFailure.PackageProfile(
            failure.Source,
            failure.Kind,
            failure.ManifestFailureReason,
            CreateEstablishedPackageProfileCoordinate(failure),
            TreatOptionalField(failure.PackageId),
            TreatOptionalField(failure.Version),
            new InertString(
                TextPolicy.Prose,
                failure.Message,
                PackageManifestFactsQuery.MaxScalarCharacters));
    }

    /// <summary>Adapts one completed package-profile stream without losing terminal state.</summary>
    public static PackageDependencyEvidenceRequest CreatePackagePrefixRequest(
        ImmutableArray<PackageProfileMatch> matches,
        ImmutableArray<PackageProfileFailure> failures,
        PackageProfileSummary summary,
        string? requestedTargetFramework = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        matches = matches.IsDefault ? [] : matches;
        failures = failures.IsDefault ? [] : failures;
        if (summary.Matches != matches.Length
            || summary.Failures != failures.Length
            || matches.Any(match => match.Source != summary.Source))
        {
            throw new ArgumentException(
                "Package-profile events must agree with their terminal summary.",
                nameof(summary));
        }

        bool isTruncated =
            summary.TruncationReason != PackageSearchTruncationReason.None;
        var completion = new PackageDependencyEvidencePackagePrefixCompletion(
            new InertString(
                TextPolicy.Field,
                summary.Prefix,
                PackageManifestFactsQuery.MaxScalarCharacters),
            summary.Source,
            summary.Candidates,
            summary.Matches,
            summary.Failures,
            summary.TruncationReason);
        return new PackageDependencyEvidenceRequest(
            [
                .. matches.Select(match =>
                    CreatePackageInput(
                        match,
                        requestedTargetFramework)),
            ],
            [
                .. failures.Select(CreatePackageProfileFailure),
            ],
            isTruncated: isTruncated,
            packagePrefixCompletion: completion);
    }

    public static PackageDependencyEvidenceOutcome Execute(
        PackageDependencyEvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (PackageDependencyEvidenceRootFailure failure in
            request.FailedRoots)
        {
            switch (failure)
            {
                case PackageDependencyEvidenceRootFailure.Package package:
                    RequirePackageAcquisitionForm(package.AcquisitionForm);
                    ArgumentNullException.ThrowIfNull(package.Failure);
                    break;
                case PackageDependencyEvidenceRootFailure.RestoredProject restored:
                    RequireRestoredAcquisitionForm(restored.AcquisitionForm);
                    ArgumentNullException.ThrowIfNull(restored.Failure);
                    break;
                case PackageDependencyEvidenceRootFailure.PackageProfile profile:
                    if (profile.Kind is PackageProfileFailureKind.Search
                            or PackageProfileFailureKind.SearchContract
                        && profile.Coordinate is not null)
                    {
                        throw new ArgumentException(
                            "Package-profile search failures cannot carry an established package coordinate.",
                            nameof(request));
                    }
                    break;
                case PackageDependencyEvidenceRootFailure.Acquisition acquisition:
                    RequireKnownAcquisitionForm(acquisition.AcquisitionForm);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown package dependency evidence root failure.");
            }
        }

        ImmutableArray<PackageDependencyEvidenceRoot> roots =
            [.. request.Roots.Select(ProjectRoot)];
        PackageDependencyEvidenceRootSetCompletion rootSetCompletion =
            !request.IsTruncated
            && request.RejectedRootCount == 0
            && request.FailedRoots.IsEmpty
                ? PackageDependencyEvidenceRootSetCompletion.Complete
                : PackageDependencyEvidenceRootSetCompletion.Incomplete;

        return new PackageDependencyEvidenceOutcome(
            roots,
            request.FailedRoots,
            new PackageDependencyEvidenceRootSetSummary(
                rootSetCompletion,
                roots.Length,
                request.RejectedRootCount,
                request.FailedRoots.Length,
                request.IsTruncated,
                request.PackagePrefixCompletion),
            SummarizePhases(roots));
    }

    public static PackageDependencyEvidenceComparison Compare(
        PackageDependencyEvidenceRoot left,
        PackageDependencyEvidenceRoot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Declaration is not PackageDependencyEvidenceDeclarationResult.Available
                { IsComplete: true } leftDeclaration
            || right.Declaration is not PackageDependencyEvidenceDeclarationResult.Available
                { IsComplete: true } rightDeclaration)
        {
            PackageDependencyEvidenceComparisonResult notComparable =
                new PackageDependencyEvidenceComparisonResult.NotComparable(
                    PackageDependencyEvidenceNotComparableReason
                        .DeclarationProjectionIncomplete);
            return new PackageDependencyEvidenceComparison(
                notComparable,
                notComparable,
                notComparable,
                notComparable);
        }

        PackageDependencyEvidenceComparisonResult core =
            CompareCore(leftDeclaration.Groups, rightDeclaration.Groups);
        PackageDependencyEvidenceComparisonResult scoped =
            CompareScoped(
                left.InputKind,
                leftDeclaration.Groups,
                right.InputKind,
                rightDeclaration.Groups);
        (PackageDependencyEvidenceComparisonResult selectedCore,
            PackageDependencyEvidenceComparisonResult selectedScoped) =
            CompareSelected(left, leftDeclaration, right, rightDeclaration);
        return new PackageDependencyEvidenceComparison(
            core,
            scoped,
            selectedCore,
            selectedScoped);
    }

    private static PackageDependencyEvidenceRoot ProjectRoot(
        PackageDependencyEvidenceInput input) =>
        input switch
        {
            PackageDependencyEvidenceInput.Package package =>
                ProjectPackage(package),
            PackageDependencyEvidenceInput.RestoredProject restored =>
                ProjectRestoredProject(restored),
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence input."),
        };

    private static PackageDependencyEvidenceRoot ProjectPackage(
        PackageDependencyEvidenceInput.Package input)
    {
        ArgumentNullException.ThrowIfNull(input.Manifest);
        ArgumentNullException.ThrowIfNull(input.Groups);
        RequirePackageAcquisitionForm(input.AcquisitionForm, input.Source);
        RequireMatchingPackageFacts(input.Manifest, input.Groups);

        var rootIdentity = new PackageDependencyEvidenceRootIdentity.Package(
            input.Manifest.Coordinate);
        PackageDependencyEvidenceDeclarationResult.Available declaration =
            ProjectPackageDeclaration(rootIdentity, input.Groups);
        PackageDependencyEvidenceSelection selection =
            ProjectPackageSelection(input.Groups, declaration.Groups);
        return new PackageDependencyEvidenceRoot(
            rootIdentity,
            new PackageDependencyEvidenceRootProvenance.Package(
                input.AcquisitionForm,
                input.Manifest.IdentityProvenance,
                input.SourceLabel,
                input.Source),
            new InertString(
                TextPolicy.Field,
                input.Manifest.Coordinate.PackageId,
                PackageManifestFactsQuery.MaxScalarCharacters),
            declaration,
            selection,
            null,
            new PackageDependencyEvidenceRelationshipResult.NotApplicable(),
            new PackageDependencyEvidenceProcessingResult.NotApplicable());
    }

    private static PackageDependencyEvidenceRoot ProjectRestoredProject(
        PackageDependencyEvidenceInput.RestoredProject input)
    {
        ArgumentNullException.ThrowIfNull(input.Facts);
        RequireRestoredAcquisitionForm(input.AcquisitionForm);

        var rootIdentity =
            new PackageDependencyEvidenceRootIdentity.RestoredProject(
                input.Facts.Root);
        return new PackageDependencyEvidenceRoot(
            rootIdentity,
            new PackageDependencyEvidenceRootProvenance.RestoredProject(
                input.AcquisitionForm,
                input.Facts.ContentProvenance,
                input.SourceLabel),
            input.SourceLabel
                ?? new InertString(TextPolicy.Prose, "Restored project"),
            ProjectRestoredDeclaration(input.Facts),
            new PackageDependencyEvidenceSelection(
                PackageDependencyEvidenceSelectionStatus.Unavailable,
                null,
                null,
                null,
                null),
            input.Facts.SelectedTarget,
            ProjectRestoredRelationships(rootIdentity, input.Facts),
            ProjectRestoredProcessing(input.Facts));
    }

    private static PackageDependencyEvidenceDeclarationResult.Available
        ProjectPackageDeclaration(
            PackageDependencyEvidenceRootIdentity.Package root,
            PackageDependencyGroups groups)
    {
        var logicalGroups = new List<(int FirstIndex, bool IsImplicit, List<int> Indices)>();
        List<int> implicitIndices = [];
        for (int index = 0; index < groups.Groups.Length; index++)
        {
            if (groups.Groups[index].IsImplicitManifestGroup)
            {
                implicitIndices.Add(index);
            }
            else
            {
                logicalGroups.Add((index, false, [index]));
            }
        }

        if (implicitIndices.Count > 0)
            logicalGroups.Add((implicitIndices.Min(), true, implicitIndices));

        var projectedGroups =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceGroup>(
                logicalGroups.Count);
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceDeclarationFailure>();
        foreach ((int firstIndex, bool isImplicit, List<int> indices) in
            logicalGroups.OrderBy(group => group.FirstIndex))
        {
            var identity = new PackageDependencyEvidenceGroupIdentity.Package(
                root,
                isImplicit,
                firstIndex);
            DeclaredPackageDependencyGroup first = groups.Groups[indices[0]];
            PackageDependencyFrameworkScopeIdentity frameworkScope =
                isImplicit
                    ? PackageDependencyFrameworkScopeIdentity.Any(
                        new InertString(
                            TextPolicy.Field,
                            first.TargetFramework,
                            PackageManifestFactsQuery.MaxScalarCharacters))
                    : CreatePackageFrameworkScope(
                        first.TargetFramework,
                        new InertString(
                            TextPolicy.Field,
                            first.TargetFramework,
                            PackageManifestFactsQuery.MaxScalarCharacters));
            ImmutableArray<PackageDependencyEvidenceDeclaration> declarations =
                ProjectPackageDeclarations(
                    identity,
                    indices.SelectMany(index => groups.Groups[index].Dependencies),
                    failures);
            projectedGroups.Add(
                new PackageDependencyEvidenceGroup(
                    identity,
                    frameworkScope,
                    [.. indices.Select(index =>
                        new PackageDependencyEvidenceGroupOccurrence.Package(index))],
                    firstIndex.ToString("D10", System.Globalization.CultureInfo.InvariantCulture),
                    declarations));
        }

        return new PackageDependencyEvidenceDeclarationResult.Available(
            projectedGroups.MoveToImmutable(),
            failures.ToImmutable(),
            failures.Count == 0
                ? PackageDependencyEvidencePhaseCompletion.Complete
                : PackageDependencyEvidencePhaseCompletion.Incomplete);
    }

    private static ImmutableArray<PackageDependencyEvidenceDeclaration>
        ProjectPackageDeclarations(
            PackageDependencyEvidenceGroupIdentity group,
            IEnumerable<DeclaredPackageDependency> source,
            ImmutableArray<PackageDependencyEvidenceDeclarationFailure>.Builder
                failures)
    {
        var occurrences = new Dictionary<
            string,
            List<(string RawId, string RawConstraint, string CanonicalConstraint)>>(
                StringComparer.Ordinal);
        int invalidOccurrences = 0;
        foreach (DeclaredPackageDependency? dependency in source)
        {
            if (dependency is null
                || string.IsNullOrWhiteSpace(dependency.Id)
                || !PackageCoordinateResolver.IsCanonicalPackageId(dependency.Id))
            {
                invalidOccurrences++;
                continue;
            }

            if (!TryCanonicalizeVersionConstraint(
                    dependency.VersionRange,
                    out string canonicalConstraint))
            {
                invalidOccurrences++;
                continue;
            }

            string canonicalId = dependency.Id.ToLowerInvariant();
            if (!occurrences.TryGetValue(canonicalId, out var values))
            {
                values = [];
                occurrences.Add(canonicalId, values);
            }

            values.Add((
                dependency.Id,
                dependency.VersionRange,
                canonicalConstraint));
        }

        if (invalidOccurrences > 0)
        {
            failures.Add(
                new PackageDependencyEvidenceDeclarationFailure
                    .InvalidPackageDeclaration(
                        group,
                        invalidOccurrences));
        }

        var declarations =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceDeclaration>();
        foreach ((string canonicalId,
            List<(string RawId, string RawConstraint, string CanonicalConstraint)> values)
            in occurrences.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string[] constraints =
                [.. values.Select(value => value.CanonicalConstraint)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)];
            if (constraints.Length != 1)
            {
                failures.Add(
                    new PackageDependencyEvidenceDeclarationFailure
                        .ConflictingPackageDeclaration(
                            group,
                            canonicalId,
                            values.Count));
                continue;
            }

            (string rawId, string rawConstraint, string canonicalConstraint) =
                values.OrderBy(
                        value => value.RawId,
                        StringComparer.Ordinal)
                    .ThenBy(
                        value => value.RawConstraint,
                        StringComparer.Ordinal)
                    .First();
            declarations.Add(
                new PackageDependencyEvidenceDeclaration(
                    new PackageDependencyEvidenceDeclarationIdentity(
                        group,
                        canonicalId),
                    canonicalId,
                    canonicalConstraint,
                    new InertString(
                        TextPolicy.Field,
                        rawId,
                        PackageManifestFactsQuery.MaxScalarCharacters),
                    new InertString(
                        TextPolicy.Field,
                        rawConstraint,
                        PackageManifestFactsQuery.MaxScalarCharacters),
                    values.Count,
                    PackageDependencyEvidenceAuthorship.LibraryDeclared));
        }

        return declarations.ToImmutable();
    }

    private static PackageDependencyEvidenceSelection ProjectPackageSelection(
        PackageDependencyGroups source,
        ImmutableArray<PackageDependencyEvidenceGroup> groups)
    {
        ValidatePackageSelection(source);
        PackageDependencyEvidenceSelectionStatus status =
            source.SelectionStatus switch
            {
                PackageDependencyGroupSelectionStatus.Selected =>
                    PackageDependencyEvidenceSelectionStatus.Selected,
                PackageDependencyGroupSelectionStatus.NoDependencyGroups =>
                    PackageDependencyEvidenceSelectionStatus.NoDependencyGroups,
                PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework =>
                    PackageDependencyEvidenceSelectionStatus.NoMatchingTargetFramework,
                _ => throw new InvalidOperationException(
                    "Unknown package dependency group selection status."),
            };

        PackageDependencyEvidenceGroup? selectedGroup = null;
        PackageDependencyEvidenceGroupOccurrence.Package? selectedOccurrence = null;
        if (source.SelectedGroupIndex is int index)
        {
            if ((uint)index >= (uint)source.Groups.Length)
            {
                throw new ArgumentException(
                    "The selected package dependency group index is outside the supplied group set.",
                    nameof(source));
            }

            if (!string.Equals(
                    source.Groups[index].TargetFramework,
                    source.SelectedTargetFramework,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The selected package dependency group does not match the selected framework.",
                    nameof(source));
            }

            selectedGroup = groups.Single(group =>
                group.SourceOccurrences.Any(occurrence =>
                    occurrence is PackageDependencyEvidenceGroupOccurrence.Package
                        { SourceIndex: var sourceIndex }
                    && sourceIndex == index));
            selectedOccurrence =
                new PackageDependencyEvidenceGroupOccurrence.Package(index);
        }

        return new PackageDependencyEvidenceSelection(
            status,
            selectedGroup?.Identity,
            selectedOccurrence,
            source.RequestedTargetFramework is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    source.RequestedTargetFramework,
                    PackageManifestFactsQuery.MaxScalarCharacters),
            source.SelectedTargetFramework is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    source.SelectedTargetFramework,
                    PackageManifestFactsQuery.MaxScalarCharacters));
    }

    private static PackageDependencyEvidenceDeclarationResult
        ProjectRestoredDeclaration(RestoredProjectDependencyFacts facts) =>
        facts.Declaration switch
        {
            RestoredProjectDeclarationResult.Available available =>
                ProjectAvailableRestoredDeclaration(facts, available),
            RestoredProjectDeclarationResult.Unavailable =>
                new PackageDependencyEvidenceDeclarationResult.Unavailable(),
            RestoredProjectDeclarationResult.Failed failed =>
                new PackageDependencyEvidenceDeclarationResult.Failed(
                    new PackageDependencyEvidenceDeclarationFailure.RestoredProject(
                        failed.Failure)),
            _ => throw new InvalidOperationException(
                "Unknown restored-project declaration result."),
        };

    private static PackageDependencyEvidenceDeclarationResult.Available
        ProjectAvailableRestoredDeclaration(
            RestoredProjectDependencyFacts facts,
            RestoredProjectDeclarationResult.Available declaration)
    {
        var groups =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceGroup>(
                declaration.Groups.Length);
        foreach (RestoredProjectDeclarationGroup sourceGroup in declaration.Groups)
        {
            var identity =
                new PackageDependencyEvidenceGroupIdentity.RestoredProject(
                    sourceGroup.Identity);
            ImmutableArray<PackageDependencyEvidenceDeclaration> declarations =
            [
                .. sourceGroup.Packages
                    .Select(package =>
                        new PackageDependencyEvidenceDeclaration(
                            new PackageDependencyEvidenceDeclarationIdentity(
                                identity,
                                package.CanonicalPackageId),
                            package.CanonicalPackageId,
                            package.CanonicalVersionConstraint,
                            package.SourcePackageIdSpelling,
                            package.SourceVersionConstraintSpelling,
                            package.SourceOccurrenceCount,
                            PackageDependencyEvidenceAuthorship
                                .ApplicationAuthored))
                    .OrderBy(
                        package => package.CanonicalPackageId,
                        StringComparer.Ordinal)
                    .ThenBy(
                        package => package.CanonicalVersionConstraint,
                        StringComparer.Ordinal),
            ];
            groups.Add(
                new PackageDependencyEvidenceGroup(
                    identity,
                    CreateRestoredFrameworkScope(
                        sourceGroup.FrameworkIdentity,
                        sourceGroup.SourcePivotSpelling),
                    [
                        new PackageDependencyEvidenceGroupOccurrence.RestoredProject(
                            sourceGroup.Identity),
                    ],
                    sourceGroup.OrderKey,
                    declarations));
        }

        ImmutableArray<PackageDependencyEvidenceDeclarationFailure> failures =
        [
            .. declaration.Failures.Select(failure =>
                new PackageDependencyEvidenceDeclarationFailure.RestoredProject(
                    failure)),
        ];
        return new PackageDependencyEvidenceDeclarationResult.Available(
            groups.MoveToImmutable(),
            failures,
            declaration.IsComplete
                ? PackageDependencyEvidencePhaseCompletion.Complete
                : PackageDependencyEvidencePhaseCompletion.Incomplete);
    }

    private static PackageDependencyEvidenceRelationshipResult
        ProjectRestoredRelationships(
            PackageDependencyEvidenceRootIdentity.RestoredProject root,
            RestoredProjectDependencyFacts facts) =>
        facts.Graph switch
        {
            RestoredProjectGraphResult.Available available =>
                new PackageDependencyEvidenceRelationshipResult.Available(
                    [
                        .. available.Packages.Select(package =>
                            new PackageDependencyEvidenceResolvedPackage(
                                new PackageDependencyEvidencePackageIdentity
                                    .RestoredProject(package.Identity),
                                package.Identity.Coordinate,
                                ProjectRelationshipRole(package.Role))),
                    ],
                    [
                        .. available.Edges.Select(edge =>
                        {
                            PackageDependencyEvidenceDeclarationIdentity?
                                declarationAssociation =
                                    edge.DeclarationAssociation is { } association
                                        ? new PackageDependencyEvidenceDeclarationIdentity(
                                            new PackageDependencyEvidenceGroupIdentity
                                                .RestoredProject(association),
                                            edge.Dependency.Coordinate.PackageId)
                                        : null;
                            return new PackageDependencyEvidenceRelationship(
                                new PackageDependencyEvidenceRelationshipIdentity
                                    .RestoredProject(edge.Identity),
                                ProjectRelationshipParent(root, edge.Parent),
                                new PackageDependencyEvidencePackageIdentity
                                    .RestoredProject(edge.Dependency),
                                edge.CanonicalVersionConstraint,
                                edge.SourceVersionConstraintSpelling,
                                edge.Dependency.Coordinate,
                                ProjectRelationshipRole(edge.Role),
                                ProjectRelationshipAuthorship(
                                    edge.Parent,
                                    declarationAssociation),
                                declarationAssociation);
                        }),
                    ],
                    [
                        .. available.Failures.Select(failure =>
                            new PackageDependencyEvidenceRelationshipFailure
                                .RestoredProject(failure)),
                    ],
                    available.IsComplete
                        ? PackageDependencyEvidencePhaseCompletion.Complete
                        : PackageDependencyEvidencePhaseCompletion.Incomplete),
            RestoredProjectGraphResult.Unavailable =>
                new PackageDependencyEvidenceRelationshipResult.Unavailable(),
            RestoredProjectGraphResult.Failed failed =>
                new PackageDependencyEvidenceRelationshipResult.Failed(
                    new PackageDependencyEvidenceRelationshipFailure
                        .RestoredProject(failed.Failure)),
            _ => throw new InvalidOperationException(
                "Unknown restored-project graph result."),
        };

    private static PackageDependencyEvidenceRelationshipParentIdentity
        ProjectRelationshipParent(
            PackageDependencyEvidenceRootIdentity.RestoredProject root,
            RestoredProjectGraphParentIdentity parent) =>
        parent switch
        {
            RestoredProjectGraphParentIdentity.Root =>
                new PackageDependencyEvidenceRelationshipParentIdentity.Root(
                    root),
            RestoredProjectGraphParentIdentity.Package package =>
                new PackageDependencyEvidenceRelationshipParentIdentity.Package(
                    new PackageDependencyEvidencePackageIdentity.RestoredProject(
                        package.Identity)),
            RestoredProjectGraphParentIdentity.Project project =>
                new PackageDependencyEvidenceRelationshipParentIdentity.Project(
                    project.Identity),
            _ => throw new InvalidOperationException(
                "Unknown restored-project relationship parent."),
        };

    private static PackageDependencyEvidenceRelationshipRole
        ProjectRelationshipRole(RestoredProjectDependencyRole role) =>
        role switch
        {
            RestoredProjectDependencyRole.Direct =>
                PackageDependencyEvidenceRelationshipRole.Direct,
            RestoredProjectDependencyRole.Transitive =>
                PackageDependencyEvidenceRelationshipRole.Transitive,
            _ => throw new InvalidOperationException(
                "Unknown restored-project dependency role."),
        };

    private static PackageDependencyEvidenceAuthorship
        ProjectRelationshipAuthorship(
            RestoredProjectGraphParentIdentity parent,
            PackageDependencyEvidenceDeclarationIdentity?
                declarationAssociation) =>
        parent switch
        {
            RestoredProjectGraphParentIdentity.Root
                when declarationAssociation.HasValue =>
                PackageDependencyEvidenceAuthorship.ApplicationAuthored,
            RestoredProjectGraphParentIdentity.Package
                when declarationAssociation is null =>
                PackageDependencyEvidenceAuthorship.LibraryDeclared,
            RestoredProjectGraphParentIdentity.Project
                when declarationAssociation is null =>
                PackageDependencyEvidenceAuthorship.Unattributed,
            RestoredProjectGraphParentIdentity.Root =>
                PackageDependencyEvidenceAuthorship.Unattributed,
            _ => throw new InvalidOperationException(
                "A non-root relationship cannot carry a root declaration association."),
        };

    private static PackageDependencyEvidenceProcessingResult
        ProjectRestoredProcessing(RestoredProjectDependencyFacts facts)
    {
        if (facts.SelectedTarget is null)
            return new PackageDependencyEvidenceProcessingResult.Unavailable();

        return facts.PackagePruning switch
        {
            RestoredProjectPackagePruningResult.Available =>
                new PackageDependencyEvidenceProcessingResult.Available(
                    [
                        PackageDependencyEvidenceProcessingObservation
                            .RestoreResolution,
                        PackageDependencyEvidenceProcessingObservation
                            .PackagePruningEvaluation,
                    ],
                    [],
                    PackageDependencyEvidencePhaseCompletion.Complete),
            RestoredProjectPackagePruningResult.Unavailable =>
                new PackageDependencyEvidenceProcessingResult.Available(
                    [
                        PackageDependencyEvidenceProcessingObservation
                            .RestoreResolution,
                    ],
                    [],
                    PackageDependencyEvidencePhaseCompletion.Complete),
            RestoredProjectPackagePruningResult.Failed failed =>
                new PackageDependencyEvidenceProcessingResult.Available(
                    [
                        PackageDependencyEvidenceProcessingObservation
                            .RestoreResolution,
                    ],
                    [
                        new PackageDependencyEvidenceProcessingFailure
                            .RestoredProjectPackagePruning(failed.Failure),
                    ],
                    PackageDependencyEvidencePhaseCompletion.Incomplete),
            _ => throw new InvalidOperationException(
                $"Unknown restored-project package-pruning result: {facts.PackagePruning.GetType().FullName}"),
        };
    }

    private static PackageDependencyFrameworkScopeIdentity
        CreatePackageFrameworkScope(
        string sourceIdentity,
        InertString sourceSpelling)
    {
        if (sourceIdentity.Length == 0
            || sourceIdentity.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return PackageDependencyFrameworkScopeIdentity.Any(sourceSpelling);
        }

        if (NuGetTargetFrameworkIdentity.TryNormalize(
                sourceIdentity,
                out string canonical))
        {
            return PackageDependencyFrameworkScopeIdentity.Exact(
                canonical,
                sourceSpelling);
        }

        return PackageDependencyFrameworkScopeIdentity.Unrecognized(
            RestoredProjectIdentityText.Opaque(sourceIdentity),
            sourceSpelling);
    }

    private static PackageDependencyFrameworkScopeIdentity
        CreateRestoredFrameworkScope(
            RestoredProjectFrameworkIdentity sourceIdentity,
            InertString sourceSpelling)
    {
        if (sourceIdentity.Kind
            == RestoredProjectFrameworkIdentityKind.Unrecognized)
        {
            return PackageDependencyFrameworkScopeIdentity.Unrecognized(
                sourceIdentity.Identity,
                sourceSpelling);
        }

        if (!NuGetTargetFrameworkIdentity.TryNormalize(
                sourceIdentity.Identity,
                out string canonical)
            || !string.Equals(
                sourceIdentity.Identity,
                canonical,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Restored-project facts supplied a non-canonical recognized framework identity.");
        }

        return PackageDependencyFrameworkScopeIdentity.Exact(
            canonical,
            sourceSpelling);
    }

    private static bool TryCanonicalizeVersionConstraint(
        string? value,
        out string canonical)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            canonical = VersionRange.All.ToNormalizedString();
            return true;
        }

        if (!VersionRange.TryParse(value, out VersionRange? range))
        {
            canonical = "";
            return false;
        }

        canonical = range.ToNormalizedString();
        return true;
    }

    private static void ValidatePackageSelection(PackageDependencyGroups source)
    {
        switch (source.SelectionStatus)
        {
            case PackageDependencyGroupSelectionStatus.Selected
                when source.SelectedGroupIndex is null
                    || source.SelectedTargetFramework is null:
                throw new ArgumentException(
                    "A selected package dependency group requires a source index and selected framework.",
                    nameof(source));
            case PackageDependencyGroupSelectionStatus.NoDependencyGroups
                when !source.Groups.IsEmpty
                    || source.SelectedGroupIndex is not null
                    || source.SelectedTargetFramework is not null:
            case PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework
                when source.SelectedGroupIndex is not null
                    || source.SelectedTargetFramework is not null:
                throw new ArgumentException(
                    "The package dependency-group selection state is inconsistent.",
                    nameof(source));
        }
    }

    private static void RequireMatchingPackageFacts(
        PackageManifestFacts facts,
        PackageDependencyGroups groups)
    {
        if (facts.DependencyGroups.Length != groups.Groups.Length)
        {
            throw new ArgumentException(
                "Package manifest facts and dependency-group facts must describe the same group set.",
                nameof(groups));
        }

        for (int index = 0; index < facts.DependencyGroups.Length; index++)
        {
            DeclaredPackageDependencyGroup left = facts.DependencyGroups[index];
            DeclaredPackageDependencyGroup right = groups.Groups[index];
            if (left.IsImplicitManifestGroup != right.IsImplicitManifestGroup
                || !string.Equals(
                    left.TargetFramework,
                    right.TargetFramework,
                    StringComparison.Ordinal)
                || left.Dependencies.Length != right.Dependencies.Length)
            {
                throw new ArgumentException(
                    "Package manifest facts and dependency-group facts must describe the same group set.",
                    nameof(groups));
            }

            for (int dependencyIndex = 0;
                dependencyIndex < left.Dependencies.Length;
                dependencyIndex++)
            {
                DeclaredPackageDependency leftDependency =
                    left.Dependencies[dependencyIndex];
                DeclaredPackageDependency rightDependency =
                    right.Dependencies[dependencyIndex];
                if (!string.Equals(
                        leftDependency.Id,
                        rightDependency.Id,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        leftDependency.VersionRange,
                        rightDependency.VersionRange,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Package manifest facts and dependency-group facts must describe the same group set.",
                        nameof(groups));
                }
            }
        }
    }

    private static void RequirePackageAcquisitionForm(
        PackageDependencyEvidenceAcquisitionForm acquisitionForm,
        PackageSourceResultIdentity? source = null)
    {
        if (acquisitionForm is not (
            PackageDependencyEvidenceAcquisitionForm.PackageArchive
            or PackageDependencyEvidenceAcquisitionForm.DirectNuspec
            or PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest))
        {
            throw new ArgumentException(
                "A package-manifest root requires package or package-source provenance.",
                nameof(acquisitionForm));
        }

        if (acquisitionForm
                == PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest
            && source is null)
        {
            throw new ArgumentException(
                "A package-source manifest root requires source provenance.",
                nameof(source));
        }
    }

    private static void RequireRestoredAcquisitionForm(
        PackageDependencyEvidenceAcquisitionForm acquisitionForm)
    {
        if (acquisitionForm is not (
            PackageDependencyEvidenceAcquisitionForm.ProjectAssets
            or PackageDependencyEvidenceAcquisitionForm.ProjectLocator))
        {
            throw new ArgumentException(
                "A restored-project root requires direct-assets or project-locator provenance.",
                nameof(acquisitionForm));
        }
    }

    private static void RequireKnownAcquisitionForm(
        PackageDependencyEvidenceAcquisitionForm acquisitionForm)
    {
        if (!Enum.IsDefined(acquisitionForm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(acquisitionForm),
                acquisitionForm,
                "Unknown package dependency evidence acquisition form.");
        }
    }

    private static PackageSourceCoordinate?
        CreateEstablishedPackageProfileCoordinate(
            PackageProfileFailure failure) =>
            failure.Kind is PackageProfileFailureKind.ManifestAcquisition
                or PackageProfileFailureKind.ManifestContract
                or PackageProfileFailureKind.InvalidManifest
                ? TryCreatePackageCoordinate(failure.PackageId, failure.Version)
                : null;

    private static InertString? TreatOptionalField(string? value) =>
        value is null
            ? null
            : new InertString(
                TextPolicy.Field,
                value,
                PackageManifestFactsQuery.MaxScalarCharacters);

    private static PackageSourceCoordinate? TryCreatePackageCoordinate(
        string? packageId,
        string? version)
    {
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        try
        {
            return PackageSourceCoordinate.Create(packageId, version);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static PackageDependencyEvidencePhaseSummary SummarizePhases(
        ImmutableArray<PackageDependencyEvidenceRoot> roots)
    {
        int notApplicableDeclarations = 0;
        int completeDeclarations = 0;
        int incompleteDeclarations = 0;
        int unavailableDeclarations = 0;
        int failedDeclarations = 0;
        int notApplicableRelationships = 0;
        int completeRelationships = 0;
        int incompleteRelationships = 0;
        int unavailableRelationships = 0;
        int failedRelationships = 0;
        int notApplicableProcessing = 0;
        int completeProcessing = 0;
        int incompleteProcessing = 0;
        int unavailableProcessing = 0;
        int failedProcessing = 0;
        foreach (PackageDependencyEvidenceRoot root in roots)
        {
            switch (root.Declaration)
            {
                case PackageDependencyEvidenceDeclarationResult.NotApplicable:
                    notApplicableDeclarations++;
                    break;
                case PackageDependencyEvidenceDeclarationResult.Available
                    { IsComplete: true }:
                    completeDeclarations++;
                    break;
                case PackageDependencyEvidenceDeclarationResult.Available:
                    incompleteDeclarations++;
                    break;
                case PackageDependencyEvidenceDeclarationResult.Unavailable:
                    unavailableDeclarations++;
                    break;
                case PackageDependencyEvidenceDeclarationResult.Failed:
                    failedDeclarations++;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown package dependency declaration result.");
            }

            switch (root.Relationships)
            {
                case PackageDependencyEvidenceRelationshipResult.NotApplicable:
                    notApplicableRelationships++;
                    break;
                case PackageDependencyEvidenceRelationshipResult.Available
                    { IsComplete: true }:
                    completeRelationships++;
                    break;
                case PackageDependencyEvidenceRelationshipResult.Available:
                    incompleteRelationships++;
                    break;
                case PackageDependencyEvidenceRelationshipResult.Unavailable:
                    unavailableRelationships++;
                    break;
                case PackageDependencyEvidenceRelationshipResult.Failed:
                    failedRelationships++;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown package dependency relationship result.");
            }

            switch (root.Processing)
            {
                case PackageDependencyEvidenceProcessingResult.NotApplicable:
                    notApplicableProcessing++;
                    break;
                case PackageDependencyEvidenceProcessingResult.Available
                    { IsComplete: true }:
                    completeProcessing++;
                    break;
                case PackageDependencyEvidenceProcessingResult.Available:
                    incompleteProcessing++;
                    break;
                case PackageDependencyEvidenceProcessingResult.Unavailable:
                    unavailableProcessing++;
                    break;
                case PackageDependencyEvidenceProcessingResult.Failed:
                    failedProcessing++;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown package dependency processing result.");
            }
        }

        return new PackageDependencyEvidencePhaseSummary(
            new PackageDependencyEvidencePhaseCounts(
                notApplicableDeclarations,
                completeDeclarations,
                incompleteDeclarations,
                unavailableDeclarations,
                failedDeclarations),
            new PackageDependencyEvidencePhaseCounts(
                notApplicableRelationships,
                completeRelationships,
                incompleteRelationships,
                unavailableRelationships,
                failedRelationships),
            new PackageDependencyEvidencePhaseCounts(
                notApplicableProcessing,
                completeProcessing,
                incompleteProcessing,
                unavailableProcessing,
                failedProcessing));
    }

    private static PackageDependencyEvidenceComparisonResult CompareCore(
        ImmutableArray<PackageDependencyEvidenceGroup> left,
        ImmutableArray<PackageDependencyEvidenceGroup> right) =>
        CanonicalCoreSignatures(left).SequenceEqual(
            CanonicalCoreSignatures(right),
            StringComparer.Ordinal)
            ? new PackageDependencyEvidenceComparisonResult.Equal()
            : new PackageDependencyEvidenceComparisonResult.Unequal();

    private static PackageDependencyEvidenceComparisonResult CompareScoped(
        PackageDependencyEvidenceInputKind leftKind,
        ImmutableArray<PackageDependencyEvidenceGroup> left,
        PackageDependencyEvidenceInputKind rightKind,
        ImmutableArray<PackageDependencyEvidenceGroup> right)
    {
        if (!ScopesAreComparable(leftKind, left, rightKind, right))
        {
            return new PackageDependencyEvidenceComparisonResult.NotComparable(
                PackageDependencyEvidenceNotComparableReason.FrameworkScope);
        }

        return CanonicalScopedSignatures(left).SequenceEqual(
            CanonicalScopedSignatures(right),
            StringComparer.Ordinal)
            ? new PackageDependencyEvidenceComparisonResult.Equal()
            : new PackageDependencyEvidenceComparisonResult.Unequal();
    }

    private static (
        PackageDependencyEvidenceComparisonResult Core,
        PackageDependencyEvidenceComparisonResult Scoped)
        CompareSelected(
            PackageDependencyEvidenceRoot left,
            PackageDependencyEvidenceDeclarationResult.Available leftDeclaration,
            PackageDependencyEvidenceRoot right,
            PackageDependencyEvidenceDeclarationResult.Available rightDeclaration)
    {
        if (left.Selection.Status
                == PackageDependencyEvidenceSelectionStatus.Unavailable
            || right.Selection.Status
                == PackageDependencyEvidenceSelectionStatus.Unavailable)
        {
            var unavailable =
                new PackageDependencyEvidenceComparisonResult.NotComparable(
                    PackageDependencyEvidenceNotComparableReason
                        .SelectionStatusUnavailable);
            return (unavailable, unavailable);
        }

        if (left.Selection.Status != right.Selection.Status)
        {
            var unequal = new PackageDependencyEvidenceComparisonResult.Unequal();
            return (unequal, unequal);
        }

        if (left.Selection.Status != PackageDependencyEvidenceSelectionStatus.Selected)
        {
            var equal = new PackageDependencyEvidenceComparisonResult.Equal();
            return (equal, equal);
        }

        PackageDependencyEvidenceGroup leftGroup =
            RequireSelectedGroup(left.Selection, leftDeclaration);
        PackageDependencyEvidenceGroup rightGroup =
            RequireSelectedGroup(right.Selection, rightDeclaration);
        return (
            CompareCore([leftGroup], [rightGroup]),
            CompareScoped(
                left.InputKind,
                [leftGroup],
                right.InputKind,
                [rightGroup]));
    }

    private static PackageDependencyEvidenceGroup RequireSelectedGroup(
        PackageDependencyEvidenceSelection selection,
        PackageDependencyEvidenceDeclarationResult.Available declaration)
    {
        PackageDependencyEvidenceGroupIdentity identity =
            selection.SelectedGroup
            ?? throw new InvalidOperationException(
                "A selected dependency group requires a normalized logical-group identity.");
        return declaration.Groups.Single(group => group.Identity == identity);
    }

    private static bool ScopesAreComparable(
        PackageDependencyEvidenceInputKind leftKind,
        ImmutableArray<PackageDependencyEvidenceGroup> left,
        PackageDependencyEvidenceInputKind rightKind,
        ImmutableArray<PackageDependencyEvidenceGroup> right)
    {
        ImmutableArray<string> leftOpaque = OpaqueScopeIdentities(left);
        ImmutableArray<string> rightOpaque = OpaqueScopeIdentities(right);
        if (leftOpaque.IsEmpty && rightOpaque.IsEmpty)
            return true;

        return leftKind == rightKind
            && leftOpaque.SequenceEqual(rightOpaque, StringComparer.Ordinal);
    }

    private static ImmutableArray<string> OpaqueScopeIdentities(
        ImmutableArray<PackageDependencyEvidenceGroup> groups) =>
    [
        .. groups
            .Where(group =>
                group.FrameworkScope.Kind
                    == PackageDependencyFrameworkScopeKind.UnrecognizedFramework)
            .Select(group =>
                group.FrameworkScope.OpaqueIdentity
                ?? throw new InvalidOperationException(
                    "An unrecognized framework scope requires opaque comparison identity."))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private static ImmutableArray<string> CanonicalCoreSignatures(
        ImmutableArray<PackageDependencyEvidenceGroup> groups) =>
    [
        .. groups.Select(CanonicalCoreSignature).Order(StringComparer.Ordinal),
    ];

    private static ImmutableArray<string> CanonicalScopedSignatures(
        ImmutableArray<PackageDependencyEvidenceGroup> groups) =>
    [
        .. groups.Select(group =>
                EncodePart(CanonicalScopeIdentity(group.FrameworkScope))
                + CanonicalCoreSignature(group))
            .Order(StringComparer.Ordinal),
    ];

    private static string CanonicalCoreSignature(
        PackageDependencyEvidenceGroup group) =>
        string.Concat(group.Declarations.Select(declaration =>
            EncodePart(declaration.CanonicalPackageId)
            + EncodePart(declaration.CanonicalVersionConstraint)));

    private static string CanonicalScopeIdentity(
        PackageDependencyFrameworkScopeIdentity scope) =>
        scope.Kind switch
        {
            PackageDependencyFrameworkScopeKind.AnyFramework => "any",
            PackageDependencyFrameworkScopeKind.ExactFramework =>
                "exact:" + scope.CanonicalFramework,
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework =>
                "opaque:" + scope.OpaqueIdentity,
            _ => throw new InvalidOperationException(
                "Unknown package dependency framework scope."),
        };

    private static string EncodePart(string value) =>
        value.Length.ToString(
            System.Globalization.CultureInfo.InvariantCulture)
        + ":"
        + value;
}
