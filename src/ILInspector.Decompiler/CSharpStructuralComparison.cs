using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ILInspector.Instructions;

namespace ILInspector.Decompiler;

/// <summary>Structural outcomes for one selected annotated-source node.</summary>
[Flags]
public enum CSharpStructuralChangeKind
{
    /// <summary>The node exists only in the after document.</summary>
    Added = 1,

    /// <summary>The node exists only in the before document.</summary>
    Removed = 2,

    /// <summary>The corresponding nodes differ in stable kind or exact selected text.</summary>
    Changed = 4,

    /// <summary>The correspondence owner reports that the node moved.</summary>
    Moved = 8,
}

/// <summary>
/// Owner-issued identity correspondence between one node in each document.
/// </summary>
/// <param name="BeforeNodeId">Document-local node id in the before document.</param>
/// <param name="AfterNodeId">Document-local node id in the after document.</param>
/// <param name="Moved">
/// Whether the correspondence owner determined that the logical node moved.
/// Coordinates are not used to infer this relationship.
/// </param>
public sealed record CSharpNodeCorrespondence(
    int BeforeNodeId,
    int AfterNodeId,
    bool Moved = false);

/// <summary>Exact identity of one serialized annotated-source revision.</summary>
public sealed record CSharpDocumentRevision
{
    /// <summary>Creates a validated SHA-256 document identity.</summary>
    public CSharpDocumentRevision(string Sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Sha256);
        if (Sha256.Length != 64 || Sha256.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Document revision must be a 64-character SHA-256 hexadecimal value.",
                nameof(Sha256));
        }
        this.Sha256 = Sha256.ToUpperInvariant();
    }

    /// <summary>SHA-256 of the canonical compact document serialization.</summary>
    public string Sha256 { get; }

    internal static CSharpDocumentRevision Create(AnnotatedSourceDocument document)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            document,
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);
        return new CSharpDocumentRevision(Convert.ToHexString(SHA256.HashData(json)));
    }
}

/// <summary>Document-scoped identity of one syntax node.</summary>
public sealed record CSharpDocumentNodeIdentity(
    CSharpDocumentRevision Document,
    int NodeId);

/// <summary>Evidence mechanism used to issue one cross-document match.</summary>
public enum CSharpNodeMatchProvenance
{
    /// <summary>Unique equality of product-owned IL-origin set and same-origin IR depth.</summary>
    IlOriginSet,
}

/// <summary>Why one node received no cross-document match.</summary>
public enum CSharpUnmatchedNodeReason
{
    /// <summary>The producer retained no supported identity evidence.</summary>
    Unsupported,

    /// <summary>The evidence key is non-unique on at least one side.</summary>
    Ambiguous,

    /// <summary>The unique evidence key exists on this side only.</summary>
    NoCounterpart,
}

/// <summary>One product-issued cross-document node match.</summary>
public sealed record CSharpNodeMatch(
    CSharpDocumentNodeIdentity Before,
    CSharpDocumentNodeIdentity After,
    CSharpNodeMatchProvenance Provenance,
    AnnotatedSourceNodeProvenance Evidence,
    bool Moved = false);

/// <summary>One explicitly unmatched syntax node.</summary>
public sealed record CSharpUnmatchedNode(
    CSharpDocumentNodeIdentity Node,
    CSharpUnmatchedNodeReason Reason,
    AnnotatedSourceNodeProvenance? Evidence = null);

/// <summary>
/// Product-issued, revision-bound correspondence over every C# node in two
/// annotated-source documents.
/// </summary>
public sealed record CSharpNodeCorrespondenceResult(
    string Subject,
    AnnotatedSourceDocument Before,
    AnnotatedSourceDocument After,
    CSharpDocumentRevision BeforeRevision,
    CSharpDocumentRevision AfterRevision,
    ImmutableArray<CSharpNodeMatch> Matches,
    ImmutableArray<CSharpUnmatchedNode> UnmatchedBefore,
    ImmutableArray<CSharpUnmatchedNode> UnmatchedAfter);

