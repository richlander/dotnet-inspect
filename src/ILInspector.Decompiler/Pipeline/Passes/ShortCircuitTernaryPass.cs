using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Normalizes a boolean conditional (ternary) whose <em>when-true</em> arm is a
/// bool constant into the short-circuit operator it spells. csc lowers
/// <c>a &amp;&amp; b</c> / <c>a || b</c> to a branch diamond that the slot-diamond and
/// boolean folds raise back to a ternary with a constant when-true arm:
/// <list type="bullet">
/// <item><c>c ? true : y</c> → <c>c || y</c></item>
/// <item><c>c ? false : y</c> → <c>!c &amp;&amp; y</c></item>
/// </list>
/// Only these two forms are opcode-exact. csc lays out <c>c ? true : y</c> and
/// <c>c ? false : y</c> with the same branch (condition true ⇒ take the constant,
/// fall through ⇒ evaluate <c>y</c>) that it emits for <c>c || y</c> / <c>!c &amp;&amp; y</c>,
/// so the rewrite recompiles to identical IL. The mirror forms with a constant
/// <em>when-false</em> arm (<c>c ? y : true</c>, <c>c ? y : false</c>) are
/// deliberately left alone: csc lays them out with the opposite branch polarity
/// than <c>!c || y</c> / <c>c &amp;&amp; y</c>, so re-forming the operator would trade
/// one branch shape for another (confirmed divergent against real csc-emitted
/// IL; the corpus compile-back gates guard the exact forms continuously).
///
/// <para>Even for the two exact forms the rewrite is declined when the surviving
/// operand renders as a bare, non-faulting place: a local, parameter, or
/// stack-slot load, or a managed by-ref (<c>in</c>/<c>ref</c>/<c>out</c>)
/// dereference. csc collapses <c>a &amp;&amp; b</c> / <c>a || b</c> to a branchless
/// <c>&amp;</c>/<c>|</c> for those operand shapes, so raising the ternary would
/// trade branch IL for branchless (see <see cref="RendersAsBranchlessBarePlace"/>).
/// Every faulting or side-effecting operand — field/element load, comparison,
/// call, and even a raw pointer dereference <c>*p</c> — keeps the branch and is
/// raised.</para>
///
/// <para>The condition is lifted into the left operand of the spelled operator, so
/// both forms decline a user-defined-truthiness condition — an <c>operator true</c>/
/// <c>operator false</c> evaluation the compiler inserts for a user type used in
/// boolean context. The printer strips such a call to its bare user-typed receiver,
/// so <c>c || y</c> / <c>!c &amp;&amp; y</c> would rebind to the user-defined conditional
/// <c>|</c>/<c>&amp;</c>, reselecting the overload and diverging in call tokens and
/// runtime result (<see cref="IsUserDefinedTruthiness"/>). A primitive-bool condition
/// — comparison, bool property/field/local/method, nested logical operator — binds
/// the primitive operator. The B-form additionally negates the condition, and
/// negation is only proven opcode-exact for a primitive-integer comparison, whose
/// dual is the same integer branch (e.g. <c>start &lt;= 0</c> ↔ <c>start &gt; 0</c>,
/// both <c>ble.s</c>). A float dual flips the ordered/unordered sense and a reference
/// <c>==</c>/<c>!=</c> dual flips the operator token, so the B-form fires only for a
/// confirmed-integer comparison (<see cref="NegateIsIntegerComparisonDual"/>); the
/// A-form (no negate) renders any primitive-bool condition verbatim.</para>
///
/// It fires only when the when-true arm is a bool constant and the when-false arm
/// is a non-constant bool expression; a both-constant <c>c ? true : false</c> is
/// an identity/negation other folds own, and is left untouched.
///
/// <para>Runs late — after every ternary-consuming pass (the tuple-binary and
/// switch-expression raises, which need the <c>c ? … : false</c> diamond shape) —
/// so it never starves a downstream consumer. It reconstructs a short-circuit
/// operator the compiler lowered to branches, so it is a decompiler-native
/// <see cref="NativeCategory.EmitArtifact"/> pass, not a Roslyn un-lowering.</para>
/// </summary>
public sealed class ShortCircuitTernaryPass : IIrPass
{
    public string Name => "short-circuit-ternary";

    public void Run(IrFunction function, PassContext context)
    {
        while (RewriteOne(function, context.Stepper))
        {
        }
    }

