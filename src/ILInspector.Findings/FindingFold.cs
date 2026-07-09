using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>
/// Projects a <see cref="FindingMatch"/> into finding rows. This is the generic diff Fold:
/// it never decides equivalence, it materializes classified rows (Present / Added / Removed,
/// with a <see cref="FindingDifferenceKind"/> facet). Move <em>acceptance</em> is itself a
/// fold: the default threshold (100) commits nothing from the fringe, while a recall-hungry
/// consumer can lower it to promote scored candidates into moves.
/// </summary>
public static class FindingFold
{
    public static ImmutableArray<Finding<T>> ToRows<T>(
        FindingMatch match,
        IReadOnlyList<FindingOccurrence<T>> oldStream,
        IReadOnlyList<FindingOccurrence<T>> newStream,
        FindingSubject subject,
        FindingDescriptor descriptor,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentNullException.ThrowIfNull(newStream);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(descriptor);

        var edges = ApplyAcceptance(match, acceptanceThreshold);
        var rows = ImmutableArray.CreateBuilder<Finding<T>>(edges.Length);
        foreach (var edge in edges)
            rows.Add(ToRow(edge, oldStream, newStream, subject, descriptor));

        return rows.ToImmutable();
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

    static Finding<T> ToRow<T>(
        FindingEdge edge,
        IReadOnlyList<FindingOccurrence<T>> oldStream,
        IReadOnlyList<FindingOccurrence<T>> newStream,
        FindingSubject subject,
        FindingDescriptor descriptor)
    {
        switch (edge.Kind)
        {
            case FindingEdgeKind.Matched:
            {
                var oldOcc = oldStream[edge.OldIndex];
                var newOcc = newStream[edge.NewIndex];
                return new Finding<T>(
                    subject,
                    descriptor,
                    FindingKind.Present,
                    new FindingAnchor(oldOcc.IdentityKey, edge.OldIndex, edge.NewIndex, oldOcc.ScopeKey),
                    FindingDifferenceKind.None,
                    Payload: newOcc.Payload);
            }

            case FindingEdgeKind.Moved:
            {
                var oldOcc = oldStream[edge.OldIndex];
                var newOcc = newStream[edge.NewIndex];
                int delta = edge.NewIndex - edge.OldIndex;
                return new Finding<T>(
                    subject,
                    descriptor,
                    FindingKind.Present,
                    new FindingAnchor(oldOcc.IdentityKey, edge.OldIndex, edge.NewIndex, oldOcc.ScopeKey),
                    FindingDifferenceKind.Moved,
                    Detail: $"moved {delta:+#;-#;0}",
                    Payload: newOcc.Payload);
            }

            case FindingEdgeKind.Added:
            {
                var newOcc = newStream[edge.NewIndex];
                return new Finding<T>(
                    subject,
                    descriptor,
                    FindingKind.Added,
                    new FindingAnchor(newOcc.IdentityKey, -1, edge.NewIndex, newOcc.ScopeKey),
                    FindingDifferenceKind.None,
                    Payload: newOcc.Payload);
            }

            case FindingEdgeKind.Removed:
            {
                var oldOcc = oldStream[edge.OldIndex];
                return new Finding<T>(
                    subject,
                    descriptor,
                    FindingKind.Removed,
                    new FindingAnchor(oldOcc.IdentityKey, edge.OldIndex, -1, oldOcc.ScopeKey),
                    FindingDifferenceKind.None,
                    Payload: oldOcc.Payload);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(edge), edge.Kind, "Unknown edge kind.");
        }
    }
}
