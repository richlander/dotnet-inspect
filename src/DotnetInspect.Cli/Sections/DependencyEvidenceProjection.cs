using System.Collections.Immutable;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Sections;

/// <summary>The closed declaration state of one admitted root, as a projected row value.</summary>
public enum DependencyEvidenceDeclarationState
{
    NotApplicable,
    Available,
    Unavailable,
    Failed,
}

/// <summary>The closed restored-graph state of one admitted root, as a projected row value.</summary>
public enum DependencyEvidenceGraphState
{
    NotApplicable,
    Available,
    Unavailable,
    Failed,
}

/// <summary>Which phase issued one projected failure record.</summary>
public enum DependencyEvidenceFailurePhase
{
    Root,
    PackageProfile,
    Declaration,
    Graph,
    Traversal,
    Library,
}

/// <summary>The closed parent family of one restored graph edge.</summary>
public enum DependencyEvidenceEdgeParentKind
{
    Root,
    Package,
    Project,
}

/// <summary>One successful normalized root/group/package declaration.</summary>
/// <remarks>
/// Owner-issued identity values travel by value: the root, group, and declaration identities are
/// the ones <see cref="PackageDependencyEvidenceQuery"/> issued, not a re-derived key.
/// <paramref name="GroupIndex"/> is the document-stable occurrence index of the owning group, so
/// two explicit groups that name the same framework stay distinguishable in every sink.
/// </remarks>
public sealed record DependencyEvidenceDependencyRow(
    int RootIndex,
    PackageDependencyEvidenceRootIdentity RootIdentity,
    InertString RootDisplay,
    PackageDependencyEvidenceInputKind Owner,
    PackageDependencyEvidenceAcquisitionForm SourceKind,
    int GroupIndex,
    PackageDependencyEvidenceGroupIdentity GroupIdentity,
    string GroupOrderKey,
    PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
    PackageDependencyFrameworkScopeKind FrameworkScopeKind,
    string? CanonicalFramework,
    InertString FrameworkSpelling,
    string PackageId,
    string VersionConstraint,
    InertString SourcePackageIdSpelling,
    InertString SourceVersionConstraintSpelling,
    int SourceOccurrences,
    bool IsSelectedGroup);

/// <summary>One admitted root occurrence with its identity, provenance, and phase state.</summary>
public sealed record DependencyEvidenceRootRow(
    int RootIndex,
    PackageDependencyEvidenceRootIdentity Identity,
    InertString Display,
    PackageDependencyEvidenceInputKind Owner,
    PackageDependencyEvidenceAcquisitionForm SourceKind,
    InertString? SourceLabel,
    string? PackageId,
    string? PackageVersion,
    PackageManifestIdentityProvenance? IdentityProvenance,
    PackageSourceResultIdentity? Source,
    string? ContentDigest,
    RestoredProjectSelectionIdentity? RestoredSelection,
    DependencyEvidenceDeclarationState DeclarationState,
    PackageDependencyEvidencePhaseCompletion? DeclarationCompletion,
    int DeclarationGroupCount,
    int DeclarationCount,
    PackageDependencyEvidenceSelectionStatus SelectionStatus,
    PackageDependencyEvidenceGroupIdentity? SelectedGroup,
    int? SelectedGroupIndex,
    PackageDependencyEvidenceGroupOccurrence? SelectedSourceOccurrence,
    InertString? RequestedFramework,
    InertString? SelectedFramework,
    DependencyEvidenceGraphState GraphState,
    PackageDependencyEvidencePhaseCompletion? GraphCompletion,
    int RestoredPackageCount,
    int RestoredEdgeCount,
    string? RestoredTargetFrameworkIdentity,
    InertString? RestoredTargetFrameworkSpelling,
    string? RestoredRuntimeIdentifier,
    InertString? RestoredRuntimeIdentifierSpelling,
    RestoredProjectTargetSelectionProvenance? RestoredTargetProvenance);

