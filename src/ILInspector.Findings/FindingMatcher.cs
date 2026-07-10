using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>How an old/new occurrence pair (or singleton) is related across versions.</summary>
public enum FindingEdgeKind
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
/// One alignment edge. <see cref="OldIndex"/> is -1 for <see cref="FindingEdgeKind.Added"/>;
/// <see cref="NewIndex"/> is -1 for <see cref="FindingEdgeKind.Removed"/>. <see cref="Confidence"/>
/// is 100 for the committed matching; lower values are reserved for accepted fringe.
/// </summary>
public sealed record FindingEdge(FindingEdgeKind Kind, int OldIndex, int NewIndex, int Confidence);

/// <summary>
/// A deferred, ambiguous match the committed core did not commit (a content-equal
/// singleton that lacks a corroborating run). Acceptance is a <em>consumer</em> decision:
/// <see cref="FindingFold"/> promotes candidates whose <see cref="Confidence"/> meets a caller
/// threshold. The default (100) accepts none, so these stay Added+Removed.
/// </summary>
public sealed record FindingMoveCandidate(int OldIndex, int NewIndex, int Confidence, string Reason);

/// <summary>The result of matching two key streams (see FindingKey).</summary>
/// <param name="Edges">
/// The committed, conservative interpretation: order-preserving matches, committed moves,
/// and Added/Removed for everything else (including the endpoints of every fringe candidate).
/// </param>
/// <param name="MoveCandidates">
/// Scored move candidates a recall-hungry consumer may accept to reclassify an Added+Removed
/// pair into a move. Empty when nothing is ambiguous.
/// </param>
public sealed record FindingMatch(
    ImmutableArray<FindingEdge> Edges,
    ImmutableArray<FindingMoveCandidate> MoveCandidates);

/// <summary>
/// Whether a finding stream's order carries meaning. The choice is per <see cref="FindingMatcher.Match"/>
/// invocation and per occurrence level — the same producer can be ordered at one level and a set at another.
/// </summary>
public enum FindingMatchMode
{
    /// <summary>Order is semantic (an IL/C# body); use the LCS committer plus the scored move pass.</summary>
    Ordered,

    /// <summary>
    /// Order is not semantic (an unordered set such as a type's members or an API surface); commit an
    /// identity-key bijection by multiset (hash buckets, O(N), no matrix). A set has no position, so
    /// there are no moves and no scored fringe. Matching considers <see cref="FindingKey.IdentityKey"/>
    /// only; <see cref="FindingKey.ScopeKey"/> is not consulted at this rung.
    /// </summary>
    IdentitySet,
}

/// <summary>Tunables for <see cref="FindingMatcher.Match"/>.</summary>
/// <param name="MinMoveRunLength">
/// The minimum length of a common contiguous residual run to commit as a move. Runs shorter
/// than this are left to the fringe, which is what gives the conservative default its
/// mismatch resistance (a lone content-equal occurrence is not silently treated as a move).
/// Applies only to <see cref="FindingMatchMode.Ordered"/> matching.
/// </param>
public sealed record FindingMatchOptions(int MinMoveRunLength = 2)
{
    /// <summary>
    /// Whether the streams are ordered or an unordered identity set. An <c>init</c> property (not a
    /// second positional parameter) so adding it keeps the record's <c>(int)</c> constructor and
    /// <c>Deconstruct</c> — no source or binary break for existing callers.
    /// </summary>
    public FindingMatchMode MatchMode { get; init; } = FindingMatchMode.Ordered;

    public static readonly FindingMatchOptions Default = new();
}

