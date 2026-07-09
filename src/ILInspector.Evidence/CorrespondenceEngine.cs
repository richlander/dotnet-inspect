using System.Collections.Immutable;

namespace ILInspector.Evidence;

/// <summary>How an old/new occurrence pair (or singleton) is related across versions.</summary>
public enum EvidenceLinkKind
{
    /// <summary>Order-preserving content match found by the committed LCS core.</summary>
    Matched,

    /// <summary>Content match recovered out of order by the move pass (a relocation).</summary>
    Moved,

    /// <summary>Present only on the new side.</summary>
    Added,

    /// <summary>Present only on the old side.</summary>
    Removed,
}

/// <summary>
/// One correspondence edge. <see cref="OldIndex"/> is -1 for <see cref="EvidenceLinkKind.Added"/>;
/// <see cref="NewIndex"/> is -1 for <see cref="EvidenceLinkKind.Removed"/>. <see cref="Confidence"/>
/// is 100 for the committed matching; lower values are reserved for accepted fringe.
/// </summary>
public sealed record EvidenceLink(EvidenceLinkKind Kind, int OldIndex, int NewIndex, int Confidence);

/// <summary>
/// A deferred, ambiguous correspondence the committed core did not commit (a content-equal
/// singleton that lacks a corroborating run). Acceptance is a <em>consumer</em> decision:
/// <see cref="EvidenceFold"/> promotes candidates whose <see cref="Score"/> meets a caller
/// threshold. The default (100) accepts none, so these stay Added+Removed.
/// </summary>
public sealed record EvidenceMoveCandidate(int OldIndex, int NewIndex, int Score, string Reason);

/// <summary>The result of matching two occurrence streams.</summary>
/// <param name="Links">
/// The committed, conservative interpretation: order-preserving matches, committed moves,
/// and Added/Removed for everything else (including the endpoints of every fringe candidate).
/// </param>
/// <param name="Fringe">
/// Scored move candidates a recall-hungry consumer may accept to reclassify an Added+Removed
/// pair into a move. Empty when nothing is ambiguous.
/// </param>
public sealed record Correspondence(
    ImmutableArray<EvidenceLink> Links,
    ImmutableArray<EvidenceMoveCandidate> Fringe);

/// <summary>Tunables for <see cref="CorrespondenceEngine.Match"/>.</summary>
/// <param name="MinMoveRun">
/// The minimum length of a common contiguous residual run to commit as a move. Runs shorter
/// than this are left to the fringe, which is what gives the conservative default its
/// mismatch resistance (a lone content-equal occurrence is not silently treated as a move).
/// </param>
public sealed record CorrespondenceOptions(int MinMoveRun = 2)
{
    public static readonly CorrespondenceOptions Default = new();
}

/// <summary>
/// The single, domain-free correspondence engine every evidence stream shares. It commits an
/// order-preserving LCS core (which reproduces a classic sequence diff when there are no moves)
/// and then recovers relocations as a scored move pass over the residual. It never decides
/// equivalence: it emits classified correspondence, and a consumer <see cref="EvidenceFold"/>
/// folds it.
/// </summary>
public static class CorrespondenceEngine
{
    public static Correspondence Match(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        CorrespondenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentNullException.ThrowIfNull(newStream);
        options ??= CorrespondenceOptions.Default;

        var matchedOld = new bool[oldStream.Count];
        var matchedNew = new bool[newStream.Count];
        var links = ImmutableArray.CreateBuilder<EvidenceLink>();

        // 1. Committed core: order-preserving LCS over content keys.
        foreach (var (oldIndex, newIndex) in LongestCommonSubsequence(oldStream, newStream))
        {
            matchedOld[oldIndex] = true;
            matchedNew[newIndex] = true;
            links.Add(new EvidenceLink(EvidenceLinkKind.Matched, oldIndex, newIndex, 100));
        }

        // 2. Residual (what the order-preserving core could not match).
        var residualOld = ResidualIndices(matchedOld);
        var residualNew = ResidualIndices(matchedNew);

        // 3. Move pass: commit maximal common contiguous residual runs (>= MinMoveRun).
        var committedOld = new bool[residualOld.Length];
        var committedNew = new bool[residualNew.Length];
        DetectMoveRuns(oldStream, newStream, residualOld, residualNew, committedOld, committedNew, options.MinMoveRun, links);

        // 4. Leftover residual: score singleton content matches as fringe, emit the rest as add/remove.
        var fringe = BuildFringe(oldStream, newStream, residualOld, residualNew, committedOld, committedNew);

        foreach (var (localIndex, oldIndex) in Indexed(residualOld))
        {
            if (!committedOld[localIndex])
                links.Add(new EvidenceLink(EvidenceLinkKind.Removed, oldIndex, -1, 100));
        }

        foreach (var (localIndex, newIndex) in Indexed(residualNew))
        {
            if (!committedNew[localIndex])
                links.Add(new EvidenceLink(EvidenceLinkKind.Added, -1, newIndex, 100));
        }

        return new Correspondence(links.ToImmutable(), fringe);
    }

