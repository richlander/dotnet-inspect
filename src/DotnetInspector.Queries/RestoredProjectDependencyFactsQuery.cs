using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// An optional exact target request: a target framework and, only together with it, a runtime
/// identifier. See <c>docs/design/restored-project-dependency-facts.md</c> for the selection
/// contract this drives.
/// </summary>
public sealed record RestoredProjectTargetRequest
{
    public RestoredProjectTargetRequest(string framework, string? runtimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        if (runtimeIdentifier is { Length: 0 })
        {
            throw new ArgumentException(
                "A runtime identifier request must be null or non-empty.",
                nameof(runtimeIdentifier));
        }

        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
    }

    /// <summary>The exact requested framework spelling, matched ordinally case-insensitively.</summary>
    public string Framework { get; }

    /// <summary>The exact requested runtime identifier spelling, or <see langword="null"/> for a framework-only request.</summary>
    public string? RuntimeIdentifier { get; }
}

/// <summary>Whether a selected target came from an explicit request or the query's own default rule.</summary>
public enum RestoredProjectTargetSelectionProvenance
{
    /// <summary>The caller supplied an exact target request.</summary>
    Requested,

    /// <summary>No request was supplied; the query selected a default target.</summary>
    Default,
}

/// <summary>Whether a declaration group's authored framework spelling resolved to a recognized canonical framework.</summary>
public enum RestoredProjectFrameworkIdentityKind
{
    /// <summary>NuGet target-framework semantics established a canonical short-folder identity.</summary>
    Recognized,

    /// <summary>The spelling is retained as an explicit, unrepaired opaque owner identity.</summary>
    Unrecognized,
}

/// <summary>Whether a package-resolving edge is reached directly from the root or transitively through another node.</summary>
public enum RestoredProjectDependencyRole
{
    /// <summary>At least one root edge reaches this package.</summary>
    Direct,

    /// <summary>Every edge reaching this package originates from another package or project node.</summary>
    Transitive,
}

/// <summary>
/// Whether a phase projected every fact it could see. Carried explicitly rather than inferred
/// from an empty failure collection, so a caller cannot read completion out of an array that
/// merely happens to be empty.
/// </summary>
public enum RestoredProjectPhaseCompletion
{
    /// <summary>Every fact the phase's capability offered was projected.</summary>
    Complete,

    /// <summary>Usable evidence remains, and typed failures prove the phase is partial.</summary>
    Incomplete,
}

/// <summary>
/// Builds and validates the only two identity currencies this query issues: canonical, already
/// validated coordinate text, and an opaque SHA-256 token over exact artifact-authored text.
/// </summary>
/// <remarks>
/// No artifact-authored spelling reaches a public identity string. A framework, runtime
/// identifier, or coordinate that passes
/// <see cref="PackageCoordinateResolver.IsAcquisitionTargetText"/> (or the canonical package-id
/// grammar) is bounded ASCII currency and travels as itself. Everything else — an unrecognized
/// framework spelling, a project target-entry key — becomes <c>sha256:&lt;hex&gt;</c> over its
/// exact UTF-8 bytes. The two forms cannot collide: <c>:</c> is outside the canonical grammar,
/// so no canonical value can spell an opaque token. Distinct source text yields distinct opaque
/// tokens, so two different unrecognized frameworks never compare equal.
/// </remarks>
static class RestoredProjectIdentityText
{
    public const string OpaquePrefix = "sha256:";

    public static string Opaque(string sourceText) =>
        OpaquePrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceText)));

    public static bool IsLowerHex(string value)
    {
        foreach (char c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        }

        return true;
    }

    public static bool IsOpaque(string value) =>
        value.Length == OpaquePrefix.Length + 64
        && value.StartsWith(OpaquePrefix, StringComparison.Ordinal)
        && IsLowerHex(value[OpaquePrefix.Length..]);

    /// <summary>True for one identity segment: canonical acquisition-target text or an opaque token.</summary>
    public static bool IsSafeSegment(string value) =>
        PackageCoordinateResolver.IsAcquisitionTargetText(value) || IsOpaque(value);

    /// <summary>True for a whole target identity: empty, one segment, or <c>framework/runtime</c>.</summary>
    public static bool IsSafeTargetIdentity(string value)
    {
        if (value.Length == 0)
            return true;

        int separator = value.IndexOf('/');
        return separator < 0
            ? IsSafeSegment(value)
            : IsSafeSegment(value[..separator]) && IsSafeSegment(value[(separator + 1)..]);
    }
}

/// <summary>
/// Exact-content provenance over the caller-supplied bytes. This is never semantic identity:
/// harmless JSON property reordering changes it while <see cref="RestoredProjectSelectionIdentity"/>
/// stays the same.
/// </summary>
public sealed record RestoredProjectContentProvenance
{
    public RestoredProjectContentProvenance(string sha256)
    {
        if (sha256 is not { Length: 64 } || !RestoredProjectIdentityText.IsLowerHex(sha256))
        {
            throw new ArgumentException(
                "A content provenance digest must be a lowercase 64-character SHA-256 hex string.",
                nameof(sha256));
        }

        Sha256 = sha256;
    }

    /// <summary>Lowercase hex SHA-256 over the exact admitted bytes.</summary>
    public string Sha256 { get; }

    internal static RestoredProjectContentProvenance FromBytes(ReadOnlyMemory<byte> bytes) =>
        new(Convert.ToHexStringLower(SHA256.HashData(bytes.Span)));
}

/// <summary>
/// The stable semantic identity of one selection: the selected target identity combined with a
/// deterministic digest over the query's canonical declaration and selected-graph facts. No
/// local path, request spelling, JSON property position, or raw byte order participates, so a
/// default request and an explicit request that select the same target share this identity while
/// retaining distinct <see cref="RestoredProjectSelectedTarget.Provenance"/>.
/// </summary>
public sealed record RestoredProjectSelectionIdentity
{
    public RestoredProjectSelectionIdentity(string targetIdentity, string factsDigest)
    {
        ArgumentNullException.ThrowIfNull(targetIdentity);
        if (!RestoredProjectIdentityText.IsSafeTargetIdentity(targetIdentity))
        {
            throw new ArgumentException(
                "A selection target identity must be canonical target text or an opaque digest.",
                nameof(targetIdentity));
        }

        if (factsDigest is not { Length: 64 } || !RestoredProjectIdentityText.IsLowerHex(factsDigest))
        {
            throw new ArgumentException(
                "A selection identity digest must be a lowercase 64-character SHA-256 hex string.",
                nameof(factsDigest));
        }

        TargetIdentity = targetIdentity;
        FactsDigest = factsDigest;
    }

    /// <summary>
    /// The selected target's identity (<c>framework</c> or <c>framework/runtime-identifier</c>,
    /// each segment canonical target text or an opaque digest), or the empty string when no
    /// target was selected.
    /// </summary>
    public string TargetIdentity { get; }

    /// <summary>Lowercase hex SHA-256 over the canonical declaration and selected-graph facts.</summary>
    public string FactsDigest { get; }
}

/// <summary>The target this query selected from <c>targets</c>, or the outcome of an unsatisfied request.</summary>
/// <param name="FrameworkIdentity">Canonical framework text, or an opaque digest over the authored spelling.</param>
/// <param name="RuntimeIdentifierIdentity">Canonical runtime-identifier text, an opaque digest, or <see langword="null"/>.</param>
public sealed record RestoredProjectSelectedTarget(
    string FrameworkIdentity,
    string? RuntimeIdentifierIdentity,
    InertString SourceFrameworkSpelling,
    InertString? SourceRuntimeIdentifierSpelling,
    RestoredProjectTargetSelectionProvenance Provenance);

/// <summary>Identifies the single project whose restore produced the admitted assets document.</summary>
public readonly record struct RestoredProjectRootIdentity(RestoredProjectSelectionIdentity Selection);

/// <summary>
/// Identifies one authored <c>project.frameworks</c> declaration group within a selection by its
/// exact authored pivot occurrence.
/// </summary>
/// <param name="PivotIdentity">
/// The canonical framework spelling when the authored pivot is recognized and already exactly
/// that spelling; otherwise an opaque digest over the exact authored pivot. Two distinct authored
/// pivots — including case-only variants — therefore never share an identity.
/// </param>
public readonly record struct RestoredProjectDeclarationGroupIdentity(
    RestoredProjectSelectionIdentity Selection,
    string PivotIdentity);

/// <summary>Identifies one resolved package node within a selection by its validated canonical coordinate.</summary>
public readonly record struct RestoredProjectPackageNodeIdentity(
    RestoredProjectSelectionIdentity Selection,
    PackageSourceCoordinate Coordinate);

/// <summary>
/// Identifies one resolved project node within a selection by an opaque digest over its exact
/// selected-target entry key. A project entry name is authored text with no canonical grammar,
/// so it is never spelled into a public identity.
/// </summary>
public readonly record struct RestoredProjectProjectNodeIdentity(
    RestoredProjectSelectionIdentity Selection,
    string SourceIdentity);

/// <summary>The closed set of graph-edge parents: the root, a package node, or a project node.</summary>
public abstract record RestoredProjectGraphParentIdentity
{
    private RestoredProjectGraphParentIdentity()
    {
    }

    public sealed record Root(RestoredProjectRootIdentity Identity) : RestoredProjectGraphParentIdentity;

    public sealed record Package(RestoredProjectPackageNodeIdentity Identity) : RestoredProjectGraphParentIdentity;

    public sealed record Project(RestoredProjectProjectNodeIdentity Identity) : RestoredProjectGraphParentIdentity;

    /// <summary>Builds an unscoped root parent marker; a later rescoping pass fills in its selection identity.</summary>
    internal static RestoredProjectGraphParentIdentity CreateRoot() => new Root(default);

    /// <summary>Builds an unscoped package-parent marker from an already-resolved package identity.</summary>
    internal static RestoredProjectGraphParentIdentity CreatePackageParent(RestoredProjectPackageNodeIdentity identity) =>
        new Package(identity);

    /// <summary>Builds an unscoped project-parent marker from an opaque digest over its target-entry key.</summary>
    internal static RestoredProjectGraphParentIdentity CreateProjectParent(string sourceIdentity) =>
        new Project(new RestoredProjectProjectNodeIdentity(default!, sourceIdentity));
}

/// <summary>Identifies one package-resolving graph edge by its parent and resolved dependency.</summary>
public readonly record struct RestoredProjectEdgeIdentity(
    RestoredProjectGraphParentIdentity Parent,
    RestoredProjectPackageNodeIdentity Dependency);

/// <summary>A declaration group's authored framework identity: recognized canonical, or explicitly unrecognized.</summary>
/// <param name="Identity">
/// The canonical framework spelling when <paramref name="Kind"/> is
/// <see cref="RestoredProjectFrameworkIdentityKind.Recognized"/>; otherwise an opaque digest over
/// the exact authored spelling, so two different unrecognized frameworks never compare equal.
/// </param>
public sealed record RestoredProjectFrameworkIdentity(
    RestoredProjectFrameworkIdentityKind Kind,
    string Identity);

/// <summary>One project-authored package dependency declaration, exactly as authored.</summary>
public sealed record RestoredProjectDeclaredPackage(
    string CanonicalPackageId,
    InertString SourcePackageIdSpelling,
    string CanonicalVersionConstraint,
    InertString SourceVersionConstraintSpelling,
    int SourceOccurrenceCount);

