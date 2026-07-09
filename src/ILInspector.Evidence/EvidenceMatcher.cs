using System.Collections.Immutable;

namespace ILInspector.Evidence;

/// <summary>How an old/new occurrence pair (or singleton) is related across versions.</summary>
public enum EvidenceMatchKind
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
/// One correspondence edge. <see cref="OldIndex"/> is -1 for <see cref="EvidenceMatchKind.Added"/>;
/// <see cref="NewIndex"/> is -1 for <see cref="EvidenceMatchKind.Removed"/>. <see cref="Confidence"/>
/// is 100 for the committed matching; lower values are reserved for accepted fringe.
/// </summary>
public sealed record EvidenceMatchEntry(EvidenceMatchKind Kind, int OldIndex, int NewIndex, int Confidence);

/// <summary>
/// A deferred, ambiguous correspondence the committed core did not commit (a content-equal
/// singleton that lacks a corroborating run). Acceptance is a <em>consumer</em> decision:
/// <see cref="EvidenceFold"/> promotes candidates whose <see cref="Confidence"/> meets a caller
/// threshold. The default (100) accepts none, so these stay Added+Removed.
/// </summary>
public sealed record EvidenceMoveCandidate(int OldIndex, int NewIndex, int Confidence, string Reason);

/// <summary>The result of matching two occurrence streams.</summary>
/// <param name="Entries">
/// The committed, conservative interpretation: order-preserving matches, committed moves,
/// and Added/Removed for everything else (including the endpoints of every fringe candidate).
/// </param>
/// <param name="MoveCandidates">
/// Scored move candidates a recall-hungry consumer may accept to reclassify an Added+Removed
/// pair into a move. Empty when nothing is ambiguous.
/// </param>
public sealed record EvidenceMatch(
    ImmutableArray<EvidenceMatchEntry> Entries,
    ImmutableArray<EvidenceMoveCandidate> MoveCandidates);

/// <summary>
/// Whether a stream's order is semantic. <see cref="Ordered"/> (IL instructions, C# statements) uses
/// the LCS + move committer. <see cref="IdentitySet"/> (an unordered set such as a type's members)
/// bypasses order entirely and commits an identity-key bijection. The choice is per match invocation
/// and per occurrence level — the same producer can be ordered at one level and a set at another
/// (C# statements vs a type's member list).
/// </summary>
public enum EvidenceStreamKind
{
    /// <summary>Order is semantic; use the LCS committer + move pass.</summary>
    Ordered,

    /// <summary>Order is not semantic; commit an identity-key bijection (hash buckets, O(N)).</summary>
    IdentitySet,
}

/// <summary>Tunables for <see cref="EvidenceMatcher.Match"/>.</summary>
/// <param name="StreamKind">Whether the streams are ordered or an unordered identity set.</param>
/// <param name="MinMoveRunLength">
/// The minimum length of a common contiguous residual run to commit as a move. Runs shorter
/// than this are left to the fringe, which is what gives the conservative default its
/// mismatch resistance (a lone content-equal occurrence is not silently treated as a move).
/// Applies only to <see cref="EvidenceStreamKind.Ordered"/> matching.
/// </param>
public sealed record EvidenceMatchOptions(
    EvidenceStreamKind StreamKind = EvidenceStreamKind.Ordered,
    int MinMoveRunLength = 2)
{
    public static readonly EvidenceMatchOptions Default = new();
}

/// <summary>
/// The single, domain-free matcher every evidence stream shares. For <see cref="EvidenceStreamKind.Ordered"/>
/// streams it commits an order-preserving LCS core (reproducing a classic sequence diff when there are no
/// moves) and recovers relocations with a scored move pass; for <see cref="EvidenceStreamKind.IdentitySet"/>
/// streams it commits an identity-key bijection with no notion of order. It never decides equivalence: it
/// emits classified correspondence, and a consumer <see cref="EvidenceFold"/> folds it.
/// </summary>
public static class EvidenceMatcher
{
    // The ordered committer is a full-matrix LCS (O(N*M) space). That is ample for method-body-scale
    // ordered streams (the only thing shipped today), but a caller feeding assembly-scale streams
    // would otherwise silently attempt a multi-gigabyte allocation. Guard the matrix with a documented
    // cell cap and fail loudly instead of OOMing. Large/unordered streams belong on the identity-set
    // committer (hash bijection, no matrix) — see issue #2585.
    const long MaxOrderedMatchCells = 64_000_000;

