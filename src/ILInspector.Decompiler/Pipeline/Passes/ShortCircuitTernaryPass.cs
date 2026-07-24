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
/// trade branch IL for branchless (see <see cref="ShortCircuitFidelity.RendersAsBranchlessBarePlace"/>).
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
/// runtime result (<see cref="ShortCircuitFidelity.IsUserDefinedTruthiness"/>). A primitive-bool condition
/// — comparison, bool property/field/local/method, nested logical operator — binds
/// the primitive operator. The B-form additionally negates the condition, and
/// negation is only proven opcode-exact for a primitive-integer comparison, whose
/// dual is the same integer branch (e.g. <c>start &lt;= 0</c> ↔ <c>start &gt; 0</c>,
/// both <c>ble.s</c>). A float dual flips the ordered/unordered sense and a reference
/// <c>==</c>/<c>!=</c> dual flips the operator token, so the B-form fires only for a
/// confirmed-integer comparison (<see cref="ShortCircuitFidelity.NegateIsIntegerComparisonDual"/>); the
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
            if (ShortCircuitFidelity.IsUserDefinedTruthiness(conditional.Condition))
                continue;

            // The B-form (`c ? false : y` → `!c && y`) additionally negates the
            // condition. The ONLY negation the pipeline is proven to spell back to
            // the same branch opcodes is a primitive-integer comparison's dual (e.g.
            // `start <= 0` → `start > 0`, both `ble.s`); a float/reference comparison
            // dual flips the ordered/unordered sense or the operator token, so the
            // B-form declines everything but a confirmed-integer comparison. The
            // A-form (no negate) renders the condition verbatim.
            if (negate && !ShortCircuitFidelity.NegateIsIntegerComparisonDual(conditional.Condition))
                continue;

            // The surviving operand is always the when-false arm. Decline when csc
            // would emit a branchless `&`/`|` for the spelled operator: raising the
            // ternary would trade branch IL for branchless.
            if (ShortCircuitFidelity.RendersAsBranchlessBarePlace((IrExpression)conditional.WhenFalse))
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

    static bool IsBool(TypeRef? type)
        => type is { Namespace: "System", Name: "Boolean" };
}
