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
/// operand is a bare local or parameter load: csc collapses <c>a &amp;&amp; b</c> /
/// <c>a || b</c> to a branchless <c>&amp;</c>/<c>|</c> for that one operand shape, so
/// raising the ternary would trade branch IL for branchless (see
/// <see cref="IsBareLocalOrArgumentLoad"/>). Every computed or memory-loaded
/// operand (field/element load, comparison, call, negation, nested logical
/// operator) keeps the branch and is opcode-exact
/// (<see cref="Conditions.Negate"/> re-forms <c>!c</c> as the comparison dual csc
/// already branches on, e.g. <c>start &lt;= 0</c> ↔ <c>start &gt; 0</c>). The one
/// exception is a float comparison condition in the B-form: negating it flips the
/// ordered/unordered sense, which the printer cannot spell, so that shape is
/// declined (<see cref="IsFloatComparison"/>).</para>
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

            // The B-form (`c ? false : y` → `!c && y`) negates the condition.
            // Negating a float comparison keeps the value NaN-correct only by
            // toggling its ordered/unordered sense (Conditions.Negate flips the
            // unordered flag), but the C# printer spells both senses with the same
            // relational operator, so recompiling the negated float comparison
            // trades csc's ordered branch (blt.s) for the unordered one
            // (blt.un.s). Decline so the rewrite stays opcode-exact; the A-form
            // (no negate) and integer/reference comparisons are unaffected.
            if (negate && IsFloatComparison(conditional.Condition))
                continue;

            // The surviving operand is always the when-false arm. Decline when csc
            // would emit a branchless `&`/`|` for the spelled operator: raising the
            // ternary would trade branch IL for branchless.
            if (IsBareLocalOrArgumentLoad((IrExpression)conditional.WhenFalse))
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
    /// <c>a &amp;&amp; b</c>/<c>a || b</c> only when the non-short-circuiting operand is
    /// a bare local or parameter load — the one shape whose evaluation it can prove
    /// is side-effect-free and cannot throw. Every other operand (field/element
    /// load, comparison, call, negation, nested logical operator) keeps the branch,
    /// so raising its ternary is opcode-exact. This mirrors Roslyn's branchless
    /// eligibility (local/parameter/constant operands; a constant operand cannot
    /// occur here because the pass requires the non-short-circuiting arm to be
    /// non-constant). Confirmed against real csc-emitted IL: a bare-load operand
    /// compiles branchless, every computed/memory-loaded operand keeps the branch.
    /// </summary>
    static bool IsBareLocalOrArgumentLoad(IrExpression operand)
        => operand is LoadLocal or LoadArgument;

    /// <summary>
    /// A comparison over IEEE-754 <c>float</c>/<c>double</c> operands. Its C#
    /// printed form (a single relational operator) cannot distinguish the ordered
    /// and unordered senses, so a negated float comparison does not round-trip to
    /// the same branch opcode (<c>blt.s</c> vs <c>blt.un.s</c>). The B-form decline
    /// in <see cref="RewriteOne"/> uses this to keep the rewrite opcode-exact.
    /// </summary>
    static bool IsFloatComparison(IrExpression condition)
        => condition is Comparison comparison
           && (TypeFamilies.Of(comparison.Left.ResultType) == StackFamily.F
               || TypeFamilies.Of(comparison.Right.ResultType) == StackFamily.F);

    static bool IsBool(TypeRef? type)
        => type is { Namespace: "System", Name: "Boolean" };
}
