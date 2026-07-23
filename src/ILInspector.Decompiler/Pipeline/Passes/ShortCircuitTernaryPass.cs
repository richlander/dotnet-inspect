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
/// operand is a bare local, parameter, or stack-slot load: csc collapses
/// <c>a &amp;&amp; b</c> / <c>a || b</c> to a branchless <c>&amp;</c>/<c>|</c> for those
/// operand shapes, so raising the ternary would trade branch IL for branchless
/// (see <see cref="IsBareLocalOrArgumentLoad"/>). Every computed or memory-loaded
/// operand (field/element load, comparison, call, negation, nested logical
/// operator) keeps the branch.</para>
///
/// <para>The B-form additionally negates the condition, and negation is only
/// proven opcode-exact for a primitive-integer comparison, whose dual is the same
/// integer branch (e.g. <c>start &lt;= 0</c> ↔ <c>start &gt; 0</c>, both
/// <c>ble.s</c>). Every other condition's negation the printer can fold to a
/// different operator token or branch polarity — a float dual flips the
/// ordered/unordered sense, an <c>==</c>/<c>!=</c> dual or negated operator call
/// flips to the inverse operator, and an <c>is</c>/<c>is null</c>/<c>!= 0</c>
/// truthiness test inverts to its opposite — so the B-form fires only for a
/// confirmed-integer comparison condition and declines the rest
/// (<see cref="NegateIsIntegerComparisonDual"/>). The A-form (no negate) renders
/// the condition verbatim and accepts any condition shape.</para>
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

            // The B-form (`c ? false : y` → `!c && y`) negates the condition. The
            // ONLY negation the pipeline is proven to spell back to the same branch
            // opcodes is a primitive-integer comparison's dual (e.g. `start <= 0` →
            // `start > 0`, both `ble.s`). Every other negation the printer can fold
            // to a different operator token or branch polarity, so the B-form fires
            // only for a confirmed-integer comparison; the A-form (no negate)
            // renders the condition verbatim and is unaffected.
            if (negate && !NegateIsIntegerComparisonDual(conditional.Condition))
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
    /// a bare local, parameter, or stack-slot load — the operand shapes whose
    /// evaluation it can prove is side-effect-free and cannot throw. (A residual
    /// stack slot renders as a bare synthetic local, so csc collapses it the same
    /// way.) Every other operand (field/element load, comparison, call, negation,
    /// nested logical operator) keeps the branch, so raising its ternary is
    /// opcode-exact. This mirrors Roslyn's branchless eligibility
    /// (local/parameter/constant operands; a constant operand cannot occur here
    /// because the pass requires the non-short-circuiting arm to be non-constant).
    /// Confirmed against real csc-emitted IL: a bare-load operand compiles
    /// branchless, every computed/memory-loaded operand keeps the branch.
    /// </summary>
    static bool IsBareLocalOrArgumentLoad(IrExpression operand)
        => operand is LoadLocal or LoadArgument or LoadStackSlot;

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