/// <summary>One <c>project.frameworks</c> declaration group, including a valid empty group.</summary>
public sealed record RestoredProjectDeclarationGroup(
    RestoredProjectDeclarationGroupIdentity Identity,
    InertString SourcePivotSpelling,
    string OrderKey,
    RestoredProjectFrameworkIdentity FrameworkIdentity,
    ImmutableArray<RestoredProjectDeclaredPackage> Packages);

/// <summary>Why one declaration-phase fact could not be represented, or why the whole phase failed.</summary>
public enum RestoredProjectDeclarationFailureReason
{
    /// <summary>The declaration capability, one group pivot, or one group's <c>dependencies</c> had an invalid shape.</summary>
    InvalidGroupShape,

    /// <summary>One dependency entry did not explicitly classify its <c>target</c> as Package or Project.</summary>
    UnclassifiedDependencyTarget,

    /// <summary>One package declaration had an invalid identity or version constraint and was skipped.</summary>
    InvalidPackageDeclaration,

    /// <summary>Two case-only-duplicate declarations for one package disagreed on constraint.</summary>
    ConflictingPackageDeclaration,

    /// <summary>A configured declaration bound was exceeded.</summary>
    ConfiguredLimitExceeded,
}

/// <summary>
/// A content-free declaration-phase typed failure and how many source occurrences it aggregates.
/// </summary>
/// <remarks>
/// One failure per reason per phase. Repeated occurrences raise <see cref="Count"/> rather than
/// appending another array element, so the public evidence cannot depend on JSON property order.
/// </remarks>
public sealed record RestoredProjectDeclarationFailure(
    RestoredProjectDeclarationFailureReason Reason,
    int Count = 1)
{
    /// <summary>How many source occurrences this reason aggregates. Always at least one.</summary>
    public int Count { get; } = Count >= 1
        ? Count
        : throw new ArgumentOutOfRangeException(nameof(Count), Count, "A failure count must be at least one.");

    public string Message => Reason switch
    {
        RestoredProjectDeclarationFailureReason.InvalidGroupShape =>
            "A project.frameworks declaration group has an invalid shape.",
        RestoredProjectDeclarationFailureReason.UnclassifiedDependencyTarget =>
            "A project.frameworks dependency entry does not classify its target as a package or a project.",
        RestoredProjectDeclarationFailureReason.InvalidPackageDeclaration =>
            "A project.frameworks package declaration has an invalid identity or version constraint.",
        RestoredProjectDeclarationFailureReason.ConflictingPackageDeclaration =>
            "A project.frameworks package declaration repeats with a conflicting version constraint.",
        RestoredProjectDeclarationFailureReason.ConfiguredLimitExceeded =>
            "The declaration phase exceeds a configured resource limit.",
        _ => "The declaration phase could not be projected.",
    };
}

/// <summary>The closed outcome of projecting <c>project.frameworks</c> declaration evidence.</summary>
public abstract record RestoredProjectDeclarationResult
{
    private RestoredProjectDeclarationResult()
    {
    }

    /// <summary>Usable declaration evidence, with completion stated rather than inferred.</summary>
    public sealed record Available : RestoredProjectDeclarationResult
    {
        public Available(
            ImmutableArray<RestoredProjectDeclarationGroup> groups,
            ImmutableArray<RestoredProjectDeclarationFailure> failures,
            RestoredProjectPhaseCompletion completion)
        {
            if (failures.IsDefaultOrEmpty != (completion == RestoredProjectPhaseCompletion.Complete))
            {
                throw new ArgumentException(
                    "A complete phase carries no failures and an incomplete phase carries at least one.",
                    nameof(completion));
            }

            Groups = groups.IsDefault ? [] : groups;
            Failures = failures.IsDefault ? [] : failures;
            Completion = completion;
        }

        public ImmutableArray<RestoredProjectDeclarationGroup> Groups { get; }

        public ImmutableArray<RestoredProjectDeclarationFailure> Failures { get; }

        public RestoredProjectPhaseCompletion Completion { get; }

        public bool IsComplete => Completion == RestoredProjectPhaseCompletion.Complete;
    }

    /// <summary>The document does not provide <c>project.frameworks</c>.</summary>
    public sealed record Unavailable : RestoredProjectDeclarationResult;

    /// <summary>The declaration capability has a fundamentally invalid shape.</summary>
    public sealed record Failed(RestoredProjectDeclarationFailure Failure) : RestoredProjectDeclarationResult;
}

/// <summary>
/// Positive evidence that the selected declaration group carries a valid
/// <c>packagesToPrune</c> map.
/// </summary>
public sealed record RestoredProjectPackagePruningEvidence(
    RestoredProjectDeclarationGroupIdentity DeclarationGroup);

/// <summary>Why selected-group package-pruning evidence could not be projected.</summary>
public enum RestoredProjectPackagePruningFailureReason
{
    /// <summary>More than one declaration group corresponds to the selected target.</summary>
    AmbiguousDeclarationGroupAssociation,

    /// <summary>The selected group's <c>packagesToPrune</c> member is not an object.</summary>
    InvalidShape,

    /// <summary>One or more pruning rules has an invalid package identity or version range.</summary>
    InvalidRule,

    /// <summary>The pruning-rule collection exceeds its configured bound.</summary>
    ConfiguredLimitExceeded,
}

/// <summary>A content-free package-pruning evidence failure.</summary>
public sealed record RestoredProjectPackagePruningFailure(
    RestoredProjectPackagePruningFailureReason Reason,
    int Count = 1)
{
    public int Count { get; } = Count >= 1
        ? Count
        : throw new ArgumentOutOfRangeException(
            nameof(Count),
            Count,
            "A failure count must be at least one.");

    public string Message => Reason switch
    {
        RestoredProjectPackagePruningFailureReason
            .AmbiguousDeclarationGroupAssociation =>
                "More than one project.frameworks group corresponds to the selected target.",
        RestoredProjectPackagePruningFailureReason.InvalidShape =>
            "The selected declaration group's packagesToPrune member has an invalid shape.",
        RestoredProjectPackagePruningFailureReason.InvalidRule =>
            "A packagesToPrune entry has an invalid package identity or version range.",
        RestoredProjectPackagePruningFailureReason.ConfiguredLimitExceeded =>
            "The packagesToPrune collection exceeds a configured resource limit.",
        _ => "Package-pruning evidence could not be projected.",
    };
}

/// <summary>The closed outcome of projecting selected-group package-pruning evidence.</summary>
public abstract record RestoredProjectPackagePruningResult
{
    private RestoredProjectPackagePruningResult()
    {
    }

    /// <summary>A valid <c>packagesToPrune</c> map was associated with the selected target.</summary>
    public sealed record Available(RestoredProjectPackagePruningEvidence Evidence) :
        RestoredProjectPackagePruningResult;

    /// <summary>No selected-group <c>packagesToPrune</c> evidence is available.</summary>
    public sealed record Unavailable : RestoredProjectPackagePruningResult;

    /// <summary>Present pruning evidence could not be associated or validated soundly.</summary>
    public sealed record Failed(RestoredProjectPackagePruningFailure Failure) :
        RestoredProjectPackagePruningResult;
}

/// <summary>One resolved package node reached from the root, with its aggregate direct/transitive role.</summary>
public sealed record RestoredProjectPackageNode(
    RestoredProjectPackageNodeIdentity Identity,
    RestoredProjectDependencyRole Role);

/// <summary>One package-resolving graph edge.</summary>
public sealed record RestoredProjectGraphEdge(
    RestoredProjectEdgeIdentity Identity,
    RestoredProjectGraphParentIdentity Parent,
    RestoredProjectPackageNodeIdentity Dependency,
    string CanonicalVersionConstraint,
    InertString SourceVersionConstraintSpelling,
    RestoredProjectDependencyRole Role,
    RestoredProjectDeclarationGroupIdentity? DeclarationAssociation)
{
    public RestoredProjectGraphParentIdentity Parent { get; } =
        Parent ?? throw new ArgumentNullException(nameof(Parent));

    public RestoredProjectDeclarationGroupIdentity? DeclarationAssociation { get; } =
        (Parent is RestoredProjectGraphParentIdentity.Root)
            == DeclarationAssociation.HasValue
                ? DeclarationAssociation
                : throw new ArgumentException(
                    "A root edge requires its correlated declaration group, and a non-root edge cannot carry one.",
                    nameof(DeclarationAssociation));
}

/// <summary>Why one graph-phase fact could not be represented, or why the whole phase failed.</summary>
public enum RestoredProjectGraphFailureReason
{
    /// <summary>A root entry could not be parsed, resolved, or given a root constraint.</summary>
    UnresolvedRootEntry,

    /// <summary>A reachable-node dependency had no unique selected-target node and was skipped.</summary>
    UnresolvedDependency,

    /// <summary>A reachable node's <c>dependencies</c> is present but is not an object.</summary>
    InvalidNodeShape,

    /// <summary>Two edges share one parent and dependency but disagree on constraint, so neither is emitted.</summary>
    ConflictingEdgeConstraint,

    /// <summary>A configured graph bound was exceeded.</summary>
    ConfiguredLimitExceeded,

    /// <summary>The selected target's own shape could not be interpreted.</summary>
    UnresolvableSelectedTargetShape,

    /// <summary>Two <c>targets</c> pivots share one canonical target identity, so selection would follow JSON order.</summary>
    AmbiguousTargetIdentity,

    /// <summary>More than one root-scoped correlation candidate exists, so root identity would be ambiguous.</summary>
    AmbiguousRootCorrelation,

    /// <summary>The uniquely correlated root group's own <c>dependencies</c> shape is invalid.</summary>
    InvalidRootCorrelationShape,
}

/// <summary>
/// A content-free graph-phase typed failure and how many source occurrences it aggregates.
/// </summary>
/// <remarks>
/// One failure per reason per phase. Repeated occurrences raise <see cref="Count"/> rather than
/// appending another array element, so the public evidence cannot depend on JSON property order.
/// </remarks>
public sealed record RestoredProjectGraphFailure(
    RestoredProjectGraphFailureReason Reason,
    int Count = 1)
{
    /// <summary>How many source occurrences this reason aggregates. Always at least one.</summary>
    public int Count { get; } = Count >= 1
        ? Count
        : throw new ArgumentOutOfRangeException(nameof(Count), Count, "A failure count must be at least one.");

    public string Message => Reason switch
    {
        RestoredProjectGraphFailureReason.UnresolvedRootEntry =>
            "A projectFileDependencyGroups root entry could not be resolved against the selected target.",
        RestoredProjectGraphFailureReason.UnresolvedDependency =>
            "A reachable dependency has no unique selected-target node.",
        RestoredProjectGraphFailureReason.InvalidNodeShape =>
            "A reachable selected-target node declares a dependencies member that is not an object.",
        RestoredProjectGraphFailureReason.ConflictingEdgeConstraint =>
            "Two graph edges share one parent and dependency but declare conflicting version constraints.",
        RestoredProjectGraphFailureReason.ConfiguredLimitExceeded =>
            "The graph phase exceeds a configured resource limit.",
        RestoredProjectGraphFailureReason.UnresolvableSelectedTargetShape =>
            "The selected target has an unresolvable shape.",
        RestoredProjectGraphFailureReason.AmbiguousTargetIdentity =>
            "Two targets pivots share one canonical target identity.",
        RestoredProjectGraphFailureReason.AmbiguousRootCorrelation =>
            "More than one root-scoped correlation candidate exists for the selected target.",
        RestoredProjectGraphFailureReason.InvalidRootCorrelationShape =>
            "The correlated root declaration group has an invalid dependencies shape.",
        _ => "The graph phase could not be projected.",
    };
}

