namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The opcode-fidelity guards shared by every pass that re-forms a short-circuit
/// <c>&amp;&amp;</c>/<c>||</c> by lifting a condition into the operator's left operand and
/// keeping one arm as the surviving right operand. csc lowers <c>a &amp;&amp; b</c>/<c>a || b</c>
/// to a branch diamond, so raising a diamond back to the operator is opcode-exact
/// only when the condition binds the primitive operator (not a user-defined
/// truthiness rebind), the negation — when the form negates the condition — is a
/// proven integer-comparison dual, and the surviving operand does not render as a
/// bare place csc would collapse to a branchless <c>&amp;</c>/<c>|</c>.
///
/// <para>Both <see cref="ShortCircuitTernaryPass"/> (the nested constant-arm ternary,
/// #3107) and <see cref="BooleanFoldingPass"/>'s guarded-return fold (#3114) perform
/// the same lift, so they share these predicates rather than mirroring them, keeping
/// the validated fidelity contract in one place.</para>
/// </summary>
static class ShortCircuitFidelity
{
    /// <summary>
    /// csc emits a branchless <c>&amp;</c>/<c>|</c> (not a short-circuit branch) for
    /// <c>a &amp;&amp; b</c>/<c>a || b</c> only when the non-short-circuiting operand
    /// renders as a bare, non-faulting place: a local, parameter, or stack-slot
    /// load, or a <em>managed by-ref</em> dereference (an <c>in</c>/<c>ref</c>/<c>out</c>
    /// parameter or <c>ref</c> local, which csc treats as non-null and side-effect
    /// -free, spelling as the bare referent). A residual stack slot renders as a bare
    /// synthetic local, so csc collapses it the same way. Raising those would trade
    /// branch IL for branchless (and, for the by-ref case, eagerly dereference a
    /// location the branch had guarded — an observable <c>NullReferenceException</c>
    /// divergence on a null by-ref). Every other operand keeps the branch and is
    /// safe to raise: a field/static/element load or call (can fault or has side
    /// effects), a comparison (even over bare locals), a nested logical operator,
    /// and — confirmed against real csc-emitted IL — a raw <em>pointer</em>
    /// dereference <c>*p</c> (which can access-violate, so csc keeps the branch).
    /// The operand map was verified opcode-by-opcode: only the bare-load and
    /// managed-by-ref shapes compile branchless; all others keep the branch.
    /// </summary>
    public static bool RendersAsBranchlessBarePlace(IrExpression operand)
        => operand is LoadLocal or LoadArgument or LoadStackSlot
           || operand is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef };

    /// <summary>
    /// The <em>soundness</em> extension of <see cref="RendersAsBranchlessBarePlace"/>
    /// for the case that predicate misses: a managed by-ref dereference that is not the
    /// surviving operand itself but is buried inside a same-kind logical chain the
    /// printer flattens into the emitted operator.
    ///
    /// <para>csc collapses <c>L &amp;&amp; R</c> to a branchless <c>L &amp; R</c> (and
    /// <c>L || R</c> to <c>L | R</c>) exactly when <c>R</c> renders as a bare place — a
    /// local/argument/field load or a managed by-ref dereference, which csc treats as
    /// non-null and side-effect-free. A <em>compound</em> right operand (a bitwise
    /// <c>&amp;</c>/<c>|</c>, a different-kind logical sub-expression, a comparison, or a
    /// call) keeps the branch, so a by-ref dereference confined to one stays guarded and
    /// is safe to fold. But the printer flattens a maximal same-kind logical chain, so
    /// when the fold emits <c>c <paramref name="outerKind"/> OP</c> and <c>OP</c> is a
    /// same-kind chain, every operand of that chain becomes a collapsible right operand
    /// of the flattened chain. A by-ref dereference reachable as one of those flattened
    /// operands is therefore dereferenced unconditionally — an observable
    /// <see cref="System.NullReferenceException"/> divergence on a null by-ref where the
    /// branch had guarded it (e.g. <c>c &amp;&amp; (*r &amp;&amp; Call())</c> flattens to
    /// <c>c &amp;&amp; *r &amp;&amp; Call()</c>; the call-free <c>c &amp; *r</c> prefix
    /// collapses branchless before the call barrier is reached).
    /// <see cref="RendersAsBranchlessBarePlace"/> only inspects the top-level operand, so
    /// it does not see a by-ref buried in such a chain; this predicate descends it.</para>
    ///
    /// <para>The reducible bool wrappers of <see cref="PeelReducibleBoolWrappers"/> are
    /// peeled BEFORE the same-kind flatten so a by-ref hidden under a leading <c>!</c>
    /// (<c>!(*r &amp;&amp; Call())</c>) or a bool-constant comparison
    /// (<c>(*r &amp;&amp; Call()) == true</c>) that <c>FoldBoolConstantComparison</c> would
    /// splice into the surrounding chain is still reached. Peeling only ignores negation
    /// parity, so it conservatively over-declines a branch-preserving <c>!*r</c> too —
    /// sound, because declining an extra valid readability raise never changes behavior
    /// while folding an unfaithful one does. A raw <em>pointer</em> dereference <c>*p</c>
    /// is excluded (its address kind is not <see cref="TypeRefKind.ByRef"/>): a pointer
    /// read can access-violate, so csc keeps the branch and lifting it is safe.</para>
    ///
    /// <para>Verified against SDK-csc <c>/optimize</c> IL + a null-by-ref runtime probe:
    /// the by-ref-before-call same-kind chain and the bare operand each diverge from
    /// their guarded original (null-by-ref throws), while the bitwise
    /// (<c>a &amp; *r</c>), different-kind logical (<c>*r || Call()</c> under an
    /// <c>&amp;&amp;</c> lift), comparison (<c>*r &gt; 0</c>), by-ref field (<c>r.b</c>),
    /// and call-argument (<c>Call(*r)</c>) folds compile to byte-identical IL and do not
    /// throw (#3114 follow-up to #3127).</para>
    /// </summary>
    public static bool LiftEagerlyDerefsByRef(IrExpression operand, LogicalKind outerKind)
    {
        // Peel BEFORE classifying the node: a same-kind chain hidden under a reducible
        // `== true`/`!= false` wrapper (which FoldBoolConstantComparison later strips,
        // splicing the chain into the surrounding `outerKind` chain) must be flattened
        // so its by-ref operands are reached. Peeling only after flattening treats such
        // a wrapper as an opaque compound and misses the buried by-ref.
        var peeled = PeelReducibleBoolWrappers(operand);
        if (peeled is LogicalBinary logical && logical.Kind == outerKind)
        {
            return LiftEagerlyDerefsByRef(logical.Left, outerKind)
                || LiftEagerlyDerefsByRef(logical.Right, outerKind);
        }
        return peeled is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef };
    }

    /// <summary>
    /// Whether <paramref name="condition"/> is a user-defined-truthiness evaluation
    /// — a call to <c>operator true</c>/<c>operator false</c> the compiler inserts
    /// when a user type is used in boolean context. The printer strips such a call
    /// to its bare user-typed receiver (it renders <c>op_True(a)</c>/<c>op_False(a)</c>
    /// as <c>a</c>), so lifting it into a short-circuit operand — <c>a || y</c> /
    /// <c>!a &amp;&amp; y</c> — would rebind to the user-defined conditional <c>|</c>/<c>&amp;</c>,
    /// reselecting the overload and changing the call tokens and runtime result. Only
    /// a type carrying <c>op_True</c>/<c>op_False</c> can bind a user-defined
    /// <c>||</c>/<c>&amp;&amp;</c>, so this is the complete rebind set; a primitive-bool
    /// condition (comparison, bool property/field/local/method, nested logical
    /// operator) binds the primitive operator and is safe to lift.
    /// </summary>
    public static bool IsUserDefinedTruthiness(IrExpression condition)
        => condition is Call { Callee.Name: "op_True" or "op_False" };

    /// <summary>
    /// Whether negating <paramref name="condition"/> re-forms to something that
    /// recompiles to the same branch opcodes. The pipeline's <see cref="Conditions.Negate"/>
    /// plus the printer's negation folds are only proven opcode-exact for a
    /// confirmed primitive-integer comparison, whose dual is the same integer
    /// branch (e.g. <c>start &lt;= 0</c> → <c>start &gt; 0</c>, both <c>ble.s</c>).
    /// Every other negation the printer can fold to a different operator token or
    /// branch polarity, so the negating form declines it:
    /// <list type="bullet">
    /// <item>a <see cref="Comparison"/> over float operands takes a dual that
    /// flips the ordered/unordered sense (<c>blt.s</c> vs <c>blt.un.s</c>);</item>
    /// <item>an <c>Equal</c>/<c>NotEqual</c> <see cref="Comparison"/> over a
    /// non-integer operand (string/object/enum/struct), and a negated
    /// comparison/equality operator <em>call</em> (<c>op_Equality</c>,
    /// <c>op_LessThan</c>, …), flip to the inverse operator and — for a
    /// user-defined operator — a different call token;</item>
    /// <item>a truthiness test (<c>is</c>/<c>is null</c>/<c>!= 0</c>) inverts to
    /// its own opposite spelling.</item>
    /// </list>
    /// Declining hands those back to the base pipeline's faithful rendering. The
    /// integer family is read the same way <c>InvertComparison</c> reads it.
    /// </summary>
    public static bool NegateIsIntegerComparisonDual(IrExpression condition)
    {
        if (condition is not Comparison comparison)
            return false;

        StackFamily? family = TypeFamilies.Of(comparison.Left.ResultType)
                              ?? TypeFamilies.Of(comparison.Right.ResultType);
        return family is StackFamily.I4 or StackFamily.I8 or StackFamily.I;
    }

    /// <summary>
    /// Whether negating <paramref name="condition"/> is opcode-exact for the
    /// guarded-return fold. This is deliberately broader than
    /// <see cref="NegateIsIntegerComparisonDual"/>: besides a primitive-integer
    /// comparison dual, the guarded-return form has folded a <em>reference
    /// null-branch</em> since before #3114 — a <c>brtrue</c>/<c>brfalse</c> over a
    /// reference whose negation flips only branch polarity, spelled <c>is null</c>
    /// ↔ <c>is not null</c> (the <c>String.IsNullOrEmpty</c> witness
    /// <c>value is null || value.Length == 0</c>). A polarity flip re-lowers to the
    /// opposite <c>brtrue</c>/<c>brfalse</c> on the same operand, so it is exact.
    ///
    /// <para>The nested ternary pass (#3107) intentionally keeps the narrower
    /// integer-only gate; broadening it is separate follow-up, so this predicate
    /// lives beside — not in place of — <see cref="NegateIsIntegerComparisonDual"/>.
    /// The hazards stay declined: a float comparison (ordered/unordered flip), a
    /// non-integer <c>==</c>/<c>!=</c> comparison or comparison-operator
    /// <em>call</em> (operator-token flip), and a bare bool are none of the
    /// accepted shapes.</para>
    /// </summary>
    public static bool NegationIsOpcodeExact(IrExpression condition)
        => NegateIsIntegerComparisonDual(condition) || IsReferenceNullBranch(condition);

    /// <summary>
    /// Whether <paramref name="condition"/> is a reference null-branch, whose
    /// negation is only a branch-polarity flip. Covers an explicit
    /// <c>x == null</c>/<c>x != null</c> test, an <c>isinst</c> type test, and a
    /// bare reference branch operand — recognised the same way the printer's
    /// truthiness spelling recognises a reference: a generic instance (provably a
    /// reference), a signature <see cref="ValueTypeHint.ReferenceType"/> hint, or a
    /// corelib object/string/array (<see cref="StackFamily.O"/>). A cross-assembly
    /// user type with no reference hint stays unrecognised and declines,
    /// conservatively handing that shape back to the faithful branch rendering.
    /// </summary>
    static bool IsReferenceNullBranch(IrExpression condition)
    {
        if (condition is Comparison { Kind: ComparisonKind.Equal or ComparisonKind.NotEqual } comparison
            && (comparison.Left is Constant { Value: null } || comparison.Right is Constant { Value: null }))
        {
            return true;
        }

        if (condition is IsInstance)
            return true;

        TypeRef? type = condition.ResultType;
        if (type is null)
            return false;
        if (type.Kind == TypeRefKind.GenericInstance)
            return true;
        if (type.DeclaredValueTypeHint == ValueTypeHint.ReferenceType)
            return true;
        return TypeFamilies.Of(type) == StackFamily.O;
    }

    /// <summary>
    /// Follow the reducible operand through the wrappers <see cref="BooleanFoldingPass"/>'s
    /// fixpoint strips or inverts <em>after</em> its guarded-return fold has lifted the
    /// expression: leading logical negations, and bool-constant comparisons
    /// (<c>x == true</c>/<c>x == false</c>/<c>x != true</c>/<c>x != false</c>) that
    /// <c>FoldBoolConstantComparison</c> reduces to <c>x</c> or <c>!x</c>. It follows the
    /// non-constant side of each comparison so a chain nested under any run of these
    /// wrappers is still reached. Used by <see cref="LiftEagerlyDerefsByRef"/>, which peels
    /// each operand before the same-kind flatten so a by-ref deref hidden under a
    /// <c>== true</c>/<c>!</c> wrapper — even one wrapping a whole same-kind chain — is still
    /// reached. This is a deliberate CONSERVATIVE over-approximation: it ignores negation
    /// parity, so it may decline a valid branch-preserving raise; modeling parity exactly
    /// would re-implement <c>FoldBoolConstantComparison</c>/<see cref="Conditions.Negate"/>
    /// here — a fragile second implementation this avoids.
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
    /// <c>x == true</c>/<c>x == false</c>/<c>x != true</c>/<c>x != false</c> (a right-hand
    /// bool <c>0</c>/<c>1</c> constant), regardless of comparison direction. Both
    /// directions are followed because the guarded-return fold's
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
