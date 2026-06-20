namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Thin, SRM-native structural-identity facts over <em>re-evaluable places</em>
/// in the decompiler IR: "are these two expressions the same side-effect-free
/// location, so a raise can fold their repeated evaluation into one?"
///
/// <para>
/// Every fold that collapses a load-test/load-store pair — <c>??=</c>, <c>??</c>,
/// <c>?.</c>, switch dispatch, field <c>??=</c>, <c>^n</c> from-end — needs this
/// question. Each pass previously hand-rolled a near-identical <c>Same*</c>
/// switch. The equality logic belongs in one place; what legitimately differs
/// between passes is <em>which node kinds each admits</em>, and that is a
/// deliberate soundness discriminator the pass still owns. So this type exposes
/// the kinds as separate atoms the caller composes, rather than one maximal
/// predicate that would silently broaden a pass.
/// </para>
///
/// <para>
/// The canonical example is <c>IndexFromEndPass</c>: it must restrict to a
/// spilled stack slot (<see cref="SameStackSlot"/>), because broadening to a
/// direct local/argument read would rewrite a faithful <c>a[a.Length - n]</c>
/// into <c>a[^n]</c> whose recompiled IL differs. The shared atom keeps the
/// equality honest; the pass keeps the discriminator.
/// </para>
/// </summary>
public static class PlaceIdentity
{
    /// <summary>
    /// Two reads of the same variable — a local, an argument, or <c>this</c>
    /// (argument 0). The atomic re-evaluation every fold relies on: a bare
    /// variable read has no side effect, so evaluating it once instead of twice
    /// reorders nothing.
    /// </summary>
    public static bool SameVariable(IrExpression? left, IrExpression? right) => (left, right) switch
    {
        (LoadArgument a, LoadArgument b) => a.Index == b.Index,
        (LoadLocal a, LoadLocal b) => a.Index == b.Index,
        _ => false,
    };

    /// <summary>
    /// Two reads of the same stack slot. The compiler spills a once-evaluated
    /// receiver into a slot and reads it twice; matching on the slot (rather than
    /// a re-loaded variable) is what proves the spill happened — the discriminator
    /// that separates a genuine compiler lowering from hand-written source.
    /// </summary>
    public static bool SameStackSlot(IrExpression? left, IrExpression? right)
        => (left, right) is (LoadStackSlot a, LoadStackSlot b) && a.Slot == b.Slot;

    /// <summary>
    /// Two reads of the same re-evaluable operand: a variable
    /// (<see cref="SameVariable"/>) or an identical literal. The leaf an indexer's
    /// arguments take when a get and a set fold into one <c>d[i]</c> place — a bare
    /// variable read or a constant has no side effect, so evaluating it once instead
    /// of twice reorders nothing. A complex index (a call, arithmetic) is not
    /// admitted here; csc spills such an index into a local, which then presents as
    /// <see cref="SameVariable"/>.
    /// </summary>
    public static bool SameOperand(IrExpression? left, IrExpression? right)
        => SameVariable(left, right)
            || ((left, right) is (Constant a, Constant b) && Equals(a.Value, b.Value));

    /// <summary>
    /// Pairwise <see cref="SameOperand"/> over two argument lists — the index
    /// arguments an indexer reads on one side of a fold and writes on the other.
    /// Equal-length empty lists trivially match (a non-indexer property).
    /// </summary>
    public static bool SameOperands(IReadOnlyList<IrExpression> left, IReadOnlyList<IrExpression> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!SameOperand(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }
}
