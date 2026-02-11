namespace DotnetInspector.Decompiler;

/// <summary>
/// Computes the dominator tree for a control flow graph using the iterative
/// algorithm from Cooper, Harvey, Kennedy ("A Simple, Fast Dominance Algorithm").
/// </summary>
public sealed class DominatorTree
{
    readonly ControlFlowGraph _cfg;
    readonly int[] _idom;          // Immediate dominator index (-1 = none)
    readonly int[] _postOrder;     // Block index → post-order number
    readonly int[] _reversePost;   // Reverse post-order traversal sequence (block indices)

    DominatorTree(ControlFlowGraph cfg, int[] idom, int[] postOrder, int[] reversePost)
    {
        _cfg = cfg;
        _idom = idom;
        _postOrder = postOrder;
        _reversePost = reversePost;
    }

    /// <summary>
    /// Build the dominator tree for the given CFG.
    /// </summary>
    public static DominatorTree Build(ControlFlowGraph cfg)
    {
        int n = cfg.BasicBlocks.Count;
        if (n == 0)
            return new DominatorTree(cfg, [], [], []);

        // Step 1: Compute reverse post-order via DFS
        var (postOrder, reversePost) = ComputeReversePostOrder(cfg);

        // Step 2: Iterative dominator computation
        int[] idom = new int[n];
        Array.Fill(idom, -1);
        idom[0] = 0; // Entry dominates itself

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (int b in reversePost)
            {
                if (b == 0) continue; // Skip entry

                var block = cfg.BasicBlocks[b];
                int newIdom = -1;

                // Find first processed predecessor
                foreach (var src in block.Sources)
                {
                    int predIdx = cfg.LookupIndex(src.Start);
                    if (predIdx >= 0 && idom[predIdx] != -1)
                    {
                        newIdom = predIdx;
                        break;
                    }
                }

                if (newIdom == -1) continue;

                // Intersect with remaining processed predecessors
                foreach (var src in block.Sources)
                {
                    int predIdx = cfg.LookupIndex(src.Start);
                    if (predIdx < 0 || idom[predIdx] == -1) continue;
                    newIdom = Intersect(idom, postOrder, newIdom, predIdx);
                }

                if (idom[b] != newIdom)
                {
                    idom[b] = newIdom;
                    changed = true;
                }
            }
        }

        return new DominatorTree(cfg, idom, postOrder, reversePost);
    }

    /// <summary>
    /// Get the immediate dominator block index for block at index <paramref name="blockIndex"/>.
    /// Returns -1 for the entry block (no dominator) or unreachable blocks.
    /// </summary>
    public int GetImmediateDominator(int blockIndex) =>
        blockIndex == 0 ? -1 : _idom[blockIndex];

    /// <summary>
    /// Returns true if block at index <paramref name="dominator"/> dominates
    /// block at index <paramref name="block"/>.
    /// </summary>
    public bool Dominates(int dominator, int block)
    {
        if (dominator == block) return true;
        int current = block;
        while (current != -1 && current != 0)
        {
            current = _idom[current];
            if (current == dominator) return true;
        }
        return dominator == 0 && current == 0;
    }

    /// <summary>
    /// Get all children in the dominator tree for block at index <paramref name="blockIndex"/>.
    /// </summary>
    public List<int> GetDominatorTreeChildren(int blockIndex)
    {
        List<int> children = [];
        for (int i = 0; i < _idom.Length; i++)
        {
            if (i != blockIndex && _idom[i] == blockIndex)
                children.Add(i);
        }
        return children;
    }

    /// <summary>
    /// Get the depth of block at <paramref name="blockIndex"/> in the dominator tree.
    /// Entry block has depth 0.
    /// </summary>
    public int GetDepth(int blockIndex)
    {
        int depth = 0;
        int current = blockIndex;
        while (current != 0 && current != -1)
        {
            current = _idom[current];
            depth++;
        }
        return depth;
    }

    // --- Internals ---

    static int Intersect(int[] idom, int[] postOrder, int b1, int b2)
    {
        while (b1 != b2)
        {
            while (postOrder[b1] < postOrder[b2]) b1 = idom[b1];
            while (postOrder[b2] < postOrder[b1]) b2 = idom[b2];
        }
        return b1;
    }

    static (int[] PostOrder, int[] ReversePost) ComputeReversePostOrder(ControlFlowGraph cfg)
    {
        int n = cfg.BasicBlocks.Count;
        var postOrder = new int[n];
        var visited = new bool[n];
        var order = new List<int>(n);

        DFS(cfg, 0, visited, order);

        // Assign post-order numbers
        for (int i = 0; i < order.Count; i++)
            postOrder[order[i]] = i;

        // Reverse post-order is the reverse of the DFS post-order
        var reversePost = new int[order.Count];
        for (int i = 0; i < order.Count; i++)
            reversePost[i] = order[order.Count - 1 - i];

        return (postOrder, reversePost);
    }

    static void DFS(ControlFlowGraph cfg, int blockIndex, bool[] visited, List<int> postOrder)
    {
        if (visited[blockIndex]) return;
        visited[blockIndex] = true;

        var block = cfg.BasicBlocks[blockIndex];
        foreach (var target in block.Targets)
        {
            int targetIdx = cfg.LookupIndex(target.Start);
            if (targetIdx >= 0)
                DFS(cfg, targetIdx, visited, postOrder);
        }

        postOrder.Add(blockIndex);
    }
}
