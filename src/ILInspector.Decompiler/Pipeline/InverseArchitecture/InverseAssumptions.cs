using System.Collections.Generic;

namespace ILInspector.Decompiler.Pipeline.InverseArchitecture;

public readonly record struct AssumptionViolation(IrNode Node, string Message);

/// <summary>
/// The runnable assumption predicates named by <c>[InverseOf(assumes: ...)]</c>.
/// Each is a release-capable check over an <see cref="IrFunction"/> that returns
/// violation messages (empty = the assumption holds on that function). The
/// inverse-architecture rule: an <c>assumes:</c> must name one of these, and the
/// coverage test resolves and structurally invokes it
/// (docs/design/inverse-architecture.md, "Two levels"); per-predicate behavior is
/// covered by predicate-specific tests. Binding the attribute's
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
    public static IReadOnlyList<AssumptionViolation> SinkDistinguishableFromStack(IrFunction function)
    {
        var shapes = function.TypeShapes;
        var violations = new List<AssumptionViolation>();
        foreach (var sink in CoercionSinks.Enumerate(function))
        {
            if (CoercionSinks.RequiresCoercion(sink, shapes))
            {
                violations.Add(new(sink.Value, 
                    $"{function.Name}: {sink.Value.Describe()} occupies a {sink.Target.ToDisplayString()} sink without a Coerce"));
            }
        }
        return violations;
    }
}
