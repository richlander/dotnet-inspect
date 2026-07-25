using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Opt-in <b>style lens</b> (#3138), off the default pipeline and gated by
/// <see cref="PrinterOptions.PreferBranchlessBoolean"/>: rewrites a flat guarded
/// boolean return whose then- or tail-arm is a <c>bool</c> constant —
/// <c>if (c) return A; return false;</c>, <c>if (c) return true; return X;</c>,
/// and the negating duals — into the compact short-circuit "bool hack"
/// (<c>return c &amp;&amp; A;</c>, <c>return c || X;</c>, <c>return !c &amp;&amp; X;</c>,
/// <c>return !c || A;</c>). This is the exact fold
/// <see cref="BooleanFoldingPass"/>'s <c>FoldGuardReturn</c> emitted <em>before</em>
/// #3114 taught it to decline the shapes that are not opcode-faithful; the lens
/// re-offers precisely those declined shapes.
///
/// Unlike the ternary lens (<see cref="PreferConditionalReturnPass"/>), this form
/// is <b>not oracle-endorsed</b> — dotnet/runtime's <c>.editorconfig</c> never
/// encourages <c>!(a &amp;&amp; b) &amp;&amp; c</c> over a readable ternary — so it is a
/// user <em>compactness/branchless</em> preference, opt-in only, and never part of
/// the "full taste" aggregate.
///
/// It runs only on the opt-in raised path, AFTER the default pipeline has
/// deliberately left the guarded bool return flat because no short-circuit fold of
/// that shape recompiles to the original branch opcodes (a bare-load operand csc
/// collapses to a branchless <c>&amp;</c>/<c>|</c>, or a negation that flips
/// ordered/unordered or operator tokens — see <see cref="ShortCircuitFidelity"/>).
/// Those are <b>opcode</b> divergences, not behavior ones: the short-circuit
/// spelling evaluates the same condition, keeps the same surviving operand, and
/// preserves short-circuit order, so the rewrite is <b>behavior-preserving</b>.
/// That is what makes it a tier-3 lens rather than a raising pass — its output is
/// byte-divergent and must never feed the compile-back fidelity gates.
///
/// <para>Two hazards from the default's guards are genuinely about <em>behavior</em>
/// (not just bytes) and are therefore KEPT here:</para>
/// <list type="bullet">
/// <item>A <b>user-defined-truthiness</b> condition
/// (<see cref="ShortCircuitFidelity.IsUserDefinedTruthiness"/>): lifting it into a
/// short-circuit operand rebinds to the user-defined <c>&amp;&amp;</c>/<c>||</c>,
/// reselecting an overload and changing the runtime result (and often failing to
/// compile).</item>
/// <item>A surviving operand that is a <b>managed by-ref dereference</b>: csc
/// compiles the short-circuit form branchless, eagerly dereferencing a location the
/// original branch had guarded — an observable <c>NullReferenceException</c>
/// divergence on a null by-ref. Every other bare-place operand (local, parameter,
/// stack slot) is side-effect-free and non-faulting, so eager evaluation is
/// behavior-safe and IS re-offered.</item>
/// </list>
/// </summary>
public sealed class PreferBranchlessBooleanPass : IIrPass
{
    public string Name => "prefer-branchless-boolean";

    public void Run(IrFunction function, PassContext context)
    {
        // Fixpoint: folding an inner guarded return can expose an outer one whose
        // tail is now the freshly minted short-circuit return.
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

    // Mirrors FoldGuardReturn's guarded-return match and short-circuit shape
    // choice exactly (else-less if whose Then is a single Return, immediately
    // followed by a sibling Return, both arms System.Boolean, exactly one arm a
    // bool constant, neither Return a live branch target). The default pass
    // declines the fidelity-erasing folds; this lens re-offers them under the
    // behavior-only contract, keeping only the two BEHAVIOR hazards.
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

        // Decide the shape COMPLETELY before any detach: bailing after a mutation
        // leaves a mutilated IfStatement whose slots have shifted.
        bool? tailConstant = tailValue is Constant { Value: bool tail } ? tail : null;
        bool? thenConstant = thenValue is Constant { Value: bool then } ? then : null;

        // The short-circuit "bool hack" only exists when exactly one arm is a bool
        // constant (the other becomes the surviving operand). Both-variable arms are
        // the ternary's domain (return c ? A : B;), and both-opposite-constants
        // already fold to `return c;`/`return !c;` in the default pipeline, so
        // neither survives to here as a fold candidate.
        bool bothConstant = thenConstant is not null && tailConstant is not null;
        if ((tailConstant is null && thenConstant is null) || bothConstant)
            return false;

        // negate/operand map (mirrors the fold shapes chosen below):
        //   tailConstant == true : Or(!c, thenValue)   negates; operand = thenValue
        //   tailConstant == false: And(c, thenValue)              operand = thenValue
        //   thenConstant == true : Or(c, tailValue)               operand = tailValue
        //   thenConstant == false: And(!c, tailValue)  negates; operand = tailValue
        IrExpression survivingOperand = tailConstant is not null ? thenValue : tailValue;

        // BEHAVIOR/SCOPE guards (kept from the default; NOT the opcode-only guards).
        //  - User-defined truthiness ANYWHERE in the condition takes the shape out
        //    of this lens's scope. Every fold of such a condition is one of:
        //    (a) INVALID — the printer strips op_True/op_False to its bare user-typed
        //        receiver, so `t && b` / `t || b` fails to compile (no user &/|,
        //        CS0019); (b) BEHAVIOR-DIVERGENT — were a user `&`/`|` present, the
        //        lift would rebind to its operator semantics instead of the guarded
        //        control flow; or (c) VALID but NOT branchless — a negation spelled
        //        as the ternary `(t ? false : true)` re-embeds a branch, which is not
        //        the compact short-circuit form this lens exists to produce. So the
        //        lens declines every user-truthiness condition wholesale. The direct
        //        root check is not enough (a `LogicalNot(op_True(t))` unwraps to a
        //        bare op_True under Conditions.Negate), so we scan the whole subtree.
        //        Plain bools, comparisons, and nullable bools never emit
        //        op_True/op_False, so this only ever declines the out-of-scope
        //        truthiness family; over-declining is always valid and faithful.
        //    The default's FoldGuardReturn is incidentally shielded from the wrapped
        //    case by the opcode-fidelity negation guard this lens relaxes, so the
        //    lens must guard it explicitly.
        //  - A surviving managed by-ref dereference is eagerly evaluated by csc's
        //    branchless lowering, dereferencing a location the branch had guarded
        //    (null-by-ref NRE divergence). Every other operand is behavior-safe:
        //    a bare local/arg/stack load has no side effect and cannot fault, and a
        //    field/element/call operand keeps the branch (csc does not go branchless
        //    for it), so short-circuit order is preserved either way.
        if (InvolvesUserDefinedTruthiness(guard.Condition)
            || survivingOperand is LoadIndirect { Address.ResultType.Kind: TypeRefKind.ByRef })
        {
            return false;
        }

        var condition = guard.Condition;
        condition.Detach();
        IrExpression folded;
        if (tailConstant is { } tailBool)
        {
            thenValue.Detach();
            // if (c) return A; return true;  ≡ return !c || A;
            // if (c) return A; return false; ≡ return c && A;
            folded = tailBool
                ? new LogicalBinary(LogicalKind.Or, Conditions.Negate(condition), thenValue)
                : new LogicalBinary(LogicalKind.And, condition, thenValue);
        }
        else
        {
            tailValue.Detach();
            // if (c) return true;  return X; ≡ return c || X;
            // if (c) return false; return X; ≡ return !c && X;
            folded = thenConstant == true
                ? new LogicalBinary(LogicalKind.Or, condition, tailValue)
                : new LogicalBinary(LogicalKind.And, Conditions.Negate(condition), tailValue);
        }

        tailReturn.Detach();
        stepper.StepOver("rewrite guarded return into branchless short-circuit", guard);
        var foldedReturn = new Return(folded);
        foldedReturn.InheritSourceOffset(guard);
        guard.ReplaceWith(foldedReturn);
        return true;
    }

    // True when a user-defined-truthiness call (op_True/op_False) appears anywhere
    // in the condition subtree — as the root, under a LogicalNot the negation would
    // unwrap, or nested in a compound condition. Such a condition is out of this
    // lens's scope: its fold is invalid, behavior-divergent, or a non-branchless
    // ternary re-embed (see the call-site comment). The scan checks the condition
    // itself AND its descendants because IrNode.Descendants is self-exclusive. It is
    // deliberately conservative: over-declining only leaves the shape flat, which is
    // always valid and faithful.
    static bool InvolvesUserDefinedTruthiness(IrExpression condition)
    {
        if (ShortCircuitFidelity.IsUserDefinedTruthiness(condition))
            return true;
        foreach (var node in condition.Descendants)
        {
            if (node is IrExpression expr && ShortCircuitFidelity.IsUserDefinedTruthiness(expr))
                return true;
        }

        return false;
    }
}
