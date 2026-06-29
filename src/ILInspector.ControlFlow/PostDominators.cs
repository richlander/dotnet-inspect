namespace ILInspector.ControlFlow;

/// <summary>
/// Immediate post-dominators over one container's block CFG (dual of a dominator tree),
/// from the Cooper-Harvey-Kennedy iterative fixpoint on the reverse CFG with a single virtual
/// exit. Pure analysis over <see cref="BlockEdges"/>; produce dominators by passing reversed
/// edges. Shared kernel: no dependency on any IL representation.
/// </summary>
public sealed class PostDominators
{
    /// <summary>The immediate post-dominator is the method exit (no real block post-dominates the query).</summary>
    public const int VirtualExit = -1;

    /// <summary>No post-dominator: the block cannot reach the exit, so the relation is undefined.</summary>
    public const int None = -2;

    readonly int[] _idom;       // index by block; value is a block index, or _exit, or undefined
    readonly int _exit;         // the virtual-exit node id (== block count)
    readonly int _undefined;    // sentinel for "not yet / not computable" (== block count + 1)

    PostDominators(int[] idom, int exit, int undefined)
    {
        _idom = idom;
        _exit = exit;
        _undefined = undefined;
    }

    /// <summary>The number of real blocks the analysis covers.</summary>
    public int Count => _exit;

    /// <summary>
    /// The immediate post-dominator of <paramref name="block"/>: a block index, or
    /// <see cref="VirtualExit"/> when the closest post-dominator is the method exit,
    /// or <see cref="None"/> when the block cannot reach the exit.
    /// </summary>
    public int ImmediatePostDominator(int block)
    {
        int idom = _idom[block];
        if (idom == _undefined)
            return None;
        return idom == _exit ? VirtualExit : idom;
    }

    /// <summary>
    /// True when <paramref name="postDominator"/> post-dominates
    /// <paramref name="block"/> (walking the immediate-post-dominator chain from
    /// <paramref name="block"/> reaches it before the exit). A block post-dominates
    /// itself.
    ///
    /// <para><paramref name="postDominator"/> must be a real block index: the chain
    /// only ever holds real blocks, so the <see cref="VirtualExit"/> and
    /// <see cref="None"/> sentinels always return false. A caller asking "does the
    /// method exit post-dominate b?" (always true) or handling an unreachable block
    /// must branch on those sentinels itself before calling this.</para>
    /// </summary>
    public bool PostDominates(int postDominator, int block)
    {
        for (int cursor = block; cursor != _exit && cursor != _undefined; cursor = _idom[cursor])
        {
            if (cursor == postDominator)
                return true;
        }
        return false;
    }

    public static PostDominators Of(IReadOnlyList<BlockEdges> edges)
    {
        int n = edges.Count;
        int exit = n;             // the virtual exit shares the index space after the blocks
        int undefined = n + 1;
        int nodes = n + 1;        // blocks plus the virtual exit

        // Forward successors including the virtual exit for any block that reaches
        // a method exit, an external target, or an EH survivor. Reverse-graph
        // predecessors of a node are exactly these forward successors.
        var forwardSuccessors = new List<int>[nodes];
        for (int i = 0; i < nodes; i++)
            forwardSuccessors[i] = [];
        for (int i = 0; i < n; i++)
        {
            forwardSuccessors[i].AddRange(edges[i].Successors);
            if (edges[i].ExitsMethod || edges[i].ExternalTargets.Count > 0 || edges[i].LeavesRegion)
                forwardSuccessors[i].Add(exit);
        }

        // Reverse-graph successors (forward predecessors): used to walk the reverse
        // CFG outward from the exit when numbering nodes in postorder.
        var reverseSuccessors = new List<int>[nodes];
        for (int i = 0; i < nodes; i++)
            reverseSuccessors[i] = [];
        for (int from = 0; from < nodes; from++)
            foreach (int to in forwardSuccessors[from])
                reverseSuccessors[to].Add(from);

        // Postorder of the reverse CFG rooted at the exit. The exit is visited
        // first, so it finishes last and holds the highest number — the property
        // the Cooper–Harvey–Kennedy "finger" intersection relies on. Nodes that
        // cannot reach the exit are never numbered (postorder stays -1).
        var postorder = new int[nodes];
        Array.Fill(postorder, -1);
        var order = new List<int>(nodes);
        var visited = new bool[nodes];
        var stack = new Stack<(int Node, int Next)>();
        stack.Push((exit, 0));
        visited[exit] = true;
        while (stack.Count > 0)
        {
            var (node, next) = stack.Pop();
            if (next < reverseSuccessors[node].Count)
            {
                stack.Push((node, next + 1));
                int child = reverseSuccessors[node][next];
                if (!visited[child])
                {
                    visited[child] = true;
                    stack.Push((child, 0));
                }
            }
            else
            {
                postorder[node] = order.Count;
                order.Add(node);
            }
        }

        var idom = new int[nodes];
        Array.Fill(idom, undefined);
        idom[exit] = exit;

        // Reverse postorder, excluding the exit root.
        var reversePostorder = new List<int>(order.Count);
        for (int i = order.Count - 1; i >= 0; i--)
            if (order[i] != exit)
                reversePostorder.Add(order[i]);

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (int b in reversePostorder)
            {
                int newIdom = undefined;
                foreach (int p in forwardSuccessors[b])   // reverse-graph predecessors of b
                {
                    if (idom[p] == undefined)
                        continue;
                    newIdom = newIdom == undefined ? p : Intersect(p, newIdom, idom, postorder);
                }
                if (newIdom != undefined && idom[b] != newIdom)
                {
                    idom[b] = newIdom;
                    changed = true;
                }
            }
        }

        return new PostDominators(idom, exit, undefined);
    }

    static int Intersect(int a, int b, int[] idom, int[] postorder)
    {
        while (a != b)
        {
            while (postorder[a] < postorder[b])
                a = idom[a];
            while (postorder[b] < postorder[a])
                b = idom[b];
        }
        return a;
    }
}