/// <summary>The closed outcome of projecting restored-graph evidence for the selected target.</summary>
public abstract record RestoredProjectGraphResult
{
    private RestoredProjectGraphResult()
    {
    }

    /// <summary>Usable graph evidence, with completion stated rather than inferred.</summary>
    public sealed record Available : RestoredProjectGraphResult
    {
        public Available(
            ImmutableArray<RestoredProjectPackageNode> packages,
            ImmutableArray<RestoredProjectGraphEdge> edges,
            ImmutableArray<RestoredProjectGraphFailure> failures,
            RestoredProjectPhaseCompletion completion)
        {
            if (failures.IsDefaultOrEmpty != (completion == RestoredProjectPhaseCompletion.Complete))
            {
                throw new ArgumentException(
                    "A complete phase carries no failures and an incomplete phase carries at least one.",
                    nameof(completion));
            }

            Packages = packages.IsDefault ? [] : packages;
            Edges = edges.IsDefault ? [] : edges;
            Failures = failures.IsDefault ? [] : failures;
            Completion = completion;
        }

        public ImmutableArray<RestoredProjectPackageNode> Packages { get; }

        public ImmutableArray<RestoredProjectGraphEdge> Edges { get; }

        public ImmutableArray<RestoredProjectGraphFailure> Failures { get; }

        public RestoredProjectPhaseCompletion Completion { get; }

        public bool IsComplete => Completion == RestoredProjectPhaseCompletion.Complete;
    }

    /// <summary>Targets, a matching target, or a root dependency set are unavailable under the document's capabilities.</summary>
    public sealed record Unavailable : RestoredProjectGraphResult;

    /// <summary>Selected-target shape or identity ambiguity prevents a sound graph.</summary>
    public sealed record Failed(RestoredProjectGraphFailure Failure) : RestoredProjectGraphResult;
}

/// <summary>Immutable facts projected from one exact <c>project.assets.json</c> selection.</summary>
public sealed record RestoredProjectDependencyFacts(
    RestoredProjectContentProvenance ContentProvenance,
    RestoredProjectSelectionIdentity SelectionIdentity,
    RestoredProjectSelectedTarget? SelectedTarget,
    RestoredProjectRootIdentity Root,
    RestoredProjectDeclarationResult Declaration,
    RestoredProjectPackagePruningResult PackagePruning,
    RestoredProjectGraphResult Graph);

/// <summary>Why the whole document could not be admitted.</summary>
public enum RestoredProjectDependencyFailureReason
{
    /// <summary>The bytes are not well-formed JSON, or contain a duplicate property name.</summary>
    MalformedOrDuplicateBearingJson,

    /// <summary>The document root is not a JSON object, or a required top-level property has the wrong shape.</summary>
    UnsupportedDocumentShape,

    /// <summary>The document's <c>version</c> is missing or is not schema version 3 or 4.</summary>
    UnsupportedSchemaVersion,

    /// <summary>The admitted bytes exceed a configured whole-document limit.</summary>
    ConfiguredLimitExceeded,
}

/// <summary>A content-free whole-document typed failure.</summary>
public sealed record RestoredProjectDependencyFailure(
    RestoredProjectDependencyFailureReason Reason)
{
    public string Message => Reason switch
    {
        RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson =>
            "The restored-project assets document is not well-formed JSON, or contains a duplicate property name.",
        RestoredProjectDependencyFailureReason.UnsupportedDocumentShape =>
            "The restored-project assets document has an unsupported document shape.",
        RestoredProjectDependencyFailureReason.UnsupportedSchemaVersion =>
            "The restored-project assets document declares an unsupported schema version.",
        RestoredProjectDependencyFailureReason.ConfiguredLimitExceeded =>
            "The restored-project assets document exceeds a configured resource limit.",
        _ => "The restored-project assets document could not be projected.",
    };
}

/// <summary>The typed outcome of executing the restored-project dependency facts query.</summary>
public abstract record RestoredProjectDependencyFactsResult
{
    private RestoredProjectDependencyFactsResult()
    {
    }

    public sealed record Available(RestoredProjectDependencyFacts Value) : RestoredProjectDependencyFactsResult;

    public sealed record Failed(RestoredProjectDependencyFailure Failure) : RestoredProjectDependencyFactsResult;
}

/// <summary>
/// Projects immutable declared-dependency and restored-graph facts from one exact, caller-supplied
/// <c>project.assets.json</c> byte sequence. See
/// <c>docs/design/restored-project-dependency-facts.md</c> for the full contract this query owns.
/// </summary>
/// <remarks>
/// The query accepts no path, filesystem, MSBuild, restore, cache, logger, or renderer capability.
/// A <c>.csproj</c> locator and a direct assets path are equivalent at this boundary once they
/// supply the same bytes and target request.
/// </remarks>
public static class RestoredProjectDependencyFactsQuery
{
    public const int MaxAssetsBytes = 4 * 1024 * 1024;
    public const int MaxScalarCharacters = 1024;
    public const int MaxDeclarationGroups = 256;
    public const int MaxDeclaredPackages = 4096;
    public const int MaxDeclaredProjectReferences = 4096;
    public const int MaxPackagePruningRules = 4096;
    public const int MaxGraphNodes = 8192;
    public const int MaxGraphEdges = 16384;

    public static InspectionQuery<RestoredProjectDependencyFactsResult> Definition { get; } =
        new("Restored project dependency facts", InspectionCost.NetworkFree);

    public static RestoredProjectDependencyFactsResult Execute(
        ReadOnlyMemory<byte> assetsBytes,
        RestoredProjectTargetRequest? request = null) =>
        Project(assetsBytes, request).Result;

    /// <summary>
    /// The single owner-issued projection over one exact assets byte sequence. It produces the
    /// published facts result and, from the same admission, target selection, and graph walk, the
    /// internal project-relationship topology that
    /// <see cref="RestoredProjectDependencyTraversalQuery"/> consumes. There is exactly one
    /// implementation of assets admission, target selection, and selected-target traversal.
    /// </summary>
    internal static RestoredProjectDependencyProjection Project(
        ReadOnlyMemory<byte> assetsBytes,
        RestoredProjectTargetRequest? request)
    {
        if (assetsBytes.Length > MaxAssetsBytes)
            return FailedProjection(RestoredProjectDependencyFailureReason.ConfiguredLimitExceeded);

        RestoredProjectContentProvenance provenance = RestoredProjectContentProvenance.FromBytes(assetsBytes);

        JsonDocument document;
        try
        {
            document = HardenedJson.Parse(assetsBytes);
        }
        catch (JsonException)
        {
            return FailedProjection(RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson);
        }
        catch (InvalidOperationException)
        {
            return FailedProjection(RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return FailedProjection(RestoredProjectDependencyFailureReason.UnsupportedDocumentShape);
            if (!ContainsOnlyValidJsonStrings(root))
                return FailedProjection(RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson);

            if (!TryGetSchemaVersion(root, out int schemaVersion))
                return FailedProjection(RestoredProjectDependencyFailureReason.UnsupportedSchemaVersion);

            if (!TryReadTargetsShape(root, out JsonElement targetsElement, out bool targetsPresent))
                return FailedProjection(RestoredProjectDependencyFailureReason.UnsupportedDocumentShape);

            // Duplicate canonical target identities are detected before selection so a
            // case-only pivot pair can never be resolved by JSON property order.
            ImmutableArray<TargetCandidate> candidates = [];
            bool ambiguousTargetIdentity = false;
            if (targetsPresent)
                ambiguousTargetIdentity = !TryReadTargetCandidates(targetsElement, out candidates);

            TargetCandidate? selectedCandidate = ambiguousTargetIdentity
                ? null
                : SelectTarget(candidates, request, schemaVersion);

            RestoredProjectSelectedTarget? selectedTarget = selectedCandidate is { } candidate
                ? new RestoredProjectSelectedTarget(
                    candidate.FrameworkIdentity,
                    candidate.RuntimeIdentifierIdentity,
                    new InertString(TextPolicy.Field, candidate.RawFramework, MaxScalarCharacters),
                    candidate.RawRuntimeIdentifier is null
                        ? null
                        : new InertString(TextPolicy.Field, candidate.RawRuntimeIdentifier, MaxScalarCharacters),
                    request is null
                        ? RestoredProjectTargetSelectionProvenance.Default
                        : RestoredProjectTargetSelectionProvenance.Requested)
                : null;

            string targetIdentityText = selectedTarget is null
                ? ""
                : selectedTarget.RuntimeIdentifierIdentity is null
                    ? selectedTarget.FrameworkIdentity
                    : $"{selectedTarget.FrameworkIdentity}/{selectedTarget.RuntimeIdentifierIdentity}";

            RestoredProjectDeclarationResult declaration = ProjectDeclaration(root);
            RestoredProjectPackagePruningResult packagePruning =
                ambiguousTargetIdentity
                    ? new RestoredProjectPackagePruningResult.Unavailable()
                    : ProjectPackagePruning(root, schemaVersion, selectedCandidate);

            RestoredProjectGraphTopology topology = RestoredProjectGraphTopology.Empty;
            RestoredProjectGraphResult graph = ambiguousTargetIdentity
                ? new RestoredProjectGraphResult.Failed(
                    new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.AmbiguousTargetIdentity))
                : ProjectGraph(root, schemaVersion, selectedCandidate, targetsElement, targetsPresent, out topology);

            string factsDigest = ComputeFactsDigest(
                targetIdentityText,
                declaration,
                packagePruning,
                graph);
            var selectionIdentity = new RestoredProjectSelectionIdentity(targetIdentityText, factsDigest);
            var rootIdentity = new RestoredProjectRootIdentity(selectionIdentity);

            return new RestoredProjectDependencyProjection(
                new RestoredProjectDependencyFactsResult.Available(
                    new RestoredProjectDependencyFacts(
                        provenance,
                        selectionIdentity,
                        selectedTarget,
                        rootIdentity,
                        RescopeDeclaration(declaration, selectionIdentity),
                        RescopePackagePruning(packagePruning, selectionIdentity),
                        RescopeGraph(graph, rootIdentity))),
                RescopeTopology(topology, rootIdentity));
        }
    }

    static RestoredProjectDependencyFactsResult Failed(RestoredProjectDependencyFailureReason reason) =>
        new RestoredProjectDependencyFactsResult.Failed(new RestoredProjectDependencyFailure(reason));

    static RestoredProjectDependencyProjection FailedProjection(RestoredProjectDependencyFailureReason reason) =>
        new(Failed(reason), RestoredProjectGraphTopology.Empty);

    static bool ContainsOnlyValidJsonStrings(JsonElement value)
    {
        try
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        _ = property.Name;
                        if (!ContainsOnlyValidJsonStrings(property.Value))
                            return false;
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (!ContainsOnlyValidJsonStrings(item))
                            return false;
                    }

