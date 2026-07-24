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
    /// Whether lifting <paramref name="operand"/> as the surviving short-circuit operand
    /// of a spelled <c>c <paramref name="outerKind"/> OP</c> re-form would eagerly
    /// dereference a managed by-ref (<c>in</c>/<c>ref</c>/<c>out</c>) the compiler's
    /// branch had guarded — an observable <see cref="System.NullReferenceException"/>
    /// divergence on a null by-ref.
    ///
    /// csc collapses a short-circuit <c>L &amp;&amp; R</c> to a branchless <c>L &amp; R</c>
    /// (and <c>L || R</c> to <c>L | R</c>) exactly when <c>R</c> renders as a bare place
    /// — a local/argument/field load or a managed by-ref dereference, which csc treats as
    /// non-null and side-effect-free. A <em>compound</em> right operand (a bitwise
    /// <c>&amp;</c>/<c>|</c>, a different-kind logical sub-expression, a comparison, or a
    /// call) keeps the branch, so a by-ref dereference buried inside one stays guarded.
    /// The printer flattens a maximal same-kind logical chain, so <c>c <paramref
    /// name="outerKind"/> OP</c> makes every operand of <c>OP</c>'s <paramref
    /// name="outerKind"/>-chain a collapsible right operand of the emitted chain; a by-ref
    /// dereference reachable as one of those flattened operands (through the reducible
    /// bool wrappers of <see cref="PeelReducibleBoolWrappers"/>) is therefore dereferenced
    /// unconditionally.
    ///
    /// So the hazard is exactly a by-ref dereference that appears as a bare operand of the
    /// flattened <paramref name="outerKind"/> chain: a bare <c>*r</c>, a <c>*r == true</c>
    /// wrapper the fixpoint reduces back to a bare <c>*r</c>, or one under a same-kind
    /// logical composition (<c>a &amp;&amp; *r</c>, <c>*r &amp;&amp; Call()</c> when
    /// <paramref name="outerKind"/> is <c>And</c>). It is NOT a hazard when the by-ref
    /// dereference is confined to a compound operand that keeps the branch: a bitwise
    /// composition (<c>a &amp; *r</c>), a different-kind logical sub-expression
    /// (<c>*r || a</c> under an <c>And</c> lift), a comparison (<c>*r &gt; 0</c>), a by-ref
    /// struct field (<c>r.b</c>), or a call argument (<c>SomeCall(*r)</c>) — all recompile
    /// with the guard intact and stay foldable. The shared, parity-agnostic
    /// <see cref="PeelReducibleBoolWrappers"/> also strips a leading <c>!</c> / <c>== false</c>,
    /// so a <c>!*r</c>/<c>*r == false</c> operand — which keeps its branch and is faithful —
    /// is conservatively declined too; declining an extra readable raise is sound, and
    /// modeling the exact reduction parity would re-implement <c>FoldBoolConstantComparison</c>.
    /// A raw <em>pointer</em> dereference <c>*p</c> is excluded (its address kind is not
    /// <see cref="TypeRefKind.ByRef"/>): a pointer read can access-violate, so csc keeps
    /// the branch and lifting it is safe.
    ///
    /// Verified against SDK-csc <c>/optimize</c> IL + a null-by-ref runtime probe: the
    /// same-kind chain (<c>c &amp;&amp; a &amp;&amp; *r</c>), the by-ref-before-call
    /// (<c>c &amp;&amp; *r &amp;&amp; Call()</c>), and the bare operand
    /// (<c>c &amp;&amp; *r</c>) each diverge from their guarded original (null-by-ref
    /// throws), while the bitwise (<c>c &amp;&amp; (a &amp; *r)</c>), different-kind
    /// (<c>c &amp;&amp; (*r || a)</c>), comparison (<c>c &amp;&amp; *r &gt; 0</c>), field
    /// (<c>c &amp;&amp; r.b</c>), and negation (<c>c &amp;&amp; !*r</c>) folds compile to
    /// byte-identical IL and do not throw.
    /// </summary>
    internal static bool LiftEagerlyDerefsByRef(IrExpression operand, LogicalKind outerKind)
    {
        foreach (var chainOperand in FlattenLogicalChain(operand, outerKind))
        {
            if (PeelReducibleBoolWrappers(chainOperand)
                is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef })
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The operands of the maximal logical chain of <paramref name="outerKind"/>
    /// (<c>&amp;&amp;</c> or <c>||</c>) rooted at <paramref name="expression"/> — the
    /// operands the printer's same-kind flattening would splice into the surrounding
    /// <c>c <paramref name="outerKind"/> …</c> chain. A <see cref="LogicalBinary"/> of the
    /// same kind is descended into; anything else (a different-kind logical, a bitwise
    /// composition, a comparison, a call, a bare place) is a single opaque operand that
    /// keeps its own branch and is yielded whole.
    /// </summary>
    static IEnumerable<IrExpression> FlattenLogicalChain(IrExpression expression, LogicalKind outerKind)
    {
        if (expression is LogicalBinary logical && logical.Kind == outerKind)
        {
            foreach (var operand in FlattenLogicalChain(logical.Left, outerKind))
                yield return operand;
            foreach (var operand in FlattenLogicalChain(logical.Right, outerKind))
                yield return operand;
        }
        else
        {
            yield return expression;
        }
    }

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
    /// live there. It also serves <see cref="LiftEagerlyDerefsByRef"/>, which peels each
    /// flattened chain operand so a by-ref deref hidden under a <c>== true</c>/<c>!</c>
    /// wrapper is still reached.)
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
