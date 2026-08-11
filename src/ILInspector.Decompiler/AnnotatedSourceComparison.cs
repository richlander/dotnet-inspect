using System.Collections.Immutable;
using ILInspector.Findings;

namespace ILInspector.Decompiler;

/// <summary>The structural relationship of one rendered-syntax node across two documents.</summary>
public enum AnnotatedSourceChangeKind
{
    Added,
    Removed,
    Changed,
    Moved,
}

/// <summary>A typed evidence item attached by an independent comparison producer.</summary>
public enum AnnotatedSourceComparisonEvidenceKind
{
    IlFidelity,
}

/// <summary>
/// Independent evidence that may accompany a structural change. Structural
/// node correspondence alone never creates fidelity evidence.
/// </summary>
public sealed record AnnotatedSourceComparisonEvidence(
    AnnotatedSourceComparisonEvidenceKind Kind,
    string Summary);

/// <summary>One document-local node projected into a cross-document comparison.</summary>
public sealed record AnnotatedSourceNodeSnapshot
{
    public AnnotatedSourceNodeSnapshot(
        int NodeId,
        string Kind,
        ImmutableArray<AnnotatedSourceSpan> Spans,
        string SelectedText,
        string RegionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Kind);
        if (Spans.IsDefaultOrEmpty)
            throw new ArgumentException("A compared node must carry at least one span.", nameof(Spans));

        this.NodeId = NodeId;
        this.Kind = Kind;
        this.Spans = Spans;
        this.SelectedText = SelectedText ?? throw new ArgumentNullException(nameof(SelectedText));
        this.RegionPath = RegionPath ?? throw new ArgumentNullException(nameof(RegionPath));
    }

    public int NodeId { get; }
    public string Kind { get; }
    public ImmutableArray<AnnotatedSourceSpan> Spans { get; }
    public string SelectedText { get; }
    public string RegionPath { get; }
}

/// <summary>One added, removed, changed, or moved rendered-syntax node.</summary>
public sealed record AnnotatedSourceNodeChange
{
    public AnnotatedSourceNodeChange(
        AnnotatedSourceChangeKind Kind,
        AnnotatedSourceNodeSnapshot? Before,
        AnnotatedSourceNodeSnapshot? After)
    {
        bool valid = Kind switch
        {
            AnnotatedSourceChangeKind.Added => Before is null && After is not null,
            AnnotatedSourceChangeKind.Removed => Before is not null && After is null,
            AnnotatedSourceChangeKind.Changed => Before is not null && After is not null,
            AnnotatedSourceChangeKind.Moved => Before is not null && After is not null
                && string.Equals(Before.Kind, After.Kind, StringComparison.Ordinal),
            _ => false,
        };
        if (!valid)
            throw new ArgumentException($"The node pair is not valid for {Kind}.");

        this.Kind = Kind;
        this.Before = Before;
        this.After = After;
    }

    public AnnotatedSourceChangeKind Kind { get; }
    public AnnotatedSourceNodeSnapshot? Before { get; }
    public AnnotatedSourceNodeSnapshot? After { get; }
    public ImmutableArray<AnnotatedSourceComparisonEvidence> Evidence { get; init; } = [];
}

/// <summary>
/// One shared structural comparison consumed by both full-body overlays and
/// rich-diff presentation.
/// </summary>
public sealed record AnnotatedSourceComparisonResult
{
    public AnnotatedSourceComparisonResult(
        AnnotatedSourceDocument Before,
        AnnotatedSourceDocument After,
        ImmutableArray<AnnotatedSourceNodeChange> Changes)
    {
        this.Before = Before ?? throw new ArgumentNullException(nameof(Before));
        this.After = After ?? throw new ArgumentNullException(nameof(After));
        if (Changes.IsDefault || Changes.Any(change => change is null))
            throw new ArgumentException("Changes must be initialized and contain no null values.", nameof(Changes));
        this.Changes = Changes;
    }

    public AnnotatedSourceDocument Before { get; }
    public AnnotatedSourceDocument After { get; }
    public ImmutableArray<AnnotatedSourceNodeChange> Changes { get; }
}

/// <summary>
/// Compares the rendered-syntax node streams of two C# annotated-source
/// documents. Node IDs remain document-local; correspondence is established
/// from stable kind identity, selected text, order, and structural context.
/// </summary>
public static class AnnotatedSourceComparer
{
    public static AnnotatedSourceComparisonResult Compare(
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        EnsureCSharp(before, nameof(before));
        EnsureCSharp(after, nameof(after));

        var beforeNodes = Snapshots(before);
        var afterNodes = Snapshots(after);
        var match = FindingMatcher.Match(
            beforeNodes.Select(Key),
            afterNodes.Select(Key),
            new FindingMatchOptions(MinMoveRunLength: 1));

        var pairedBefore = new bool[beforeNodes.Length];
        var pairedAfter = new bool[afterNodes.Length];
        var changes = ImmutableArray.CreateBuilder<AnnotatedSourceNodeChange>();

        var anchors = match.Edges
            .Where(edge => edge.Kind == FindingEdgeKind.Matched)
            .OrderBy(edge => edge.OldIndex)
            .ToArray();

        foreach (var edge in anchors)
        {
            pairedBefore[edge.OldIndex] = true;
            pairedAfter[edge.NewIndex] = true;
        }

        foreach (var edge in match.Edges.Where(edge => edge.Kind == FindingEdgeKind.Moved))
        {
            pairedBefore[edge.OldIndex] = true;
            pairedAfter[edge.NewIndex] = true;
            changes.Add(new AnnotatedSourceNodeChange(
                AnnotatedSourceChangeKind.Moved,
                beforeNodes[edge.OldIndex],
                afterNodes[edge.NewIndex]));
        }

        int previousBefore = -1;
        int previousAfter = -1;
        foreach (var anchor in anchors.Append(new FindingEdge(
            FindingEdgeKind.Matched,
            beforeNodes.Length,
            afterNodes.Length,
            100)))
        {
            PairSegment(
                beforeNodes,
                afterNodes,
                pairedBefore,
                pairedAfter,
                previousBefore + 1,
                anchor.OldIndex,
                previousAfter + 1,
                anchor.NewIndex,
                changes);
            previousBefore = anchor.OldIndex;
            previousAfter = anchor.NewIndex;
        }

        return new AnnotatedSourceComparisonResult(
            before,
            after,
            [.. changes.OrderBy(ChangeStart).ThenBy(change => change.Kind)]);
    }

