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

    public void Run(IrFunction function, PassContext context)
    {
        while (MaterializeBooleanElementAccesses(function, context.Stepper)
            || FoldOnce(function, context.Stepper)
            || MaterializeBooleanSlots(function, context.Stepper))
        {
        }
    }

    /// <summary>
    /// Retypes bool-array element loads/stores from the array type itself.
    /// IL spells bool-array traffic with the same byte-width opcodes used for
    /// byte/sbyte arrays, so late sugar can still carry <c>ldelem.u1</c> or
    /// <c>stelem.i1</c> as byte/sbyte even when the receiver is a <c>bool[]</c>.
    /// The receiver's declared element type is authoritative; recover it before
    /// boolean comparison folding so <c>visited[i] == 0</c> becomes
    /// <c>!visited[i]</c> and stores print <c>true</c>/<c>false</c>.
    /// </summary>
    static bool MaterializeBooleanElementAccesses(IrFunction function, Stepper stepper)
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        foreach (var node in function.Descendants.ToList())
        {
            switch (node)
            {
                case LoadElement load when IsBool(ArrayElementType(load.Array)) && !IsBool(load.ResultType):
                {
                    var parts = load.DetachChildren();
                    stepper.StepOver("retype bool-array element load", load);
                    load.ReplaceWith(new LoadElement(boolType, (IrExpression)parts[0], (IrExpression)parts[1]));
                    return true;
                }
                case StoreElement store when IsBool(ArrayElementType(store.Array))
                    && (!IsBool(store.ElementType) || store.Value is Constant { Value: int storedInt } && storedInt is 0 or 1):
                {
                    var parts = store.DetachChildren();
                    var value = (IrExpression)parts[2];
                    if (value is Constant { Value: int intValue } && intValue is 0 or 1)
                        value = new Constant(intValue == 1, boolType);
                    stepper.StepOver("retype bool-array element store", store);
                    store.ReplaceWith(new StoreElement(boolType, (IrExpression)parts[0], (IrExpression)parts[1], value));
                    return true;
                }
            }
        }
        return false;
    }

    static TypeRef? ArrayElementType(IrExpression array)
        => array.ResultType is { Kind: TypeRefKind.SzArray or TypeRefKind.Array, ElementType: { } element } ? element : null;

    /// <summary>
    /// Retypes a stack slot to <c>bool</c> when its stores prove it holds a
    /// boolean: either at least one store is a non-constant bool expression, or
    /// every load is consumed by a bool-only position, and every store is a bool
    /// or an <c>int</c> literal 0/1 (IL's spelling of false/true). The 0/1
    /// constant stores become bool literals and the slot's loads retype, so a
    /// bool-returning method that returns the slot stops declaring it
    /// <c>int</c> (CS0029). The proof is producer-side bool evidence or
    /// consumer-side bool-only use; a genuine integer slot that flows into an
    /// integer use is left alone.
    /// </summary>
    static bool MaterializeBooleanSlots(IrFunction function, Stepper stepper)
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
            var candidateStores = stores
                .Where(s => IsZeroOne(s.Value) || IsBool(s.Value.ResultType))
                .ToList();
            bool evidence = candidateStores.Any(s => s.Value is not Constant && IsBool(s.Value.ResultType));
            var loads = loadsBySlot.GetValueOrDefault(slot) ?? [];
            var candidateLoads = loads
                .Where(load => IsBool(load.Type) || load.Type is { } type && TypeFamilies.IsIntegerLike(type))
                .ToList();
            bool allLoadsAcceptBool = candidateLoads.All(load => ConsumerAcceptsBool(function, load));
            evidence = evidence || (candidateLoads.Count > 0 && allLoadsAcceptBool);
            if (!evidence)
                continue;
            if (!candidateStores.Any(s => IsZeroOne(s.Value)) && !candidateLoads.Any(l => !IsBool(l.Type)))
                continue;  // nothing left to retype
            if (!allLoadsAcceptBool)
                continue;  // a load is consumed as a non-bool; the slot is reused, not purely boolean

            var boolType = TypeRef.CoreLib("System", "Boolean");
            stepper.StepOver($"retype stack slot {slot} to bool", stores[0]);
            foreach (var store in candidateStores)
                if (store.Value is Constant { Value: int value })
                    store.Value.ReplaceWith(new Constant(value == 1, boolType));
            foreach (var load in candidateLoads)
            {
                if (IsBool(load.Type))
                    continue;
                // A 0/1 literal sibling in a `== `/`!=` with this load is the bool
                // identity/negation idiom (csc's `ceq`/`cgt` against 0). Retype it
                // alongside the load so the comparison stays bool == bool, not the
                // CS0019 `bool == int` a load-only retype would leave;
                // FoldBoolConstantComparison then reduces it to `x`/`!x`.
                if (load.Parent is Comparison { Kind: ComparisonKind.Equal or ComparisonKind.NotEqual } comparison)
                {
                    var sibling = ReferenceEquals(comparison.Left, load) ? comparison.Right : comparison.Left;
                    if (sibling is Constant { Value: int siblingValue } && siblingValue is 0 or 1)
                        sibling.ReplaceWith(new Constant(siblingValue == 1, boolType));
                }
                load.ReplaceWith(new LoadStackSlot(slot, boolType));
            }
            changed = true;
        }
        return changed;
    }

    static bool IsZeroOne(IrExpression expression) => expression is Constant { Value: int value } && value is 0 or 1;

    static bool IsBool(TypeRef? type) => type is { Namespace: "System", Name: "Boolean" };

    /// <summary>
    /// Whether the value produced by <paramref name="node"/> is consumed where a
    /// <c>bool</c> is valid. Retyping a value to bool is sound only when it is
    /// boolean at every USE, not just where it is produced: edge slots are keyed
    /// by stack position and carried type, so one slot number can still span
    /// disjoint same-typed live ranges, and a stores-only check can retype a
    /// value that another range consumes as an integer — a silent overload miscompile
    /// (<c>f(1)</c> becomes <c>f(true)</c>) or CS0019/CS0029. Unrecognized
    /// consumers are treated as non-bool (bail), keeping the pass conservative.
    /// </summary>
    static bool ConsumerAcceptsBool(IrFunction function, IrExpression node) => ConsumerAcceptsBool(function, node, []);

    static bool ConsumerAcceptsBool(IrFunction function, IrExpression node, HashSet<int> visitedSlots) => node.Parent switch
    {
        null => true,
        Return => IsBool(function.Signature.ReturnType),
        IfStatement statement => ReferenceEquals(statement.Condition, node),
        ConditionalBranch branch => ReferenceEquals(branch.Condition, node),
        LogicalBinary or LogicalNot or Coalesce => true,
        WhileLoop loop => ReferenceEquals(loop.Condition, node),
        DoWhileLoop loop => ReferenceEquals(loop.Condition, node),
        ForLoop loop => ReferenceEquals(loop.Condition, node),
        Binary { Kind: BinaryKind.And or BinaryKind.Or or BinaryKind.Xor } binary
            when BoolBinaryAcceptsBoolOperand(binary, node) => ConsumerAcceptsBool(function, binary, visitedSlots),
        Conditional conditional => ReferenceEquals(conditional.Condition, node) || IsBool(conditional.ResultType),
        Comparison { Kind: ComparisonKind.Equal or ComparisonKind.NotEqual } comparison
            => ComparisonSiblingAcceptsBool(comparison, node),
        Call call => ParameterAcceptsBool(call.Callee.ParameterTypes, call.Callee.HasThis ? node.ChildIndex - 1 : node.ChildIndex),
        NewObject newObject => ParameterAcceptsBool(newObject.Constructor.ParameterTypes, node.ChildIndex),
        StoreLocal store => ReferenceEquals(store.Value, node) && IsBool(store.Type),
        StoreArgument store => ReferenceEquals(store.Value, node) && IsBool(store.Type),
        StoreField store => ReferenceEquals(store.Value, node) && IsBool(store.Field.Type),
        StoreIndirect store => ReferenceEquals(store.Value, node) && IsBool(IndirectStoreType(store)),
        // A value stored into a slot is boolean only if every load of that slot
        // is itself consumed as a bool (the slot carries it onward).
        StoreStackSlot store => SlotLoadsAcceptBool(function, store.Slot, visitedSlots),
        _ => false,
    };

    /// <summary>
    /// Whether an <c>==</c>/<c>!=</c> comparison stays well-typed after its
    /// <paramref name="operand"/> retypes to <c>bool</c>: the sibling must already
    /// be a <c>bool</c>, or a <c>0</c>/<c>1</c> int literal the retype flips to
    /// <c>false</c>/<c>true</c> alongside it. A non-bool, non-0/1 sibling would
    /// leave <c>bool == int</c> (CS0019) — the slot is not purely boolean there, so
    /// the retype must bail.
    /// </summary>
    static bool ComparisonSiblingAcceptsBool(Comparison comparison, IrExpression operand)
    {
        var sibling = ReferenceEquals(comparison.Left, operand) ? comparison.Right : comparison.Left;
        return IsBool(sibling.ResultType) || (sibling is Constant { Value: int value } && value is 0 or 1);
    }

    static TypeRef? IndirectStoreType(StoreIndirect store)
    {
        var pointee = store.Address.ResultType is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer, ElementType: { } element }
            ? element
            : null;
        return pointee is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Boolean" or "Char" }
            ? pointee
            : store.Type ?? pointee;
    }

    static bool SlotLoadsAcceptBool(IrFunction function, int slot, HashSet<int> visitedSlots)
    {
        if (!visitedSlots.Add(slot))
            return false;  // a slot-copy cycle — bail rather than assume boolean
        return function.Descendants.OfType<LoadStackSlot>()
            .Where(load => load.Slot == slot)
            .All(load => ConsumerAcceptsBool(function, load, visitedSlots));
    }

    static bool ParameterAcceptsBool(IReadOnlyList<TypeRef> parameterTypes, int index)
        => index >= 0 && index < parameterTypes.Count && IsBool(parameterTypes[index]);

    static bool BoolBinaryAcceptsBoolOperand(Binary binary, IrExpression operand)
    {
        if (ReferenceEquals(binary.Left, operand))
            return IsBoolLike(binary.Right);
        return ReferenceEquals(binary.Right, operand) && IsBoolLike(binary.Left);
    }

    static bool IsBoolLike(IrExpression expression)
        => IsBool(expression.ResultType)
            || expression is Constant { Value: bool }
            || IsZeroOne(expression);

    static bool FoldOnce(IrFunction function, Stepper stepper)
    {
        foreach (var node in function.Descendants.ToList())
        {
            if (node.Parent is null)
                continue;
            bool folded = node switch
            {
                IfStatement statement => FoldNestedGuard(statement, stepper) || FoldGuardReturn(statement, stepper)
                    || FoldTernaryReturn(statement, stepper)
                    || FoldSlotDiamond(function, statement, stepper) || FoldCoalesce(statement, stepper),
                Comparison comparison => FoldBoolConstantComparison(comparison, stepper),
                Conditional conditional => MaterializeBoolConditional(function, conditional, stepper),
                _ => false,
            };
            if (folded)
                return true;
        }
        return false;
    }

    /// <summary>
    /// <c>cond ? 0 : boolExpr</c> (and nested 0/1/bool conditionals) →
    /// <c>cond ? false : boolExpr</c>. IL has no bool constant (ldc.i4 serves
    /// bools too), so a select tree that sets literal 0/1 arms beside a genuine
    /// bool arm is a boolean expression. Retyping the constants — and each
    /// conditional's merged result — recovers it; the slot then declares as
    /// <c>bool</c>, not <c>int</c> (the source of CS0029/CS0165 when a bool load
    /// later gets a split stack-slot name). The non-constant bool arm plus the
    /// downstream consumer check are the proof: a real integer select never pairs
    /// with one, and a reused spill slot is not retyped unless every live load
    /// after the store is consumed as bool. Stepper audit specimen:
    /// <c>CfgSampleClass.SelectBoolReturn</c>, where step 1 folds the stack-slot
    /// diamond and step 2 may materialize the <c>0</c> arm only because the slot
    /// feeds a bool return.
    /// </summary>
    static bool MaterializeBoolConditional(IrFunction function, Conditional conditional, Stepper stepper)
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        if (!CanMaterializeBoolExpression(conditional, out bool hasBoolEvidence, out bool needsRewrite)
            || !hasBoolEvidence
            || !needsRewrite)
        {
            return false;
        }

        StoreStackSlot? slotStore = conditional.Parent as StoreStackSlot;
        List<LoadStackSlot>? liveLoads = null;
        if (slotStore is not null)
        {
            liveLoads = LiveLoadsAfterStore(function, slotStore).ToList();
            if (!liveLoads.All(load => ConsumerAcceptsBool(function, load)))
                return false;
        }
        // Only recover the bool spelling when the ternary's result is consumed as
        // a bool; otherwise retyping it (and its merged type) would push a bool
        // into a position a later pass treats as int — no TypedConstantsPass runs
        // after boolean-folding to reconcile it (CS0029/CS0266).
        else if (!ConsumerAcceptsBool(function, conditional))
        {
            return false;
        }
        stepper.StepOver("materialize bool ternary constant arm", conditional);
        MaterializeBoolExpression(conditional, boolType);
        if (liveLoads is not null)
        {
            foreach (var load in liveLoads)
                if (!IsBool(load.Type))
                    load.ReplaceWith(new LoadStackSlot(slotStore!.Slot, boolType));
        }
        return true;
    }

    static bool CanMaterializeBoolExpression(IrExpression expression, out bool hasBoolEvidence, out bool needsRewrite)
    {
        hasBoolEvidence = false;
        needsRewrite = false;
        switch (expression)
        {
            case Constant { Value: int value } when value is 0 or 1:
                needsRewrite = true;
                return true;
            case { ResultType: { Namespace: "System", Name: "Boolean" } }:
                hasBoolEvidence = expression is not Constant;
                return true;
            case Conditional conditional:
            {
                if (!CanMaterializeBoolExpression(conditional.WhenTrue, out bool trueEvidence, out bool trueRewrite)
                    || !CanMaterializeBoolExpression(conditional.WhenFalse, out bool falseEvidence, out bool falseRewrite))
                {
                    return false;
                }
                hasBoolEvidence = trueEvidence || falseEvidence || IsBool(conditional.Condition.ResultType);
                needsRewrite = trueRewrite || falseRewrite || !IsBool(conditional.MergedType);
                return true;
            }
            default:
                return false;
        }
    }

    static void MaterializeBoolExpression(IrExpression expression, TypeRef boolType)
    {
        switch (expression)
        {
            case Constant { Value: int value } constant when value is 0 or 1:
                constant.ReplaceWith(new Constant(value == 1, boolType));
                break;
            case Conditional conditional:
                MaterializeBoolExpression(conditional.WhenTrue, boolType);
                MaterializeBoolExpression(conditional.WhenFalse, boolType);
                conditional.MergedType = boolType;
                break;
        }
    }

    static IEnumerable<LoadStackSlot> LiveLoadsAfterStore(IrFunction function, StoreStackSlot store)
    {
        var nodes = function.Descendants.ToList();
        int storeIndex = nodes.IndexOf(store);
        if (storeIndex < 0)
            yield break;
        for (int i = storeIndex + 1; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (IsDescendantOf(node, store))
                continue;
            if (node is StoreStackSlot nextStore && nextStore.Slot == store.Slot)
                yield break;
            if (node is LoadStackSlot load && load.Slot == store.Slot)
                yield return load;
        }
    }

    static bool IsDescendantOf(IrNode node, IrNode ancestor)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor))
                return true;
        return false;
    }

    /// <summary>X == false → !X (via the type-aware duals), X == true → X, and the != duals — the ceq-with-zero value form of boolean tests.</summary>
    static bool FoldBoolConstantComparison(Comparison comparison, Stepper stepper)
    {
        if (comparison.Kind is not (ComparisonKind.Equal or ComparisonKind.NotEqual))
            return false;
        if (comparison.Left.ResultType is not { Namespace: "System", Name: "Boolean" }
            || BoolConstant(comparison.Right) is not { } constant)
        {
            return false;
        }
        var operand = comparison.Left;
        comparison.DetachChildren();
        bool keepIdentity = constant == (comparison.Kind == ComparisonKind.Equal);
        stepper.StepOver($"fold X == {constant.ToString().ToLowerInvariant()} to boolean test", comparison);
        comparison.ReplaceWith(keepIdentity ? operand : Conditions.Negate(operand));
        return true;
    }

    /// <summary>
    /// A bool literal, or csc's <c>0</c>/<c>1</c> int spelling of one (a
    /// <c>ceq</c>/<c>cgt</c> against 0 is the <c>!x</c>/<c>x</c> idiom over a
    /// bool-typed operand). Returns the bool value, or null when the operand is
    /// not a bool/0/1 constant. Generalizes the bool-slot retype (#1019) to any
    /// bool-typed left operand — a ternary, call result, or field, not only a slot.
    /// </summary>
    static bool? BoolConstant(IrExpression expression) => expression switch
    {
        Constant { Value: bool value } => value,
        Constant { Value: int value } when value is 0 or 1 => value == 1,
        _ => null,
    };

    /// <summary>if (a) { if (b) { T } } → if (a &amp;&amp; b) { T }</summary>
    static bool FoldNestedGuard(IfStatement outer, Stepper stepper)
    {
        if (outer.HasElse || outer.Then.Children.Count != 1
            || outer.Then.Children[0] is not IfStatement { HasElse: false } inner)
        {
            return false;
        }
        var outerCondition = outer.Condition;
        outerCondition.Detach();
        var innerParts = inner.DetachChildren();  // [condition, then]
        stepper.StepOver("fold nested if guards into &&", outer);
        outer.ReplaceWith(new IfStatement(
            new LogicalBinary(LogicalKind.And, outerCondition, (IrExpression)innerParts[0]),
            (Block)innerParts[1],
            null));
        return true;
    }

    /// <summary>if (c) return A; return B; → short-circuit when either side is a bool constant.</summary>
    static bool FoldGuardReturn(IfStatement guard, Stepper stepper)
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
        if (thenConstant is { } thenBool && tailConstant is { } tailBool2 && thenBool != tailBool2)
        {
            // Both arms are opposite bool constants, so the condition itself is
            // the result: if (c) return true; return false; ≡ return c; and the
            // dual ≡ return !c;. The generic fold below would append the literal
            // arm as a dead `c && true` / `!c || false`.
            folded = thenBool ? condition : Conditions.Negate(condition);
        }
        else if (tailConstant is { } tailBool)
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
        stepper.StepOver("fold guarded return into short-circuit", guard);
        guard.ReplaceWith(new Return(folded));
        return true;
    }

    /// <summary>
    /// <c>if (c) return A; return B;</c> → <c>return !c ? B : A;</c> for simple
    /// top-level relational value arms. The polarity swap recovers the source-like
    /// comparison shape csc lowered into the guard.
    /// </summary>
    static bool FoldTernaryReturn(IfStatement guard, Stepper stepper)
    {
        if (guard.HasElse || guard.Parent is not Block container)
            return false;
        if (container.Parent is not BlockContainer { Parent: IrFunction } || container.Children.Count != 2)
            return false;
        if (guard.Then.Children.Count != 1 || guard.Then.Children[0] is not Return { Value: { } thenValue })
            return false;
        if (guard.ChildIndex + 1 >= container.Children.Count
            || container.Children[guard.ChildIndex + 1] is not Return { Value: { } tailValue } tailReturn)
        {
            return false;
        }
        if (thenValue.ResultType is { Namespace: "System", Name: "Boolean" }
            || tailValue.ResultType is { Namespace: "System", Name: "Boolean" })
        {
            return false;
        }
        if (!IsTernaryComparison(guard.Condition) || !IsSimpleTernaryArm(thenValue) || !IsSimpleTernaryArm(tailValue))
            return false;

        TypeRef? mergedType = null;
        if (thenValue.ResultType is { } thenType
            && tailValue.ResultType is { } tailType
            && thenType.Equals(tailType))
        {
            mergedType = thenType;
        }

        var condition = guard.Condition;
        condition.Detach();
        thenValue.Detach();
        tailValue.Detach();
        tailReturn.Detach();

        stepper.StepOver("fold guarded return into ternary", guard);
        guard.ReplaceWith(new Return(
            new Conditional(Conditions.Negate(condition), tailValue, thenValue) { MergedType = mergedType }));
        return true;
    }

    static bool IsTernaryComparison(IrExpression condition)
    {
        if (condition is not Comparison comparison)
            return false;
        if (comparison.IsUnsigned)
            return false;
        if (IsNullLiteral(comparison.Left) || IsNullLiteral(comparison.Right))
            return false;
        if (IsUnsignedIntegral(comparison.Left.ResultType) || IsUnsignedIntegral(comparison.Right.ResultType))
            return false;
        return true;
    }

    static bool IsSimpleTernaryArm(IrExpression value)
        => value is LoadArgument or LoadLocal or Constant;

    static bool IsNullLiteral(IrExpression expression)
        => expression is Constant { Value: null };

    static bool IsUnsignedIntegral(TypeRef? type)
        => type is { Namespace: "System", Name: "UInt32" or "UInt64" or "UInt16" or "Byte" or "UIntPtr" };

    /// <summary>
    /// T = X; if (X is null) { T = Y; } → T = X ?? Y; — the compiler's
    /// null-coalescing lowering. X must be a plain load matching the tested
    /// operand exactly; both stores must target the same place.
    /// </summary>
    static bool FoldCoalesce(IfStatement guard, Stepper stepper)
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
                => SameNullTestedLoad(tested, p.Value),
            (StoreLocal p, StoreLocal i) when p.Index == i.Index
                => SameNullTestedLoad(tested, p.Value),
            _ => false,
        };
        if (!match)
            return false;

        var first = previous.DetachChildren()[0];
        var fallback = inner.DetachChildren()[0];
        var coalesce = new Coalesce((IrExpression)first, (IrExpression)fallback);
        guard.Detach();
        stepper.StepOver("fold null check into ?? coalesce", previous);
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

    // The ?? coalesce reads the tested place once for the brfalse and once as the
    // store value; folding is sound for a re-evaluable place — a variable or the
    // spilled stack slot csc emits for the value.
    static bool SameNullTestedLoad(IrExpression tested, IrExpression stored)
        => PlaceIdentity.SameVariable(tested, stored) || PlaceIdentity.SameStackSlot(tested, stored);

    /// <summary>if (c) { S = A } else { S = B } → S = c ? A : B;</summary>
    static bool FoldSlotDiamond(IrFunction function, IfStatement diamond, Stepper stepper)
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
        // The importer merged this slot to the genuine common supertype at the
        // join this diamond feeds. Slot numbers are stack-depth positions and can
        // be reused by earlier live ranges, so look after the whole diamond, not
        // at the first same-number load in the method.
        var mergedType = FirstLoadAfter(function, diamond, thenStore.Slot)?.Type
            ?? EqualArmType(whenTrue, whenFalse);
        stepper.StepOver("fold store diamond into ternary", diamond);
        diamond.ReplaceWith(new StoreStackSlot(thenStore.Slot, new Conditional(condition, whenTrue, whenFalse) { MergedType = mergedType }));
        return true;
    }

    static LoadStackSlot? FirstLoadAfter(IrFunction function, IrNode node, int slot)
    {
        bool after = false;
        foreach (var candidate in function.Descendants)
        {
            if (!after)
            {
                if (ReferenceEquals(candidate, node))
                    after = true;
                continue;
            }
            if (IsDescendantOf(candidate, node))
                continue;
            if (candidate is LoadStackSlot load && load.Slot == slot && load.Type is not null)
                return load;
        }
        return null;
    }

    static TypeRef? EqualArmType(IrExpression whenTrue, IrExpression whenFalse)
        => whenTrue.ResultType is { } trueType && trueType.Equals(whenFalse.ResultType) ? trueType : null;
}