    public static EvidenceMatch Match(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        EvidenceMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentNullException.ThrowIfNull(newStream);
        options ??= EvidenceMatchOptions.Default;

        return options.StreamKind == EvidenceStreamKind.IdentitySet
            ? MatchIdentitySet(oldStream, newStream)
            : MatchOrdered(oldStream, newStream, options);
    }

    // Order-free committer: pair occurrences by identity-key equality (hash buckets, deterministic by
    // index within a bucket), leftover old -> Removed, leftover new -> Added. O(N) time and space, so
    // it needs no matrix cell cap and scales to assembly-size member sets. There is no positional move
    // at this rung — a set has no position; a relocation only appears once a soft tier drops a facet
    // (e.g. declaring type) from the identity key (see #2585).
    static EvidenceMatch MatchIdentitySet(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream)
    {
        var newByKey = new Dictionary<string, Queue<int>>();
        for (int j = 0; j < newStream.Count; j++)
        {
            if (!newByKey.TryGetValue(newStream[j].IdentityKey, out var queue))
                newByKey[newStream[j].IdentityKey] = queue = new Queue<int>();
            queue.Enqueue(j);
        }

        var matchedNew = new bool[newStream.Count];
        var links = ImmutableArray.CreateBuilder<EvidenceMatchEntry>();

        for (int i = 0; i < oldStream.Count; i++)
        {
            if (newByKey.TryGetValue(oldStream[i].IdentityKey, out var queue) && queue.Count > 0)
            {
                int j = queue.Dequeue();
                matchedNew[j] = true;
                links.Add(new EvidenceMatchEntry(EvidenceMatchKind.Matched, i, j, 100));
            }
            else
            {
                links.Add(new EvidenceMatchEntry(EvidenceMatchKind.Removed, i, -1, 100));
            }
        }

        for (int j = 0; j < newStream.Count; j++)
        {
            if (!matchedNew[j])
                links.Add(new EvidenceMatchEntry(EvidenceMatchKind.Added, -1, j, 100));
        }

        return new EvidenceMatch(links.ToImmutable(), ImmutableArray<EvidenceMoveCandidate>.Empty);
    }