/// <summary>
/// The single, domain-free matcher every finding stream shares. For <see cref="FindingMatchMode.Ordered"/>
/// streams it commits an order-preserving LCS core (which reproduces a classic sequence diff when there are
/// no moves) and then recovers relocations as a scored move pass over the residual; for
/// <see cref="FindingMatchMode.IdentitySet"/> streams it commits an identity-key bijection by multiset with
/// no notion of order (and no matrix, so it scales past the ordered cell cap). It never inspects a payload and
/// never decides equivalence: it emits a classified alignment (a set of edges), and a consumer
/// <see cref="FindingFold"/> folds it.
/// </summary>
public static class FindingMatcher
{
    // The ordered committer is a full-matrix LCS (O(N*M) space). That is ample for method-body-scale
    // ordered streams (the only thing shipped today), but a caller feeding assembly-scale streams
    // would otherwise silently attempt a multi-gigabyte allocation. Guard the matrix with a documented
    // cell cap and fail loudly instead of OOMing. Large/unordered streams belong on the identity-set
    // committer (hash bijection, no matrix) — see issue #2585.
    /// <summary>The maximum dynamic-programming matrix cells accepted by ordered matching.</summary>
    public static readonly long MaxOrderedMatchCells = 64_000_000;

    public static FindingMatch Match(
        IEnumerable<FindingKey> oldStream,
        IEnumerable<FindingKey> newStream,
        FindingMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldStream);
        ArgumentNullException.ThrowIfNull(newStream);
        options ??= FindingMatchOptions.Default;

        // Both committers need the counts and (for ordered) two-pass random access, so materialize
        // once to a concrete FindingKey[]: the ordered LCS hot loop then indexes an array directly
        // (no interface dispatch), and the set committer streams it into its buckets.
        var oldKeys = oldStream as FindingKey[] ?? oldStream.ToArray();
        var newKeys = newStream as FindingKey[] ?? newStream.ToArray();
        ValidateKeys(oldKeys, nameof(oldStream));
        ValidateKeys(newKeys, nameof(newStream));

