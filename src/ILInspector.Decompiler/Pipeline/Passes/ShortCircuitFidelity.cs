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
    /// when a user type is used in boolean context, possibly wrapped by the negations
    /// and bool-constant comparisons the fold's own fixpoint peels away (see
    /// <see cref="PeelReducibleBoolWrappers"/>). The printer strips such a call to its
    /// bare user-typed receiver (it renders <c>op_True(a)</c>/<c>op_False(a)</c> as
    /// <c>a</c>), so lifting or returning the receiver rebinds to a user-defined or
    /// nonexistent operator: <c>a || y</c>/<c>a &amp;&amp; y</c> bind the user-defined
    /// conditional <c>|</c>/<c>&amp;</c> (result typed as the user type, not
    /// <c>bool</c>), and a bare <c>return a</c> needs a user→bool conversion that does
    /// not exist.
    ///
    /// Only a type carrying <c>op_True</c>/<c>op_False</c> reaches the leaf, so this is
    /// the complete rebind set; a primitive-bool condition (comparison against a non-
    /// truthiness value, bool property/field/local/method, nested logical operator)
    /// binds the primitive operator and is safe to lift.
    /// </summary>
    internal static bool IsUserDefinedTruthiness(IrExpression condition)
        => PeelReducibleBoolWrappers(condition) is Call { Callee.Name: "op_True" or "op_False" };

    /// <summary>
    /// Whether <paramref name="operand"/> is (or, after the fold's own fixpoint peels
    /// the wrappers in <see cref="PeelReducibleBoolWrappers"/>, becomes) a managed
    /// by-ref (<c>in</c>/<c>ref</c>/<c>out</c>) dereference. csc treats a managed
    /// by-ref as non-null and side-effect-free, so a spelled <c>a &amp;&amp; *r</c>/
    /// <c>a || *r</c> collapses to a branchless <c>&amp;</c>/<c>|</c> that eagerly
    /// dereferences a location the branch had guarded — an observable
    /// <see cref="System.NullReferenceException"/> divergence on a null by-ref. A raw
    /// <em>pointer</em> dereference <c>*p</c> is deliberately excluded: a pointer read
    /// can access-violate, so csc keeps the branch and lifting it is safe.
    /// </summary>
    internal static bool IsManagedByRefDeref(IrExpression operand)
        => PeelReducibleBoolWrappers(operand) is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef };

    /// <summary>
    /// Follow the reducible operand through the wrappers that
    /// <see cref="BooleanFoldingPass"/>'s fixpoint strips or inverts <em>after</em> its
    /// guarded-return fold has already lifted the expression: leading logical negations
    /// (the fold lifts a condition through <see cref="Conditions.Negate"/> on its
    /// <c>||</c>/negated-<c>&amp;&amp;</c> arms), and bool-constant comparisons
    /// (<c>x == true</c>/<c>x == false</c>/<c>x != true</c>/<c>x != false</c>) that
    /// <c>FoldBoolConstantComparison</c> reduces to <c>x</c> or <c>!x</c>. It follows the
    /// non-constant side of each comparison so a hazard nested under any chain of these
    /// wrappers is still reached — e.g. the double negation <c>(*r == false) == false</c>
    /// whose outer <see cref="Conditions.Negate"/> inverts the inner comparison and
    /// reduces back to the bare <c>*r</c>, or <c>op_True(t) == false</c> whose inversion
    /// re-exposes the bare truthiness call.
    ///
    /// This is a deliberate CONSERVATIVE over-approximation: it ignores negation parity,
    /// so it treats <c>!x</c> forms (which keep a branch — e.g. the safe
    /// <c>c &amp;&amp; !*r</c>, or a valid inverted-truthiness <c>(t ? false : true)</c>)
    /// the same as the bare hazard and may decline a valid, branch-preserving raise.
    /// Declining an extra readable raise is sound; emitting a rebound or eager-deref one
    /// is not. Modeling parity exactly would require re-simulating
    /// <c>FoldBoolConstantComparison</c> and <see cref="Conditions.Negate"/> here — a
    /// fragile second implementation this intentionally avoids. (For the ternary re-form
    /// the comparison reduction has already run upstream, so only the negation peel is
    /// live there.)
    /// </summary>
    static IrExpression PeelReducibleBoolWrappers(IrExpression expression)
    {
        var inner = expression;
        while (true)
        {
            if (inner is LogicalNot { Operand: { } negated })
                inner = negated;
            else if (BoolConstantComparisonOperand(inner) is { } operand)
                inner = operand;
            else
                return inner;
        }
    }

    /// <summary>
    /// The non-constant operand of a bool-constant comparison
    /// <c>FoldBoolConstantComparison</c> reduces — the bool-typed left side of
    /// <c>x == true</c>/<c>x == false</c>/<c>x != true</c>/<c>x != false</c> (a
    /// right-hand bool <c>0</c>/<c>1</c> constant), regardless of comparison direction.
    /// Both directions are followed because the guarded-return fold's
    /// <see cref="Conditions.Negate"/> can invert the comparison on its arm before the
    /// reduction runs. Returns null for any non-reducible shape.
    /// </summary>
    static IrExpression? BoolConstantComparisonOperand(IrExpression expression)
    {
        if (expression is not Comparison { Kind: ComparisonKind.Equal or ComparisonKind.NotEqual } comparison)
            return null;
        if (comparison.Left.ResultType is not { Namespace: "System", Name: "Boolean" })
            return null;
        return BoolConstantValue(comparison.Right) is not null ? comparison.Left : null;
    }

    static bool? BoolConstantValue(IrExpression expression) => expression switch
    {
        Constant { Value: bool value } => value,
        Constant { Value: int value } when value is 0 or 1 => value == 1,
        _ => null,
    };
}
