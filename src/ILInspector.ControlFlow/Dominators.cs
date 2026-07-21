namespace ILInspector.ControlFlow;

/// <summary>
/// Immediate dominators over one container's block CFG, from the Cooper-Harvey-Kennedy
/// iterative fixpoint on the forward CFG rooted at block 0 (the entry). Pure analysis
/// over <see cref="BlockEdges"/>; shared kernel: no dependency on any IL representation.
/// The dual of <see cref="PostDominators"/> (that class computes the same fixpoint over
/// the reverse CFG rooted at a virtual exit).
/// </summary>
public sealed class Dominators
{
    readonly int[] _idom;
    readonly int _undefined;

    Dominators(int[] idom, int undefined)
    {
        _idom = idom;
        _undefined = undefined;
    }

    /// <summary>The number of blocks the analysis covers.</summary>
    public int Count => _idom.Length;

    /// <summary>
    /// True when <paramref name="dominator"/> dominates <paramref name="block"/> (walking the
    /// immediate-dominator chain from <paramref name="block"/> reaches it before the entry).
    /// A block dominates itself. Out-of-range indices return false rather than throwing.
    /// </summary>
    public bool Dominates(int dominator, int block)
    {
        if ((uint)dominator >= (uint)_idom.Length || (uint)block >= (uint)_idom.Length)
            return false;
        for (int cursor = block; cursor != _undefined; cursor = _idom[cursor])
        {
            if (cursor == dominator)
                return true;
            if (cursor == _idom[cursor])
                break;
        }
        return false;
    }

    public static Dominators Of(IReadOnlyList<BlockEdges> edges)
    {
        int n = edges.Count;
        int undefined = n + 1;
        var idom = new int[n];
        Array.Fill(idom, undefined);
        if (n == 0)
            return new Dominators(idom, undefined);

        var predecessors = new List<int>[n];
        for (int i = 0; i < n; i++)
            predecessors[i] = [];
        for (int i = 0; i < n; i++)
            foreach (int successor in edges[i].Successors)
                if ((uint)successor < (uint)n)
                    predecessors[successor].Add(i);

        // Postorder of the forward CFG rooted at the entry (block 0).
        var postorder = new int[n];
        Array.Fill(postorder, -1);
        var order = new List<int>(n);
        var visited = new bool[n];
        var stack = new Stack<(int Block, int Next)>();
        stack.Push((0, 0));
        visited[0] = true;
        while (stack.Count > 0)
        {
            var (block, next) = stack.Pop();
            var successors = edges[block].Successors;
            if (next < successors.Count)
            {
                stack.Push((block, next + 1));
                int successor = successors[next];
                if ((uint)successor < (uint)n && !visited[successor])
                {
                    visited[successor] = true;
                    stack.Push((successor, 0));
                }
            }
            else
            {
                postorder[block] = order.Count;
                order.Add(block);
            }
        }

        idom[0] = 0;

        // Reverse postorder, excluding the entry root.
        var reversePostorder = new List<int>(order.Count);
        for (int i = order.Count - 1; i >= 0; i--)
            if (order[i] != 0)
                reversePostorder.Add(order[i]);

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (int block in reversePostorder)
            {
                int newIdom = undefined;
                foreach (int predecessor in predecessors[block])
                {
                    if (idom[predecessor] == undefined)
                        continue;
                    newIdom = newIdom == undefined
                        ? predecessor
                        : Intersect(predecessor, newIdom, idom, postorder);
                }
                if (newIdom != undefined && idom[block] != newIdom)
                {
                    idom[block] = newIdom;
                    changed = true;
                }
            }
        }

        return new Dominators(idom, undefined);
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
