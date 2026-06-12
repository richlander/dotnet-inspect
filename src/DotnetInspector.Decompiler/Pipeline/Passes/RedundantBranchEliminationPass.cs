namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Removes branches to the immediately following block — fallthrough does
/// the same thing, and Debug-config epilogues (store; br L; L: load; ret)
/// leave these everywhere. Runs before expression inlining so the
/// store/load pair becomes a plain fallthrough edge it can see.
/// </summary>
public sealed class RedundantBranchEliminationPass : IIrPass
{
    public string Name => "redundant-branch-elimination";

    public void Run(IrFunction function)
    {
        var blocks = function.Body.Blocks;
        for (int i = 0; i < blocks.Count - 1; i++)
        {
            if (blocks[i].Children.Count > 0
                && blocks[i].Children[^1] is Branch branch
                && branch.TargetOffset == blocks[i + 1].StartOffset)
            {
                branch.Detach();
            }
        }
    }
}
