using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The self-contained real-gap classifier behind <c>--gaps</c>. A fully-raised
/// method's tree holds only structured nodes (IfStatement, loops, Switch,
/// TryCatch); it never keeps a raw branch terminator. So residual control flow —
/// a <see cref="Branch"/>/<see cref="ConditionalBranch"/>/<see cref="SwitchBranch"/>
/// the structuring passes could not consume, or an EH <see cref="Leave"/> — is a
/// surviving <c>goto</c>, and an <see cref="UnsupportedNode"/> is an import-side
/// fidelity gap. It reads only the candidate's own raised tree, so it is a
/// self-contained completeness signal — it measures completeness, not correctness.
/// </summary>
public static class Gaps
{
    /// <summary>
    /// The most-actionable residual-control-flow kind in a raised function, or
    /// null when it is fully raised (no residual node). Structuring residue ranks
    /// ahead of EH and the unsupported-node residue, since structuring is the
    /// completeness target. Run the passes before calling — this reads the finished tree.
    /// </summary>
    public static string? Residual(IrFunction function)
    {
        bool hasConditional = false, hasBranch = false, hasSwitch = false, hasLeave = false, hasUnsupported = false;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case ConditionalBranch: hasConditional = true; break;
                case Branch: hasBranch = true; break;
                case SwitchBranch: hasSwitch = true; break;
                case Leave or EndFinally or EndFilter: hasLeave = true; break;
                case UnsupportedNode: hasUnsupported = true; break;
            }
        }

        return hasConditional ? "structuring: conditional-branch"
            : hasBranch ? "structuring: unconditional-branch"
            : hasSwitch ? "structuring: switch-branch"
            : hasLeave ? "eh: leave/endfinally"
            : hasUnsupported ? "fidelity: unsupported-node"
            : null;
    }
}