                    break;
                case JsonValueKind.String:
                    _ = value.GetString();
                    break;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    static bool TryGetSchemaVersion(JsonElement root, out int version)
    {
        version = 0;
        if (!root.TryGetProperty("version", out JsonElement versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out int parsed))
        {
            return false;
        }

        if (parsed is not (3 or 4))
            return false;

        version = parsed;
        return true;
    }

    static bool TryReadTargetsShape(JsonElement root, out JsonElement targets, out bool present)
    {
        targets = default;
        present = false;
        if (!root.TryGetProperty("targets", out JsonElement targetsElement))
            return true;

        if (targetsElement.ValueKind != JsonValueKind.Object)
            return false;

        targets = targetsElement;
        present = true;
        return true;
    }

    static IEnumerable<JsonProperty> EnumeratePropertiesInCanonicalOrder(JsonElement value) =>
        value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal);

    // ---- Target selection -------------------------------------------------

    /// <summary>
    /// One <c>targets</c> pivot. <see cref="FrameworkIdentity"/> and
    /// <see cref="RuntimeIdentifierIdentity"/> are canonical-or-opaque public identities.
    /// </summary>
    sealed record TargetCandidate(
        string RawKey,
        string RawFramework,
        string? RawRuntimeIdentifier,
        string? NormalizedRuntimeIdentifier,
        string FrameworkIdentity,
        string? RuntimeIdentifierIdentity)
    {
        /// <summary>The complete containment-safe identity used to resolve equal-priority targets.</summary>
        public string IdentityKey => RuntimeIdentifierIdentity is null
            ? FrameworkIdentity
            : $"{FrameworkIdentity}/{RuntimeIdentifierIdentity}";
    }

    readonly record struct TargetCorrelationKey(string Framework, string? RuntimeIdentifier);

    sealed class TargetCorrelationKeyComparer : IEqualityComparer<TargetCorrelationKey>
    {
        public static TargetCorrelationKeyComparer Instance { get; } = new();

        public bool Equals(TargetCorrelationKey x, TargetCorrelationKey y) =>
            string.Equals(x.Framework, y.Framework, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.RuntimeIdentifier, y.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(TargetCorrelationKey value)
        {
            var hash = new HashCode();
            hash.Add(value.Framework, StringComparer.OrdinalIgnoreCase);
            hash.Add(value.RuntimeIdentifier, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    static bool TryReadTargetCandidates(JsonElement targets, out ImmutableArray<TargetCandidate> candidates)
    {
        var builder = ImmutableArray.CreateBuilder<TargetCandidate>();
        var seen = new HashSet<TargetCorrelationKey>(TargetCorrelationKeyComparer.Instance);
        foreach (JsonProperty property in EnumeratePropertiesInCanonicalOrder(targets))
        {
            string key = property.Name;
            if (key.Length == 0 || key.Length > MaxScalarCharacters)
                continue;

            int separator = key.IndexOf('/');
            string rawFramework = separator < 0 ? key : key[..separator];
            string? rawRuntimeIdentifier = separator < 0 ? null : key[(separator + 1)..];
            if (rawFramework.Length == 0 || rawRuntimeIdentifier is { Length: 0 })
                continue;

            string? normalizedRid = rawRuntimeIdentifier;
            var candidate = new TargetCandidate(
                key,
                rawFramework,
                rawRuntimeIdentifier,
                normalizedRid,
                FrameworkIdentityText(rawFramework),
                rawRuntimeIdentifier is null
                    ? null
                    : PackageCoordinateResolver.IsAcquisitionTargetText(rawRuntimeIdentifier)
                        ? rawRuntimeIdentifier.ToLowerInvariant()
                        : RestoredProjectIdentityText.Opaque(rawRuntimeIdentifier));

            if (!seen.Add(new TargetCorrelationKey(
                    RestoredProjectIdentityText.IsOpaque(
                        candidate.FrameworkIdentity)
                            ? candidate.RawFramework
                            : candidate.FrameworkIdentity,
                    candidate.NormalizedRuntimeIdentifier)))
            {
                candidates = [];
                return false;
            }

            builder.Add(candidate);
        }

        candidates = builder.ToImmutable();
        return true;
    }

    /// <summary>Recognized canonical framework text, or an opaque digest over the authored spelling.</summary>
    static string FrameworkIdentityText(string rawFramework) =>
        NuGetTargetFrameworkIdentity.TryNormalize(
            rawFramework,
            out string canonicalFramework)
                ? canonicalFramework
                : RestoredProjectIdentityText.Opaque(rawFramework);

    static TargetCandidate? SelectTarget(
        ImmutableArray<TargetCandidate> candidates,
        RestoredProjectTargetRequest? request,
        int schemaVersion)
    {
        if (request is not null)
        {
            IEnumerable<TargetCandidate> matches = request.RuntimeIdentifier is null
                ? candidates.Where(c => c.RawRuntimeIdentifier is null && MatchesRequestedFramework(c, request.Framework, schemaVersion))
                : candidates.Where(c =>
                    c.RawRuntimeIdentifier is not null
                    && MatchesRequestedFramework(c, request.Framework, schemaVersion)
                    && string.Equals(c.RawRuntimeIdentifier, request.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase));
            return matches.FirstOrDefault();
        }

        TargetCandidate[] nonRuntime = [.. candidates.Where(c => c.RawRuntimeIdentifier is null)];
        TargetCandidate[] pool = nonRuntime.Length > 0 ? nonRuntime : [.. candidates];
        return pool.Length == 0
            ? null
            : pool
                .OrderByDescending(c => !RestoredProjectIdentityText.IsOpaque(
                    c.FrameworkIdentity))
                .ThenByDescending(c =>
                    RestoredProjectIdentityText.IsOpaque(c.FrameworkIdentity)
                        ? 0
                        : TfmSelector.GetTfmPriority(c.FrameworkIdentity))
                .ThenBy(c => c.IdentityKey, StringComparer.Ordinal)
                .First();
    }

    /// <summary>
    /// Schema 4 pivots are target aliases, so a request matches them directly. Schema 3 pivots are
    /// long framework names, so a short request is correlated through canonical NuGet framework
    /// identity.
    /// </summary>
    static bool MatchesRequestedFramework(TargetCandidate candidate, string requestedFramework, int schemaVersion) =>
        string.Equals(candidate.RawFramework, requestedFramework, StringComparison.OrdinalIgnoreCase)
        || (schemaVersion == 3
            && NuGetTargetFrameworkIdentity.TryNormalize(
                requestedFramework,
                out string requestedIdentity)
            && string.Equals(
                candidate.FrameworkIdentity,
                requestedIdentity,
                StringComparison.Ordinal));

    // ---- Declaration phase --------------------------------------------------

    static RestoredProjectDeclarationResult ProjectDeclaration(JsonElement root)
    {
        if (!root.TryGetProperty("project", out JsonElement project)
            || project.ValueKind != JsonValueKind.Object
            || !project.TryGetProperty("frameworks", out JsonElement frameworks))
        {
            return new RestoredProjectDeclarationResult.Unavailable();
        }

        if (frameworks.ValueKind != JsonValueKind.Object)
        {
            return new RestoredProjectDeclarationResult.Failed(
                new RestoredProjectDeclarationFailure(RestoredProjectDeclarationFailureReason.InvalidGroupShape));
        }

        var groups = ImmutableArray.CreateBuilder<RestoredProjectDeclarationGroup>();
        var failures = new FailureTally<RestoredProjectDeclarationFailureReason>();
        int declaredPackageCount = 0;
        int declaredProjectReferenceCount = 0;
        bool limitExceeded = false;

        int authoredGroupCount = 0;
        foreach (JsonProperty pivotProperty in EnumeratePropertiesInCanonicalOrder(frameworks))
        {
            if (++authoredGroupCount > MaxDeclarationGroups)
            {
                limitExceeded = true;
                break;
            }

            string rawPivot = pivotProperty.Name;
            if (rawPivot.Length == 0
                || rawPivot.Length > MaxScalarCharacters
                || pivotProperty.Value.ValueKind != JsonValueKind.Object)
            {
                failures.Add(RestoredProjectDeclarationFailureReason.InvalidGroupShape);
                continue;
            }

            bool recognized = NuGetTargetFrameworkIdentity.TryNormalize(
                rawPivot,
                out string normalizedTfm);
            RestoredProjectFrameworkIdentity frameworkIdentity = recognized
                ? new RestoredProjectFrameworkIdentity(RestoredProjectFrameworkIdentityKind.Recognized, normalizedTfm)
                : new RestoredProjectFrameworkIdentity(
                    RestoredProjectFrameworkIdentityKind.Unrecognized,
                    RestoredProjectIdentityText.Opaque(rawPivot));

            // The group is identified by its exact authored pivot occurrence: canonical text only
            // when the pivot is already exactly its recognized canonical spelling, so case-only
            // variants stay distinct groups instead of colliding into one identity.
            string pivotIdentity = DeclarationPivotIdentity(rawPivot, recognized, normalizedTfm);

            ImmutableArray<RestoredProjectDeclaredPackage> packages = [];
            if (pivotProperty.Value.TryGetProperty("dependencies", out JsonElement dependencies))
            {
                // An absent dependencies member is a valid empty group; a present non-object one
                // is invalid evidence and never a silently empty group.
                if (dependencies.ValueKind != JsonValueKind.Object)
                {
                    failures.Add(RestoredProjectDeclarationFailureReason.InvalidGroupShape);
                }
                else
                {
                    packages = ProjectDeclaredPackages(
                        dependencies,
                        failures,
                        ref declaredPackageCount,
                        ref declaredProjectReferenceCount,
                        ref limitExceeded);
                }
            }

            groups.Add(new RestoredProjectDeclarationGroup(
                new RestoredProjectDeclarationGroupIdentity(default!, pivotIdentity),
                new InertString(TextPolicy.Field, rawPivot, MaxScalarCharacters),
                pivotIdentity,
                frameworkIdentity,
                packages));

            if (limitExceeded)
                break;
        }

        if (limitExceeded)
            failures.Add(RestoredProjectDeclarationFailureReason.ConfiguredLimitExceeded);

        ImmutableArray<RestoredProjectDeclarationGroup> orderedGroups =
            [.. groups.OrderBy(g => g.OrderKey, StringComparer.Ordinal)];
        ImmutableArray<RestoredProjectDeclarationFailure> orderedFailures =
            [.. failures.Ordered().Select(entry => new RestoredProjectDeclarationFailure(entry.Reason, entry.Count))];

        return new RestoredProjectDeclarationResult.Available(
            orderedGroups,
            orderedFailures,
            orderedFailures.IsEmpty
                ? RestoredProjectPhaseCompletion.Complete
                : RestoredProjectPhaseCompletion.Incomplete);
    }

    static string DeclarationPivotIdentity(
        string rawPivot,
        bool recognized,
        string normalizedTfm) =>
        recognized && string.Equals(normalizedTfm, rawPivot, StringComparison.Ordinal)
            ? normalizedTfm
            : RestoredProjectIdentityText.Opaque(rawPivot);

    /// <summary>How one <c>project.frameworks</c> dependency entry classifies itself.</summary>
    enum DeclaredTargetKind
    {
        Package,
        Project,
        Unclassified,
    }

    static DeclaredTargetKind ClassifyDeclaredTarget(JsonElement dependency)
    {
        if (dependency.ValueKind != JsonValueKind.Object
            || !dependency.TryGetProperty("target", out JsonElement target)
            || target.ValueKind != JsonValueKind.String)
        {
            return DeclaredTargetKind.Unclassified;
        }

        string? text = target.GetString();
        if (text is null || text.Length > MaxScalarCharacters)
            return DeclaredTargetKind.Unclassified;

        if (string.Equals(text, "Package", StringComparison.OrdinalIgnoreCase))
            return DeclaredTargetKind.Package;

        return string.Equals(text, "Project", StringComparison.OrdinalIgnoreCase)
            ? DeclaredTargetKind.Project
            : DeclaredTargetKind.Unclassified;
    }

    static ImmutableArray<RestoredProjectDeclaredPackage> ProjectDeclaredPackages(
        JsonElement dependencies,
        FailureTally<RestoredProjectDeclarationFailureReason> failures,
        ref int declaredPackageCount,
        ref int declaredProjectReferenceCount,
        ref bool limitExceeded)
    {
        var byCanonicalId = new Dictionary<string, List<(string RawId, string RawVersion, string CanonicalVersion)>>(
            StringComparer.Ordinal);

        foreach (JsonProperty dependency in EnumeratePropertiesInCanonicalOrder(dependencies))
        {
            if (limitExceeded)
                break;

            DeclaredTargetKind kind = ClassifyDeclaredTarget(dependency.Value);

            // Project references are internal root-graph inputs, never public package
            // declarations, so they carry their own bound instead of consuming the package one.
            if (kind == DeclaredTargetKind.Project)
            {
                declaredProjectReferenceCount++;
                if (declaredProjectReferenceCount > MaxDeclaredProjectReferences)
                    limitExceeded = true;
                continue;
            }

            declaredPackageCount++;
            if (declaredPackageCount > MaxDeclaredPackages)
            {
                limitExceeded = true;
                break;
            }

            if (kind == DeclaredTargetKind.Unclassified)
            {
                failures.Add(RestoredProjectDeclarationFailureReason.UnclassifiedDependencyTarget);
                continue;
            }

            string rawId = dependency.Name;
            if (rawId.Length == 0
                || rawId.Length > MaxScalarCharacters
                || !PackageCoordinateResolver.IsCanonicalPackageId(rawId)
                || !dependency.Value.TryGetProperty("version", out JsonElement versionElement)
                || versionElement.ValueKind != JsonValueKind.String)
            {
                failures.Add(RestoredProjectDeclarationFailureReason.InvalidPackageDeclaration);
                continue;
            }

            string? rawVersion = versionElement.GetString();
            if (rawVersion is not { Length: > 0 }
                || rawVersion.Length > MaxScalarCharacters
                || !VersionRange.TryParse(rawVersion, out VersionRange? range))
            {
                failures.Add(RestoredProjectDeclarationFailureReason.InvalidPackageDeclaration);
                continue;
            }

            string canonicalId = rawId.ToLowerInvariant();
            string canonicalVersion = range.ToNormalizedString();
            if (!byCanonicalId.TryGetValue(canonicalId, out List<(string, string, string)>? occurrences))
            {
                occurrences = [];
                byCanonicalId.Add(canonicalId, occurrences);
            }

            occurrences.Add((rawId, rawVersion, canonicalVersion));
        }

        var packages = ImmutableArray.CreateBuilder<RestoredProjectDeclaredPackage>();
        foreach ((string canonicalId, List<(string RawId, string RawVersion, string CanonicalVersion)> occurrences)
            in byCanonicalId.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            string[] distinctConstraints = [.. occurrences
                .Select(o => o.CanonicalVersion)
                .Distinct(StringComparer.Ordinal)];
            if (distinctConstraints.Length > 1)
            {
                failures.Add(RestoredProjectDeclarationFailureReason.ConflictingPackageDeclaration);
                continue;
            }

            (string rawId, string rawVersion, string canonicalVersion) = occurrences
                .OrderBy(o => o.RawId, StringComparer.Ordinal)
                .First();
            packages.Add(new RestoredProjectDeclaredPackage(
                canonicalId,
                new InertString(TextPolicy.Field, rawId, MaxScalarCharacters),
                canonicalVersion,
                new InertString(TextPolicy.Field, rawVersion, MaxScalarCharacters),
                occurrences.Count));
        }

        return packages.ToImmutable();
    }

    // ---- Graph phase ----------------------------------------------------

    static RestoredProjectGraphResult ProjectGraph(
        JsonElement root,
        int schemaVersion,
        TargetCandidate? selectedCandidate,
        JsonElement targetsElement,
        bool targetsPresent,
        out RestoredProjectGraphTopology topology)
    {
        topology = RestoredProjectGraphTopology.Empty;
        if (!targetsPresent || selectedCandidate is not { } candidate)
            return new RestoredProjectGraphResult.Unavailable();

        if (!targetsElement.TryGetProperty(candidate.RawKey, out JsonElement selectedTargetValue)
            || selectedTargetValue.ValueKind != JsonValueKind.Object)
        {
            return new RestoredProjectGraphResult.Failed(
                new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.UnresolvableSelectedTargetShape));
        }

        if (!root.TryGetProperty("projectFileDependencyGroups", out JsonElement rootGroups)
            || rootGroups.ValueKind != JsonValueKind.Object)
        {
            return new RestoredProjectGraphResult.Unavailable();
        }

        // The graph phase correlates its own root constraint group directly from the document.
        // It never reads the declaration phase's projection, so an unrelated declaration failure
        // cannot destroy usable selected-graph evidence.
        RootConstraintCorrelation correlation = CorrelateRootConstraints(root, schemaVersion, candidate);
        if (correlation.Outcome is not RootCorrelationOutcome.Correlated)
        {
            return correlation.Outcome switch
            {
                RootCorrelationOutcome.Unavailable => new RestoredProjectGraphResult.Unavailable(),
                RootCorrelationOutcome.Ambiguous => new RestoredProjectGraphResult.Failed(
                    new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.AmbiguousRootCorrelation)),
                _ => new RestoredProjectGraphResult.Failed(
                    new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.InvalidRootCorrelationShape)),
            };
        }

        JsonProperty[] rootEntryMatches =
            [.. rootGroups.EnumerateObject().Where(p => CorrelatesWithPivot(p.Name, schemaVersion, candidate))];
        if (rootEntryMatches.Length == 0)
            return new RestoredProjectGraphResult.Unavailable();
        if (rootEntryMatches.Length > 1)
        {
            return new RestoredProjectGraphResult.Failed(
                new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.AmbiguousRootCorrelation));
        }

        JsonElement rootEntries = rootEntryMatches[0].Value;
        if (rootEntries.ValueKind != JsonValueKind.Array)
        {
            return new RestoredProjectGraphResult.Failed(
                new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.UnresolvableSelectedTargetShape));
        }

        var traversal = new GraphTraversal(
            selectedTargetValue,
            correlation.Constraints,
            correlation.DeclarationGroupPivotIdentity!,
            correlation.LimitExceeded);
        traversal.Traverse(rootEntries);

        // Build() closes the failure tally, so the topology — which carries the same failure
        // occurrences with their owning node — is read only after it.
        RestoredProjectGraphResult result = traversal.Build();
        topology = traversal.BuildTopology();
        return result;
    }

    enum RootCorrelationOutcome
    {
        Correlated,
        Unavailable,
        Ambiguous,
        InvalidShape,
    }

    /// <summary>One authored root constraint occurrence for one canonical package id.</summary>
    readonly record struct RootConstraint(string CanonicalConstraint, InertString SourceSpelling);

    readonly record struct RootConstraintCorrelation(
        RootCorrelationOutcome Outcome,
        Dictionary<string, List<RootConstraint>> Constraints,
        string? DeclarationGroupPivotIdentity = null,
        bool LimitExceeded = false);

    static Dictionary<string, List<RootConstraint>> EmptyRootConstraints =>
        new(StringComparer.Ordinal);

    static RestoredProjectPackagePruningResult ProjectPackagePruning(
        JsonElement root,
        int schemaVersion,
        TargetCandidate? selectedCandidate)
    {
        if (selectedCandidate is not { } candidate)
            return new RestoredProjectPackagePruningResult.Unavailable();

        RootCorrelationOutcome correlation = CorrelateFrameworkGroup(
            root,
            schemaVersion,
            candidate,
            out JsonProperty matchedProperty);
        if (correlation == RootCorrelationOutcome.Unavailable)
            return new RestoredProjectPackagePruningResult.Unavailable();
        if (correlation == RootCorrelationOutcome.Ambiguous)
        {
            return new RestoredProjectPackagePruningResult.Failed(
                new RestoredProjectPackagePruningFailure(
                    RestoredProjectPackagePruningFailureReason
                        .AmbiguousDeclarationGroupAssociation));
        }
        if (correlation == RootCorrelationOutcome.InvalidShape)
        {
            return new RestoredProjectPackagePruningResult.Failed(
                new RestoredProjectPackagePruningFailure(
                    RestoredProjectPackagePruningFailureReason.InvalidShape));
        }

        JsonElement group = matchedProperty.Value;
        if (!group.TryGetProperty("packagesToPrune", out JsonElement packagesToPrune))
            return new RestoredProjectPackagePruningResult.Unavailable();
        if (packagesToPrune.ValueKind != JsonValueKind.Object)
        {
            return new RestoredProjectPackagePruningResult.Failed(
                new RestoredProjectPackagePruningFailure(
                    RestoredProjectPackagePruningFailureReason.InvalidShape));
        }

        int scanned = 0;
        int invalid = 0;
        foreach (JsonProperty rule in EnumeratePropertiesInCanonicalOrder(packagesToPrune))
        {
            if (++scanned > MaxPackagePruningRules)
            {
                return new RestoredProjectPackagePruningResult.Failed(
                    new RestoredProjectPackagePruningFailure(
                        RestoredProjectPackagePruningFailureReason.ConfiguredLimitExceeded));
            }

            string rawPackageId = rule.Name;
            if (rawPackageId.Length == 0
                || rawPackageId.Length > MaxScalarCharacters
                || !PackageCoordinateResolver.IsCanonicalPackageId(rawPackageId)
                || rule.Value.ValueKind != JsonValueKind.String)
            {
                invalid++;
                continue;
            }

            string? rawVersionRange = rule.Value.GetString();
            if (rawVersionRange is not { Length: > 0 }
                || rawVersionRange.Length > MaxScalarCharacters
                || !VersionRange.TryParse(rawVersionRange, out _))
            {
                invalid++;
            }
        }

        if (invalid > 0)
        {
            return new RestoredProjectPackagePruningResult.Failed(
                new RestoredProjectPackagePruningFailure(
                    RestoredProjectPackagePruningFailureReason.InvalidRule,
                    invalid));
        }

        string rawPivot = matchedProperty.Name;
        bool recognized = NuGetTargetFrameworkIdentity.TryNormalize(
            rawPivot,
            out string normalizedTfm);
        string pivotIdentity = DeclarationPivotIdentity(
            rawPivot,
            recognized,
            normalizedTfm);

        return new RestoredProjectPackagePruningResult.Available(
            new RestoredProjectPackagePruningEvidence(
                new RestoredProjectDeclarationGroupIdentity(default!, pivotIdentity)));
    }

    /// <summary>
    /// Finds the single <c>project.frameworks</c> group corresponding to the selected target and
    /// reads its authored package constraints. Distinct constraints for one canonical id are all
    /// retained so the edge-uniqueness rule, not this lookup, decides the conflict.
    /// </summary>
    static RootConstraintCorrelation CorrelateRootConstraints(
        JsonElement root,
        int schemaVersion,
        TargetCandidate candidate)
    {
        RootCorrelationOutcome correlation = CorrelateFrameworkGroup(
            root,
            schemaVersion,
            candidate,
            out JsonProperty matchedProperty);
        if (correlation != RootCorrelationOutcome.Correlated)
            return new RootConstraintCorrelation(correlation, EmptyRootConstraints);

        JsonElement group = matchedProperty.Value;
        string rawPivot = matchedProperty.Name;
        bool recognized = NuGetTargetFrameworkIdentity.TryNormalize(
            rawPivot,
            out string normalizedTfm);
        string declarationGroupPivotIdentity =
            DeclarationPivotIdentity(rawPivot, recognized, normalizedTfm);

        var constraints = new Dictionary<string, List<RootConstraint>>(StringComparer.Ordinal);
        if (!group.TryGetProperty("dependencies", out JsonElement dependencies))
        {
            return new RootConstraintCorrelation(
                RootCorrelationOutcome.Correlated,
                constraints,
                declarationGroupPivotIdentity);
        }

        if (dependencies.ValueKind != JsonValueKind.Object)
            return new RootConstraintCorrelation(RootCorrelationOutcome.InvalidShape, EmptyRootConstraints);

        int scanned = 0;
        bool limitExceeded = false;
        foreach (JsonProperty dependency in EnumeratePropertiesInCanonicalOrder(dependencies))
        {
            if (++scanned > MaxDeclaredPackages + MaxDeclaredProjectReferences)
            {
                limitExceeded = true;
                break;
            }

            if (ClassifyDeclaredTarget(dependency.Value) != DeclaredTargetKind.Package)
                continue;

            string rawId = dependency.Name;
            if (rawId.Length == 0
                || rawId.Length > MaxScalarCharacters
                || !PackageCoordinateResolver.IsCanonicalPackageId(rawId)
                || !dependency.Value.TryGetProperty("version", out JsonElement versionElement)
                || versionElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? rawVersion = versionElement.GetString();
            if (rawVersion is not { Length: > 0 }
                || rawVersion.Length > MaxScalarCharacters
                || !VersionRange.TryParse(rawVersion, out VersionRange? range))
            {
                continue;
            }

            string canonicalId = rawId.ToLowerInvariant();
            if (!constraints.TryGetValue(canonicalId, out List<RootConstraint>? occurrences))
            {
                occurrences = [];
                constraints.Add(canonicalId, occurrences);
            }

            var constraint = new RootConstraint(
                range.ToNormalizedString(),
                new InertString(TextPolicy.Field, rawVersion, MaxScalarCharacters));
            if (!occurrences.Contains(constraint))
                occurrences.Add(constraint);
        }

        return new RootConstraintCorrelation(
            RootCorrelationOutcome.Correlated,
            constraints,
            declarationGroupPivotIdentity,
            limitExceeded);
    }

    static RootCorrelationOutcome CorrelateFrameworkGroup(
        JsonElement root,
        int schemaVersion,
        TargetCandidate candidate,
        out JsonProperty matchedProperty)
    {
        matchedProperty = default;
        if (!root.TryGetProperty("project", out JsonElement project)
            || project.ValueKind != JsonValueKind.Object
            || !project.TryGetProperty("frameworks", out JsonElement frameworks)
            || frameworks.ValueKind != JsonValueKind.Object)
        {
            return RootCorrelationOutcome.Unavailable;
        }

        JsonProperty? matched = null;
        foreach (JsonProperty pivot in EnumeratePropertiesInCanonicalOrder(frameworks))
        {
            if (pivot.Name.Length == 0
                || pivot.Name.Length > MaxScalarCharacters
                || !CorrelatesWithPivot(pivot.Name, schemaVersion, candidate))
            {
                continue;
            }

            if (matched is not null)
                return RootCorrelationOutcome.Ambiguous;

            matched = pivot;
        }

        if (matched is not { } correlated)
            return RootCorrelationOutcome.Unavailable;
        if (correlated.Value.ValueKind != JsonValueKind.Object)
            return RootCorrelationOutcome.InvalidShape;

        matchedProperty = correlated;
        return RootCorrelationOutcome.Correlated;
    }

    /// <summary>
    /// Schema 4 pivots are target aliases matched directly; schema 3 pivots are correlated through
    /// canonical NuGet framework identity.
    /// </summary>
    static bool CorrelatesWithPivot(string rawPivot, int schemaVersion, TargetCandidate candidate) =>
        schemaVersion == 4
            ? string.Equals(rawPivot, candidate.RawFramework, StringComparison.OrdinalIgnoreCase)
            : string.Equals(
                rawPivot,
                candidate.RawFramework,
                StringComparison.OrdinalIgnoreCase)
                || (NuGetTargetFrameworkIdentity.TryNormalize(
                        rawPivot,
                        out string pivotIdentity)
                    && string.Equals(
                        pivotIdentity,
                        candidate.FrameworkIdentity,
                        StringComparison.Ordinal));

    /// <summary>
    /// Aggregates repeated failure reasons into one deterministic count per reason, so public
    /// failure evidence never depends on the order the document happened to present its members.
    /// </summary>
    sealed class FailureTally<TReason>
        where TReason : struct, Enum
    {
        readonly Dictionary<TReason, int> _counts = new();

        public void Add(TReason reason) =>
            _counts[reason] = _counts.TryGetValue(reason, out int count) ? count + 1 : 1;

        public IEnumerable<(TReason Reason, int Count)> Ordered() =>
            _counts
                .OrderBy(entry => entry.Key, Comparer<TReason>.Default)
                .Select(entry => (entry.Key, entry.Value));
    }

    /// <summary>
    /// Iterative traversal of the selected target. Depth is carried on an explicit stack, and every
    /// reachable node — package or project — is registered against <see cref="MaxGraphNodes"/>, so a
    /// project-only chain can neither exhaust the CLR stack nor evade the node bound.
    /// </summary>
    sealed class GraphTraversal
    {
        readonly JsonElement _selectedTarget;
        readonly Dictionary<string, List<RootConstraint>> _rootConstraints;
        readonly string _rootDeclarationGroupPivotIdentity;
        readonly Dictionary<string, string> _uniqueKeyByName = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _ambiguousNames = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, RestoredProjectPackageNodeIdentity> _packageNodes = new(StringComparer.Ordinal);
        readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
        readonly HashSet<PackageSourceCoordinate> _directPackages = new();
        readonly Dictionary<EdgeKey, RestoredProjectGraphEdge> _edges = new();
        readonly HashSet<EdgeKey> _conflictedEdges = new();
        readonly Dictionary<string, RestoredProjectProjectNodeEvidence> _projectNodes = new(StringComparer.Ordinal);
        readonly Dictionary<ProjectRelationshipKey, RestoredProjectProjectRelationshipEvidence>
            _projectRelationships = new();
        readonly List<RestoredProjectGraphFailureOccurrence> _failureOccurrences = new();
        readonly FailureTally<RestoredProjectGraphFailureReason> _failures = new();
        readonly Stack<PendingNode> _pending = new();
        int _edgeOccurrenceCount;
        int _projectRelationshipOccurrenceCount;
        bool _projectRelationshipLimitExceeded;
        bool _limitExceeded;

        public GraphTraversal(
            JsonElement selectedTarget,
            Dictionary<string, List<RootConstraint>> rootConstraints,
            string rootDeclarationGroupPivotIdentity,
            bool rootConstraintLimitExceeded)
        {
            _selectedTarget = selectedTarget;
            _rootConstraints = rootConstraints;
            _rootDeclarationGroupPivotIdentity = rootDeclarationGroupPivotIdentity;
            if (rootConstraintLimitExceeded)
                Fail(RestoredProjectGraphFailureReason.ConfiguredLimitExceeded, ownerNodeKey: null);
            IndexTargetNodes();
        }

        readonly record struct PendingNode(string Key, RestoredProjectGraphParentIdentity Parent);

        readonly record struct EdgeKey(string ParentKey, string PackageId, string PackageVersion);

        readonly record struct ProjectRelationshipKey(string ParentKey, string DependencyKey);

        /// <summary>
        /// Records one graph-phase failure in the published tally and, beside it, the node whose
        /// expansion produced it. The owning node is never published by the facts owner; it lets
        /// the traversal owner decide whether a failure lies inside a requested depth boundary.
        /// </summary>
        void Fail(RestoredProjectGraphFailureReason reason, string? ownerNodeKey)
        {
            _failures.Add(reason);
            _failureOccurrences.Add(new RestoredProjectGraphFailureOccurrence(reason, ownerNodeKey));
        }

        /// <summary>
        /// Indexes the selected target once, bounding it by <see cref="MaxGraphNodes"/>. Every
        /// reachable node — package or project alike — is a selected-target node, so this single
        /// bound covers the whole reachable set including a project-only chain.
        /// </summary>
        void IndexTargetNodes()
        {
            int indexed = 0;
            foreach (JsonProperty node in EnumeratePropertiesInCanonicalOrder(_selectedTarget))
            {
                if (++indexed > MaxGraphNodes)
                {
                    _limitExceeded = true;
                    return;
                }

                string key = node.Name;
                if (key.Length == 0 || key.Length > MaxScalarCharacters)
                    continue;

                int separator = key.LastIndexOf('/');
                if (separator <= 0 || separator == key.Length - 1)
                    continue;

                string name = key[..separator];
                if (_ambiguousNames.Contains(name))
                    continue;

                if (!_uniqueKeyByName.TryAdd(name, key))
                {
                    _uniqueKeyByName.Remove(name);
                    _ambiguousNames.Add(name);
                }
            }
        }

        public void Traverse(JsonElement rootEntries)
        {
            int entries = 0;
            foreach (JsonElement rootEntry in rootEntries.EnumerateArray())
            {
                if (_limitExceeded || ++entries > MaxGraphNodes)
                {
                    _limitExceeded = true;
                    break;
                }

                if (rootEntry.ValueKind != JsonValueKind.String)
                {
                    Fail(RestoredProjectGraphFailureReason.UnresolvedRootEntry, RestoredProjectNodeKey.Root);
                    continue;
                }

                string? entryText = rootEntry.GetString();
                if (entryText is null
                    || entryText.Length == 0
                    || entryText.Length > MaxScalarCharacters
                    || !TryReadRootEntryName(entryText, out string rootName))
                {
                    Fail(RestoredProjectGraphFailureReason.UnresolvedRootEntry, RestoredProjectNodeKey.Root);
                    continue;
                }

                TraverseRootEntry(rootName);
            }

            Expand();
        }

        /// <summary>
        /// Reads a <c>projectFileDependencyGroups</c> entry name. The rightmost <c> &gt;= </c> is the
        /// range marker, because a name cannot contain one but a range spelling could; an entry with
        /// no marker is the documented no-range form and is entirely a name.
        /// </summary>
        internal static bool TryReadRootEntryName(string entryText, out string name)
        {
            name = "";
            int marker = entryText.LastIndexOf(" >= ", StringComparison.Ordinal);
            string candidate = (marker < 0 ? entryText : entryText[..marker]).Trim();
            if (candidate.Length == 0)
                return false;

            name = candidate;
            return true;
        }

        void TraverseRootEntry(string rootName)
        {
            if (!TryResolveNode(rootName, out string? matchedKey, out string? nodeType, out _))
            {
                if (!_limitExceeded)
                    Fail(RestoredProjectGraphFailureReason.UnresolvedRootEntry, RestoredProjectNodeKey.Root);
                return;
            }

            if (string.Equals(nodeType, "project", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryBuildProjectParent(matchedKey!, out RestoredProjectGraphParentIdentity projectParent))
                {
                    Fail(RestoredProjectGraphFailureReason.UnresolvedRootEntry, RestoredProjectNodeKey.Root);
                    return;
                }

                AddProjectRelationship(
                    RestoredProjectGraphParentIdentity.CreateRoot(),
                    RestoredProjectNodeKey.Root,
                    projectParent);
                Push(matchedKey!, projectParent);
                return;
            }

            if (!string.Equals(nodeType, "package", StringComparison.OrdinalIgnoreCase)
                || !TryBuildPackageIdentity(matchedKey!, out RestoredProjectPackageNodeIdentity packageIdentity))
            {
                Fail(RestoredProjectGraphFailureReason.UnresolvedRootEntry, RestoredProjectNodeKey.Root);
                return;
            }

            if (!_rootConstraints.TryGetValue(rootName.ToLowerInvariant(), out List<RootConstraint>? constraints)
                || constraints.Count == 0)
            {
                Fail(RestoredProjectGraphFailureReason.UnresolvedRootEntry, RestoredProjectNodeKey.Root);
                _directPackages.Add(packageIdentity.Coordinate);
                Push(matchedKey!, RestoredProjectGraphParentIdentity.CreatePackageParent(packageIdentity));
                return;
            }

            // Every distinct authored constraint is offered to the edge set. One survives; two
            // conflicting ones cancel each other and leave the graph visibly incomplete.
            foreach (RootConstraint constraint in constraints)
            {
                AddEdge(
                    RestoredProjectGraphParentIdentity.CreateRoot(),
                    "",
                    packageIdentity,
                    constraint.CanonicalConstraint,
                    constraint.SourceSpelling,
                    _rootDeclarationGroupPivotIdentity);
            }

            _directPackages.Add(packageIdentity.Coordinate);
            Push(matchedKey!, RestoredProjectGraphParentIdentity.CreatePackageParent(packageIdentity));
        }

        void Push(string key, RestoredProjectGraphParentIdentity parent)
        {
            if (_expanded.Contains(key))
                return;

            _pending.Push(new PendingNode(key, parent));
        }

        void Expand()
        {
            while (_pending.Count > 0 && !_limitExceeded)
            {
                PendingNode pending = _pending.Pop();
                if (!_expanded.Add(pending.Key))
                    continue;

                if (!_selectedTarget.TryGetProperty(pending.Key, out JsonElement nodeValue)
                    || nodeValue.ValueKind != JsonValueKind.Object)
                {
                    Fail(RestoredProjectGraphFailureReason.InvalidNodeShape, ParentKeyText(pending.Parent));
                    continue;
                }

                // An absent dependencies member is a valid leaf. A present non-object one is
                // incomplete typed evidence, never a silently complete leaf.
                if (!nodeValue.TryGetProperty("dependencies", out JsonElement dependencies))
                    continue;

                if (dependencies.ValueKind != JsonValueKind.Object)
                {
                    Fail(RestoredProjectGraphFailureReason.InvalidNodeShape, ParentKeyText(pending.Parent));
                    continue;
                }

                string parentKey = ParentKeyText(pending.Parent);
                foreach (JsonProperty dependency in EnumeratePropertiesInCanonicalOrder(dependencies))
                {
                    if (_limitExceeded)
                        return;

                    ExpandDependency(pending.Parent, parentKey, dependency);
                }
            }
        }

        void ExpandDependency(
            RestoredProjectGraphParentIdentity parent,
            string parentKey,
            JsonProperty dependency)
        {
            if (dependency.Name.Length == 0 || dependency.Name.Length > MaxScalarCharacters)
            {
                Fail(RestoredProjectGraphFailureReason.UnresolvedDependency, parentKey);
                return;
            }

            if (dependency.Value.ValueKind != JsonValueKind.String)
            {
                Fail(RestoredProjectGraphFailureReason.UnresolvedDependency, parentKey);
                return;
            }

            string? rawConstraint = dependency.Value.GetString();
            if (rawConstraint is not { Length: > 0 }
                || rawConstraint.Length > MaxScalarCharacters
                || !VersionRange.TryParse(rawConstraint, out VersionRange? range))
            {
                Fail(RestoredProjectGraphFailureReason.UnresolvedDependency, parentKey);
                return;
            }

            if (!TryResolveNode(dependency.Name, out string? matchedKey, out string? nodeType, out _))
            {
                if (!_limitExceeded)
                    Fail(RestoredProjectGraphFailureReason.UnresolvedDependency, parentKey);
                return;
            }

            if (string.Equals(nodeType, "project", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryBuildProjectParent(matchedKey!, out RestoredProjectGraphParentIdentity projectParent))
                {
                    Fail(RestoredProjectGraphFailureReason.UnresolvedDependency, parentKey);
                    return;
                }

                AddProjectRelationship(parent, parentKey, projectParent);
                Push(matchedKey!, projectParent);
                return;
            }

            if (!string.Equals(nodeType, "package", StringComparison.OrdinalIgnoreCase)
                || !TryBuildPackageIdentity(matchedKey!, out RestoredProjectPackageNodeIdentity packageIdentity))
            {
                Fail(RestoredProjectGraphFailureReason.UnresolvedDependency, parentKey);
                return;
            }

            AddEdge(
                parent,
                parentKey,
                packageIdentity,
                range.ToNormalizedString(),
                new InertString(TextPolicy.Field, rawConstraint, MaxScalarCharacters),
                declarationGroupPivotIdentity: null);
            Push(matchedKey!, RestoredProjectGraphParentIdentity.CreatePackageParent(packageIdentity));
        }

        /// <summary>
        /// Enforces one edge per parent/dependency identity. An equal-semantics repeat coalesces;
        /// a conflicting one withdraws the edge and records typed incompleteness, so no arbitrary
        /// duplicate survives.
        /// </summary>
        void AddEdge(
            RestoredProjectGraphParentIdentity parent,
            string parentKey,
            RestoredProjectPackageNodeIdentity dependency,
            string canonicalConstraint,
            InertString sourceConstraint,
            string? declarationGroupPivotIdentity)
        {
            if (++_edgeOccurrenceCount > MaxGraphEdges)
            {
                _limitExceeded = true;
                return;
            }

            var key = new EdgeKey(parentKey, dependency.Coordinate.PackageId, dependency.Coordinate.Version);
            if (_conflictedEdges.Contains(key))
                return;

            if (_edges.TryGetValue(key, out RestoredProjectGraphEdge? existing))
            {
                if (string.Equals(existing.CanonicalVersionConstraint, canonicalConstraint, StringComparison.Ordinal))
                {
                    if (string.CompareOrdinal(
                            sourceConstraint.ToString(),
                            existing.SourceVersionConstraintSpelling.ToString()) < 0)
                    {
                        _edges[key] = existing with { SourceVersionConstraintSpelling = sourceConstraint };
                    }

                    return;
                }

                _edges.Remove(key);
                _conflictedEdges.Add(key);
                Fail(RestoredProjectGraphFailureReason.ConflictingEdgeConstraint, parentKey);
                return;
            }

            RestoredProjectDependencyRole role = parent is RestoredProjectGraphParentIdentity.Root
                ? RestoredProjectDependencyRole.Direct
                : RestoredProjectDependencyRole.Transitive;
            _edges.Add(key, new RestoredProjectGraphEdge(
                new RestoredProjectEdgeIdentity(parent, dependency),
                parent,
                dependency,
                canonicalConstraint,
                sourceConstraint,
                role,
                declarationGroupPivotIdentity is null
                    ? null
                    : new RestoredProjectDeclarationGroupIdentity(
                        default!,
                        declarationGroupPivotIdentity)));
        }

        bool TryBuildPackageIdentity(string matchedKey, out RestoredProjectPackageNodeIdentity identity)
        {
            identity = default;
            if (_packageNodes.TryGetValue(matchedKey, out RestoredProjectPackageNodeIdentity existing))
            {
                identity = existing;
                return true;
            }

            int separator = matchedKey.LastIndexOf('/');
            if (separator <= 0 || separator == matchedKey.Length - 1)
                return false;

            string id = matchedKey[..separator];
            string version = matchedKey[(separator + 1)..];
            if (!PackageCoordinateResolver.IsCanonicalPackageId(id))
                return false;

            try
            {
                PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(id, version);
                identity = new RestoredProjectPackageNodeIdentity(default!, coordinate);
                _packageNodes[matchedKey] = identity;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves one project node's identity and retains its exact authored target-entry
        /// spelling as contained text. The opaque digest remains the only public identity; the
        /// spelling is never repaired, parsed, or promoted into identity.
        /// </summary>
        bool TryBuildProjectParent(
            string matchedKey,
            out RestoredProjectGraphParentIdentity parent)
        {
            parent = default!;
            int separator = matchedKey.LastIndexOf('/');
            if (separator <= 0
                || separator == matchedKey.Length - 1
                || !NuGetVersion.TryParse(matchedKey[(separator + 1)..], out _))
            {
                return false;
            }

            parent = RestoredProjectGraphParentIdentity.CreateProjectParent(
                RestoredProjectIdentityText.Opaque(matchedKey));
            var identity = ((RestoredProjectGraphParentIdentity.Project)parent).Identity;
            _projectNodes.TryAdd(
                matchedKey,
                new RestoredProjectProjectNodeEvidence(
                    identity,
                    new InertString(TextPolicy.Field, matchedKey, MaxScalarCharacters)));
            return true;
        }

        /// <summary>
        /// Records one project-resolving relationship. Occurrences coalesce by parent and
        /// dependency exactly as package edges do, and the relationship count is bounded
        /// independently so a project-dense document cannot change published facts completion.
        /// </summary>
        void AddProjectRelationship(
            RestoredProjectGraphParentIdentity parent,
            string parentKey,
            RestoredProjectGraphParentIdentity dependency)
        {
            if (++_projectRelationshipOccurrenceCount
                > RestoredProjectDependencyTraversalQuery.MaxProjectRelationships)
            {
                _projectRelationshipLimitExceeded = true;
                return;
            }

            var identity = ((RestoredProjectGraphParentIdentity.Project)dependency).Identity;
            _projectRelationships.TryAdd(
                new ProjectRelationshipKey(parentKey, RestoredProjectNodeKey.ForProject(identity)),
                new RestoredProjectProjectRelationshipEvidence(parent, identity));
        }

        bool TryResolveNode(string name, out string? matchedKey, out string? nodeType, out JsonElement nodeValue)
        {
            matchedKey = null;
            nodeType = null;
            nodeValue = default;
            if (name.Length == 0
                || name.Length > MaxScalarCharacters
                || !_uniqueKeyByName.TryGetValue(name, out string? key))
            {
                return false;
            }

            if (!_selectedTarget.TryGetProperty(key, out nodeValue) || nodeValue.ValueKind != JsonValueKind.Object)
                return false;

            if (!nodeValue.TryGetProperty("type", out JsonElement typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? type = typeElement.GetString();
            if (type is null || type.Length == 0 || type.Length > MaxScalarCharacters)
                return false;

            matchedKey = key;
            nodeType = type;
            return true;
        }

        static string ParentKeyText(RestoredProjectGraphParentIdentity parent) =>
            RestoredProjectNodeKey.For(parent);

        public RestoredProjectGraphResult Build()
        {
            if (_limitExceeded)
                Fail(RestoredProjectGraphFailureReason.ConfiguredLimitExceeded, ownerNodeKey: null);

            ImmutableArray<RestoredProjectPackageNode> packages =
                [.. _packageNodes.Values
                    .Distinct()
                    .OrderBy(p => p.Coordinate.PackageId, StringComparer.Ordinal)
                    .ThenBy(p => p.Coordinate.Version, StringComparer.Ordinal)
                    .Select(identity => new RestoredProjectPackageNode(
                        identity,
                        _directPackages.Contains(identity.Coordinate)
                            ? RestoredProjectDependencyRole.Direct
                            : RestoredProjectDependencyRole.Transitive))];

            ImmutableArray<RestoredProjectGraphEdge> edges =
                [.. _edges
                    .OrderBy(entry => entry.Key.ParentKey, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.PackageId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.PackageVersion, StringComparer.Ordinal)
                    .Select(entry => entry.Value)];

            ImmutableArray<RestoredProjectGraphFailure> failures =
                [.. _failures.Ordered().Select(entry => new RestoredProjectGraphFailure(entry.Reason, entry.Count))];

            return new RestoredProjectGraphResult.Available(
                packages,
                edges,
                failures,
                failures.IsEmpty
                    ? RestoredProjectPhaseCompletion.Complete
                    : RestoredProjectPhaseCompletion.Incomplete);
        }

        /// <summary>
        /// The internal project-relationship topology of the same walk. Call after
        /// <see cref="Build"/> so the failure occurrences match the published tally exactly.
        /// </summary>
        public RestoredProjectGraphTopology BuildTopology() =>
            new(
                [.. _projectNodes.Values.OrderBy(node => node.Identity.SourceIdentity, StringComparer.Ordinal)],
                [.. _projectRelationships
                    .OrderBy(entry => entry.Key.ParentKey, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.DependencyKey, StringComparer.Ordinal)
                    .Select(entry => entry.Value)],
                [.. _failureOccurrences],
                _projectRelationshipLimitExceeded);
    }

    // ---- Rescoping identities with the final selection identity ---------

    static RestoredProjectDeclarationResult RescopeDeclaration(
        RestoredProjectDeclarationResult declaration,
        RestoredProjectSelectionIdentity selection) =>
        declaration switch
        {
            RestoredProjectDeclarationResult.Available available => new RestoredProjectDeclarationResult.Available(
                [.. available.Groups.Select(g => g with
                {
                    Identity = new RestoredProjectDeclarationGroupIdentity(selection, g.Identity.PivotIdentity),
                })],
                available.Failures,
                available.Completion),
            _ => declaration,
        };

    static RestoredProjectPackagePruningResult RescopePackagePruning(
        RestoredProjectPackagePruningResult packagePruning,
        RestoredProjectSelectionIdentity selection) =>
        packagePruning switch
        {
            RestoredProjectPackagePruningResult.Available available =>
                new RestoredProjectPackagePruningResult.Available(
                    new RestoredProjectPackagePruningEvidence(
                        available.Evidence.DeclarationGroup with
                        {
                            Selection = selection,
                        })),
            _ => packagePruning,
        };

    static RestoredProjectGraphResult RescopeGraph(
        RestoredProjectGraphResult graph,
        RestoredProjectRootIdentity root)
    {
        if (graph is not RestoredProjectGraphResult.Available available)
            return graph;

        RestoredProjectPackageNodeIdentity Rescope(RestoredProjectPackageNodeIdentity identity) =>
            new(root.Selection, identity.Coordinate);

        ImmutableArray<RestoredProjectPackageNode> packages =
            [.. available.Packages.Select(p => p with { Identity = Rescope(p.Identity) })];
        ImmutableArray<RestoredProjectGraphEdge> edges =
            [.. available.Edges.Select(e =>
            {
                RestoredProjectGraphParentIdentity parent = RescopeNode(e.Parent, root);
                RestoredProjectPackageNodeIdentity dependency = Rescope(e.Dependency);
                return new RestoredProjectGraphEdge(
                    new RestoredProjectEdgeIdentity(parent, dependency),
                    parent,
                    dependency,
                    e.CanonicalVersionConstraint,
                    e.SourceVersionConstraintSpelling,
                    e.Role,
                    e.DeclarationAssociation is { } association
                        ? association with { Selection = root.Selection }
                        : null);
            })];

        return new RestoredProjectGraphResult.Available(packages, edges, available.Failures, available.Completion);
    }

    /// <summary>Rescopes one closed graph node identity into the final selection identity.</summary>
    static RestoredProjectGraphParentIdentity RescopeNode(
        RestoredProjectGraphParentIdentity node,
        RestoredProjectRootIdentity root) => node switch
        {
            RestoredProjectGraphParentIdentity.Root => new RestoredProjectGraphParentIdentity.Root(root),
            RestoredProjectGraphParentIdentity.Package p => new RestoredProjectGraphParentIdentity.Package(
                new RestoredProjectPackageNodeIdentity(root.Selection, p.Identity.Coordinate)),
            RestoredProjectGraphParentIdentity.Project p => new RestoredProjectGraphParentIdentity.Project(
                p.Identity with { Selection = root.Selection }),
            _ => node,
        };

    static RestoredProjectGraphTopology RescopeTopology(
        RestoredProjectGraphTopology topology,
        RestoredProjectRootIdentity root) =>
        new(
            [.. topology.ProjectNodes.Select(node => node with
            {
                Identity = node.Identity with { Selection = root.Selection },
            })],
            [.. topology.ProjectRelationships.Select(relationship => new RestoredProjectProjectRelationshipEvidence(
                RescopeNode(relationship.Parent, root),
                relationship.Dependency with { Selection = root.Selection }))],
            topology.FailureOccurrences,
            topology.RelationshipLimitExceeded);

    // ---- Facts digest -----------------------------------------------------

    /// <summary>
    /// A typed, length-prefixed canonical encoding of the facts. Every text field is written as
    /// its UTF-16 length, a separator, and the field itself, and every collection is written as
    /// its element count followed by its already-canonically-ordered elements. No artifact string
    /// can therefore imitate a field boundary, a collection boundary, or another field's value,
    /// so two different fact sets cannot share a digest by spelling a delimiter.
    /// </summary>
    static string ComputeFactsDigest(
        string targetIdentity,
        RestoredProjectDeclarationResult declaration,
        RestoredProjectPackagePruningResult packagePruning,
        RestoredProjectGraphResult graph)
    {
        var text = new StringBuilder();
        Field(text, "rpdf/2");
        Field(text, targetIdentity);
        AppendDeclaration(text, declaration);
        AppendPackagePruning(text, packagePruning);
        AppendGraph(text, graph);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    static void Field(StringBuilder text, string value) =>
        text.Append(value.Length).Append(':').Append(value).Append(';');

    static void Count(StringBuilder text, int value) => text.Append('#').Append(value).Append(';');

    static void AppendDeclaration(StringBuilder text, RestoredProjectDeclarationResult declaration)
    {
        switch (declaration)
        {
            case RestoredProjectDeclarationResult.Available available:
                Field(text, "declaration.available");
                Count(text, (int)available.Completion);
                Count(text, available.Groups.Length);
                foreach (RestoredProjectDeclarationGroup group in available.Groups)
                {
                    Field(text, group.Identity.PivotIdentity);
                    Count(text, (int)group.FrameworkIdentity.Kind);
                    Field(text, group.FrameworkIdentity.Identity);
                    Count(text, group.Packages.Length);
                    foreach (RestoredProjectDeclaredPackage package in group.Packages)
                    {
                        Field(text, package.CanonicalPackageId);
                        Field(text, package.CanonicalVersionConstraint);
                        Count(text, package.SourceOccurrenceCount);
                    }
                }

                Count(text, available.Failures.Length);
                foreach (RestoredProjectDeclarationFailure failure in available.Failures)
                {
                    Count(text, (int)failure.Reason);
                    Count(text, failure.Count);
                }

                break;
            case RestoredProjectDeclarationResult.Unavailable:
                Field(text, "declaration.unavailable");
                break;
            case RestoredProjectDeclarationResult.Failed failed:
                Field(text, "declaration.failed");
                Count(text, (int)failed.Failure.Reason);
                Count(text, failed.Failure.Count);
                break;
        }
    }

    static void AppendPackagePruning(
        StringBuilder text,
        RestoredProjectPackagePruningResult packagePruning)
    {
        switch (packagePruning)
        {
            case RestoredProjectPackagePruningResult.Available available:
                Field(text, "package-pruning.available");
                Field(text, available.Evidence.DeclarationGroup.PivotIdentity);
                break;
            case RestoredProjectPackagePruningResult.Unavailable:
                Field(text, "package-pruning.unavailable");
                break;
            case RestoredProjectPackagePruningResult.Failed failed:
                Field(text, "package-pruning.failed");
                Count(text, (int)failed.Failure.Reason);
                Count(text, failed.Failure.Count);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown package-pruning result: {packagePruning.GetType().FullName}");
        }
    }

    static void AppendGraph(StringBuilder text, RestoredProjectGraphResult graph)
    {
        switch (graph)
        {
            case RestoredProjectGraphResult.Available available:
                Field(text, "graph.available");
                Count(text, (int)available.Completion);
                Count(text, available.Packages.Length);
                foreach (RestoredProjectPackageNode package in available.Packages)
                {
                    Field(text, package.Identity.Coordinate.PackageId);
                    Field(text, package.Identity.Coordinate.Version);
                    Count(text, (int)package.Role);
                }

                Count(text, available.Edges.Length);
                foreach (RestoredProjectGraphEdge edge in available.Edges)
                {
                    Field(text, DescribeParent(edge.Parent));
                    Field(text, edge.Dependency.Coordinate.PackageId);
                    Field(text, edge.Dependency.Coordinate.Version);
                    Field(text, edge.CanonicalVersionConstraint);
                    Count(text, (int)edge.Role);
                    Field(text, edge.DeclarationAssociation?.PivotIdentity ?? "");
                }

                Count(text, available.Failures.Length);
                foreach (RestoredProjectGraphFailure failure in available.Failures)
                {
                    Count(text, (int)failure.Reason);
                    Count(text, failure.Count);
                }

                break;
            case RestoredProjectGraphResult.Unavailable:
                Field(text, "graph.unavailable");
                break;
            case RestoredProjectGraphResult.Failed failed:
                Field(text, "graph.failed");
                Count(text, (int)failed.Failure.Reason);
                Count(text, failed.Failure.Count);
                break;
        }
    }

    static string DescribeParent(RestoredProjectGraphParentIdentity parent) => parent switch
    {
        RestoredProjectGraphParentIdentity.Root => "root",
        RestoredProjectGraphParentIdentity.Package p => $"pkg:{p.Identity.Coordinate.PackageId}/{p.Identity.Coordinate.Version}",
        RestoredProjectGraphParentIdentity.Project p => $"proj:{p.Identity.SourceIdentity}",
        _ => "?",
    };
}