/// <summary>One normalized logical declaration group, including a valid empty group.</summary>
public sealed record DependencyEvidenceGroupRow(
    int RootIndex,
    PackageDependencyEvidenceRootIdentity RootIdentity,
    InertString RootDisplay,
    PackageDependencyEvidenceInputKind Owner,
    int GroupIndex,
    PackageDependencyEvidenceGroupIdentity Identity,
    string OrderKey,
    ImmutableArray<PackageDependencyEvidenceGroupOccurrence> SourceOccurrences,
    PackageDependencyFrameworkScopeKind FrameworkScopeKind,
    string? CanonicalFramework,
    InertString FrameworkSpelling,
    bool IsImplicitManifestGroup,
    int DeclarationCount,
    bool IsSelected)
{
    /// <summary>The owner-issued occurrence count, read from the retained occurrences.</summary>
    public int SourceOccurrenceCount => SourceOccurrences.Sum(occurrence =>
        occurrence is
            PackageDependencyEvidenceGroupOccurrence.AuthoredProjectDeclaration
            authored
                ? authored.SourceOccurrenceCount
                : 1);
}

/// <summary>One owner-issued restored package graph edge.</summary>
public sealed record DependencyEvidenceRestoredEdgeRow(
    int RootIndex,
    PackageDependencyEvidenceRootIdentity RootIdentity,
    InertString RootDisplay,
    RestoredProjectEdgeIdentity Identity,
    DependencyEvidenceEdgeParentKind ParentKind,
    RestoredProjectGraphParentIdentity Parent,
    string? ParentPackageId,
    string? ParentPackageVersion,
    string? ParentProjectIdentity,
    RestoredProjectPackageNodeIdentity Dependency,
    string PackageId,
    string PackageVersion,
    string VersionConstraint,
    InertString SourceVersionConstraintSpelling,
    RestoredProjectDependencyRole Role,
    PackageDependencyEvidenceDeclarationIdentity? DeclarationAssociation);

/// <summary>One owner-issued resolved package node and its aggregate role.</summary>
public sealed record DependencyEvidenceRestoredPackageRow(
    int RootIndex,
    PackageDependencyEvidenceRootIdentity RootIdentity,
    InertString RootDisplay,
    RestoredProjectPackageNodeIdentity Identity,
    string PackageId,
    string PackageVersion,
    RestoredProjectDependencyRole Role);

/// <summary>One typed root, profile, declaration, or graph failure with its occurrence count.</summary>
/// <remarks>
/// A declaration failure the owner scoped to one group keeps that group identity and its
/// document-stable index, so a failure is attributable to the same group its sibling rows name.
/// </remarks>
public sealed record DependencyEvidenceFailureRow(
    DependencyEvidenceFailurePhase Phase,
    string Reason,
    PackageDependencyEvidenceAcquisitionForm? SourceKind,
    int? RootIndex,
    PackageDependencyEvidenceRootIdentity? RootIdentity,
    PackageDependencyEvidenceGroupIdentity? Group,
    int? GroupIndex,
    PackageSourceResultIdentity? Source,
    InertString? Subject,
    string? PackageId,
    string? PackageVersion,
    InertString? SourceLabel,
    InertString Message,
    int Occurrences,
    string? EvidenceIdentity = null);

/// <summary>
/// Root-set and aggregate phase completion, retained as document fields at every verbosity.
/// </summary>
public sealed record DependencyEvidenceSummary(
    PackageDependencyEvidenceRootSetCompletion RootSetCompletion,
    int AdmittedRootCount,
    int RejectedRootCount,
    int FailedRootCount,
    bool IsTruncated,
    PackageDependencyEvidencePhaseSummary Phases,
    PackageDependencyEvidencePackagePrefixCompletion? PackagePrefix);

