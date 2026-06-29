using ILInspector.ControlFlow;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Structural control-flow edges for one container's blocks — computed in a
/// single place so the printer's definite-assignment dataflow and the
/// <c>--dump --cfg</c> view can never disagree about successors. Edges follow
/// the block terminator: a branch/conditional/switch to its target(s) (plus
/// fall-through for the conditional and switch), nothing for a method exit,
/// and fall-through to the next block otherwise. Targets outside the container
/// and EH-survivor terminators are reported as such rather than resolved, so
/// the dataflow can bail on shapes it does not model while the view still shows
/// the edges it does.
/// </summary>
public static class Cfg
{

    public static IReadOnlyList<BlockEdges> Build(IReadOnlyList<Block> blocks)
    {
        int n = blocks.Count;
        var offsetToIndex = new Dictionary<int, int>(n);
        for (int i = 0; i < n; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        var result = new BlockEdges[n];
        for (int i = 0; i < n; i++)
        {
            var successors = new List<int>();
            var external = new List<int>();
            bool exits = false, leaves = false;

            void AddTarget(int offset)
            {
                if (offsetToIndex.TryGetValue(offset, out int index))
                    successors.Add(index);
                else
                    external.Add(offset);
            }

            switch (blocks[i].Children.Count > 0 ? blocks[i].Children[^1] : null)
            {
                case Branch b:
                    AddTarget(b.TargetOffset);
                    break;
                case ConditionalBranch cb:
                    AddTarget(cb.TargetOffset);
                    if (i + 1 < n) successors.Add(i + 1);
                    break;
                case SwitchBranch sb:
                    foreach (var target in sb.TargetOffsets)
                        AddTarget(target);
                    if (i + 1 < n) successors.Add(i + 1);
                    break;
                case Return or Throw:
                    exits = true;
                    break;
                case Leave or EndFinally or EndFilter:
                    leaves = true;
                    break;
                default:
                    if (i + 1 < n) successors.Add(i + 1);
                    break;
            }

            result[i] = new BlockEdges(successors, external, exits, leaves);
        }
        return result;
    }
}
