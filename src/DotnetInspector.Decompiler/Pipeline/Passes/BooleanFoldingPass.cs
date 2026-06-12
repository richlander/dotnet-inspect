namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>
/// Folds structured shapes into short-circuit boolean expressions and
/// ternaries — the inverse of the compiler's condition lowering, running
/// after structuring so the shapes are tree nodes:
///
/// 1. Nested guards: <c>if (a) { if (b) { T } }</c> → <c>if (a &amp;&amp; b) { T }</c>.
/// 2. Guard-return chains: <c>if (c) return A; return true;</c> →
///    <c>return !c || A;</c> (and the &amp;&amp;/constant-arm duals).
/// 3. Slot diamonds: <c>if (c) { S = A } else { S = B }</c> → <c>S = c ? A : B;</c>
///    (a later inlining run collapses the slot into its use).
///
/// Runs to fixpoint so chains compose.
/// </summary>
public sealed class BooleanFoldingPass : IIrPass
{
    public string Name => "boolean-folding";

    public void Run(IrFunction function)
    {
        while (FoldOnce(function))
        {
        }
    }

    static bool FoldOnce(IrFunction function)
    {
        foreach (var node in function.Descendants.ToList())
        {
            if (node.Parent is null)
                continue;
            bool folded = node switch
            {
                IfStatement statement => FoldNestedGuard(statement) || FoldGuardReturn(statement) || FoldSlotDiamond(statement),
                Comparison comparison => FoldBoolConstantComparison(comparison),
                _ => false,
            };
            if (folded)
                return true;
        }
        return false;
    }

    /// <summary>X == false → !X (via the type-aware duals), X == true → X, and the != duals — the ceq-with-zero value form of boolean tests.</summary>
    static bool FoldBoolConstantComparison(Comparison comparison)
    {
        if (comparison.Kind is not (ComparisonKind.Equal or ComparisonKind.NotEqual))
            return false;
        if (comparison.Left.ResultType is not { Namespace: "System", Name: "Boolean" }
            || comparison.Right is not Constant { Value: bool constant })
        {
            return false;
        }
        var operand = comparison.Left;
        comparison.DetachChildren();
        bool keepIdentity = constant == (comparison.Kind == ComparisonKind.Equal);
        comparison.ReplaceWith(keepIdentity ? operand : Conditions.Negate(operand));
        return true;
    }

    /// <summary>if (a) { if (b) { T } } → if (a &amp;&amp; b) { T }</summary>
    static bool FoldNestedGuard(IfStatement outer)
    {
        if (outer.HasElse || outer.Then.Children.Count != 1
            || outer.Then.Children[0] is not IfStatement { HasElse: false } inner)
        {
            return false;
        }
        var outerCondition = outer.Condition;
        outerCondition.Detach();
        var innerParts = inner.DetachChildren();  // [condition, then]
        outer.ReplaceWith(new IfStatement(
            new LogicalBinary(LogicalKind.And, outerCondition, (IrExpression)innerParts[0]),
            (Block)innerParts[1],
            null));
        return true;
    }

    /// <summary>if (c) return A; return B; → short-circuit when either side is a bool constant.</summary>
    static bool FoldGuardReturn(IfStatement guard)
    {
        if (guard.HasElse || guard.Parent is not Block container)
            return false;
        if (guard.Then.Children.Count != 1 || guard.Then.Children[0] is not Return { Value: { } thenValue })
            return false;
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

        IrExpression folded;
        var condition = guard.Condition;
        condition.Detach();
        if (tailValue is Constant { Value: bool tailBool })
        {
            thenValue.Detach();
            // if (c) return A; return true;  ≡ return !c || A;
            // if (c) return A; return false; ≡ return c && A;
            folded = tailBool
                ? new LogicalBinary(LogicalKind.Or, Conditions.Negate(condition), thenValue)
                : new LogicalBinary(LogicalKind.And, condition, thenValue);
        }
        else if (thenValue is Constant { Value: bool thenBool })
        {
            tailValue.Detach();
            // if (c) return true;  return X; ≡ return c || X;
            // if (c) return false; return X; ≡ return !c && X;
            folded = thenBool
                ? new LogicalBinary(LogicalKind.Or, condition, tailValue)
                : new LogicalBinary(LogicalKind.And, Conditions.Negate(condition), tailValue);
        }
        else
        {
            return false;  // general ternary returns are a separate decision
        }

        tailReturn.Detach();
        guard.ReplaceWith(new Return(folded));
        return true;
    }

    /// <summary>if (c) { S = A } else { S = B } → S = c ? A : B;</summary>
    static bool FoldSlotDiamond(IfStatement diamond)
    {
        if (diamond.Else is not { Children.Count: 1 } elseArm
            || diamond.Then.Children.Count != 1
            || diamond.Then.Children[0] is not StoreStackSlot thenStore
            || elseArm.Children[0] is not StoreStackSlot elseStore
            || thenStore.Slot != elseStore.Slot)
        {
            return false;
        }
        var condition = diamond.Condition;
        condition.Detach();
        var whenTrue = (IrExpression)thenStore.DetachChildren()[0];
        var whenFalse = (IrExpression)elseStore.DetachChildren()[0];
        if (condition is LogicalNot doubleNegative)
        {
            // !c ? b : a reads backwards; unwrap and swap the arms.
            condition = (IrExpression)doubleNegative.DetachChildren()[0];
            (whenTrue, whenFalse) = (whenFalse, whenTrue);
        }
        diamond.ReplaceWith(new StoreStackSlot(thenStore.Slot, new Conditional(condition, whenTrue, whenFalse)));
        return true;
    }
}
