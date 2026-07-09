using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>
/// A consumer-selected equivalence relation over a diff, expressed as data (an allow-list of pair
/// kinds and difference classes) rather than a hard-coded differ verdict. Two streams are
/// "equivalent for this consumer" iff every transition falls inside the allow-list. This is the
/// mechanism behind "equivalence is a fold": the differ emits classified pairs once, and each
/// consumer picks which classes it forgives.
/// </summary>
public sealed record FindingEquivalence(
    ImmutableHashSet<PairKind> AllowedKinds,
    ImmutableHashSet<FindingDifferenceKind> AllowedDifferenceKinds)
{
    public bool IsEquivalent(IReadOnlyList<IPairFinding> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        foreach (var pair in pairs)
        {
            if (!AllowedKinds.Contains(pair.Kind))
                return false;
            if (!AllowedDifferenceKinds.Contains(pair.Difference))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Byte/behavior fidelity: only unchanged content counts as equal, and only incidental
    /// encoding is folded. A move is a real difference (order is semantic for IL), so a
    /// reordered body is not exact.
    /// </summary>
    public static readonly FindingEquivalence Exact = new(
        [PairKind.Present],
        [FindingDifferenceKind.None, FindingDifferenceKind.EncodingOnly]);

    /// <summary>
    /// "Same operations, order aside": moves are forgiven, but additions and removals are not.
    /// Appropriate for a consumer that only cares whether the multiset of operations changed.
    /// </summary>
    public static readonly FindingEquivalence Multiset = new(
        [PairKind.Present],
        [FindingDifferenceKind.None, FindingDifferenceKind.EncodingOnly, FindingDifferenceKind.Moved]);
}
