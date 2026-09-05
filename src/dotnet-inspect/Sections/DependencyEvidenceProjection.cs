using System.Collections.Immutable;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>The closed declaration state of one admitted root, as a projected row value.</summary>
public enum DependencyEvidenceDeclarationState
{
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
    PackageDependencyEvidenceRootOwner Owner,
    PackageDependencyEvidenceSourceKind SourceKind,
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
    PackageDependencyEvidenceRootOwner Owner,
    PackageDependencyEvidenceSourceKind SourceKind,
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
    PackageDependencyEvidenceRootOwner Owner,
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
    public int SourceOccurrenceCount => SourceOccurrences.Length;
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
    RestoredProjectDependencyRole Role);

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
    PackageDependencyEvidenceSourceKind? SourceKind,
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
    int Occurrences);

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
    {
        ArgumentNullException.ThrowIfNull(outcome);

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

        foreach (PackageDependencyEvidenceRootFailure failure in outcome.FailedRoots)
            failures.Add(ProjectRootFailure(failure));

        int nextGroupIndex = 0;
        for (int index = 0; index < outcome.Roots.Length; index++)
        {
            PackageDependencyEvidenceRoot root = outcome.Roots[index];
            ProjectRoot(
                index,
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
                groupIndexes[group.Identity] = groupIndex;
                bool isSelected = root.Selection.SelectedGroup is { } selected
                    && selected == group.Identity;
                groups.Add(
                    new DependencyEvidenceGroupRow(
                        index,
                        root.Identity,
                        root.Display,
                        root.Provenance.Owner,
                        groupIndex,
                        group.Identity,
                        group.OrderKey,
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
                            root.Provenance.Owner,
                            root.Provenance.SourceKind,
                            groupIndex,
                            group.Identity,
                            group.OrderKey,
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

        var graph = root.Graph as PackageDependencyEvidenceGraphResult.Available;
        if (graph is not null)
        {
            foreach (RestoredProjectPackageNode node in graph.Packages)
            {
                packages.Add(
                    new DependencyEvidenceRestoredPackageRow(
                        index,
                        root.Identity,
                        root.Display,
                        node.Identity,
                        node.Identity.Coordinate.PackageId,
                        node.Identity.Coordinate.Version,
                        node.Role));
            }

            foreach (RestoredProjectGraphEdge edge in graph.Edges)
            {
                (DependencyEvidenceEdgeParentKind parentKind,
                    string? parentPackageId,
                    string? parentPackageVersion,
                    string? parentProjectIdentity) = ReadParent(edge.Parent);
                edges.Add(
                    new DependencyEvidenceRestoredEdgeRow(
                        index,
                        root.Identity,
                        root.Display,
                        edge.Identity,
                        parentKind,
                        edge.Parent,
                        parentPackageId,
                        parentPackageVersion,
                        parentProjectIdentity,
                        edge.Dependency,
                        edge.Dependency.Coordinate.PackageId,
                        edge.Dependency.Coordinate.Version,
                        edge.CanonicalVersionConstraint,
                        edge.SourceVersionConstraintSpelling,
                        edge.Role));
            }

            foreach (RestoredProjectGraphFailure failure in graph.Failures)
                failures.Add(ProjectGraphFailure(index, root, failure));
        }
        else if (root.Graph is PackageDependencyEvidenceGraphResult.Failed
            failedGraph)
        {
            failures.Add(ProjectGraphFailure(index, root, failedGraph.Failure));
        }

        roots.Add(
            new DependencyEvidenceRootRow(
                index,
                root.Identity,
                root.Display,
                root.Provenance.Owner,
                root.Provenance.SourceKind,
                root.Provenance.SourceLabel,
                coordinate.PackageId,
                coordinate.Version,
                (root.Provenance as PackageDependencyEvidenceRootProvenance.Package)
                    ?.IdentityProvenance,
                (root.Provenance as PackageDependencyEvidenceRootProvenance.Package)
                    ?.Source,
                (root.Provenance as PackageDependencyEvidenceRootProvenance.RestoredProject)
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
                root.Graph switch
                {
                    PackageDependencyEvidenceGraphResult.NotApplicable =>
                        DependencyEvidenceGraphState.NotApplicable,
                    PackageDependencyEvidenceGraphResult.Available =>
                        DependencyEvidenceGraphState.Available,
                    PackageDependencyEvidenceGraphResult.Unavailable =>
                        DependencyEvidenceGraphState.Unavailable,
                    _ => DependencyEvidenceGraphState.Failed,
                },
                graph?.Completion,
                graph?.Packages.Length ?? 0,
                graph?.Edges.Length ?? 0,
                root.RestoredTarget?.FrameworkIdentity,
                root.RestoredTarget?.SourceFrameworkSpelling,
                root.RestoredTarget?.RuntimeIdentifierIdentity,
                root.RestoredTarget?.SourceRuntimeIdentifierSpelling,
                root.RestoredTarget?.Provenance));
    }

    private static DependencyEvidenceFailureRow ProjectRootFailure(
        PackageDependencyEvidenceRootFailure failure) =>
        failure switch
        {
            PackageDependencyEvidenceRootFailure.Package package =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.Root,
                    package.Failure.Reason.ToString(),
                    package.SourceKind,
                    null,
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
                    restored.SourceKind,
                    null,
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
            PackageDependencyEvidenceRootFailure.PackageProfile profile =>
                new DependencyEvidenceFailureRow(
                    DependencyEvidenceFailurePhase.PackageProfile,
                    profile.ManifestFailureReason is { } manifestReason
                        ? $"{profile.Kind}.{manifestReason}"
                        : profile.Kind.ToString(),
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
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
                    acquisition.SourceKind,
                    null,
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
                    root.Provenance.SourceKind,
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
                    root.Provenance.SourceKind,
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
                    root.Provenance.SourceKind,
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
            root.Provenance.SourceKind,
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
            _ => throw new InvalidOperationException(
                "Unknown package dependency evidence root identity."),
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
