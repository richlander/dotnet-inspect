namespace ILInspector.Decompiler.Pipeline;

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
        while (FoldOnce(function) || MaterializeBooleanSlots(function))
        {
        }
    }

    /// <summary>
    /// Retypes a stack slot to <c>bool</c> when its stores prove it holds a
    /// boolean: at least one store is a non-constant bool expression and every
    /// other store is a bool or an <c>int</c> literal 0/1 (IL's spelling of
    /// false/true). The 0/1 constant stores become bool literals and the slot's
    /// loads retype, so a bool-returning method that returns the slot stops
    /// declaring it <c>int</c> (CS0029). The non-constant bool store is the
    /// proof — a genuine integer slot never receives one.
    /// </summary>
    static bool MaterializeBooleanSlots(IrFunction function)
    {
        var storesBySlot = new Dictionary<int, List<StoreStackSlot>>();
        var loadsBySlot = new Dictionary<int, List<LoadStackSlot>>();
        foreach (var node in function.Descendants)
        {
            if (node is StoreStackSlot store)
                (storesBySlot.TryGetValue(store.Slot, out var list) ? list : storesBySlot[store.Slot] = []).Add(store);
            else if (node is LoadStackSlot load)
                (loadsBySlot.TryGetValue(load.Slot, out var list) ? list : loadsBySlot[load.Slot] = []).Add(load);
        }

        bool changed = false;
        foreach (var (slot, stores) in storesBySlot)
        {
            bool evidence = stores.Any(s => s.Value is not Constant && IsBool(s.Value.ResultType));
            bool consistent = stores.All(s => IsZeroOne(s.Value) || IsBool(s.Value.ResultType));
            if (!evidence || !consistent)
                continue;
            var loads = loadsBySlot.GetValueOrDefault(slot) ?? [];
            if (!stores.Any(s => IsZeroOne(s.Value)) && !loads.Any(l => !IsBool(l.Type)))
                continue;  // nothing left to retype

            var boolType = TypeRef.CoreLib("System", "Boolean");
            foreach (var store in stores)
                if (store.Value is Constant { Value: int value })
                    store.Value.ReplaceWith(new Constant(value == 1, boolType));
            foreach (var load in loads)
                if (!IsBool(load.Type))
                    load.ReplaceWith(new LoadStackSlot(slot, boolType));
            changed = true;
        }
        return changed;
    }

    static bool IsZeroOne(IrExpression expression) => expression is Constant { Value: int value } && value is 0 or 1;

    static bool IsBool(TypeRef? type) => type is { Namespace: "System", Name: "Boolean" };

    static bool FoldOnce(IrFunction function)
    {
        foreach (var node in function.Descendants.ToList())
        {
            if (node.Parent is null)
                continue;
            bool folded = node switch
            {
                IfStatement statement => FoldNestedGuard(statement) || FoldGuardReturn(statement)
                    || FoldSlotDiamond(function, statement) || FoldCoalesce(statement),
                Comparison comparison => FoldBoolConstantComparison(comparison),
                Conditional conditional => MaterializeBoolConditional(conditional),
                _ => false,
            };
            if (folded)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>cond ? 0 : boolExpr</c> → <c>cond ? false : boolExpr</c>. IL has no
    /// bool constant (ldc.i4 serves bools too), so a select that sets a literal
    /// 0/1 beside a genuine bool arm is a boolean expression. Retyping the
    /// constant — and the conditional's merged result — recovers it; the slot
    /// then declares as <c>bool</c>, not <c>int</c> (the source of CS0029 when a
    /// bool-returning method returns the slot). The non-constant bool arm is the
    /// proof: a real integer select never pairs with one.
    /// </summary>
    static bool MaterializeBoolConditional(Conditional conditional)
    {
        var constant = (conditional.WhenTrue as Constant) ?? (conditional.WhenFalse as Constant);
        var other = conditional.WhenTrue is Constant ? conditional.WhenFalse : conditional.WhenTrue;
        if (constant is not { Value: int value } || value is not (0 or 1))
            return false;
        if (other is Constant || other.ResultType is not { Namespace: "System", Name: "Boolean" })
            return false;
        var boolType = TypeRef.CoreLib("System", "Boolean");
        constant.ReplaceWith(new Constant(value == 1, boolType));
        conditional.MergedType = boolType;
        return true;
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

        // Decide the shape COMPLETELY before any detach: bailing after a
        // mutation leaves a mutilated IfStatement whose slots have shifted.
        bool? tailConstant = tailValue is Constant { Value: bool tail } ? tail : null;
        bool? thenConstant = thenValue is Constant { Value: bool then } ? then : null;
        if (tailConstant is null && thenConstant is null)
            return false;  // general ternary returns are a separate decision

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
        guard.ReplaceWith(new Return(folded));
        return true;
    }

    /// <summary>
    /// T = X; if (X is null) { T = Y; } → T = X ?? Y; — the compiler's
    /// null-coalescing lowering. X must be a plain load matching the tested
    /// operand exactly; both stores must target the same place.
    /// </summary>
    static bool FoldCoalesce(IfStatement guard)
    {
        if (guard.HasElse || guard.Parent is not Block container || guard.ChildIndex == 0)
            return false;
        // The null test arrives as a comparison (ceq lowering) or as
        // brfalse over the reference (LogicalNot after structuring).
        IrExpression? tested = guard.Condition switch
        {
            Comparison { Kind: ComparisonKind.Equal, Right: Constant { Value: null } } c => c.Left,
            LogicalNot { Operand: { } operand } => operand,
            _ => null,
        };
        if (tested is null || guard.Then.Children.Count != 1)
            return false;
        // ?? is reference-only; brfalse over a known integer/bool means == 0.
        if (TypeFamilies.Of(tested.ResultType) is StackFamily.I4 or StackFamily.I8 or StackFamily.I or StackFamily.F)
            return false;
        var previous = container.Children[guard.ChildIndex - 1];
        var inner = guard.Then.Children[0];

        // Same place, both stores; tested operand is the same load as the
        // first store's value.
        bool match = (previous, inner) switch
        {
            (StoreStackSlot p, StoreStackSlot i) when p.Slot == i.Slot
                => SameLoad(tested, p.Value),
            (StoreLocal p, StoreLocal i) when p.Index == i.Index
                => SameLoad(tested, p.Value),
            _ => false,
        };
        if (!match)
            return false;

        var first = previous.DetachChildren()[0];
        var fallback = inner.DetachChildren()[0];
        var coalesce = new Coalesce((IrExpression)first, (IrExpression)fallback);
        guard.Detach();
        switch (previous)
        {
            case StoreStackSlot slot:
                previous.ReplaceWith(new StoreStackSlot(slot.Slot, coalesce));
                break;
            case StoreLocal local:
                previous.ReplaceWith(new StoreLocal(local.Index, local.Type, coalesce));
                break;
        }
        return true;
    }

    static bool SameLoad(IrExpression tested, IrExpression stored) => (tested, stored) switch
    {
        (LoadStackSlot a, LoadStackSlot b) => a.Slot == b.Slot,
        (LoadLocal a, LoadLocal b) => a.Index == b.Index,
        (LoadArgument a, LoadArgument b) => a.Index == b.Index,
        _ => false,
    };

    /// <summary>if (c) { S = A } else { S = B } → S = c ? A : B;</summary>
    static bool FoldSlotDiamond(IrFunction function, IfStatement diamond)
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
        // The importer merged this slot to the genuine common supertype of the
        // arms; carry it so the ternary types honestly when the arms differ
        // (the bare WhenTrue fallback would otherwise narrow the result).
        var mergedType = function.Descendants
            .OfType<LoadStackSlot>()
            .FirstOrDefault(load => load.Slot == thenStore.Slot && load.Type is not null)?.Type;
        diamond.ReplaceWith(new StoreStackSlot(thenStore.Slot, new Conditional(condition, whenTrue, whenFalse) { MergedType = mergedType }));
        return true;
    }
}
