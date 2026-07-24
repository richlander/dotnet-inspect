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
    /// Whether lifting <paramref name="operand"/> behind a spelled <c>&amp;&amp;</c>/
    /// <c>||</c> would eagerly dereference a managed by-ref
    /// (<c>in</c>/<c>ref</c>/<c>out</c>) the branch had guarded. csc treats a managed
    /// by-ref as non-null and side-effect-free, so it collapses <c>c &amp;&amp; OP</c>
    /// to a branchless <c>c &amp; OP</c> whenever <c>OP</c> is entirely side-effect-free
    /// (has no call). That collapse re-evaluates every by-ref dereference in <c>OP</c>
    /// unconditionally — an observable <see cref="System.NullReferenceException"/>
    /// divergence on a null by-ref that the compiler's branch had short-circuited.
    ///
    /// So the hazard is present exactly when <c>OP</c> contains a managed by-ref
    /// dereference AND contains no call: a bare <c>*r</c>, a bool-constant comparison
    /// (<c>*r == true</c>) or negation (<c>!*r</c>) over one, or — the case the earlier
    /// leaf-only peel missed — one nested inside a raised logical/bitwise composition
    /// (<c>a &amp;&amp; *r</c>, <c>a &amp; *r</c>, <c>r | a</c>; csc branchless-collapses
    /// the inner <c>a &amp;&amp; r</c> to <c>a &amp; r</c>, and the outer lift then
    /// collapses that whole operand). Conversely a call anywhere in <c>OP</c> is a
    /// side-effect barrier csc will not hoist past the lift, so it keeps the branch and
    /// guards the whole operand — <c>c &amp;&amp; SomeCall(*r)</c> stays faithful and is
    /// deliberately kept foldable. A raw <em>pointer</em> dereference <c>*p</c> is
    /// excluded (its address kind is not <see cref="TypeRefKind.ByRef"/>): a pointer read
    /// can access-violate, so csc keeps the branch and lifting it is safe.
    /// </summary>
    internal static bool IsManagedByRefDeref(IrExpression operand)
        => ContainsManagedByRefDereference(operand) && !ContainsCall(operand);

    /// <summary>
    /// Whether the <paramref name="operand"/> subtree contains a managed by-ref
    /// (<c>in</c>/<c>ref</c>/<c>out</c>) dereference. A raw pointer dereference is
    /// excluded — only <see cref="TypeRefKind.ByRef"/> addresses qualify.
    /// </summary>
    static bool ContainsManagedByRefDereference(IrExpression operand)
        => operand is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef }
            || operand.Descendants.OfType<LoadIndirect>().Any(load => load.Address.ResultType is { Kind: TypeRefKind.ByRef });

    /// <summary>
    /// Whether the <paramref name="operand"/> subtree contains a call — the
    /// side-effect barrier csc will not hoist past a lifted short-circuit, so it keeps
    /// the compiler's branch and every by-ref dereference the call transitively guards
    /// stays guarded.
    /// </summary>
    static bool ContainsCall(IrExpression operand)
        => operand is Call or CallIndirect
            || operand.Descendants.Any(node => node is Call or CallIndirect);

    /// <summary>
    /// Follow the reducible operand through the wrappers that
    /// <see cref="BooleanFoldingPass"/>'s fixpoint strips or inverts <em>after</em> its
    /// guarded-return fold has already lifted the expression: leading logical negations
    /// (the fold lifts a condition through <see cref="Conditions.Negate"/> on its
    /// <c>||</c>/negated-<c>&amp;&amp;</c> arms), and bool-constant comparisons
    /// (<c>x == true</c>/<c>x == false</c>/<c>x != true</c>/<c>x != false</c>) that
    /// <c>FoldBoolConstantComparison</c> reduces to <c>x</c> or <c>!x</c>. It follows the
    /// non-constant side of each comparison so a truthiness call nested under any chain of
    /// these wrappers is still reached — e.g. <c>op_True(t) == false</c> whose outer
    /// <see cref="Conditions.Negate"/> inverts the inner comparison and reduces back to
    /// the bare truthiness call, or the double negation <c>(op_True(t) == false) == false</c>.
    ///
    /// This is a deliberate CONSERVATIVE over-approximation: it ignores negation parity,
    /// so it treats <c>!x</c> forms (which keep a branch — e.g. a valid inverted-truthiness
    /// <c>(t ? false : true)</c>) the same as the bare hazard and may decline a valid,
    /// branch-preserving raise. Declining an extra readable raise is sound; emitting a
    /// rebound one is not. Modeling parity exactly would require re-simulating
    /// <c>FoldBoolConstantComparison</c> and <see cref="Conditions.Negate"/> here — a
    /// fragile second implementation this intentionally avoids. (For the ternary re-form
    /// the comparison reduction has already run upstream, so only the negation peel is
    /// live there. The by-ref hazard no longer routes through this peel — see
    /// <see cref="IsManagedByRefDeref"/>, which scans the whole operand subtree.)
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