    static EvidenceMatch MatchOrdered(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        EvidenceMatchOptions options)
    {
        long cells = ((long)oldStream.Count + 1) * ((long)newStream.Count + 1);
        if (cells > MaxOrderedMatchCells)
        {
            throw new ArgumentException(
                $"Ordered matching is bounded to {MaxOrderedMatchCells:N0} matrix cells " +
                $"({oldStream.Count}x{newStream.Count} requested). Streams this large need the " +
                "identity-set committer, not the ordered LCS (see issue #2585).");
        }

        var matchedOld = new bool[oldStream.Count];
        var matchedNew = new bool[newStream.Count];
        var links = ImmutableArray.CreateBuilder<EvidenceMatchEntry>();

        // 1. Committed core: order-preserving LCS over content keys.
        foreach (var (oldIndex, newIndex) in LongestCommonSubsequence(oldStream, newStream))
        {
            matchedOld[oldIndex] = true;
            matchedNew[newIndex] = true;
            links.Add(new EvidenceMatchEntry(EvidenceMatchKind.Matched, oldIndex, newIndex, 100));
        }

        // 2. Residual (what the order-preserving core could not match).
        var residualOld = ResidualIndices(matchedOld);
        var residualNew = ResidualIndices(matchedNew);

        // 3. Move pass: commit maximal common contiguous residual runs (>= MinMoveRunLength).
        var committedOld = new bool[residualOld.Length];
        var committedNew = new bool[residualNew.Length];
        DetectMoveRuns(oldStream, newStream, residualOld, residualNew, committedOld, committedNew, options.MinMoveRunLength, links);

        // 4. Leftover residual: score singleton content matches as fringe, emit the rest as add/remove.
        var fringe = BuildFringe(oldStream, newStream, residualOld, residualNew, committedOld, committedNew);

        foreach (var (localIndex, oldIndex) in Indexed(residualOld))
        {
            if (!committedOld[localIndex])
                links.Add(new EvidenceMatchEntry(EvidenceMatchKind.Removed, oldIndex, -1, 100));
        }

        foreach (var (localIndex, newIndex) in Indexed(residualNew))
        {
            if (!committedNew[localIndex])
                links.Add(new EvidenceMatchEntry(EvidenceMatchKind.Added, -1, newIndex, 100));
        }

        return new EvidenceMatch(links.ToImmutable(), fringe);
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
        ImmutableArray<EvidenceMatchEntry>.Builder links)
    {
        // A committed move is a run of >= minRun operations that is contiguous in BOTH original
        // streams (a relocated block), not merely adjacent in the residual arrays. Requiring
        // original-stream contiguity is what keeps coincidental singleton relocations that happen
        // to be residual-adjacent (separated by matched anchors in the real stream) out of the
        // committed set and in the scored fringe instead.
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
                    if (oldStream[residualOld[a]].IdentityKey != newStream[residualNew[b]].IdentityKey)
                        continue;

                    int len = 0;
                    while (a + len < residualOld.Length
                        && b + len < residualNew.Length
                        && !committedOld[a + len]
                        && !committedNew[b + len]
                        && oldStream[residualOld[a + len]].IdentityKey == newStream[residualNew[b + len]].IdentityKey
                        && (len == 0
                            || (residualOld[a + len] == residualOld[a + len - 1] + 1
                                && residualNew[b + len] == residualNew[b + len - 1] + 1)))
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
                links.Add(new EvidenceMatchEntry(EvidenceMatchKind.Moved, residualOld[bestA + k], residualNew[bestB + k], 100));
            }
        }
    }

    // Emit the FULL scored candidate graph: every content-equal pair of still-uncommitted residual
    // occurrences. Deferring the actual one-to-one resolution to EvidenceFold.ApplyAcceptance (which
    // sorts by score and enforces the matching constraint) is deliberate — it lets scope-corroborated
    // (higher-scored) pairings win over incidental index-order pairings. This is the "scored fringe"
    // half of committed-core-plus-scored-fringe.
    static ImmutableArray<EvidenceMoveCandidate> BuildFringe(
        IReadOnlyList<EvidenceOccurrence> oldStream,
        IReadOnlyList<EvidenceOccurrence> newStream,
        int[] residualOld,
        int[] residualNew,
        bool[] committedOld,
        bool[] committedNew)
    {
        var fringe = ImmutableArray.CreateBuilder<EvidenceMoveCandidate>();

        for (int a = 0; a < residualOld.Length; a++)
        {
            if (committedOld[a])
                continue;

            for (int b = 0; b < residualNew.Length; b++)
            {
                if (committedNew[b])
                    continue;
                if (oldStream[residualOld[a]].IdentityKey != newStream[residualNew[b]].IdentityKey)
                    continue;

                string? oldScope = oldStream[residualOld[a]].ScopeKey;
                string? newScope = newStream[residualNew[b]].ScopeKey;
                bool scopeCorroborates = oldScope is not null && oldScope == newScope;
                int score = scopeCorroborates ? 75 : 50;
                string reason = scopeCorroborates ? "content+scope" : "content-only";

                fringe.Add(new EvidenceMoveCandidate(residualOld[a], residualNew[b], score, reason));
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
                lengths[oldIndex, newIndex] = oldStream[oldIndex].IdentityKey == newStream[newIndex].IdentityKey
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        var pairs = new List<(int OldIndex, int NewIndex)>();
        int i = 0;
        int j = 0;
        while (i < oldLength && j < newLength)
        {
            if (oldStream[i].IdentityKey == newStream[j].IdentityKey)
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
