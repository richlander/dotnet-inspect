using System.Collections.Immutable;
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
    CSharpStructuralFidelityEvidence? Fidelity = null)
{
    /// <summary>Whether the selected structure is unchanged.</summary>
    public bool IsExact => Rows.IsEmpty;
}

public static partial class CSharpBodyDiff
{
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