    static bool RewriteOne(IrFunction function, Stepper stepper)
    {
        foreach (var conditional in function.Descendants.OfType<Conditional>().ToList())
        {
            if (!TryClassify(conditional, out var kind, out bool negate))
                continue;

            // Both forms lift the condition into the LEFT operand of the spelled
            // `||`/`&&`. A user-defined-truthiness condition — an `operator true`/
            // `operator false` evaluation the compiler inserts for a user type used
            // in boolean context — renders as its bare user-typed receiver (the same
            // strip the printer applies), so `c || y` / `!c && y` would rebind to the
            // user-defined conditional `|`/`&`, reselecting the overload and diverging
            // in call tokens and runtime result. A primitive-bool condition
            // (comparison, bool property/field/local/method, nested logical operator)
            // binds the primitive operator, so decline only the truthiness lift.
            if (IsUserDefinedTruthiness(conditional.Condition))
                continue;

            // The B-form (`c ? false : y` → `!c && y`) additionally negates the
            // condition. The ONLY negation the pipeline is proven to spell back to
            // the same branch opcodes is a primitive-integer comparison's dual (e.g.
            // `start <= 0` → `start > 0`, both `ble.s`); a float/reference comparison
            // dual flips the ordered/unordered sense or the operator token, so the
            // B-form declines everything but a confirmed-integer comparison. The
            // A-form (no negate) renders the condition verbatim.
            if (negate && !NegateIsIntegerComparisonDual(conditional.Condition))
                continue;

            // The surviving operand is always the when-false arm. Decline when csc
            // would emit a branchless `&`/`|` for the spelled operator: raising the
            // ternary would trade branch IL for branchless (and, for a by-ref deref
            // reachable as a bare operand of the same-`kind` chain, eagerly dereference a
            // guarded location — an NRE divergence).
            if (RendersAsBranchlessBarePlace((IrExpression)conditional.WhenFalse, kind))
                continue;

            var children = conditional.DetachChildren();
            var condition = (IrExpression)children[0];
            var operand = (IrExpression)children[2];
            var left = negate ? Conditions.Negate(condition) : condition;

            stepper.StepOver("normalize constant-arm ternary to short-circuit &&/||", conditional);
            conditional.ReplaceWith(new LogicalBinary(kind, left, operand));
            return true;
        }
        return false;
    }

    /// <summary>
    /// A ternary whose when-true arm is a bool constant and whose when-false arm is
    /// a non-constant bool expression (the surviving short-circuit operand).
    /// <paramref name="negate"/> is whether the condition must be inverted.
    /// </summary>
    static bool TryClassify(Conditional c, out LogicalKind kind, out bool negate)
    {
        kind = default;
        negate = false;

        if (!IsBool(c.ResultType))
            return false;

        switch (c)
        {
            // c ? true : y  →  c || y
            case { WhenTrue: Constant { Value: true }, WhenFalse: var y } when y is not Constant:
                kind = LogicalKind.Or;
                negate = false;
                return true;

            // c ? false : y  →  !c && y
            case { WhenTrue: Constant { Value: false }, WhenFalse: var y } when y is not Constant:
                kind = LogicalKind.And;
                negate = true;
                return true;

            default:
                return false;
        }
    }

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
    ///
    /// The by-ref hazard is <paramref name="outerKind"/>-relative: the printer flattens
    /// only a same-kind logical chain, so a by-ref dereference is eager only when it is a
    /// bare operand of the emitted <paramref name="outerKind"/> chain — <see
    /// cref="ShortCircuitFidelity.LiftEagerlyDerefsByRef"/> flattens against that kind.
    /// A by-ref confined to a different-kind logical or a bitwise/call operand keeps its
    /// own branch and stays raisable.
    /// </summary>
    static bool RendersAsBranchlessBarePlace(IrExpression operand, LogicalKind outerKind)
        => operand is LoadLocal or LoadArgument or LoadStackSlot
           || ShortCircuitFidelity.LiftEagerlyDerefsByRef(operand, outerKind);

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
    static bool IsUserDefinedTruthiness(IrExpression condition)
        => ShortCircuitFidelity.IsUserDefinedTruthiness(condition);

    /// <summary>
    /// Whether negating <paramref name="condition"/> re-forms to something that
    /// recompiles to the same branch opcodes. The pipeline's <see cref="Conditions.Negate"/>
    /// plus the printer's negation folds are only proven opcode-exact for a
    /// confirmed primitive-integer comparison, whose dual is the same integer
    /// branch (e.g. <c>start &lt;= 0</c> → <c>start &gt; 0</c>, both <c>ble.s</c>).
    /// Every other negation the printer can fold to a different operator token or
    /// branch polarity, so the B-form declines it:
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
    static bool NegateIsIntegerComparisonDual(IrExpression condition)
    {
        if (condition is not Comparison comparison)
            return false;

        StackFamily? family = TypeFamilies.Of(comparison.Left.ResultType)
                              ?? TypeFamilies.Of(comparison.Right.ResultType);
        return family is StackFamily.I4 or StackFamily.I8 or StackFamily.I;
    }

    static bool IsBool(TypeRef? type)
        => type is { Namespace: "System", Name: "Boolean" };
}
