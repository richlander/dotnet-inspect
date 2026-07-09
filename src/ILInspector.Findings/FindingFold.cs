using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>
/// Projects a <see cref="FindingMatch"/> plus the two atom streams into <see cref="PairFinding{T}"/>
/// transitions. This is the generic diff fold: it never decides equivalence, it materializes
/// classified pairs (Present / Added / Removed, with a <see cref="FindingDifferenceKind"/> facet).
/// Move <em>acceptance</em> is itself a fold: the default threshold (100) commits nothing from the
/// fringe, while a recall-hungry consumer can lower it to promote scored candidates into moves.
/// Subject and descriptor are read from the atoms, not threaded in — the thing you found already
/// carries them.
/// </summary>
public static class FindingFold
{
    public static ImmutableArray<PairFinding<T>> ToPairs<T>(
        FindingMatch match,
        IReadOnlyList<Finding<T>> oldStream,
        IReadOnlyList<Finding<T>> newStream,
        int acceptanceThreshold = 100)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentNullException.ThrowIfNull(newStream);

        var edges = ApplyAcceptance(match, acceptanceThreshold);
        var pairs = ImmutableArray.CreateBuilder<PairFinding<T>>(edges.Length);
        foreach (var edge in edges)
            pairs.Add(ToPair(edge, oldStream, newStream));

        return pairs.ToImmutable();
    }

    static ImmutableArray<FindingEdge> ApplyAcceptance(FindingMatch match, int threshold)
    {
        if (threshold > 99 || match.MoveCandidates.IsDefaultOrEmpty)
            return match.Edges;

        var usedOld = new HashSet<int>();
        var usedNew = new HashSet<int>();
        var accepted = new List<FindingMoveCandidate>();
        foreach (var candidate in match.MoveCandidates
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.OldIndex)
            .ThenBy(c => c.NewIndex))
        {
            if (candidate.Confidence < threshold)
                continue;
            if (!usedOld.Add(candidate.OldIndex))
                continue;
            if (!usedNew.Add(candidate.NewIndex))
            {
                usedOld.Remove(candidate.OldIndex);
                continue;
            }

            accepted.Add(candidate);
        }

        if (accepted.Count == 0)
            return match.Edges;

        var result = ImmutableArray.CreateBuilder<FindingEdge>();
        foreach (var edge in match.Edges)
        {
            if (edge.Kind == FindingEdgeKind.Removed && usedOld.Contains(edge.OldIndex))
                continue;
            if (edge.Kind == FindingEdgeKind.Added && usedNew.Contains(edge.NewIndex))
                continue;

            result.Add(edge);
        }

        foreach (var candidate in accepted)
            result.Add(new FindingEdge(FindingEdgeKind.Moved, candidate.OldIndex, candidate.NewIndex, candidate.Confidence));

        return result.ToImmutable();
    }

    static PairFinding<T> ToPair<T>(
        FindingEdge edge,
        IReadOnlyList<Finding<T>> oldStream,
        IReadOnlyList<Finding<T>> newStream)
        where T : notnull
    {
        switch (edge.Kind)
        {
            case FindingEdgeKind.Matched:
                return new PairFinding<T>(oldStream[edge.OldIndex], newStream[edge.NewIndex]);

            case FindingEdgeKind.Moved:
            {
                int delta = edge.NewIndex - edge.OldIndex;
                return new PairFinding<T>(
                    oldStream[edge.OldIndex],
                    newStream[edge.NewIndex],
                    FindingDifferenceKind.Moved,
                    Detail: $"moved {delta:+#;-#;0}");
            }

            case FindingEdgeKind.Added:
                return new PairFinding<T>(Old: null, New: newStream[edge.NewIndex]);

            case FindingEdgeKind.Removed:
                return new PairFinding<T>(Old: oldStream[edge.OldIndex], New: null);

            default:
                throw new ArgumentOutOfRangeException(nameof(edge), edge.Kind, "Unknown edge kind.");
        }
    }
}
