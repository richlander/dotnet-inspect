using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Opt-in <b>style lens</b> (#3138), off the default pipeline and gated by
/// <see cref="PrinterOptions.PreferConditionalExpressionReturn"/>: rewrites a
/// flat guarded boolean return
/// <c>if (c) return A; return B;</c> into the conditional expression
/// <c>return c ? A : B;</c> — the runtime <c>.editorconfig</c> IDE0046 preferred
/// spelling (<c>dotnet_style_prefer_conditional_expression_over_return</c>).
///
/// It runs only on the opt-in raised path, AFTER the default pipeline has
/// deliberately left the shape flat: no short-circuit <c>&amp;&amp;</c>/<c>||</c>
/// fold of a bool guarded return is opcode-faithful, so
/// <see cref="BooleanFoldingPass"/> declines it (#3114 /
/// <c>ShortCircuitFidelity</c>) and the flat <c>if</c>/<c>return</c> pair is what
/// survives to render.
///
/// The ternary is the CANONICAL desugaring of the guarded return: same condition,
/// same arms, same short-circuit evaluation order, so the rewrite is
/// unconditionally <b>behavior-preserving</b>. It is <b>not</b> opcode-faithful —
/// the recompiled branch stream flips polarity and reorders blocks — so this pass
/// must never run on a byte-faithful path (default / lowered) and its output must
/// not feed the compile-back fidelity gates. That contract is what makes it a
/// tier-3 style lens rather than a raising pass.
/// </summary>
public sealed class PreferConditionalReturnPass : IIrPass
{
    public string Name => "prefer-conditional-return";

    public void Run(IrFunction function, PassContext context)
    {
        // Fixpoint: collapsing an inner guarded return can expose an outer one
        // whose tail is now the freshly minted `return c ? A : B;`.
        while (RewriteOnce(function, context.Stepper))
        {
        }
    }

    static bool RewriteOnce(IrFunction function, Stepper stepper)
    {
        foreach (var node in function.Descendants.ToList())
        {
            if (node.Parent is null || node is not IfStatement guard)
                continue;
            if (TryRewriteGuardReturn(function, guard, stepper))
                return true;
        }

        return false;
    }

    // Mirrors FoldGuardReturn's guarded-return match exactly (else-less if whose
    // Then is a single Return, immediately followed by a sibling Return, both
    // arms System.Boolean, neither Return a live branch target). The default
    // pass declines the FIDELITY-erasing short-circuit fold of this shape; the
    // ternary is its behavior-equivalent tier-3 rendering.
    static bool TryRewriteGuardReturn(IrFunction function, IfStatement guard, Stepper stepper)
    {
        if (guard.HasElse || guard.Parent is not Block container)
            return false;
        if (guard.Then.Children.Count != 1
            || guard.Then.Children[0] is not Return { Value: { } thenValue } thenReturn)
        {
            return false;
        }
        if (guard.ChildIndex + 1 >= container.Children.Count
            || container.Children[guard.ChildIndex + 1] is not Return { Value: { } tailValue } tailReturn)
        {
            return false;
        }
        if (thenValue.ResultType is not { Namespace: "System", Name: "Boolean" }
            || tailValue.ResultType is not { Namespace: "System", Name: "Boolean" })
        {
            return false;
        }
        if (BooleanFoldingPass.ConsumesBranchTarget(function, guard, thenReturn, tailReturn))
            return false;

        // `if (c) return A; return B;` ≡ `return c ? A : B;` — same condition
        // polarity, same arms, same evaluation order (IDE0046 keeps the guard
        // condition as-is, unlike the polarity-swapped FoldTernaryReturn).
        var condition = guard.Condition;
        condition.Detach();
        thenValue.Detach();
        tailValue.Detach();

        var ternary = new Conditional(condition, thenValue, tailValue)
        {
            MergedType = TypeRef.CoreLib("System", "Boolean"),
        };

        tailReturn.Detach();
        stepper.StepOver("rewrite guarded return into conditional expression", guard);
        var mergedReturn = new Return(ternary);
        mergedReturn.InheritSourceOffset(guard);
        guard.ReplaceWith(mergedReturn);
        return true;
    }
}
