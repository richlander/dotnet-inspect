using System.Collections.Immutable;

namespace ILInspector.Evidence;

/// <summary>
/// Projects a <see cref="Correspondence"/> into evidence rows. This is the generic diff Fold:
/// it never decides equivalence, it materializes classified rows (Present / Added / Removed,
/// with a <see cref="EvidenceDifferenceClass"/> facet). Move <em>acceptance</em> is itself a
/// fold: the default threshold (100) commits nothing from the fringe, while a recall-hungry
/// consumer can lower it to promote scored candidates into moves.
/// </summary>
public static class EvidenceFold
{
    public static ImmutableArray<EvidenceRow> ToRows(
        Correspondence correspondence,
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

    static ImmutableArray<EvidenceLink> ApplyAcceptance(Correspondence correspondence, int threshold)
    {
        if (threshold > 99 || correspondence.Fringe.IsDefaultOrEmpty)
            return correspondence.Links;

        var usedOld = new HashSet<int>();
        var usedNew = new HashSet<int>();
        var accepted = new List<EvidenceMoveCandidate>();
        foreach (var candidate in correspondence.Fringe
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.OldIndex)
            .ThenBy(c => c.NewIndex))
        {
            if (candidate.Score < threshold)
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
            return correspondence.Links;

        var result = ImmutableArray.CreateBuilder<EvidenceLink>();
        foreach (var link in correspondence.Links)
        {
            if (link.Kind == EvidenceLinkKind.Removed && usedOld.Contains(link.OldIndex))
                continue;
            if (link.Kind == EvidenceLinkKind.Added && usedNew.Contains(link.NewIndex))
                continue;

            result.Add(link);
        }

        foreach (var candidate in accepted)
            result.Add(new EvidenceLink(EvidenceLinkKind.Moved, candidate.OldIndex, candidate.NewIndex, candidate.Score));

        return result.ToImmutable();
    }

    static EvidenceRow ToRow(
        EvidenceLink link,
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        EvidenceSubject subject,
        EvidenceDescriptor descriptor)
    {
        switch (link.Kind)
        {
            case EvidenceLinkKind.Matched:
            {
                var oldOcc = oldStream[link.OldIndex];
                var newOcc = newStream[link.NewIndex];
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidencePolarity.Present,
                    new EvidenceAnchor(oldOcc.ContentKey, link.OldIndex, link.NewIndex, oldOcc.ScopeKey),
                    EvidenceDifferenceClass.None,
                    Payload: newOcc.Payload);
            }

            case EvidenceLinkKind.Moved:
            {
                var oldOcc = oldStream[link.OldIndex];
                var newOcc = newStream[link.NewIndex];
                int delta = link.NewIndex - link.OldIndex;
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidencePolarity.Present,
                    new EvidenceAnchor(oldOcc.ContentKey, link.OldIndex, link.NewIndex, oldOcc.ScopeKey),
                    EvidenceDifferenceClass.Moved,
                    Detail: $"moved {delta:+#;-#;0}",
                    Payload: newOcc.Payload);
            }

            case EvidenceLinkKind.Added:
            {
                var newOcc = newStream[link.NewIndex];
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidencePolarity.Added,
                    new EvidenceAnchor(newOcc.ContentKey, -1, link.NewIndex, newOcc.ScopeKey),
                    EvidenceDifferenceClass.None,
                    Payload: newOcc.Payload);
            }

            case EvidenceLinkKind.Removed:
            {
                var oldOcc = oldStream[link.OldIndex];
                return new EvidenceRow(
                    subject,
                    descriptor,
                    EvidencePolarity.Removed,
                    new EvidenceAnchor(oldOcc.ContentKey, link.OldIndex, -1, oldOcc.ScopeKey),
                    EvidenceDifferenceClass.None,
                    Payload: oldOcc.Payload);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(link), link.Kind, "Unknown link kind.");
        }
    }
}