/// <summary>
/// Independently supplied compile-back fidelity evidence for the two rendered
/// document revisions.
/// </summary>
/// <param name="Before">Compile-back outcome for the before revision.</param>
/// <param name="After">Compile-back outcome for the after revision.</param>
/// <param name="Note">
/// Optional evidence-bounded detail, such as a retained terminal IL instruction.
/// The structural comparison does not derive or validate this text.
/// </param>
public sealed record CSharpStructuralFidelityEvidence(
    IlBodyDiffOutcome Before,
    IlBodyDiffOutcome After,
    string? Note = null);

/// <summary>
/// Inputs for one decompiler-owned structural comparison over selected nodes.
/// </summary>
/// <param name="Subject">Stable member identity or other owner-issued subject label.</param>
/// <param name="Before">Before annotated-source document.</param>
/// <param name="After">After annotated-source document.</param>
/// <param name="BeforeNodeIds">Selected before-document nodes that may participate.</param>
/// <param name="AfterNodeIds">Selected after-document nodes that may participate.</param>
/// <param name="Correspondences">
/// Owner-issued one-to-one correspondence for selected nodes. Unmatched selected
/// nodes become removed or added rows.
/// </param>
/// <param name="Fidelity">Optional independent compile-back evidence.</param>
public sealed record CSharpStructuralComparisonInput(
    string Subject,
    AnnotatedSourceDocument Before,
    AnnotatedSourceDocument After,
    IReadOnlyList<int> BeforeNodeIds,
    IReadOnlyList<int> AfterNodeIds,
    IReadOnlyList<CSharpNodeCorrespondence> Correspondences,
    CSharpStructuralFidelityEvidence? Fidelity = null);

/// <summary>One typed structural delta between two annotated-source documents.</summary>
/// <param name="Change">One or more explicit structural outcomes.</param>
/// <param name="BeforeNodeId">Before document-local node id, when present.</param>
/// <param name="AfterNodeId">After document-local node id, when present.</param>
/// <param name="BeforeKind">Stable before kind id, when present.</param>
/// <param name="AfterKind">Stable after kind id, when present.</param>
/// <param name="BeforeLabel">Product-owned before display label, when present.</param>
/// <param name="AfterLabel">Product-owned after display label, when present.</param>
/// <param name="BeforeRegion">Smallest enclosing before region role, when known.</param>
/// <param name="AfterRegion">Smallest enclosing after region role, when known.</param>
/// <param name="BeforeSpans">Exact absolute UTF-16 spans in the before document.</param>
/// <param name="AfterSpans">Exact absolute UTF-16 spans in the after document.</param>
public sealed record CSharpStructuralDiffRow(
    CSharpStructuralChangeKind Change,
    int? BeforeNodeId,
    int? AfterNodeId,
    string? BeforeKind,
    string? AfterKind,
    string? BeforeLabel,
    string? AfterLabel,
    PrintedRegionRole? BeforeRegion,
    PrintedRegionRole? AfterRegion,
    ImmutableArray<AnnotatedSourceSpan> BeforeSpans,
    ImmutableArray<AnnotatedSourceSpan> AfterSpans);

/// <summary>
/// One producer-owned comparison consumed by both full-body caret and rich-diff
/// presentation.
/// </summary>
/// <param name="Subject">Stable member identity or owner-issued subject label.</param>
/// <param name="Before">Before annotated-source document.</param>
/// <param name="After">After annotated-source document.</param>
/// <param name="Rows">Deterministically ordered structural deltas.</param>
/// <param name="Fidelity">Optional independent compile-back evidence.</param>
public sealed record CSharpStructuralComparison(
    string Subject,
    AnnotatedSourceDocument Before,
    AnnotatedSourceDocument After,
    ImmutableArray<CSharpStructuralDiffRow> Rows,
    CSharpStructuralFidelityEvidence? Fidelity = null,
    CSharpNodeCorrespondenceResult? Correspondence = null)
{
    /// <summary>Whether the selected structure is unchanged.</summary>
    public bool IsExact => Rows.IsEmpty && IsCorrespondenceComplete;

    /// <summary>Whether every C# node had enough unique evidence for a verdict.</summary>
    public bool IsCorrespondenceComplete => Correspondence is null
        || Correspondence.UnmatchedBefore.All(static node =>
            node.Reason == CSharpUnmatchedNodeReason.NoCounterpart)
        && Correspondence.UnmatchedAfter.All(static node =>
            node.Reason == CSharpUnmatchedNodeReason.NoCounterpart);
}