        return options.MatchMode == FindingMatchMode.IdentitySet
            ? MatchIdentitySet(oldKeys, newKeys)
            : MatchOrdered(oldKeys, newKeys, options);
    }

    static void ValidateKeys(FindingKey[] keys, string parameterName)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].IdentityKey is null)
            {
                throw new ArgumentException(
                    $"Finding key at index {i} must be initialized.",
                    parameterName);
            }
        }
    }

    // Order-free committer: pair occurrences by identity-key equality via hash buckets, deterministic
    // by stream order (the i-th occurrence of a key on the old side pairs with the i-th on the new).
    // A set has no position, so there are no moves and no scored fringe — and no O(N*M) matrix, so it
    // needs no cell cap and scales to assembly-size member/API-surface sets. A relocation only appears
    // at this rung once a soft tier drops a facet (e.g. declaring type) from the identity key (#2585).
    static FindingMatch MatchIdentitySet(FindingKey[] oldKeys, FindingKey[] newKeys)
    {
        var newByKey = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (int j = 0; j < newKeys.Length; j++)
            Bucket(newByKey, newKeys[j].IdentityKey).Enqueue(j);

        var matchedNew = new bool[newKeys.Length];
        var edges = ImmutableArray.CreateBuilder<FindingEdge>();

        for (int i = 0; i < oldKeys.Length; i++)
        {
            string key = oldKeys[i].IdentityKey;
            var queue = newByKey.TryGetValue(key, out var q) ? q : null;
            if (queue is { Count: > 0 })
            {
                int j = queue.Dequeue();
                matchedNew[j] = true;
                edges.Add(new FindingEdge(FindingEdgeKind.Matched, i, j, 100));
            }
            else
            {
                edges.Add(new FindingEdge(FindingEdgeKind.Removed, i, -1, 100));
            }
        }

        for (int j = 0; j < newKeys.Length; j++)
        {
            if (!matchedNew[j])
                edges.Add(new FindingEdge(FindingEdgeKind.Added, -1, j, 100));
        }

        return new FindingMatch(edges.ToImmutable(), ImmutableArray<FindingMoveCandidate>.Empty);

        static Queue<int> Bucket(Dictionary<string, Queue<int>> byKey, string key)
        {
            if (!byKey.TryGetValue(key, out var queue))
                byKey[key] = queue = new Queue<int>();
            return queue;
        }
    }

    static FindingMatch MatchOrdered(FindingKey[] oldKeys, FindingKey[] newKeys, FindingMatchOptions options)
    {
        long cells = ((long)oldKeys.Length + 1) * ((long)newKeys.Length + 1);
        if (cells > MaxOrderedMatchCells)
        {
            throw new ArgumentException(
                $"Ordered matching is bounded to {MaxOrderedMatchCells:N0} matrix cells " +
                $"({oldKeys.Length}x{newKeys.Length} requested). Streams this large need the " +
                "identity-set committer (FindingMatchMode.IdentitySet), not the ordered LCS (see issue #2585).");
        }

        var matchedOld = new bool[oldKeys.Length];
        var matchedNew = new bool[newKeys.Length];
        var edges = ImmutableArray.CreateBuilder<FindingEdge>();

        // 1. Committed core: order-preserving LCS over content keys.
        foreach (var (oldIndex, newIndex) in LongestCommonSubsequence(oldKeys, newKeys))
        {
            matchedOld[oldIndex] = true;
            matchedNew[newIndex] = true;
            edges.Add(new FindingEdge(FindingEdgeKind.Matched, oldIndex, newIndex, 100));
        }

        // 2. Residual (what the order-preserving core could not match).
        var residualOld = ResidualIndices(matchedOld);
        var residualNew = ResidualIndices(matchedNew);

        // 3. Move pass: commit maximal common contiguous residual runs (>= MinMoveRunLength).
        var committedOld = new bool[residualOld.Length];
        var committedNew = new bool[residualNew.Length];
        DetectMoveRuns(oldKeys, newKeys, residualOld, residualNew, committedOld, committedNew, options.MinMoveRunLength, edges);

        // 4. Leftover residual: score singleton content matches as fringe, emit the rest as add/remove.
        var fringe = BuildFringe(oldKeys, newKeys, residualOld, residualNew, committedOld, committedNew);

        foreach (var (localIndex, oldIndex) in Indexed(residualOld))
        {
            if (!committedOld[localIndex])
                edges.Add(new FindingEdge(FindingEdgeKind.Removed, oldIndex, -1, 100));
        }

        foreach (var (localIndex, newIndex) in Indexed(residualNew))
        {
            if (!committedNew[localIndex])
                edges.Add(new FindingEdge(FindingEdgeKind.Added, -1, newIndex, 100));
        }

        return new FindingMatch(edges.ToImmutable(), fringe);
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
        FindingKey[] oldStream,
        FindingKey[] newStream,
        int[] residualOld,
        int[] residualNew,
        bool[] committedOld,
        bool[] committedNew,
        int minRun,
        ImmutableArray<FindingEdge>.Builder edges)
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
                edges.Add(new FindingEdge(FindingEdgeKind.Moved, residualOld[bestA + k], residualNew[bestB + k], 100));
            }
        }
    }

    // Emit the FULL scored candidate graph: every content-equal pair of still-uncommitted residual
    // occurrences. Deferring the actual one-to-one resolution to FindingFold.ApplyAcceptance (which
    // sorts by score and enforces the matching constraint) is deliberate — it lets scope-corroborated
    // (higher-scored) pairings win over incidental index-order pairings. This is the "scored fringe"
    // half of committed-core-plus-scored-fringe.
    static ImmutableArray<FindingMoveCandidate> BuildFringe(
        FindingKey[] oldStream,
        FindingKey[] newStream,
        int[] residualOld,
        int[] residualNew,
        bool[] committedOld,
        bool[] committedNew)
    {
        var fringe = ImmutableArray.CreateBuilder<FindingMoveCandidate>();

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

                fringe.Add(new FindingMoveCandidate(residualOld[a], residualNew[b], score, reason));
            }
        }

        return fringe.ToImmutable();
    }

    // Same construction and tiebreak as ILInspector.Instructions.IlBodyDiff so the committed
    // core reproduces the existing IL sequence diff exactly on move-free inputs.
    static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(
        FindingKey[] oldStream,
        FindingKey[] newStream)
    {
        int oldLength = oldStream.Length;
        int newLength = newStream.Length;
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
