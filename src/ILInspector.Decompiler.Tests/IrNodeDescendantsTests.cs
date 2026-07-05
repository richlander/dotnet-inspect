using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Permanent order-equivalence canary for <see cref="IrNode.Descendants"/>. The
/// property is a hot-path primitive with 320 call sites; its contract is a
/// depth-first pre-order walk of the subtree (excluding the node itself),
/// children in source order. These tests pin that sequence so the iterative
/// implementation cannot silently drift from the recursive pre-order it replaced.
/// </summary>
public class IrNodeDescendantsTests
{
    // Blocks nest via Block.Add(IrNode); StartOffset is the order marker.
    static Block Node(int offset, params Block[] children)
    {
        var block = new Block(offset);
        foreach (var child in children)
            block.Add(child);
        return block;
    }

    [Fact]
    public void Descendants_MultiLevelTree_YieldsPreOrderChildrenInSourceOrder()
    {
        //        root(0x00)
        //        ├── a(0x10)
        //        │   ├── a1(0x11)
        //        │   └── a2(0x12)
        //        └── b(0x20)
        //            └── b1(0x21)
        var root = Node(0x00,
            Node(0x10, Node(0x11), Node(0x12)),
            Node(0x20, Node(0x21)));

        var order = root.Descendants.Select(n => ((Block)n).StartOffset).ToArray();

        // Pre-order, source order, excluding the root itself.
        Assert.Equal(new[] { 0x10, 0x11, 0x12, 0x20, 0x21 }, order);
    }

    [Fact]
    public void Descendants_Leaf_IsEmpty()
    {
        var leaf = Node(0x00);

        Assert.Empty(leaf.Descendants);
    }

    [Fact]
    public void Descendants_MatchesReferenceRecursivePreOrder()
    {
        // Independent recursive reference over the public Children view; the
        // iterative Descendants must reproduce it exactly for an irregular tree.
        var root = Node(0x00,
            Node(0x10,
                Node(0x11, Node(0x13)),
                Node(0x12)),
            Node(0x20),
            Node(0x30, Node(0x31), Node(0x32, Node(0x33))));

        static IEnumerable<IrNode> ReferencePreOrder(IrNode node)
        {
            foreach (var child in node.Children)
            {
                yield return child;
                foreach (var d in ReferencePreOrder(child))
                    yield return d;
            }
        }

        Assert.Equal(ReferencePreOrder(root).ToList(), root.Descendants.ToList());
    }

    [Fact]
    public void Descendants_PooledStack_AllocationIndependentOfSubtreeSize()
    {
        // A shallow tree and a large deep tree. With the work stack pooled, a traversal
        // allocates only its fixed iterator state machine — never a stack or a backing
        // array that grows with the subtree — so per-traversal allocation does not grow
        // with node count.
        var small = Node(0x00, Node(0x10), Node(0x20));
        var large = BuildBalanced(depth: 8, fanout: 3); // 3^0+..+3^8 = 9841 nodes

        static long MinPerTraversalBytes(IrNode root)
        {
            static int Walk(IrNode node)
            {
                int count = 0;
                foreach (var _ in node.Descendants)
                    count++;
                return count;
            }

            Walk(root); // warm up JIT + populate the pool

            // Take the minimum over several rounds. A stray allocation on this thread
            // (tiered JIT, GC bookkeeping, a neighbouring test) can only ADD bytes to a
            // round, so the minimum reflects the true per-traversal cost — the iterator
            // state machine alone. This keeps the assertion robust under the full suite.
            long best = long.MaxValue;
            for (int round = 0; round < 20; round++)
            {
                const int iterations = 200;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < iterations; i++)
                    Walk(root);
                best = Math.Min(best, (GC.GetAllocatedBytesForCurrentThread() - before) / iterations);
            }
            return best;
        }

        long smallBytes = MinPerTraversalBytes(small);
        long largeBytes = MinPerTraversalBytes(large);

        // Independent of subtree size: the 9841-node walk allocates no more than the
        // 2-node walk (both only the state machine). If the stack were not pooled, the
        // large tree would add its Stack plus a frontier-sized backing array — hundreds
        // of bytes — far above this tolerance for measurement jitter.
        Assert.True(
            largeBytes <= smallBytes + 64,
            $"pooled traversal allocation should not grow with subtree size; small={smallBytes} B, large={largeBytes} B");
    }

    [Fact]
    public void Descendants_NestedReentrantTraversal_IsCorrect()
    {
        var root = Node(0x00,
            Node(0x10, Node(0x11, Node(0x13)), Node(0x12)),
            Node(0x20, Node(0x21)));

        // Interleave an inner traversal inside the outer one: the pooled stacks must
        // not be shared, so each descendant's own subtree count is exact.
        var pairs = new List<(int Node, int SubtreeCount)>();
        foreach (var node in root.Descendants)
        {
            int inner = 0;
            foreach (var _ in node.Descendants)
                inner++;
            pairs.Add((((Block)node).StartOffset, inner));
        }

        Assert.Equal(
            new[]
            {
                (0x10, 3), // a: a1, a3, a2
                (0x11, 1), // a1: a3
                (0x13, 0),
                (0x12, 0),
                (0x20, 1), // b: b1
                (0x21, 0),
            },
            pairs);
    }

    [Fact]
    public void Descendants_EarlyBreak_ReturnsStackToPool()
    {
        var root = BuildBalanced(depth: 4, fanout: 3);

        // Break out early many times; the finally must return each rented stack, so a
        // subsequent full traversal still allocates only its state machine (no growth).
        for (int i = 0; i < 100; i++)
        {
            foreach (var _ in root.Descendants)
                break;
        }

        // Full traversal after the early breaks still yields the complete subtree.
        int count = 0;
        foreach (var _ in root.Descendants)
            count++;
        Assert.Equal(CountNodes(root) - 1, count);
    }

    static Block BuildBalanced(int depth, int fanout)
    {
        var node = new Block(depth);
        if (depth > 0)
        {
            for (int i = 0; i < fanout; i++)
                node.Add(BuildBalanced(depth - 1, fanout));
        }
        return node;
    }

    static int CountNodes(IrNode node)
    {
        int count = 1;
        foreach (var child in node.Children)
            count += CountNodes(child);
        return count;
    }
}
