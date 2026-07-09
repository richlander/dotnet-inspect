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
    public static ImmutableArray<Finding> ToRows(
        FindingMatch correspondence,
        IReadOnlyList<FindingOccurrence> oldStream,
        IReadOnlyList<FindingOccurrence> newStream,
        FindingSubject subject,
        FindingDescriptor descriptor,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentNullException.ThrowIfNull(newStream);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(descriptor);

        var links = ApplyAcceptance(correspondence, acceptanceThreshold);
        var rows = ImmutableArray.CreateBuilder<Finding>(links.Length);
        foreach (var link in links)
            rows.Add(ToRow(link, oldStream, newStream, subject, descriptor));

        return rows.ToImmutable();
    }

    static ImmutableArray<FindingMatchEntry> ApplyAcceptance(FindingMatch correspondence, int threshold)
    {
        if (threshold > 99 || correspondence.MoveCandidates.IsDefaultOrEmpty)
            return correspondence.Entries;

        var usedOld = new HashSet<int>();
        var usedNew = new HashSet<int>();
        var accepted = new List<FindingMoveCandidate>();
        foreach (var candidate in correspondence.MoveCandidates
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
            return correspondence.Entries;

        var result = ImmutableArray.CreateBuilder<FindingMatchEntry>();
        foreach (var link in correspondence.Entries)
        {
            if (link.Kind == FindingMatchKind.Removed && usedOld.Contains(link.OldIndex))
                continue;
            if (link.Kind == FindingMatchKind.Added && usedNew.Contains(link.NewIndex))
                continue;

            result.Add(link);
        }

        foreach (var candidate in accepted)
            result.Add(new FindingMatchEntry(FindingMatchKind.Moved, candidate.OldIndex, candidate.NewIndex, candidate.Confidence));

        return result.ToImmutable();
    }

    static Finding ToRow(
        FindingMatchEntry link,
        IReadOnlyList<FindingOccurrence> oldStream,
        IReadOnlyList<FindingOccurrence> newStream,
        FindingSubject subject,
        FindingDescriptor descriptor)
    {
        switch (link.Kind)
        {
            case FindingMatchKind.Matched:
            {
                var oldOcc = oldStream[link.OldIndex];
                var newOcc = newStream[link.NewIndex];
                return new Finding(
                    subject,
                    descriptor,
                    FindingKind.Present,
                    new FindingAnchor(oldOcc.IdentityKey, link.OldIndex, link.NewIndex, oldOcc.ScopeKey),
                    FindingDifferenceKind.None,
                    Payload: newOcc.Payload);
            }

            case FindingMatchKind.Moved:
            {
                var oldOcc = oldStream[link.OldIndex];
                var newOcc = newStream[link.NewIndex];
                int delta = link.NewIndex - link.OldIndex;
                return new Finding(
                    subject,
                    descriptor,
                    FindingKind.Present,
                    new FindingAnchor(oldOcc.IdentityKey, link.OldIndex, link.NewIndex, oldOcc.ScopeKey),
                    FindingDifferenceKind.Moved,
                    Detail: $"moved {delta:+#;-#;0}",
                    Payload: newOcc.Payload);
            }

            case FindingMatchKind.Added:
            {
                var newOcc = newStream[link.NewIndex];
                return new Finding(
                    subject,
                    descriptor,
                    FindingKind.Added,
                    new FindingAnchor(newOcc.IdentityKey, -1, link.NewIndex, newOcc.ScopeKey),
                    FindingDifferenceKind.None,
                    Payload: newOcc.Payload);
            }

            case FindingMatchKind.Removed:
            {
                var oldOcc = oldStream[link.OldIndex];
                return new Finding(
                    subject,
                    descriptor,
                    FindingKind.Removed,
                    new FindingAnchor(oldOcc.IdentityKey, link.OldIndex, -1, oldOcc.ScopeKey),
                    FindingDifferenceKind.None,
                    Payload: oldOcc.Payload);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(link), link.Kind, "Unknown link kind.");
        }
    }
}
