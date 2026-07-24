namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Fidelity predicates shared by the two short-circuit re-forms —
/// <see cref="ShortCircuitTernaryPass"/> (constant-arm ternary) and
/// <see cref="BooleanFoldingPass"/>'s guarded-return fold. Both lift a lowered
/// branch shape into a spelled <c>&amp;&amp;</c>/<c>||</c>; these identify the
/// condition and operand shapes where that lift would change the bound C#
/// semantics or produce output that does not compile. Sharing keeps the two
/// passes' hazard sets from drifting apart.
/// </summary>
internal static class ShortCircuitFidelity
{
    /// <summary>
    /// Whether <paramref name="condition"/> is a user-defined-truthiness evaluation
    /// — a call to <c>operator true</c>/<c>operator false</c> the compiler inserts
    /// when a user type is used in boolean context, optionally under one or more
    /// logical negations. The printer strips such a call to its bare user-typed
    /// receiver (it renders <c>op_True(a)</c>/<c>op_False(a)</c> — and their negations
    /// — as <c>a</c>/inverted <c>a</c>), so lifting or returning the receiver rebinds
    /// to a user-defined or nonexistent operator: <c>a || y</c>/<c>a &amp;&amp; y</c>
    /// bind the user-defined conditional <c>|</c>/<c>&amp;</c> (result typed as the
    /// user type, not <c>bool</c>), and a bare <c>return a</c> needs a user→bool
    /// conversion that does not exist.
    ///
    /// The leading-negation peel is load-bearing: the guarded-return fold lifts the
    /// condition through <see cref="Conditions.Negate"/> on its <c>||</c>/negated
    /// <c>&amp;&amp;</c> arms, which unwraps a <c>LogicalNot</c> and re-exposes the
    /// bare truthiness call — so a <c>!op_True(a)</c> condition would otherwise slip
    /// past a match on the raw shape.
    ///
    /// Only a type carrying <c>op_True</c>/<c>op_False</c> reaches here, so this is the
    /// complete rebind set; a primitive-bool condition (comparison, bool
    /// property/field/local/method, nested logical operator) binds the primitive
    /// operator and is safe to lift.
    /// </summary>
    internal static bool IsUserDefinedTruthiness(IrExpression condition)
    {
        var inner = condition;
        while (inner is LogicalNot { Operand: { } negated })
            inner = negated;
        return inner is Call { Callee.Name: "op_True" or "op_False" };
    }

    /// <summary>
    /// Whether <paramref name="operand"/> is (or, after the bool-constant-comparison
    /// reduction the lift's own pass will apply, becomes) a managed by-ref
    /// (<c>in</c>/<c>ref</c>/<c>out</c>) dereference. csc treats a managed by-ref as
    /// non-null and side-effect-free, so a spelled <c>a &amp;&amp; *r</c>/<c>a || *r</c>
    /// collapses to a branchless <c>&amp;</c>/<c>|</c> that eagerly dereferences a
    /// location the branch had guarded — an observable
    /// <see cref="System.NullReferenceException"/> divergence on a null by-ref.
    ///
    /// The identity-comparison peel is load-bearing for the guarded-return fold: it
    /// lifts the surviving operand <em>before</em> <see cref="BooleanFoldingPass"/>'s
    /// fixpoint reduces an identity bool-constant comparison (<c>x == true</c> /
    /// <c>x != false</c>) to the bare <c>x</c>. Peeling exactly those forms — the ones
    /// that strip to a bare operand, not the <c>== false</c> / <c>!= true</c> forms
    /// that keep a negation (and a branch) — reaches a by-ref deref hiding under
    /// <c>(*r) == true</c>. (For the ternary re-form this reduction has already run,
    /// so the peel is inert there.)
    ///
    /// A raw <em>pointer</em> dereference <c>*p</c> is deliberately excluded: a pointer
    /// read can access-violate, so csc keeps the branch and lifting it is safe.
    /// </summary>
    internal static bool IsManagedByRefDeref(IrExpression operand)
    {
        var inner = operand;
        while (StripBareBoolComparison(inner) is { } bare)
            inner = bare;
        return inner is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef };
    }

    /// <summary>
    /// The bare left operand that <see cref="BooleanFoldingPass"/>'s
    /// <c>FoldBoolConstantComparison</c> reduces an identity bool test to — <c>x</c>
    /// from <c>x == true</c> or <c>x != false</c> (a bool-typed left compared to a
    /// right-hand bool <c>0</c>/<c>1</c> constant). Returns null for the negating
    /// forms (<c>x == false</c> / <c>x != true</c>, which fold to <c>!x</c> and keep a
    /// branch) and for any non-identity shape.
    /// </summary>
    static IrExpression? StripBareBoolComparison(IrExpression expression)
    {
        if (expression is not Comparison { Kind: ComparisonKind.Equal or ComparisonKind.NotEqual } comparison)
            return null;
        if (comparison.Left.ResultType is not { Namespace: "System", Name: "Boolean" })
            return null;
        if (BoolConstantValue(comparison.Right) is not { } constant)
            return null;
        bool keepIdentity = constant == (comparison.Kind == ComparisonKind.Equal);
        return keepIdentity ? comparison.Left : null;
    }

    static bool? BoolConstantValue(IrExpression expression) => expression switch
    {
        Constant { Value: bool value } => value,
        Constant { Value: int value } when value is 0 or 1 => value == 1,
        _ => null,
    };
}
