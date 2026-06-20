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
    public static bool SameVariable(IrNode? left, IrNode? right) => (left, right) switch
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
    public static bool SameStackSlot(IrNode? left, IrNode? right)
        => (left, right) is (LoadStackSlot a, LoadStackSlot b) && a.Slot == b.Slot;
}