    static void PairSegment(
        ImmutableArray<AnnotatedSourceNodeSnapshot> before,
        ImmutableArray<AnnotatedSourceNodeSnapshot> after,
        bool[] pairedBefore,
        bool[] pairedAfter,
        int beforeStart,
        int beforeEnd,
        int afterStart,
        int afterEnd,
        ImmutableArray<AnnotatedSourceNodeChange>.Builder changes)
    {
        int[] oldResidual = Enumerable.Range(beforeStart, beforeEnd - beforeStart)
            .Where(index => !pairedBefore[index])
            .ToArray();
        int[] newResidual = Enumerable.Range(afterStart, afterEnd - afterStart)
            .Where(index => !pairedAfter[index])
            .ToArray();

        var kindMatches = FindingMatcher.Match(
            oldResidual.Select(index => new FindingKey(before[index].Kind)),
            newResidual.Select(index => new FindingKey(after[index].Kind)),
            new FindingMatchOptions(MinMoveRunLength: int.MaxValue));
        foreach (var edge in kindMatches.Edges.Where(edge => edge.Kind == FindingEdgeKind.Matched))
        {
            pairedBefore[oldResidual[edge.OldIndex]] = true;
            pairedAfter[newResidual[edge.NewIndex]] = true;
        }

        oldResidual = oldResidual.Where(index => !pairedBefore[index]).ToArray();
        newResidual = newResidual.Where(index => !pairedAfter[index]).ToArray();

        int paired = Math.Min(oldResidual.Length, newResidual.Length);
        for (int i = 0; i < paired; i++)
        {
            pairedBefore[oldResidual[i]] = true;
            pairedAfter[newResidual[i]] = true;
            changes.Add(new AnnotatedSourceNodeChange(
                AnnotatedSourceChangeKind.Changed,
                before[oldResidual[i]],
                after[newResidual[i]]));
        }

        for (int i = paired; i < oldResidual.Length; i++)
        {
            pairedBefore[oldResidual[i]] = true;
            changes.Add(new AnnotatedSourceNodeChange(
                AnnotatedSourceChangeKind.Removed,
                before[oldResidual[i]],
                After: null));
        }

        for (int i = paired; i < newResidual.Length; i++)
        {
            pairedAfter[newResidual[i]] = true;
            changes.Add(new AnnotatedSourceNodeChange(
                AnnotatedSourceChangeKind.Added,
                Before: null,
                after[newResidual[i]]));
        }
    }

    static ImmutableArray<AnnotatedSourceNodeSnapshot> Snapshots(AnnotatedSourceDocument document)
        => [.. document.Nodes
            .OrderBy(node => node.Spans[0].Start)
            .ThenByDescending(node => node.Spans.Sum(span => span.Length))
            .ThenBy(node => node.Kind, StringComparer.Ordinal)
            .ThenBy(node => node.Id)
            .Select(node => Snapshot(document, node))];

    static AnnotatedSourceNodeSnapshot Snapshot(
        AnnotatedSourceDocument document,
        AnnotatedSourceNode node)
    {
        string selectedText = string.Concat(node.Spans.Select(
            span => document.Text.Substring(span.Start, span.Length)));
        string regionPath = string.Join(
            " > ",
            document.Regions
                .Where(region => Contains(region.Spans, node.Spans))
                .OrderByDescending(region => region.Spans.Sum(span => span.Length))
                .ThenBy(region => region.Spans[0].Start)
                .Select(region => region.Role.ToString()));
        return new AnnotatedSourceNodeSnapshot(
            node.Id,
            node.Kind,
            [.. node.Spans],
            selectedText,
            regionPath);
    }

    static bool Contains(
        IReadOnlyList<AnnotatedSourceSpan> regions,
        IReadOnlyList<AnnotatedSourceSpan> nodes)
        => nodes.All(node => regions.Any(region =>
            region.Start <= node.Start
            && region.Start + region.Length >= node.Start + node.Length));

    static FindingKey Key(AnnotatedSourceNodeSnapshot node)
        => new(
            $"{node.Kind.Length}:{node.Kind}{node.SelectedText}",
            node.RegionPath.Length == 0 ? null : node.RegionPath);

    static int ChangeStart(AnnotatedSourceNodeChange change)
        => change.Before?.Spans[0].Start ?? change.After!.Spans[0].Start;

    static void EnsureCSharp(AnnotatedSourceDocument document, string parameterName)
    {
        if (document.Nodes.Any(node => node.Medium != SourceLineKind.CSharp))
        {
            throw new ArgumentException(
                "Structural source comparison accepts C#-only annotated-source documents.",
                parameterName);
        }
    }
}
