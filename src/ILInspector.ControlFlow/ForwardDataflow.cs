namespace ILInspector.ControlFlow;

/// <summary>How predecessor out-sets merge into a block's in-set.</summary>
public enum DataflowMerge
{
    /// <summary>May analysis: a fact is present when any predecessor carries it.</summary>
    Union,

    /// <summary>Must analysis: a fact is present only when every predecessor carries it.</summary>
    Intersection,
}

/// <summary>How the entry block's in-set handles CFG predecessors.</summary>
public enum DataflowEntry
{
    /// <summary>The entry block's in-set is exactly the external entry facts.</summary>
    Fixed,

    /// <summary>The entry block also joins reachable predecessor out-sets, useful for general loops.</summary>
    MergePredecessors,
}

/// <summary>A block-local gen/kill transfer function over integer fact ids.</summary>
public sealed record GenKillSet(IReadOnlySet<int> Gen, IReadOnlySet<int> Kill)
{
    public static readonly GenKillSet Empty = new(new HashSet<int>(), new HashSet<int>());
}

/// <summary>Dataflow state for one block.</summary>
public sealed record DataflowBlockState(
    IReadOnlyList<int> Predecessors,
    IReadOnlySet<int> In,
    IReadOnlySet<int> Out,
    bool Reachable);

/// <summary>Result of a forward dataflow fixpoint over one block container.</summary>
public sealed record ForwardDataflowResult(IReadOnlyList<DataflowBlockState> Blocks);

/// <summary>
/// Representation-agnostic forward gen/kill dataflow over <see cref="BlockEdges"/>.
/// Consumers supply their own fact ids and transfer sets; the kernel owns only
/// CFG predecessor construction, merge, transfer, and fixpoint iteration.
/// </summary>
public static class ForwardDataflow
{
    /// <summary>
    /// Solves a forward gen/kill fixpoint with block 0 as the entry block.
    /// For intersection analyses, non-entry blocks start at <paramref name="universe"/>;
    /// for union analyses, they start empty. External targets/leaves are represented in
    /// <see cref="BlockEdges"/> for callers to handle before invoking the kernel.
    /// </summary>
    public static ForwardDataflowResult Solve(
        IReadOnlyList<BlockEdges> edges,
        IReadOnlyList<GenKillSet> transfers,
        IReadOnlySet<int> entry,
        IReadOnlySet<int> universe,
        DataflowMerge merge,
        DataflowEntry entryBehavior = DataflowEntry.Fixed)
    {
        if (edges.Count != transfers.Count)
            throw new ArgumentException("Edge and transfer counts must match.", nameof(transfers));

        // Intersection (must) analyses join the entry block with its back-edge
        // predecessors under MergePredecessors, which can silently drop a fact
        // that holds on entry but is killed around a loop. That is sound but
        // imprecise (it floods toward "no fact"), and no consumer needs it:
        // must-entry facts are external axioms, so must-analyses use Fixed.
        // Reject the pair so a future consumer cannot adopt it unknowingly.
        if (merge == DataflowMerge.Intersection && entryBehavior == DataflowEntry.MergePredecessors)
            throw new ArgumentException(
                "Intersection (must) analyses must use DataflowEntry.Fixed; MergePredecessors can drop entry facts across back-edges.",
                nameof(entryBehavior));

        int n = edges.Count;
        if (n == 0)
            return new ForwardDataflowResult([]);

        var predecessors = BuildPredecessors(edges);
        var reachable = ComputeReachable(edges);

        var inSets = new HashSet<int>[n];
        var outSets = new HashSet<int>[n];
        inSets[0] = new HashSet<int>(entry);
        outSets[0] = Apply(inSets[0], transfers[0]);

        for (int i = 1; i < n; i++)
        {
            inSets[i] = merge == DataflowMerge.Intersection
                ? new HashSet<int>(universe)
                : [];
            outSets[i] = Apply(inSets[i], transfers[i]);
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int block = entryBehavior == DataflowEntry.Fixed ? 1 : 0; block < n; block++)
            {
                if (predecessors[block].Count == 0 && block != 0)
                    continue;
                if (block != 0 && !predecessors[block].Any(predecessor => reachable[predecessor]))
                    continue;

                var merged = MergePredecessors(predecessors[block], outSets, reachable, merge, seed: block == 0 ? entry : null);
                if (!merged.SetEquals(inSets[block]))
                {
                    inSets[block] = merged;
                    outSets[block] = Apply(merged, transfers[block]);
                    changed = true;
                }
            }
        }

        var states = new DataflowBlockState[n];
        for (int i = 0; i < n; i++)
        {
            states[i] = new DataflowBlockState(
                predecessors[i],
                new HashSet<int>(inSets[i]),
                new HashSet<int>(outSets[i]),
                reachable[i]);
        }
        return new ForwardDataflowResult(states);
    }

    static IReadOnlyList<int>[] BuildPredecessors(IReadOnlyList<BlockEdges> edges)
    {
        int n = edges.Count;
        var predecessors = new List<int>[n];
        for (int i = 0; i < n; i++)
            predecessors[i] = [];
        for (int from = 0; from < n; from++)
        {
            foreach (int to in edges[from].Successors)
            {
                if ((uint)to >= (uint)n)
                    throw new ArgumentOutOfRangeException(nameof(edges), $"Successor {to} from block {from} is outside the block range.");
                predecessors[to].Add(from);
            }
        }
        return predecessors;
    }

    static bool[] ComputeReachable(IReadOnlyList<BlockEdges> edges)
    {
        int n = edges.Count;
        var reachable = new bool[n];
        var stack = new Stack<int>();
        reachable[0] = true;
        stack.Push(0);
        while (stack.Count > 0)
        {
            int block = stack.Pop();
            foreach (int successor in edges[block].Successors)
            {
                if (!reachable[successor])
                {
                    reachable[successor] = true;
                    stack.Push(successor);
                }
            }
        }
        return reachable;
    }

    static HashSet<int> MergePredecessors(
        IReadOnlyList<int> predecessors,
        HashSet<int>[] outSets,
        IReadOnlyList<bool> reachable,
        DataflowMerge merge,
        IReadOnlySet<int>? seed)
    {
        if (merge == DataflowMerge.Union)
        {
            var result = seed is null ? new HashSet<int>() : new HashSet<int>(seed);
            foreach (int predecessor in predecessors)
                if (reachable[predecessor])
                    result.UnionWith(outSets[predecessor]);
            return result;
        }

        HashSet<int>? intersection = seed is null ? null : new HashSet<int>(seed);
        foreach (int predecessor in predecessors)
        {
            if (!reachable[predecessor])
                continue;
            if (intersection is null)
                intersection = new HashSet<int>(outSets[predecessor]);
            else
                intersection.IntersectWith(outSets[predecessor]);
        }
        return intersection ?? [];
    }

    static HashSet<int> Apply(IReadOnlySet<int> input, GenKillSet transfer)
    {
        var output = new HashSet<int>(input);
        output.ExceptWith(transfer.Kill);
        output.UnionWith(transfer.Gen);
        return output;
    }
}
