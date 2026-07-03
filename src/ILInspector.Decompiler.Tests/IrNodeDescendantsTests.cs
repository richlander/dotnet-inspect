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
}