    static int[] ResidualIndices(bool[] matched)
    {
        var result = new List<int>();
        for (int i = 0; i < matched.Length; i++)
        {
            if (!matched[i])
                result.Add(i);
        }

        return [.. result];
    }

    static IEnumerable<(int Local, int Value)> Indexed(int[] values)
    {
        for (int i = 0; i < values.Length; i++)
            yield return (i, values[i]);
    }

    static void DetectMoveRuns(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        int[] residualOld,
        int[] residualNew,
        bool[] committedOld,
        bool[] committedNew,
        int minRun,
        ImmutableArray<EvidenceLink>.Builder links)
    {
        if (minRun < 1)
            minRun = 1;

        while (true)
        {
            int bestLen = 0;
            int bestA = -1;
            int bestB = -1;

            for (int a = 0; a < residualOld.Length; a++)
            {
                if (committedOld[a])
                    continue;

                for (int b = 0; b < residualNew.Length; b++)
                {
                    if (committedNew[b])
                        continue;
                    if (oldStream[residualOld[a]].ContentKey != newStream[residualNew[b]].ContentKey)
                        continue;

                    int len = 0;
                    while (a + len < residualOld.Length
                        && b + len < residualNew.Length
                        && !committedOld[a + len]
                        && !committedNew[b + len]
                        && oldStream[residualOld[a + len]].ContentKey == newStream[residualNew[b + len]].ContentKey)
                    {
                        len++;
                    }

                    // Longest wins; deterministic tiebreak on the smallest (a, b).
                    if (len > bestLen || (len == bestLen && (a < bestA || (a == bestA && b < bestB))))
                    {
                        bestLen = len;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (bestLen < minRun || bestA < 0)
                return;

            for (int k = 0; k < bestLen; k++)
            {
                committedOld[bestA + k] = true;
                committedNew[bestB + k] = true;
                links.Add(new EvidenceLink(EvidenceLinkKind.Moved, residualOld[bestA + k], residualNew[bestB + k], 100));
            }
        }
    }

    static ImmutableArray<EvidenceMoveCandidate> BuildFringe(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        int[] residualOld,
        int[] residualNew,
        bool[] committedOld,
        bool[] committedNew)
    {
        var fringe = ImmutableArray.CreateBuilder<EvidenceMoveCandidate>();
        var takenNew = new bool[residualNew.Length];

        for (int a = 0; a < residualOld.Length; a++)
        {
            if (committedOld[a])
                continue;

            for (int b = 0; b < residualNew.Length; b++)
            {
                if (committedNew[b] || takenNew[b])
                    continue;
                if (oldStream[residualOld[a]].ContentKey != newStream[residualNew[b]].ContentKey)
                    continue;

                string? oldScope = oldStream[residualOld[a]].ScopeKey;
                string? newScope = newStream[residualNew[b]].ScopeKey;
                bool scopeCorroborates = oldScope is not null && oldScope == newScope;
                int score = scopeCorroborates ? 75 : 50;
                string reason = scopeCorroborates ? "content+scope" : "content-only";

                fringe.Add(new EvidenceMoveCandidate(residualOld[a], residualNew[b], score, reason));
                takenNew[b] = true;
                break;
            }
        }

        return fringe.ToImmutable();
    }

    // Same construction and tiebreak as ILInspector.Instructions.IlBodyDiff so the committed
    // core reproduces the existing IL sequence diff exactly on move-free inputs.
    static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream)
    {
        int oldLength = oldStream.Count;
        int newLength = newStream.Count;
        var lengths = new int[oldLength + 1, newLength + 1];
        for (int oldIndex = oldLength - 1; oldIndex >= 0; oldIndex--)
        {
            for (int newIndex = newLength - 1; newIndex >= 0; newIndex--)
            {
                lengths[oldIndex, newIndex] = oldStream[oldIndex].ContentKey == newStream[newIndex].ContentKey
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        var pairs = new List<(int OldIndex, int NewIndex)>();
        int i = 0;
        int j = 0;
        while (i < oldLength && j < newLength)
        {
            if (oldStream[i].ContentKey == newStream[j].ContentKey)
            {
                pairs.Add((i, j));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }
}