public static partial class CSharpBodyDiff
{
    /// <summary>
    /// Issues trusted correspondence from product-owned IL provenance. The
    /// documents must describe the same exact physical method body.
    /// </summary>
    public static CSharpNodeCorrespondenceResult IssueCorrespondence(
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Source is null || after.Source is null)
        {
            throw new ArgumentException(
                "Trusted correspondence requires physical method provenance on both documents.");
        }
        if (!SamePhysicalMethod(before.Source, after.Source))
        {
            throw new ArgumentException(
                "Trusted correspondence requires both documents to describe the same exact method body.");
        }

        var beforeRevision = CSharpDocumentRevision.Create(before);
        var afterRevision = CSharpDocumentRevision.Create(after);
        var beforeNodes = before.Nodes
            .Where(static node => node.Medium == SourceLineKind.CSharp)
            .ToArray();
        var afterNodes = after.Nodes
            .Where(static node => node.Medium == SourceLineKind.CSharp)
            .ToArray();
        var beforeGroups = GroupByProvenance(beforeNodes);
        var afterGroups = GroupByProvenance(afterNodes);
        bool beforePopulationComplete = beforeNodes.All(static node => node.Provenance is not null);
        bool afterPopulationComplete = afterNodes.All(static node => node.Provenance is not null);
        var matches = ImmutableArray.CreateBuilder<CSharpNodeMatch>();
        var unmatchedBefore = ImmutableArray.CreateBuilder<CSharpUnmatchedNode>();
        var unmatchedAfter = ImmutableArray.CreateBuilder<CSharpUnmatchedNode>();

        foreach (var node in beforeNodes)
        {
            var identity = new CSharpDocumentNodeIdentity(beforeRevision, node.Id);
            if (node.Provenance is null)
            {
                unmatchedBefore.Add(new(identity, CSharpUnmatchedNodeReason.Unsupported));
                continue;
            }

            var origins = OriginSet.From(node.Provenance);
            var beforeCandidates = beforeGroups[origins];
            afterGroups.TryGetValue(origins, out var afterCandidates);
            if (beforeCandidates.Count == 1
                && afterCandidates is { Count: 1 })
            {
                var afterNode = afterCandidates[0];
                matches.Add(new CSharpNodeMatch(
                    identity,
                    new CSharpDocumentNodeIdentity(afterRevision, afterNode.Id),
                    CSharpNodeMatchProvenance.IlOriginSet,
                    node.Provenance));
            }
            else
            {
                unmatchedBefore.Add(new CSharpUnmatchedNode(
                    identity,
                    beforeCandidates.Count == 1
                        && (afterCandidates is null or { Count: 0 })
                        && afterPopulationComplete
                        ? CSharpUnmatchedNodeReason.NoCounterpart
                        : CSharpUnmatchedNodeReason.Ambiguous,
                    node.Provenance));
            }
        }

        foreach (var node in afterNodes)
        {
            var identity = new CSharpDocumentNodeIdentity(afterRevision, node.Id);
            if (node.Provenance is null)
            {
                unmatchedAfter.Add(new(identity, CSharpUnmatchedNodeReason.Unsupported));
                continue;
            }

            var origins = OriginSet.From(node.Provenance);
            var afterCandidates = afterGroups[origins];
            beforeGroups.TryGetValue(origins, out var beforeCandidates);
            if (afterCandidates.Count == 1
                && beforeCandidates is { Count: 1 })
                continue;

            unmatchedAfter.Add(new CSharpUnmatchedNode(
                identity,
                afterCandidates.Count == 1
                    && (beforeCandidates is null or { Count: 0 })
                    && beforePopulationComplete
                    ? CSharpUnmatchedNodeReason.NoCounterpart
                    : CSharpUnmatchedNodeReason.Ambiguous,
                node.Provenance));
        }

