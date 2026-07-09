using System.Collections.Immutable;

namespace ILInspector.Evidence;

/// <summary>
/// Projects a <see cref="EvidenceMatch"/> into evidence rows. This is the generic diff Fold:
/// it never decides equivalence, it materializes classified rows (Present / Added / Removed,
/// with a <see cref="EvidenceDifferenceKind"/> facet). Move <em>acceptance</em> is itself a
/// fold: the default threshold (100) commits nothing from the fringe, while a recall-hungry
/// consumer can lower it to promote scored candidates into moves.
/// </summary>
public static class EvidenceFold
{
    public static ImmutableArray<EvidenceRow> ToRows(
        EvidenceMatch correspondence,
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        EvidenceSubject subject,
        EvidenceDescriptor descriptor,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentNullException.ThrowIfNull(newStream);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(descriptor);

        var links = ApplyAcceptance(correspondence, acceptanceThreshold);
        var rows = ImmutableArray.CreateBuilder<EvidenceRow>(links.Length);
        foreach (var link in links)
            rows.Add(ToRow(link, oldStream, newStream, subject, descriptor));

        return rows.ToImmutable();
    }

    static ImmutableArray<EvidenceMatchEntry> ApplyAcceptance(EvidenceMatch correspondence, int threshold)
    {
        if (threshold > 99 || correspondence.MoveCandidates.IsDefaultOrEmpty)
            return correspondence.Entries;

        var usedOld = new HashSet<int>();
        var usedNew = new HashSet<int>();
        var accepted = new List<EvidenceMoveCandidate>();
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

        var result = ImmutableArray.CreateBuilder<EvidenceMatchEntry>();
        foreach (var link in correspondence.Entries)
        {
            if (link.Kind == EvidenceMatchKind.Removed && usedOld.Contains(link.OldIndex))
                continue;
            if (link.Kind == EvidenceMatchKind.Added && usedNew.Contains(link.NewIndex))
                continue;

            result.Add(link);
        }

        foreach (var candidate in accepted)
            result.Add(new EvidenceMatchEntry(EvidenceMatchKind.Moved, candidate.OldIndex, candidate.NewIndex, candidate.Confidence));

        return result.ToImmutable();
    }

    static EvidenceRow ToRow(
        EvidenceMatchEntry link,
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        EvidenceSubject subject,
        EvidenceDescriptor descriptor)
    {
        switch (link.Kind)
        {
            case EvidenceMatchKind.Matched:
            {
                var oldOcc = oldStream[link.OldIndex];
                var newOcc = newStream[link.NewIndex];
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidenceRowKind.Present,
                    new EvidenceAnchor(oldOcc.IdentityKey, link.OldIndex, link.NewIndex, oldOcc.ScopeKey),
                    EvidenceDifferenceKind.None,
                    Confidence: link.Confidence,
                    Payload: newOcc.Payload);
            }

            case EvidenceMatchKind.Moved:
            {
                var oldOcc = oldStream[link.OldIndex];
                var newOcc = newStream[link.NewIndex];
                int delta = link.NewIndex - link.OldIndex;
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidenceRowKind.Present,
                    new EvidenceAnchor(oldOcc.IdentityKey, link.OldIndex, link.NewIndex, oldOcc.ScopeKey),
                    EvidenceDifferenceKind.Moved,
                    Confidence: link.Confidence,
                    Detail: $"moved {delta:+#;-#;0}",
                    Payload: newOcc.Payload);
            }

            case EvidenceMatchKind.Added:
            {
                var newOcc = newStream[link.NewIndex];
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidenceRowKind.Added,
                    new EvidenceAnchor(newOcc.IdentityKey, -1, link.NewIndex, newOcc.ScopeKey),
                    EvidenceDifferenceKind.None,
                    Confidence: link.Confidence,
                    Payload: newOcc.Payload);
            }

            case EvidenceMatchKind.Removed:
            {
                var oldOcc = oldStream[link.OldIndex];
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidenceRowKind.Removed,
                    new EvidenceAnchor(oldOcc.IdentityKey, link.OldIndex, -1, oldOcc.ScopeKey),
                    EvidenceDifferenceKind.None,
                    Confidence: link.Confidence,
                    Payload: oldOcc.Payload);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(link), link.Kind, "Unknown link kind.");
        }
    }
}
