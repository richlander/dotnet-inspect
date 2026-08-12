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
    internal string RegionFingerprint { get; init; } = "";
    internal string RegionOrdinalPath { get; init; } = "";
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

    /// <summary>
    /// The projected C# document whose text and node IDs define every
    /// <see cref="AnnotatedSourceNodeChange.Before"/> coordinate.
    /// </summary>
    public AnnotatedSourceDocument Before { get; }

    /// <summary>
    /// The projected C# document whose text and node IDs define every
    /// <see cref="AnnotatedSourceNodeChange.After"/> coordinate.
    /// </summary>
    public AnnotatedSourceDocument After { get; }
    public ImmutableArray<AnnotatedSourceNodeChange> Changes { get; }
}

/// <summary>
/// Compares the C# rendered-syntax node streams of two annotated-source
/// documents. Interleaved IL is projected out before matching. Node IDs remain
/// document-local; correspondence is established from stable kind identity,
/// selected text, order, and structural context.
/// </summary>
public static class AnnotatedSourceComparer
{
    public static AnnotatedSourceComparisonResult Compare(
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        before = AnnotatedSourceDocumentProjection.CSharpOnly(before);
        after = AnnotatedSourceDocumentProjection.CSharpOnly(after);

        var beforeNodes = Snapshots(before);
        var afterNodes = Snapshots(after);
        var (beforeKeys, afterKeys) = Keys(beforeNodes, afterNodes, includeText: true);
        var match = FindingMatcher.Match(
            beforeKeys,
            afterKeys,
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

        var oldNodes = oldResidual.Select(index => before[index]).ToImmutableArray();
        var newNodes = newResidual.Select(index => after[index]).ToImmutableArray();
        var (oldKeys, newKeys) = Keys(oldNodes, newNodes, includeText: false);
        var kindMatches = FindingMatcher.Match(
            oldKeys,
            newKeys,
            new FindingMatchOptions(MinMoveRunLength: int.MaxValue));
        foreach (var edge in kindMatches.Edges.Where(edge => edge.Kind == FindingEdgeKind.Matched))
        {
            int oldIndex = oldResidual[edge.OldIndex];
            int newIndex = newResidual[edge.NewIndex];
            pairedBefore[oldIndex] = true;
            pairedAfter[newIndex] = true;
            changes.Add(new AnnotatedSourceNodeChange(
                AnnotatedSourceChangeKind.Changed,
                before[oldIndex],
                after[newIndex]));
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
    {
        var regions = DescribeRegions(document);
        return [.. document.Nodes
            .OrderBy(node => node.Spans[0].Start)
            .ThenByDescending(node => node.Spans.Sum(span => span.Length))
            .ThenBy(node => node.Kind, StringComparer.Ordinal)
            .ThenBy(node => node.Id)
            .Select(node => Snapshot(document, node, regions))];
    }

    static AnnotatedSourceNodeSnapshot Snapshot(
        AnnotatedSourceDocument document,
        AnnotatedSourceNode node,
        ImmutableArray<RegionDescriptor> regions)
    {
        string selectedText = string.Concat(node.Spans.Select(
            span => document.Text.Substring(span.Start, span.Length)));
        RegionDescriptor[] containing =
        [
            .. regions.Where(region => Contains(region.Region.Spans, node.Spans)),
        ];
        string regionPath = string.Join(
            " > ",
            containing.Select(region => region.Region.Role.ToString()));
        var snapshot = new AnnotatedSourceNodeSnapshot(
            node.Id,
            node.Kind,
            [.. node.Spans],
            selectedText,
            regionPath);
        if (containing.Length == 0)
            return snapshot;

        var innermost = containing[^1];
        return snapshot with
        {
            RegionFingerprint = TextExcept(
                document.Text,
                innermost.Region.Spans,
                node.Spans),
            RegionOrdinalPath = string.Join(
                " > ",
                containing.Select(region =>
                    $"{region.Region.Role}[{region.SiblingOrdinal}]")),
        };
    }

    static bool Contains(
        IReadOnlyList<AnnotatedSourceSpan> regions,
        IReadOnlyList<AnnotatedSourceSpan> nodes)
        => nodes.All(node => regions.Any(region =>
            region.Start <= node.Start
            && region.Start + region.Length >= node.Start + node.Length));

    static (
        ImmutableArray<FindingKey> Before,
        ImmutableArray<FindingKey> After) Keys(
            ImmutableArray<AnnotatedSourceNodeSnapshot> before,
            ImmutableArray<AnnotatedSourceNodeSnapshot> after,
            bool includeText)
    {
        string Base(AnnotatedSourceNodeSnapshot node)
            => Part(node.Kind)
                + (includeText ? Part(node.SelectedText) : "")
                + Part(node.RegionPath);

        string[] oldIdentities = [.. before.Select(Base)];
        string[] newIdentities = [.. after.Select(Base)];
        (oldIdentities, newIdentities) = EnrichSafely(
            oldIdentities,
            newIdentities,
            before,
            after,
            node => node.RegionFingerprint);
        (oldIdentities, newIdentities) = EnrichSafely(
            oldIdentities,
            newIdentities,
            before,
            after,
            node => node.RegionOrdinalPath);

        FindingKey Key(AnnotatedSourceNodeSnapshot node, string identity)
            => new(
                identity,
                node.RegionPath.Length == 0 ? null : node.RegionPath);

        return (
            [.. before.Select((node, index) => Key(node, oldIdentities[index]))],
            [.. after.Select((node, index) => Key(node, newIdentities[index]))]);
    }

    static (string[] Before, string[] After) EnrichSafely(
        string[] beforeIdentities,
        string[] afterIdentities,
        ImmutableArray<AnnotatedSourceNodeSnapshot> before,
        ImmutableArray<AnnotatedSourceNodeSnapshot> after,
        Func<AnnotatedSourceNodeSnapshot, string> discriminator)
    {
        var identities = beforeIdentities
            .Concat(afterIdentities)
            .Distinct(StringComparer.Ordinal);
        var enrichable = new HashSet<string>(StringComparer.Ordinal);
        foreach (string identity in identities)
        {
            int[] oldIndices = Enumerable.Range(0, beforeIdentities.Length)
                .Where(index => beforeIdentities[index] == identity)
                .ToArray();
            int[] newIndices = Enumerable.Range(0, afterIdentities.Length)
                .Where(index => afterIdentities[index] == identity)
                .ToArray();
            if (oldIndices.Length <= 1 && newIndices.Length <= 1)
                continue;

            int pairCapacity = oldIndices
                .Select(index => discriminator(before[index]))
                .Concat(newIndices.Select(index => discriminator(after[index])))
                .Distinct(StringComparer.Ordinal)
                .Sum(value => Math.Min(
                    oldIndices.Count(index => discriminator(before[index]) == value),
                    newIndices.Count(index => discriminator(after[index]) == value)));
            if (pairCapacity == Math.Min(oldIndices.Length, newIndices.Length))
                enrichable.Add(identity);
        }

        string Enrich(
            string identity,
            AnnotatedSourceNodeSnapshot node)
            => enrichable.Contains(identity)
                ? identity + Part(discriminator(node))
                : identity;

        return (
            [.. before.Select((node, index) => Enrich(beforeIdentities[index], node))],
            [.. after.Select((node, index) => Enrich(afterIdentities[index], node))]);
    }

    static string Part(string value) => $"{value.Length}:{value}";

    static ImmutableArray<RegionDescriptor> DescribeRegions(AnnotatedSourceDocument document)
    {
        var ordered = document.Regions
            .Select((region, index) => new
            {
                Region = region,
                OriginalIndex = index,
                Length = region.Spans.Sum(span => span.Length),
                Start = region.Spans[0].Start,
            })
            .OrderByDescending(item => item.Length)
            .ThenBy(item => item.Start)
            .ThenBy(item => item.Region.Role)
            .ThenBy(item => item.OriginalIndex)
            .ToArray();
        var descriptors = ImmutableArray.CreateBuilder<RegionDescriptor>(ordered.Length);
        foreach (var item in ordered)
        {
            int parent = -1;
            int parentLength = int.MaxValue;
            for (int index = 0; index < descriptors.Count; index++)
            {
                var candidate = descriptors[index];
                int candidateLength = candidate.Region.Spans.Sum(span => span.Length);
                if (candidateLength < parentLength
                    && !candidate.Region.Spans.SequenceEqual(item.Region.Spans)
                    && Contains(candidate.Region.Spans, item.Region.Spans))
                {
                    parent = index;
                    parentLength = candidateLength;
                }
            }

            descriptors.Add(new RegionDescriptor(item.Region, parent, SiblingOrdinal: 0));
        }
        var result = ImmutableArray.CreateBuilder<RegionDescriptor>(descriptors.Count);
        for (int index = 0; index < descriptors.Count; index++)
        {
            var descriptor = descriptors[index];
            int start = descriptor.Region.Spans[0].Start;
            int siblingOrdinal = descriptors
                .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                .Count(item =>
                    item.candidate.Parent == descriptor.Parent
                    && item.candidate.Region.Role == descriptor.Region.Role
                    && (item.candidate.Region.Spans[0].Start < start
                        || item.candidate.Region.Spans[0].Start == start
                            && item.candidateIndex < index));
            result.Add(descriptor with { SiblingOrdinal = siblingOrdinal });
        }
        return result.ToImmutable();
    }

    static string TextExcept(
        string text,
        IReadOnlyList<AnnotatedSourceSpan> source,
        IReadOnlyList<AnnotatedSourceSpan> excluded)
    {
        var result = new System.Text.StringBuilder();
        foreach (var sourceSpan in source)
        {
            int cursor = sourceSpan.Start;
            int sourceEnd = sourceSpan.Start + sourceSpan.Length;
            foreach (var excludedSpan in excluded)
            {
                int excludedEnd = excludedSpan.Start + excludedSpan.Length;
                if (excludedEnd <= cursor)
                    continue;
                if (excludedSpan.Start >= sourceEnd)
                    break;

                int through = Math.Min(excludedSpan.Start, sourceEnd);
                if (cursor < through)
                    result.Append(text, cursor, through - cursor);
                cursor = Math.Max(cursor, Math.Min(excludedEnd, sourceEnd));
            }
            if (cursor < sourceEnd)
                result.Append(text, cursor, sourceEnd - cursor);
        }
        return result.ToString();
    }

    static int ChangeStart(AnnotatedSourceNodeChange change)
        => change.Before?.Spans[0].Start ?? change.After!.Spans[0].Start;

    sealed record RegionDescriptor(
        AnnotatedSourceRegion Region,
        int Parent,
        int SiblingOrdinal);
}
