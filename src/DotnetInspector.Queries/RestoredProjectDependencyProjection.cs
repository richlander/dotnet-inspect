using System.Collections.Immutable;
using InertText;

namespace DotnetInspector.Queries;

/// <summary>
/// The internal node-key currency shared by the restored-project facts projection and the
/// restored-project traversal owner. It is derived only from already-safe public identity
/// segments — canonical package coordinates and opaque project digests — so no artifact-authored
/// spelling can imitate another node's key.
/// </summary>
static class RestoredProjectNodeKey
{
    /// <summary>The explicit restored root. Its key is empty, which no node key can spell.</summary>
    public const string Root = "";

    public static string ForPackage(RestoredProjectPackageNodeIdentity identity) =>
        $"pkg:{identity.Coordinate.PackageId}/{identity.Coordinate.Version}";

    public static string ForProject(RestoredProjectProjectNodeIdentity identity) =>
        $"proj:{identity.SourceIdentity}";

    public static string For(RestoredProjectGraphParentIdentity node) => node switch
    {
        RestoredProjectGraphParentIdentity.Root => Root,
        RestoredProjectGraphParentIdentity.Package package => ForPackage(package.Identity),
        RestoredProjectGraphParentIdentity.Project project => ForProject(project.Identity),
        _ => throw new InvalidOperationException($"Unknown restored-project node: {node.GetType().FullName}"),
    };
}

/// <summary>
/// One resolved project node observed while walking the selected target, with the exact authored
/// target-entry spelling retained as contained text rather than as identity.
/// </summary>
sealed record RestoredProjectProjectNodeEvidence(
    RestoredProjectProjectNodeIdentity Identity,
    InertString SourceSpelling);

/// <summary>One project-resolving relationship: the root or a graph node depending on a project node.</summary>
sealed record RestoredProjectProjectRelationshipEvidence(
    RestoredProjectGraphParentIdentity Parent,
    RestoredProjectProjectNodeIdentity Dependency);

/// <summary>
/// One graph-phase failure occurrence together with the node whose expansion produced it.
/// </summary>
/// <param name="OwnerNodeKey">
/// The <see cref="RestoredProjectNodeKey"/> of the node being expanded when the failure occurred,
/// or <see langword="null"/> when the failure is not attributable to one node expansion.
/// </param>
readonly record struct RestoredProjectGraphFailureOccurrence(
    RestoredProjectGraphFailureReason Reason,
    string? OwnerNodeKey);

/// <summary>
/// The project-relationship topology of one selected target. The facts owner does not publish it;
/// it exists so the restored-project traversal owner can connect root, project, and package nodes
/// without a second parse or a second target-selection implementation.
/// </summary>
sealed record RestoredProjectGraphTopology(
    ImmutableArray<RestoredProjectProjectNodeEvidence> ProjectNodes,
    ImmutableArray<RestoredProjectProjectRelationshipEvidence> ProjectRelationships,
    ImmutableArray<RestoredProjectGraphFailureOccurrence> FailureOccurrences,
    bool RelationshipLimitExceeded)
{
    public static RestoredProjectGraphTopology Empty { get; } = new([], [], [], false);
}

/// <summary>
/// The single owner-issued projection over one exact assets byte sequence: the published facts
/// result and the internal project-relationship topology computed by the same walk.
/// </summary>
sealed record RestoredProjectDependencyProjection(
    RestoredProjectDependencyFactsResult Result,
    RestoredProjectGraphTopology Topology);