        return new CSharpNodeCorrespondenceResult(
            before.Source.Subject,
            before,
            after,
            beforeRevision,
            afterRevision,
            matches
                .OrderBy(static match => match.Before.NodeId)
                .ToImmutableArray(),
            unmatchedBefore
                .OrderBy(static unmatched => unmatched.Node.NodeId)
                .ToImmutableArray(),
            unmatchedAfter
                .OrderBy(static unmatched => unmatched.Node.NodeId)
                .ToImmutableArray());
    }

    /// <summary>
    /// Feeds a validated product-issued correspondence result into the existing
    /// structural comparison consumer.
    /// </summary>
    public static CSharpStructuralComparison CompareStructure(
        CSharpNodeCorrespondenceResult correspondence,
        CSharpStructuralFidelityEvidence? fidelity = null)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        ValidateIssuedCorrespondence(correspondence);

        var before = CSharpAnnotatedSourceProjection.Create(correspondence.Before);
        var after = CSharpAnnotatedSourceProjection.Create(correspondence.After);
        int[] beforeNodeIds =
        [
            .. correspondence.Matches.Select(match => before.NodeIds[match.Before.NodeId]),
            .. correspondence.UnmatchedBefore
                .Where(static unmatched => unmatched.Reason == CSharpUnmatchedNodeReason.NoCounterpart)
                .Select(unmatched => before.NodeIds[unmatched.Node.NodeId])
        ];
        int[] afterNodeIds =
        [
            .. correspondence.Matches.Select(match => after.NodeIds[match.After.NodeId]),
            .. correspondence.UnmatchedAfter
                .Where(static unmatched => unmatched.Reason == CSharpUnmatchedNodeReason.NoCounterpart)
                .Select(unmatched => after.NodeIds[unmatched.Node.NodeId])
        ];
        CSharpNodeCorrespondence[] matches =
        [
            .. correspondence.Matches.Select(match => new CSharpNodeCorrespondence(
                before.NodeIds[match.Before.NodeId],
                after.NodeIds[match.After.NodeId],
                match.Moved))
        ];

        var comparison = CompareStructure(new CSharpStructuralComparisonInput(
            correspondence.Subject,
            before.Document,
            after.Document,
            beforeNodeIds,
            afterNodeIds,
            matches,
            fidelity));
        return comparison with { Correspondence = correspondence };
    }

    /// <summary>
    /// Compares selected C# nodes using owner-issued cross-document
    /// correspondence. Node ids are dereferenced only within the document that
    /// minted them; coordinates and display text never establish identity.
    /// </summary>
    public static CSharpStructuralComparison CompareStructure(
        CSharpStructuralComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Subject);
        ArgumentNullException.ThrowIfNull(input.Before);
        ArgumentNullException.ThrowIfNull(input.After);
        ArgumentNullException.ThrowIfNull(input.BeforeNodeIds);
        ArgumentNullException.ThrowIfNull(input.AfterNodeIds);
        ArgumentNullException.ThrowIfNull(input.Correspondences);
        if (input.Fidelity is { } fidelity
            && (!Enum.IsDefined(fidelity.Before) || !Enum.IsDefined(fidelity.After)))
        {
            throw new ArgumentException(
                "Structural fidelity evidence contains an unknown IL body-diff outcome.",
                nameof(input.Fidelity));
        }

        ValidateCSharpDocument(input.Before, nameof(input.Before));
        ValidateCSharpDocument(input.After, nameof(input.After));

        var beforeSelection = ValidateSelection(
            input.BeforeNodeIds,
            input.Before.Nodes.Count,
            nameof(input.BeforeNodeIds));
        var afterSelection = ValidateSelection(
            input.AfterNodeIds,
            input.After.Nodes.Count,
            nameof(input.AfterNodeIds));

        var matchedBefore = new HashSet<int>();
        var matchedAfter = new HashSet<int>();
        var rows = ImmutableArray.CreateBuilder<CSharpStructuralDiffRow>();

        foreach (var correspondence in input.Correspondences)
        {
            ArgumentNullException.ThrowIfNull(correspondence);
            if (!beforeSelection.Contains(correspondence.BeforeNodeId))
            {
                throw new ArgumentException(
                    $"Correspondence names before node {correspondence.BeforeNodeId}, which is not selected.",
                    nameof(input.Correspondences));
            }
            if (!afterSelection.Contains(correspondence.AfterNodeId))
            {
                throw new ArgumentException(
                    $"Correspondence names after node {correspondence.AfterNodeId}, which is not selected.",
                    nameof(input.Correspondences));
            }
            if (!matchedBefore.Add(correspondence.BeforeNodeId))
            {
                throw new ArgumentException(
                    $"Before node {correspondence.BeforeNodeId} has more than one correspondence.",
                    nameof(input.Correspondences));
            }
            if (!matchedAfter.Add(correspondence.AfterNodeId))
            {
                throw new ArgumentException(
                    $"After node {correspondence.AfterNodeId} has more than one correspondence.",
                    nameof(input.Correspondences));
            }

            var beforeNode = input.Before.Nodes[correspondence.BeforeNodeId];
            var afterNode = input.After.Nodes[correspondence.AfterNodeId];
            var change = CSharpStructuralChangeKind.Changed;
            bool changed = !string.Equals(beforeNode.Kind, afterNode.Kind, StringComparison.Ordinal)
                || !SelectedTextEqual(input.Before, beforeNode, input.After, afterNode);
            if (!changed)
                change = 0;
            if (correspondence.Moved)
                change |= CSharpStructuralChangeKind.Moved;
            if (change == 0)
                continue;

            rows.Add(CreateRow(
                change,
                input.Before,
                beforeNode,
                input.After,
                afterNode));
        }

        foreach (int nodeId in beforeSelection)
        {
            if (!matchedBefore.Contains(nodeId))
            {
                rows.Add(CreateRow(
                    CSharpStructuralChangeKind.Removed,
                    input.Before,
                    input.Before.Nodes[nodeId],
                    afterDocument: null,
                    afterNode: null));
            }
        }

        foreach (int nodeId in afterSelection)
        {
            if (!matchedAfter.Contains(nodeId))
            {
                rows.Add(CreateRow(
                    CSharpStructuralChangeKind.Added,
                    beforeDocument: null,
                    beforeNode: null,
                    input.After,
                    input.After.Nodes[nodeId]));
            }
        }

        var ordered = rows
            .OrderBy(PrimaryStart)
            .ThenBy(static row => row.Change)
            .ThenBy(static row => row.BeforeNodeId ?? int.MaxValue)
            .ThenBy(static row => row.AfterNodeId ?? int.MaxValue)
            .ToImmutableArray();

        return new CSharpStructuralComparison(
            input.Subject,
            input.Before,
            input.After,
            ordered,
            input.Fidelity);
    }

    static Dictionary<OriginSet, List<AnnotatedSourceNode>> GroupByProvenance(
        IReadOnlyList<AnnotatedSourceNode> nodes)
    {
        var groups = new Dictionary<OriginSet, List<AnnotatedSourceNode>>();
        foreach (var node in nodes)
        {
            if (node.Provenance is null)
                continue;
            var key = OriginSet.From(node.Provenance);
            if (!groups.TryGetValue(key, out var values))
                groups[key] = values = [];
            values.Add(node);
        }
        return groups;
    }

    static bool SamePhysicalMethod(
        AnnotatedSourceDocumentSource before,
        AnnotatedSourceDocumentSource after)
        => before.ModuleVersionId == after.ModuleVersionId
            && before.MethodToken == after.MethodToken
            && string.Equals(
                before.BodyFingerprint,
                after.BodyFingerprint,
                StringComparison.Ordinal);

    static void ValidateIssuedCorrespondence(CSharpNodeCorrespondenceResult correspondence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correspondence.Subject);
        ArgumentNullException.ThrowIfNull(correspondence.Before);
        ArgumentNullException.ThrowIfNull(correspondence.After);
        ArgumentNullException.ThrowIfNull(correspondence.BeforeRevision);
        ArgumentNullException.ThrowIfNull(correspondence.AfterRevision);
        if (correspondence.Matches.IsDefault
            || correspondence.UnmatchedBefore.IsDefault
            || correspondence.UnmatchedAfter.IsDefault)
        {
            throw new ArgumentException("Issued correspondence arrays must be initialized.", nameof(correspondence));
        }

        var expected = IssueCorrespondence(correspondence.Before, correspondence.After);
        if (correspondence.Subject != expected.Subject
            || correspondence.BeforeRevision != expected.BeforeRevision
            || correspondence.AfterRevision != expected.AfterRevision
            || !correspondence.Matches.SequenceEqual(expected.Matches)
            || !correspondence.UnmatchedBefore.SequenceEqual(expected.UnmatchedBefore)
            || !correspondence.UnmatchedAfter.SequenceEqual(expected.UnmatchedAfter))
        {
            throw new ArgumentException(
                "Correspondence does not match the product-issued result for its exact document revisions.",
                nameof(correspondence));
        }
    }

    readonly record struct OriginSet(string Offsets)
    {
        public static OriginSet From(AnnotatedSourceNodeProvenance provenance)
            => new(string.Join(",", provenance.IlOffsets));
    }

    static void ValidateCSharpDocument(AnnotatedSourceDocument document, string parameterName)
    {
        if (document.Nodes.Any(static node => node.Medium != SourceLineKind.CSharp))
        {
            throw new ArgumentException(
                "Structural review requires a C#-only annotated-source document.",
                parameterName);
        }
    }

    static HashSet<int> ValidateSelection(
        IReadOnlyList<int> nodeIds,
        int nodeCount,
        string parameterName)
    {
        var selected = new HashSet<int>();
        foreach (int nodeId in nodeIds)
        {
            if (nodeId < 0 || nodeId >= nodeCount)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    nodeId,
                    $"Selected node id must be between 0 and {nodeCount - 1}.");
            }
            if (!selected.Add(nodeId))
            {
                throw new ArgumentException(
                    $"Node {nodeId} is selected more than once.",
                    parameterName);
            }
        }
        return selected;
    }

    static bool SelectedTextEqual(
        AnnotatedSourceDocument beforeDocument,
        AnnotatedSourceNode beforeNode,
        AnnotatedSourceDocument afterDocument,
        AnnotatedSourceNode afterNode)
    {
        if (beforeNode.Spans.Count != afterNode.Spans.Count)
            return false;

        for (int index = 0; index < beforeNode.Spans.Count; index++)
        {
            var beforeSpan = beforeNode.Spans[index];
            var afterSpan = afterNode.Spans[index];
            if (!beforeDocument.Text.AsSpan(beforeSpan.Start, beforeSpan.Length)
                .SequenceEqual(afterDocument.Text.AsSpan(afterSpan.Start, afterSpan.Length)))
            {
                return false;
            }
        }

        return true;
    }

    static CSharpStructuralDiffRow CreateRow(
        CSharpStructuralChangeKind change,
        AnnotatedSourceDocument? beforeDocument,
        AnnotatedSourceNode? beforeNode,
        AnnotatedSourceDocument? afterDocument,
        AnnotatedSourceNode? afterNode)
        => new(
            change,
            beforeNode?.Id,
            afterNode?.Id,
            beforeNode?.Kind,
            afterNode?.Kind,
            beforeNode is null ? null : AnnotatedSourceNodeKinds.GetDisplayLabel(beforeNode.Kind),
            afterNode is null ? null : AnnotatedSourceNodeKinds.GetDisplayLabel(afterNode.Kind),
            beforeNode is null ? null : EnclosingRegion(beforeDocument!, beforeNode),
            afterNode is null ? null : EnclosingRegion(afterDocument!, afterNode),
            beforeNode is null ? [] : [.. beforeNode.Spans],
            afterNode is null ? [] : [.. afterNode.Spans]);

    static PrintedRegionRole? EnclosingRegion(
        AnnotatedSourceDocument document,
        AnnotatedSourceNode node)
        => document.Regions
            .Where(region => ContainsAll(region.Spans, node.Spans))
            .OrderBy(static region => region.Spans.Sum(static span => (long)span.Length))
            .ThenBy(static region => region.Spans[0].Start)
            .ThenBy(static region => region.Role)
            .Select(static region => (PrintedRegionRole?)region.Role)
            .FirstOrDefault();

    static bool ContainsAll(
        IReadOnlyList<AnnotatedSourceSpan> containers,
        IReadOnlyList<AnnotatedSourceSpan> contained)
        => contained.All(span => containers.Any(container =>
            container.Start <= span.Start
            && span.Start - container.Start <= container.Length - span.Length));

    static int PrimaryStart(CSharpStructuralDiffRow row)
        => row.BeforeSpans.IsEmpty
            ? row.AfterSpans[0].Start
            : row.BeforeSpans[0].Start;
}
