namespace ILInspector.Decompiler.Pipeline.InverseArchitecture;

/// <summary>
/// The runnable assumption predicates named by <c>[InverseOf(assumes: ...)]</c>.
/// Each is a release-capable check over an <see cref="IrFunction"/> that returns
/// violation messages (empty = the assumption holds on that function). The
/// inverse-architecture rule: an <c>assumes:</c> must name one of these, and the
/// coverage test invokes it over a fixture corpus
/// (docs/design/inverse-architecture.md, "Two levels"). Binding the attribute's
/// claim to a runnable check is the residual-drift guard — an assumption that
/// cannot be spelled as a predicate stays in prose behind a
/// <see cref="NotInvertedAttribute"/> marker instead.
/// </summary>
public static class InverseAssumptions
{
    /// <summary>
    /// A coercion sink is distinguishable from the value's stack type: every
    /// in-domain typed sink routes through a <c>Coerce</c> or is provably at its
    /// target. The executable form of the assumption the value-typed-emission
    /// slice-3 review turned on; reuses <see cref="CoercionInvariant.Check"/> so
    /// the ledger's assertion and the shipped invariant cannot diverge.
    /// </summary>
    public static IReadOnlyList<string> SinkDistinguishableFromStack(IrFunction function)
        => CoercionInvariant.Check(function);
}
