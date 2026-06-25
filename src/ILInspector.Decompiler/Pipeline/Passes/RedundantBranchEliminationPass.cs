namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Removes branches to the immediately following block — fallthrough does the
/// same thing. Two shapes:
///   • an unconditional <see cref="Branch"/> to the next block (Debug-config
///     epilogues — <c>store; br L; L: load; ret</c> — leave these everywhere);
///   • a <see cref="ConditionalBranch"/> to the next block — both arms reach the
///     same place, so the branch is dead (<c>if (c) goto IL_x; IL_x:</c>). When
///     the condition is side-effect-free it is dropped entirely; a condition that
///     could have side effects is left in place (evaluating it still matters).
/// Runs before structuring and expression inlining, so this surviving-goto
/// residue — which would otherwise make the printer's definite-assignment walk
/// bail and <c>= default</c> every local — is gone before those passes see it.
/// </summary>
public sealed class RedundantBranchEliminationPass : IIrPass
{
    public string Name => "redundant-branch-elimination";

    public void Run(IrFunction function, PassContext context)
    {
        var blocks = function.Body.Blocks;
        for (int i = 0; i < blocks.Count - 1; i++)
        {
            if (blocks[i].Children.Count == 0)
                continue;
            int nextOffset = blocks[i + 1].StartOffset;
            switch (blocks[i].Children[^1])
            {
                case Branch branch when branch.TargetOffset == nextOffset:
                    context.Stepper.StepOver("remove redundant branch to the following block", branch);
                    branch.Detach();
                    break;
                case ConditionalBranch conditional
                    when conditional.TargetOffset == nextOffset && IsSideEffectFree(conditional.Condition):
                    context.Stepper.StepOver("remove redundant conditional branch to the following block", conditional);
                    conditional.Detach();
                    break;
            }
        }
    }

    /// <summary>
    /// Whether evaluating the expression has no observable effect, so dropping it
    /// is sound. Conservative: any call-like node (or an unsupported one) is
    /// assumed to have side effects, leaving its branch in place rather than risk
    /// eliding an effect. A static field access is also treated as effectful because
    /// <c>ldsfld</c>/<c>ldsflda</c> can trigger the declaring type's static constructor.
    /// </summary>
    static bool IsSideEffectFree(IrExpression condition)
        => condition.Descendants.Prepend(condition).All(node => node is not
            (Call or CallIndirect or NewObject or DelegateCreation or UnsupportedNode)
            and not LoadField { Instance: null }
            and not LoadFieldAddress { Instance: null });
}