/// <summary>
/// The immutable typed CLI projection over one <see cref="PackageDependencyEvidenceOutcome"/>.
/// </summary>
/// <remarks>
/// Every renderer — Markout, typed JSON, lowered JSON, and count — consumes this one projection.
/// No sink reopens an archive, nuspec, or assets file, and every artifact-authored value stays an
/// <see cref="InertString"/> until a serializer or Markout display property unwraps it.
/// </remarks>
public sealed record DependencyEvidenceProjection(
    DependencyEvidenceSummary Summary,
    ImmutableArray<DependencyEvidenceDependencyRow> Dependencies,
    ImmutableArray<DependencyEvidenceRootRow> Roots,
    ImmutableArray<DependencyEvidenceRestoredEdgeRow> RestoredEdges,
    ImmutableArray<DependencyEvidenceFailureRow> Failures,
    ImmutableArray<DependencyEvidenceGroupRow> DependencyGroups,
    ImmutableArray<DependencyEvidenceRestoredPackageRow> RestoredPackages)
{
    /// <summary>
    /// Projects the host-neutral outcome without re-deriving normalization, framework, graph,
    /// comparison, failure, or completion semantics.
    /// </summary>
    public static DependencyEvidenceProjection Create(
        PackageDependencyEvidenceOutcome outcome)
        => Create(outcome, admittedRootIndexes: null, failedRootIndexes: null);

    internal static DependencyEvidenceProjection Create(
        PackageDependencyEvidenceOutcome outcome,
        IReadOnlyList<int>? admittedRootIndexes,
        IReadOnlyList<int?>? failedRootIndexes)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (admittedRootIndexes is not null
            && admittedRootIndexes.Count != outcome.Roots.Length)
        {
            throw new ArgumentException(
                "Each admitted evidence root requires one document root index.",
                nameof(admittedRootIndexes));
        }
        if (failedRootIndexes is not null
            && failedRootIndexes.Count != outcome.FailedRoots.Length)
        {
            throw new ArgumentException(
                "Each failed evidence root requires one document root index.",
                nameof(failedRootIndexes));
        }

        var dependencies =
            ImmutableArray.CreateBuilder<DependencyEvidenceDependencyRow>();
        var roots = ImmutableArray.CreateBuilder<DependencyEvidenceRootRow>();
        var edges =
            ImmutableArray.CreateBuilder<DependencyEvidenceRestoredEdgeRow>();
        var failures =
            ImmutableArray.CreateBuilder<DependencyEvidenceFailureRow>();
        var groups = ImmutableArray.CreateBuilder<DependencyEvidenceGroupRow>();
        var packages =
            ImmutableArray.CreateBuilder<DependencyEvidenceRestoredPackageRow>();

        for (int index = 0; index < outcome.FailedRoots.Length; index++)
        {
            failures.Add(
                ProjectRootFailure(
                    outcome.FailedRoots[index],
                    failedRootIndexes?[index]));
        }

        int nextGroupIndex = 0;
        for (int index = 0; index < outcome.Roots.Length; index++)
        {
            PackageDependencyEvidenceRoot root = outcome.Roots[index];
            ProjectRoot(
                admittedRootIndexes?[index] ?? index,
                root,
                ref nextGroupIndex,
                dependencies,
                roots,
                edges,
                failures,
                groups,
                packages);
        }

        return new DependencyEvidenceProjection(
            new DependencyEvidenceSummary(
                outcome.RootSet.Completion,
                outcome.RootSet.AdmittedRootCount,
                outcome.RootSet.RejectedRootCount,
                outcome.RootSet.FailedRootCount,
                outcome.RootSet.IsTruncated,
                outcome.Phases,
                outcome.RootSet.PackagePrefixCompletion),
            dependencies.ToImmutable(),
            roots.ToImmutable(),
            edges.ToImmutable(),
            failures.ToImmutable(),
            groups.ToImmutable(),
            packages.ToImmutable());
    }

    private static void ProjectRoot(
        int index,
        PackageDependencyEvidenceRoot root,
        ref int nextGroupIndex,
        ImmutableArray<DependencyEvidenceDependencyRow>.Builder dependencies,
        ImmutableArray<DependencyEvidenceRootRow>.Builder roots,
        ImmutableArray<DependencyEvidenceRestoredEdgeRow>.Builder edges,
        ImmutableArray<DependencyEvidenceFailureRow>.Builder failures,
        ImmutableArray<DependencyEvidenceGroupRow>.Builder groups,
        ImmutableArray<DependencyEvidenceRestoredPackageRow>.Builder packages)
    {
        PackageSourceCoordinateParts coordinate = ReadCoordinate(root.Identity);
        DependencyEvidenceDeclarationState declarationState =
            root.Declaration switch
            {
                PackageDependencyEvidenceDeclarationResult.Available =>
                    DependencyEvidenceDeclarationState.Available,
                PackageDependencyEvidenceDeclarationResult.NotApplicable =>
                    DependencyEvidenceDeclarationState.NotApplicable,
                PackageDependencyEvidenceDeclarationResult.Unavailable =>
                    DependencyEvidenceDeclarationState.Unavailable,
                _ => DependencyEvidenceDeclarationState.Failed,
            };
        var available = root.Declaration
            as PackageDependencyEvidenceDeclarationResult.Available;
        int declarationCount = 0;
        Dictionary<PackageDependencyEvidenceGroupIdentity, int> groupIndexes = [];

        if (available is not null)
        {
            foreach (PackageDependencyEvidenceGroup group in available.Groups)
            {
                int groupIndex = nextGroupIndex++;
                string groupOrderKey = ProjectGroupOrderKey(group, groupIndex);
                groupIndexes[group.Identity] = groupIndex;
                bool isSelected = root.Selection.SelectedGroup is { } selected
                    && selected == group.Identity;
                groups.Add(
                    new DependencyEvidenceGroupRow(
                        index,
                        root.Identity,
                        root.Display,
                        root.InputKind,
                        groupIndex,
                        group.Identity,
                        groupOrderKey,
                        group.SourceOccurrences,
                        group.FrameworkScope.Kind,
                        group.FrameworkScope.CanonicalFramework,
                        group.FrameworkScope.SourceSpelling,
                        group.Identity is
                            PackageDependencyEvidenceGroupIdentity.Package
                            {
                                IsImplicitManifestGroup: true,
                            },
                        group.Declarations.Length,
                        isSelected));

                foreach (PackageDependencyEvidenceDeclaration declaration in
                    group.Declarations)
                {
                    declarationCount++;
                    dependencies.Add(
                        new DependencyEvidenceDependencyRow(
                            index,
                            root.Identity,
                            root.Display,
                            root.InputKind,
                            root.Provenance.AcquisitionForm,
                            groupIndex,
                            group.Identity,
                            groupOrderKey,
                            declaration.Identity,
                            group.FrameworkScope.Kind,
                            group.FrameworkScope.CanonicalFramework,
                            group.FrameworkScope.SourceSpelling,
                            declaration.CanonicalPackageId,
                            declaration.CanonicalVersionConstraint,
                            declaration.SourcePackageIdSpelling,
                            declaration.SourceVersionConstraintSpelling,
                            declaration.SourceOccurrenceCount,
                            isSelected));
                }
            }

            foreach (PackageDependencyEvidenceDeclarationFailure failure in
                available.Failures)
            {
                failures.Add(
                    ProjectDeclarationFailure(index, root, failure, groupIndexes));
            }
        }
        else if (root.Declaration
            is PackageDependencyEvidenceDeclarationResult.Failed failed)
        {
            failures.Add(
                ProjectDeclarationFailure(
                    index,
                    root,
                    failed.Failure,
                    groupIndexes));
        }

        var relationships =
            root.Relationships
                as PackageDependencyEvidenceRelationshipResult.Available;
        if (relationships is not null)
        {
            foreach (PackageDependencyEvidenceResolvedPackage package in
                relationships.Packages)
            {
                RestoredProjectPackageNodeIdentity identity =
                    ReadRestoredPackageIdentity(package.Identity);
                packages.Add(
                    new DependencyEvidenceRestoredPackageRow(
                        index,
                        root.Identity,
                        root.Display,
                        identity,
                        package.Coordinate.PackageId,
                        package.Coordinate.Version,
                        ReadRestoredRole(package.Role)));
            }

            foreach (PackageDependencyEvidenceRelationship relationship in
                relationships.Relationships)
            {
                RestoredProjectGraphParentIdentity parent =
                    ReadRestoredParent(relationship.Parent);
                RestoredProjectPackageNodeIdentity dependency =
                    ReadRestoredPackageIdentity(relationship.Dependency);
                (DependencyEvidenceEdgeParentKind parentKind,
                    string? parentPackageId,
                    string? parentPackageVersion,
                    string? parentProjectIdentity) = ReadParent(parent);
                edges.Add(
                    new DependencyEvidenceRestoredEdgeRow(
                        index,
                        root.Identity,
                        root.Display,
                        ReadRestoredRelationshipIdentity(relationship.Identity),
                        parentKind,
                        parent,
                        parentPackageId,
                        parentPackageVersion,
                        parentProjectIdentity,
                        dependency,
                        relationship.ResolvedCoordinate.PackageId,
                        relationship.ResolvedCoordinate.Version,
                        relationship.CanonicalRequestedConstraint
                            ?? throw new InvalidOperationException(
                                "A restored-project relationship requires a requested constraint."),
                        relationship.SourceRequestedConstraintSpelling
                            ?? throw new InvalidOperationException(
                                "A restored-project relationship requires source constraint evidence."),
                        ReadRestoredRole(relationship.Role),
                        relationship.DeclarationAssociation));
            }

            foreach (PackageDependencyEvidenceRelationshipFailure failure in
                relationships.Failures)
            {
                failures.Add(
                    ProjectGraphFailure(
                        index,
                        root,
                        ReadRestoredRelationshipFailure(failure)));
            }
        }
        else if (root.Relationships
            is PackageDependencyEvidenceRelationshipResult.Failed
                failedRelationships)
        {
            failures.Add(
                ProjectGraphFailure(
                    index,
                    root,
                    ReadRestoredRelationshipFailure(
                        failedRelationships.Failure)));
        }

        roots.Add(
            new DependencyEvidenceRootRow(
                index,
                root.Identity,
                root.Display,
                root.InputKind,
                root.Provenance.AcquisitionForm,
                root.Provenance.SourceLabel,
                coordinate.PackageId,
                coordinate.Version,
                (root.Provenance as PackageDependencyEvidenceRootProvenance.Package)
                    ?.IdentityProvenance,
                (root.Provenance as PackageDependencyEvidenceRootProvenance.Package)
                    ?.Source,
                (root.Provenance as PackageDependencyEvidenceRootProvenance.AuthoredProject)
                    ?.ContentProvenance.Sha256
                ?? (root.Provenance as PackageDependencyEvidenceRootProvenance.RestoredProject)
                    ?.ContentProvenance.Sha256,
                coordinate.RestoredSelection,
                declarationState,
                available?.Completion,
                available?.Groups.Length ?? 0,
                declarationCount,
                root.Selection.Status,
                root.Selection.SelectedGroup,
                root.Selection.SelectedGroup is { } selectedGroup
                    && groupIndexes.TryGetValue(
                        selectedGroup,
                        out int selectedGroupIndex)
                    ? selectedGroupIndex
                    : null,
                root.Selection.SelectedSourceOccurrence,
                root.Selection.RequestedFramework,
                root.Selection.SelectedFramework,
                root.Relationships switch
                {
                    PackageDependencyEvidenceRelationshipResult.NotApplicable =>
                        DependencyEvidenceGraphState.NotApplicable,
                    PackageDependencyEvidenceRelationshipResult.Available =>
                        DependencyEvidenceGraphState.Available,
                    PackageDependencyEvidenceRelationshipResult.Unavailable =>
                        DependencyEvidenceGraphState.Unavailable,
                    _ => DependencyEvidenceGraphState.Failed,
                },
                relationships?.Completion,
                relationships?.Packages.Length ?? 0,
                relationships?.Relationships.Length ?? 0,
                root.RestoredTarget?.FrameworkIdentity,
                root.RestoredTarget?.SourceFrameworkSpelling,
                root.RestoredTarget?.RuntimeIdentifierIdentity,
                root.RestoredTarget?.SourceRuntimeIdentifierSpelling,
                root.RestoredTarget?.Provenance));
    }

    private static string ProjectGroupOrderKey(
        PackageDependencyEvidenceGroup group,
        int groupIndex) =>
        group.FrameworkScope.Kind is
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework
            or PackageDependencyFrameworkScopeKind.UnresolvedFramework
                ? "group:"
                    + groupIndex.ToString(
                        "D10",
                        System.Globalization.CultureInfo.InvariantCulture)
                : group.OrderKey;

    private static DependencyEvidenceFailureRow ProjectRootFailure(
        PackageDependencyEvidenceRootFailure failure,
        int? rootIndex) =>
        failure switch
        {
            PackageDependencyEvidenceRootFailure.Package package =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Root,
                    package.Failure.Reason.ToString(),
                    package.AcquisitionForm,
                    rootIndex,
                    null,
                    null,
                    null,
                    null,
                    package.SourceLabel,
                    package.Coordinate?.PackageId,
                    package.Coordinate?.Version,
                    package.SourceLabel,
                    Prose(package.Failure.Message),
                    1),
            PackageDependencyEvidenceRootFailure.RestoredProject restored =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Root,
                    restored.Failure.Reason.ToString(),
                    restored.AcquisitionForm,
                    rootIndex,
                    null,
                    null,
                    null,
                    null,
                    restored.SourceLabel,
                    null,
                    null,
                    restored.SourceLabel,
                    Prose(restored.Failure.Message),
                    1),
            PackageDependencyEvidenceRootFailure.AuthoredProject authored =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Root,
                    authored.Failure.Reason.ToString(),
                    authored.AcquisitionForm,
                    rootIndex,
                    null,
                    null,
                    null,
                    null,
                    authored.SourceLabel,
                    null,
                    null,
                    authored.SourceLabel,
                    Prose(authored.Failure.Message),
                    1),
            PackageDependencyEvidenceRootFailure.PackageProfile profile =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.PackageProfile,
                    profile.ManifestFailureReason is { } manifestReason
                        ? $"{profile.Kind}.{manifestReason}"
                        : profile.Kind.ToString(),
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    null,
                    null,
                    null,
                    null,
                    profile.Source,
                    profile.PackageId,
                    profile.Coordinate?.PackageId,
                    profile.Coordinate?.Version,
                    profile.Source.Producer.Display,
                    profile.Message,
                    1),
            PackageDependencyEvidenceRootFailure.Acquisition acquisition =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Root,
                    acquisition.Reason.ToString(),
                    acquisition.AcquisitionForm,
                    rootIndex,
                    null,
                    null,
                    null,
                    null,
                    acquisition.SourceLabel,
                    acquisition.Coordinate?.PackageId,
                    acquisition.Coordinate?.Version,
                    acquisition.SourceLabel,
                    Prose(DescribeAcquisition(acquisition.Reason)),
                    1),
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence root failure."),
        };

    private static DependencyEvidenceFailureRow ProjectDeclarationFailure(
        int index,
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclarationFailure failure,
        IReadOnlyDictionary<PackageDependencyEvidenceGroupIdentity, int> groupIndexes)
    {
        PackageDependencyEvidenceGroupIdentity? group = failure switch
        {
            PackageDependencyEvidenceDeclarationFailure
                .ConflictingPackageDeclaration conflicting => conflicting.Group,
            PackageDependencyEvidenceDeclarationFailure
                .InvalidPackageDeclaration invalid => invalid.Group,
            _ => null,
        };
        int? groupIndex = group is not null
            && groupIndexes.TryGetValue(group, out int resolved)
                ? resolved
                : null;

        return failure switch
        {
            PackageDependencyEvidenceDeclarationFailure
                .ConflictingPackageDeclaration conflicting =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Declaration,
                    "ConflictingPackageDeclaration",
                    root.Provenance.AcquisitionForm,
                    index,
                    root.Identity,
                    group,
                    groupIndex,
                    RootSource(root),
                    root.Display,
                    conflicting.CanonicalPackageId,
                    null,
                    root.Provenance.SourceLabel,
                    Prose(
                        "Two declarations of one package disagree on their version constraint."),
                    conflicting.SourceOccurrenceCount),
            PackageDependencyEvidenceDeclarationFailure
                .InvalidPackageDeclaration invalid =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Declaration,
                    "InvalidPackageDeclaration",
                    root.Provenance.AcquisitionForm,
                    index,
                    root.Identity,
                    group,
                    groupIndex,
                    RootSource(root),
                    root.Display,
                    null,
                    null,
                    root.Provenance.SourceLabel,
                    Prose(
                        "A declared package identity or version constraint is invalid."),
                    invalid.SourceOccurrenceCount),
            PackageDependencyEvidenceDeclarationFailure.RestoredProject restored =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Declaration,
                    restored.Failure.Reason.ToString(),
                    root.Provenance.AcquisitionForm,
                    index,
                    root.Identity,
                    null,
                    null,
                    RootSource(root),
                    root.Display,
                    null,
                    null,
                    root.Provenance.SourceLabel,
                    Prose(restored.Failure.Message),
                    restored.Failure.Count),
            PackageDependencyEvidenceDeclarationFailure.AuthoredProject authored =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Declaration,
                    authored.Limitation.Reason.ToString(),
                    root.Provenance.AcquisitionForm,
                    index,
                    root.Identity,
                    null,
                    null,
                    RootSource(root),
                    root.Display,
                    null,
                    null,
                    root.Provenance.SourceLabel,
                    Prose(
                        DescribeAuthoredProjectLimitation(
                            authored.Limitation.Reason)),
                    authored.Limitation.Count),
            PackageDependencyEvidenceDeclarationFailure
                .AuthoredProjectUnresolvedSyntax unresolved =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Declaration,
                    unresolved.Syntax.Kind.ToString(),
                    root.Provenance.AcquisitionForm,
                    index,
                    root.Identity,
                    null,
                    null,
                    RootSource(root),
                    root.Display,
                    null,
                    null,
                    root.Provenance.SourceLabel,
                    Prose(
                        "Authored package-reference syntax could not establish a direct declaration."),
                    1,
                    unresolved.Syntax.OpaqueIdentity),
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence declaration failure."),
        };
    }

    private static DependencyEvidenceFailureRow ProjectGraphFailure(
        int index,
        PackageDependencyEvidenceRoot root,
        RestoredProjectGraphFailure failure) =>
        new(
            DependencyEvidenceFailurePhase.Graph,
            failure.Reason.ToString(),
            root.Provenance.AcquisitionForm,
            index,
            root.Identity,
            null,
            null,
            RootSource(root),
            root.Display,
            null,
            null,
            root.Provenance.SourceLabel,
            Prose(failure.Message),
            failure.Count);

    private static RestoredProjectPackageNodeIdentity
        ReadRestoredPackageIdentity(
            PackageDependencyEvidencePackageIdentity identity) =>
        identity is PackageDependencyEvidencePackageIdentity.RestoredProject
            restored
            ? restored.Identity
            : throw new InvalidOperationException(
                "The current CLI restored-package projection requires restored-project identity.");

    private static RestoredProjectEdgeIdentity
        ReadRestoredRelationshipIdentity(
            PackageDependencyEvidenceRelationshipIdentity identity) =>
        identity is PackageDependencyEvidenceRelationshipIdentity.RestoredProject
            restored
            ? restored.Identity
            : throw new InvalidOperationException(
                "The current CLI restored-edge projection requires restored-project identity.");

    private static RestoredProjectGraphParentIdentity ReadRestoredParent(
        PackageDependencyEvidenceRelationshipParentIdentity parent) =>
        parent switch
        {
            PackageDependencyEvidenceRelationshipParentIdentity.Root root
                when root.Identity
                    is PackageDependencyEvidenceRootIdentity.RestoredProject
                        restored =>
                new RestoredProjectGraphParentIdentity.Root(restored.Identity),
            PackageDependencyEvidenceRelationshipParentIdentity.Package package =>
                new RestoredProjectGraphParentIdentity.Package(
                    ReadRestoredPackageIdentity(package.Identity)),
            PackageDependencyEvidenceRelationshipParentIdentity.Project project =>
                new RestoredProjectGraphParentIdentity.Project(project.Identity),
            _ => throw new InvalidOperationException(
                "The current CLI restored-edge projection requires restored-project parent identity."),
        };

    private static RestoredProjectDependencyRole ReadRestoredRole(
        PackageDependencyEvidenceRelationshipRole? role) =>
        role switch
        {
            PackageDependencyEvidenceRelationshipRole.Direct =>
                RestoredProjectDependencyRole.Direct,
            PackageDependencyEvidenceRelationshipRole.Transitive =>
                RestoredProjectDependencyRole.Transitive,
            _ => throw new InvalidOperationException(
                "The current CLI restored-graph projection requires a dependency role."),
        };

    private static RestoredProjectGraphFailure
        ReadRestoredRelationshipFailure(
            PackageDependencyEvidenceRelationshipFailure failure) =>
        failure is PackageDependencyEvidenceRelationshipFailure.RestoredProject
            restored
            ? restored.Failure
            : throw new InvalidOperationException(
                "The current CLI restored-graph projection requires a restored-project failure.");

    private static PackageSourceResultIdentity? RootSource(
        PackageDependencyEvidenceRoot root) =>
        (root.Provenance as PackageDependencyEvidenceRootProvenance.Package)?.Source;

    private static (
        DependencyEvidenceEdgeParentKind Kind,
        string? PackageId,
        string? PackageVersion,
        string? ProjectIdentity) ReadParent(
            RestoredProjectGraphParentIdentity parent) =>
        parent switch
        {
            RestoredProjectGraphParentIdentity.Root =>
                (DependencyEvidenceEdgeParentKind.Root, null, null, null),
            RestoredProjectGraphParentIdentity.Package package => (
                DependencyEvidenceEdgeParentKind.Package,
                package.Identity.Coordinate.PackageId,
                package.Identity.Coordinate.Version,
                null),
            RestoredProjectGraphParentIdentity.Project project => (
                DependencyEvidenceEdgeParentKind.Project,
                null,
                null,
                project.Identity.SourceIdentity),
            _ => throw new InvalidOperationException(
                "Unknown restored project graph parent identity."),
        };

    private static PackageSourceCoordinateParts ReadCoordinate(
        PackageDependencyEvidenceRootIdentity identity) =>
        identity switch
        {
            PackageDependencyEvidenceRootIdentity.Package package =>
                new PackageSourceCoordinateParts(
                    package.Coordinate.PackageId,
                    package.Coordinate.Version,
                    null),
            PackageDependencyEvidenceRootIdentity.RestoredProject restored =>
                new PackageSourceCoordinateParts(
                    null,
                    null,
                    restored.Identity.Selection),
            PackageDependencyEvidenceRootIdentity.AuthoredProject =>
                new PackageSourceCoordinateParts(null, null, null),
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence root identity."),
        };

    private static string DescribeAuthoredProjectLimitation(
        AuthoredProjectDependencyLimitationReason reason) =>
        reason switch
        {
            AuthoredProjectDependencyLimitationReason.ExplicitImport =>
                "An explicit import requires MSBuild evaluation.",
            AuthoredProjectDependencyLimitationReason.PropertyIndirection =>
                "A dependency value uses property indirection.",
            AuthoredProjectDependencyLimitationReason.ItemOrMetadataExpression =>
                "A dependency value uses an item or metadata expression.",
            AuthoredProjectDependencyLimitationReason.CentralPackageManagement =>
                "A package version depends on central package management.",
            AuthoredProjectDependencyLimitationReason.UnsupportedTargetDeclaration =>
                "A target-framework declaration could not be projected.",
            AuthoredProjectDependencyLimitationReason.ConflictingTargetDeclarations =>
                "Target-framework declarations conflict.",
            AuthoredProjectDependencyLimitationReason.UnsupportedCondition =>
                "A package declaration condition could not be evaluated.",
            AuthoredProjectDependencyLimitationReason.UnsupportedPackageReferenceShape =>
                "A package reference has an unsupported authored shape.",
            AuthoredProjectDependencyLimitationReason.PackageItemOperation =>
                "A package item operation requires evaluated project semantics.",
            AuthoredProjectDependencyLimitationReason.MissingVersionConstraint =>
                "A package declaration has no established version constraint.",
            AuthoredProjectDependencyLimitationReason.InvalidPackageId =>
                "A package declaration has an invalid package identity.",
            AuthoredProjectDependencyLimitationReason.InvalidVersionConstraint =>
                "A package declaration has an invalid version constraint.",
            AuthoredProjectDependencyLimitationReason.ConflictingVersionForms =>
                "A package declaration contains conflicting version forms.",
            AuthoredProjectDependencyLimitationReason.ConflictingPackageDeclaration =>
                "Package declarations conflict within one authored scope.",
            _ => "The authored-project declaration projection is incomplete.",
        };

    private static string DescribeAcquisition(
        PackageDependencyEvidenceAcquisitionFailureReason reason) =>
        reason switch
        {
            PackageDependencyEvidenceAcquisitionFailureReason.NotFound =>
                "The requested root was not found.",
            PackageDependencyEvidenceAcquisitionFailureReason.NotRestored =>
                "The requested project has no restored assets. Run 'dotnet restore'.",
            PackageDependencyEvidenceAcquisitionFailureReason.SourceUnavailable =>
                "No authorized package source could serve the requested root.",
            PackageDependencyEvidenceAcquisitionFailureReason.ProducerContract =>
                "The requested root violates the acquisition contract.",
            _ => "The requested root could not be acquired.",
        };

    private static InertString Prose(string message) =>
        new(
            TextPolicy.Prose,
            message,
            PackageManifestFactsQuery.MaxScalarCharacters);

    private readonly record struct PackageSourceCoordinateParts(
        string? PackageId,
        string? Version,
        RestoredProjectSelectionIdentity? RestoredSelection);
}
