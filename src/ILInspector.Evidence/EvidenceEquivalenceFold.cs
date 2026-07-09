using System.Collections.Immutable;

namespace ILInspector.Evidence;

/// <summary>
/// A consumer-selected equivalence relation over a diff, expressed as data (an allow-list of
/// polarities and difference classes) rather than a hard-coded differ verdict. Two streams are
/// "equivalent for this consumer" iff every row falls inside the allow-list. This is the
/// mechanism behind "equivalence is a fold": the differ emits classified rows once, and each
/// consumer picks which classes it forgives.
/// </summary>
public sealed record EvidenceEquivalenceFold(
    ImmutableHashSet<EvidencePolarity> EquivalentPolarities,
    ImmutableHashSet<EvidenceDifferenceClass> EquivalentClasses)
{
    public bool IsEquivalent(IReadOnlyList<EvidenceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            if (!EquivalentPolarities.Contains(row.Polarity))
                return false;
            if (!EquivalentClasses.Contains(row.Difference))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Byte/behavior fidelity: only unchanged content counts as equal, and only incidental
    /// encoding is folded. A move is a real difference (order is semantic for IL), so a
    /// reordered body is not exact.
    /// </summary>
    public static readonly EvidenceEquivalenceFold Exact = new(
        [EvidencePolarity.Present],
        [EvidenceDifferenceClass.None, EvidenceDifferenceClass.EncodingOnly]);

    /// <summary>
    /// "Same operations, order aside": moves are forgiven, but additions and removals are not.
    /// Appropriate for a consumer that only cares whether the multiset of operations changed.
    /// </summary>
    public static readonly EvidenceEquivalenceFold Multiset = new(
        [EvidencePolarity.Present],
        [EvidenceDifferenceClass.None, EvidenceDifferenceClass.EncodingOnly, EvidenceDifferenceClass.Moved]);
}
