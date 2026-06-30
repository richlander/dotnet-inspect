namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the union switch-expression lowering Roslyn currently emits:
/// one cached <c>union.Value</c>, ordered type tests, value arms, and the
/// compiler's unreachable <c>ThrowSwitchExpressionException</c> fallback. This is
/// deliberately narrower than general pattern-switch reconstruction.
/// </summary>
public sealed class UnionSwitchExpressionPass : IIrPass
{
    public string Name => "union-switch-expression";

    sealed record Arm(TypeRef PatternType, int? LocalIndex, IrExpression Value, IReadOnlyList<IrNode> LocalRoots, IrExpression? Guard = null);
    sealed record Match(int StartIndex, UnionSwitchExpression SwitchExpression);
    sealed record BoolMatch(int StartIndex, IrExpression Expression);

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (TryMatchClassExhaustiveGuardedBlocks(function, container, out var classExhaustiveSwitch))
            {
                int startOffset = container.Blocks[0].StartOffset;
                container.DetachChildren();
                var replacementBlock = new Block(startOffset);
                replacementBlock.Add(new Return(classExhaustiveSwitch));
                container.Add(replacementBlock);
                context.Stepper.StepOver("raise class union guarded dispatch to switch expression", container);
                continue;
            }

            if (container.Blocks is [var block] && TryMatch(function, block, out var match))
            {
                block.SetChild(match.StartIndex, new Return(match.SwitchExpression));
                for (int i = block.Children.Count - 1; i > match.StartIndex; i--)
                    block.Children[i].Detach();
                context.Stepper.StepOver("raise union type-test dispatch to switch expression", block);
                continue;
            }

            if (container.Blocks is [var boolBlock] && TryMatchUnionBoolTypeTest(function, boolBlock, out var boolMatch))
            {
                boolBlock.SetChild(boolMatch.StartIndex, new Return(boolMatch.Expression));
                for (int i = boolBlock.Children.Count - 1; i > boolMatch.StartIndex; i--)
                    boolBlock.Children[i].Detach();
                context.Stepper.StepOver("raise union type-test bool dispatch", boolBlock);
            }
        }
    }

    static bool TryMatch(IrFunction function, Block block, out Match match)
    {
        match = null!;
        var children = block.Children;
        for (int start = 0; start < children.Count; start++)
        {
            if (TryMatchSwitchStatementReturnChainAt(function, children, start, out var statementSwitch))
            {
                match = new Match(start, statementSwitch);
                return true;
            }

            if (TryMatchClassDefaultAt(function, children, start, out var classDefaultSwitch))
            {
                match = new Match(start, classDefaultSwitch);
                return true;
            }

            if (TryMatchClassNullGuardAt(function, children, start, out var guardedSwitch))
            {
                match = new Match(start, guardedSwitch);
                return true;
            }

            if (TryMatchDefaultChainAt(function, children, start, out var defaultSwitch))
            {
                match = new Match(start, defaultSwitch);
                return true;
            }

            if (TryMatchExhaustiveGuardedChainAt(function, children, start, out var exhaustiveGuardedSwitch))
            {
                match = new Match(start, exhaustiveGuardedSwitch);
                return true;
            }

            if (TryMatchAt(function, children, start, out var switchExpression))
            {
                match = new Match(start, switchExpression);
                return true;
            }
        }

        return false;
    }

    static bool TryMatchUnionBoolTypeTest(IrFunction function, Block block, out BoolMatch match)
    {
        match = null!;
        var children = block.Children;
        for (int start = 0; start + 2 < children.Count; start++)
        {
            if (start + 3 != children.Count
                || children[start] is not StoreLocal { Value: LoadProperty unionValue } valueStore
                || !IsUnionValueProperty(function, unionValue)
                || children[start + 1] is not IfStatement ifStatement
                || ifStatement.Then.Children is not [StoreLocal thenStore]
                || ifStatement.Else?.Children is not [StoreLocal elseStore]
                || children[start + 2] is not Return ret
                || !StoreReturnMatch(thenStore, ret, thenStore.Index)
                || elseStore.Index != thenStore.Index
                || !TryBoolConstants(thenStore.Value, elseStore.Value, out bool conditionIsTrueArm)
                || !TryRewriteTempTypeTestCondition(ifStatement.Condition, valueStore.Index, unionValue, out var condition))
            {
                continue;
            }

            if (!conditionIsTrueArm)
                condition = Conditions.Negate(condition);

            if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, valueStore.Index, [valueStore, ifStatement.Condition])
                || !ReferenceOwnership.LocalReferencesOnlyWithin(function, thenStore.Index, [thenStore, elseStore, ret.Value!]))
            {
                continue;
            }

            match = new BoolMatch(start, condition);
            return true;
        }

        return false;
    }

    static bool TryRewriteTempTypeTestCondition(IrExpression expression, int tempLocal, LoadProperty unionValue, out IrExpression rewritten)
    {
        switch (expression)
        {
            case IsInstance test when IsTempTypeTest(test, tempLocal):
                rewritten = new IsInstance(test.Type, (IrExpression)unionValue.Clone());
                return true;
            case LogicalBinary logical:
            {
                if (!TryRewriteTempTypeTestCondition(logical.Left, tempLocal, unionValue, out var left)
                    || !TryRewriteTempTypeTestCondition(logical.Right, tempLocal, unionValue, out var right))
                {
                    rewritten = null!;
                    return false;
                }

                rewritten = new LogicalBinary(logical.Kind, left, right);
                return true;
            }
            default:
                rewritten = null!;
                return false;
        }
    }

    static bool TryBoolConstants(IrExpression whenTrue, IrExpression whenFalse, out bool conditionIsTrueArm)
    {
        if (IsTrueConstant(whenTrue) && IsFalseConstant(whenFalse))
        {
            conditionIsTrueArm = true;
            return true;
        }

        if (IsFalseConstant(whenTrue) && IsTrueConstant(whenFalse))
        {
            conditionIsTrueArm = false;
            return true;
        }

        conditionIsTrueArm = false;
        return false;
    }

    static bool IsTrueConstant(IrExpression expression)
        => expression is Constant { Value: true } or Constant { Value: 1 };

    static bool IsFalseConstant(IrExpression expression)
        => expression is Constant { Value: false } or Constant { Value: 0 };

    static bool TryMatchSwitchStatementReturnChainAt(
        IrFunction function,
        IReadOnlyList<IrNode> children,
        int start,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        int current = start;
        StoreLocal? receiverStore = null;
        if (current + 1 < children.Count
            && children[current] is StoreLocal possibleReceiver
            && children[current + 1] is StoreLocal { Value: LoadProperty possibleUnionValue }
            && IsLocalReceiver(possibleUnionValue.Instance, possibleReceiver.Index))
        {
            receiverStore = possibleReceiver;
            current++;
        }

        if (current + 2 >= children.Count
            || children[current] is not StoreLocal { Value: LoadProperty unionValue } valueStore
            || !IsUnionValueProperty(function, unionValue)
            || children[current + 1] is not StoreLocal { Value: IsInstance firstTest } firstStore
            || !IsTempTypeTest(firstTest, valueStore.Index)
            || children[current + 2] is not IfStatement firstIf
            || !IsNotLocal(firstIf.Condition, firstStore.Index))
        {
            return false;
        }

        if (firstIf.HasElse)
        {
            if (firstIf.Else is not { } elseBlock
                || !TryElseArm(elseBlock.Children, firstTest.Type, firstStore.Index, [firstStore, firstIf.Condition], out var firstArm))
            {
                return false;
            }

            IrExpression defaultValue;
            IReadOnlyList<Arm> innerArms;
            if (current + 4 == children.Count
                && children[current + 3] is Return { Value: { } tailDefault }
                && TryReturnArms(firstIf.Then.Children, valueStore.Index, out var tailArms))
            {
                defaultValue = tailDefault;
                innerArms = tailArms;
            }
            else if (current + 3 == children.Count
                && TryReturnArmsWithDefault(firstIf.Then.Children, valueStore.Index, out var embeddedArms, out var embeddedDefault))
            {
                defaultValue = embeddedDefault;
                innerArms = embeddedArms;
            }
            else
            {
                return false;
            }

            return BuildStatementSwitch(function, unionValue, valueStore, firstStore, receiverStore, [firstArm, .. innerArms], defaultValue, firstIf, out switchExpression);
        }

        if (current + 4 != children.Count
            || children[current + 3] is not Return { Value: { } firstValue }
            || !TryReturnArmsWithDefault(firstIf.Then.Children, valueStore.Index, out var innerArmsNoElse, out var defaultFromThen))
        {
            return false;
        }

        var firstArmNoElse = new Arm(
            firstTest.Type,
            firstStore.Index,
            firstValue,
            [firstStore, firstIf.Condition, firstValue]);
        return BuildStatementSwitch(function, unionValue, valueStore, firstStore, receiverStore, [firstArmNoElse, .. innerArmsNoElse], defaultFromThen, firstIf, out switchExpression);
    }

    static bool TryReturnArmsWithDefault(IReadOnlyList<IrNode> nodes, int tempLocal, out IReadOnlyList<Arm> arms, out IrExpression defaultValue)
    {
        arms = [];
        defaultValue = null!;
        if (nodes.Count < 1)
            return false;

        if (nodes[^1] is IfStatement finalArmIf
            && TryReturnArmWithDefault(finalArmIf, tempLocal, out var finalArm, out var finalDefault))
        {
            IReadOnlyList<Arm> prefixArms = [];
            if (nodes.Count > 1 && !TryReturnArms(nodes.Take(nodes.Count - 1).ToArray(), tempLocal, out prefixArms))
                return false;

            arms = [.. prefixArms, finalArm];
            defaultValue = finalDefault;
            return true;
        }

        if (nodes.Count < 2 || nodes[^1] is not Return { Value: { } retDefault })
            return false;

        if (!TryReturnArms(nodes.Take(nodes.Count - 1).ToArray(), tempLocal, out arms))
            return false;

        defaultValue = retDefault;
        return true;
    }

    static bool TryReturnArms(IReadOnlyList<IrNode> nodes, int tempLocal, out IReadOnlyList<Arm> arms)
    {
        var builder = new List<Arm>();
        foreach (var node in nodes)
        {
            if (node is not IfStatement armIf || !TryReturnArm(armIf, tempLocal, out var arm))
            {
                arms = [];
                return false;
            }

            builder.Add(arm);
        }

        arms = builder;
        return arms.Count > 0;
    }

    static bool TryElseArm(
        IReadOnlyList<IrNode> nodes,
        TypeRef patternType,
        int localIndex,
        IReadOnlyList<IrNode> patternRoots,
        out Arm arm)
    {
        arm = null!;
        switch (nodes)
        {
            case [Return { Value: { } value }]:
            {
                var roots = new List<IrNode>(patternRoots) { value };
                arm = new Arm(patternType, localIndex, value, roots);
                return true;
            }
            case [IfStatement guardIf] when !guardIf.HasElse && guardIf.Then.Children is [Return { Value: { } guardedValue }]:
            {
                var roots = new List<IrNode>(patternRoots) { guardIf.Condition, guardedValue };
                arm = new Arm(patternType, localIndex, guardedValue, roots, guardIf.Condition);
                return true;
            }
            default:
                return false;
        }
    }

    static bool TryReturnArm(IfStatement armIf, int tempLocal, out Arm arm)
    {
        arm = null!;
        if (armIf.HasElse
            || !TryArmPattern(armIf.Condition, tempLocal, out var patternType, out var localIndex, out var patternRoots))
        {
            return false;
        }

        if (armIf.Then.Children is [Return { Value: { } value }])
        {
            var roots = new List<IrNode>(patternRoots) { value };
            arm = new Arm(patternType, localIndex, value, roots);
            return true;
        }

        if (armIf.Then.Children is [IfStatement guardIf]
            && !guardIf.HasElse
            && guardIf.Then.Children is [Return { Value: { } guardedValue }])
        {
            var roots = new List<IrNode>(patternRoots) { guardIf.Condition, guardedValue };
            arm = new Arm(patternType, localIndex, guardedValue, roots, guardIf.Condition);
            return true;
        }

        return false;
    }

    static bool TryReturnArmWithDefault(IfStatement armIf, int tempLocal, out Arm arm, out IrExpression defaultValue)
    {
        arm = null!;
        defaultValue = null!;
        if (armIf.HasElse
            || !TryArmPattern(armIf.Condition, tempLocal, out var patternType, out var localIndex, out var patternRoots)
            || armIf.Then.Children is not [IfStatement guardIf, Return { Value: { } armDefault }]
            || guardIf.HasElse
            || guardIf.Then.Children is not [Return { Value: { } guardedValue }])
        {
            return false;
        }

        var roots = new List<IrNode>(patternRoots) { guardIf.Condition, guardedValue };
        arm = new Arm(patternType, localIndex, guardedValue, roots, guardIf.Condition);
        defaultValue = armDefault;
        return true;
    }

    static bool BuildStatementSwitch(
        IrFunction function,
        LoadProperty unionValue,
        StoreLocal valueStore,
        StoreLocal firstStore,
        StoreLocal? receiverStore,
        IReadOnlyList<Arm> arms,
        IrExpression defaultValue,
        IfStatement dispatchRoot,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        var allowedTempUses = new List<IrNode> { valueStore, firstStore, dispatchRoot };
        if (receiverStore is not null)
        {
            allowedTempUses.Add(receiverStore);
            if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, receiverStore.Index, [receiverStore, valueStore]))
                return false;
        }

        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, valueStore.Index, allowedTempUses)
            || arms.Any(arm => !ArmLocalReferencesAreOwned(function, arm))
            || arms.Select(arm => arm.PatternType).Distinct().Count() != arms.Count)
        {
            return false;
        }

        switchExpression = new UnionSwitchExpression(
            UnionValueForSwitch(unionValue, receiverStore),
            arms.Select(arm => new UnionSwitchExpressionArm(
                arm.PatternType,
                arm.LocalIndex,
                (IrExpression)arm.Value.Clone(),
                arm.Guard is null ? null : (IrExpression)arm.Guard.Clone())),
            (IrExpression)defaultValue.Clone());
        return true;
    }

    static bool IsLocalReceiver(IrExpression? expression, int local)
        => expression is LoadLocal load && load.Index == local
            || expression is LoadLocalAddress address && address.Index == local;

    static LoadProperty UnionValueForSwitch(LoadProperty unionValue, StoreLocal? receiverStore)
    {
        var instance = receiverStore is null
            ? unionValue.Instance is null ? null : (IrExpression)unionValue.Instance.Clone()
            : (IrExpression)receiverStore.Value.Clone();
        return new LoadProperty(
            unionValue.Accessor,
            instance,
            unionValue.IndexArguments.Select(argument => (IrExpression)argument.Clone()).ToArray())
        { IsVirtual = unionValue.IsVirtual };
    }

    static bool TryMatchClassExhaustiveGuardedBlocks(
        IrFunction function,
        BlockContainer container,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        var blocks = container.Blocks;
        if (blocks.Count != 9
            || blocks[0].Children is not [ConditionalBranch nullBranch]
            || nullBranch.Condition is not LogicalNot { Operand: var receiver }
            || blocks[1].Children is not
            [
                StoreLocal { Value: LoadProperty unionValue } valueStore,
                StoreLocal { Value: IsInstance firstTest } firstStore,
                ConditionalBranch firstNullBranch
            ]
            || !IsUnionValueProperty(function, unionValue)
            || !PlaceIdentity.SameVariable(unionValue.Instance, receiver)
            || !IsTempTypeTest(firstTest, valueStore.Index)
            || !IsNotLocal(firstNullBranch.Condition, firstStore.Index)
            || blocks[2].Children is not [ConditionalBranch guardBranch]
            || blocks[3].Children is not [StoreLocal firstFallbackStore, Return firstFallbackReturn]
            || blocks[4].Children is not
            [
                StoreLocal { Value: IsInstance finalTest } finalStore,
                ConditionalBranch finalValueBranch
            ]
            || !IsTempTypeTest(finalTest, valueStore.Index)
            || finalValueBranch.Condition is not LoadLocal finalCondition || finalCondition.Index != finalStore.Index
            || blocks[5].Children is not [Branch finalThrowBranch]
            || blocks[6].Children is not [StoreLocal firstGuardedStore, Return firstGuardedReturn]
            || blocks[7].Children is not [StoreLocal finalValueStore, Return finalValueReturn]
            || !TryClassFinalBlock(blocks[8], firstFallbackStore.Index, receiver, out var defaultValue)
            || nullBranch.TargetOffset != blocks[8].StartOffset
            || firstNullBranch.TargetOffset != blocks[4].StartOffset
            || guardBranch.TargetOffset != blocks[6].StartOffset
            || finalValueBranch.TargetOffset != blocks[7].StartOffset
            || finalThrowBranch.TargetOffset != blocks[8].StartOffset
            || !StoreReturnMatch(firstFallbackStore, firstFallbackReturn, firstFallbackStore.Index)
            || !StoreReturnMatch(firstGuardedStore, firstGuardedReturn, firstFallbackStore.Index)
            || !StoreReturnMatch(finalValueStore, finalValueReturn, firstFallbackStore.Index))
        {
            return false;
        }

        var firstArms = BuildSplitArms(
            firstTest.Type,
            firstStore.Index,
            [firstStore, firstNullBranch.Condition],
            firstGuardedStore.Value,
            guardBranch.Condition,
            firstFallbackStore.Value,
            defaultValue);
        var finalArm = new Arm(finalTest.Type, finalStore.Index, finalValueStore.Value,
            [finalStore, finalValueBranch.Condition, finalValueStore.Value]);
        var arms = firstArms.Append(finalArm).ToArray();

        var allowedTempUses = blocks.Take(8).SelectMany(block => block.Children).ToArray();
        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, valueStore.Index, allowedTempUses)
            || arms.Any(arm => !ArmLocalReferencesAreOwned(function, arm))
            || !DuplicateTypesAreGuarded(arms))
        {
            return false;
        }

        switchExpression = new UnionSwitchExpression(
            (IrExpression)unionValue.Clone(),
            arms.Select(arm => new UnionSwitchExpressionArm(
                arm.PatternType,
                arm.LocalIndex,
                (IrExpression)arm.Value.Clone(),
                arm.Guard is null ? null : (IrExpression)arm.Guard.Clone())),
            defaultValue is null ? null : (IrExpression)defaultValue.Clone());
        return true;
    }

    static bool TryClassFinalBlock(
        Block finalBlock,
        int resultLocal,
        IrExpression receiver,
        out IrExpression? defaultValue)
    {
        defaultValue = null;
        if (finalBlock.Children is [ExpressionStatement throwStatement, Return throwReturn]
            && IsThrowSwitchExpression(throwStatement)
            && ThrowArgumentMatches(throwStatement, receiver)
            && ReturnsLocal(throwReturn, resultLocal))
        {
            return true;
        }

        if (finalBlock.Children is [StoreLocal defaultStore, Return defaultReturn]
            && StoreReturnMatch(defaultStore, defaultReturn, resultLocal))
        {
            defaultValue = defaultStore.Value;
            return true;
        }

        return false;
    }

    static IReadOnlyList<Arm> BuildSplitArms(
        TypeRef patternType,
        int localIndex,
        IReadOnlyList<IrNode> patternRoots,
        IrExpression guardedValue,
        IrExpression guard,
        IrExpression fallbackValue,
        IrExpression? defaultValue)
    {
        if (defaultValue is not null)
        {
            if (PlaceIdentity.SameOperand(fallbackValue, defaultValue))
            {
                return BuildArms(patternType, localIndex, patternRoots,
                [
                    new ArmBody(guardedValue, guard, [guard], KeepsLocal: true),
                ]);
            }

            if (PlaceIdentity.SameOperand(guardedValue, defaultValue))
            {
                return BuildArms(patternType, localIndex, patternRoots,
                [
                    new ArmBody(fallbackValue, Conditions.Negate((IrExpression)guard.Clone()), [guard], KeepsLocal: true),
                ]);
            }
        }

        return BuildArms(patternType, localIndex, patternRoots,
        [
            new ArmBody(guardedValue, guard, [guard], KeepsLocal: true),
            new ArmBody(fallbackValue, Guard: null, GuardRoots: [], ReferencesLocal(fallbackValue, localIndex)),
        ]);
    }

    static bool TryMatchClassDefaultAt(
        IrFunction function,
        IReadOnlyList<IrNode> children,
        int start,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        if (start + 2 != children.Count
            || children[start] is not IfStatement { HasElse: false } nullGuard
            || children[start + 1] is not Return { Value: { } defaultValue })
        {
            return false;
        }

        var body = nullGuard.Then.Children;
        if (body.Count < 2
            || body[0] is not StoreLocal { Value: LoadProperty unionValue } valueStore
            || !IsUnionValueProperty(function, unionValue)
            || !PlaceIdentity.SameVariable(unionValue.Instance, nullGuard.Condition))
        {
            return false;
        }

        int tempLocal = valueStore.Index;
        var armNodes = body.Skip(1).ToArray();
        if (!TryDefaultArms(armNodes, tempLocal, defaultValue, out var arms))
            return false;

        var allowedTempUses = new List<IrNode> { valueStore };
        allowedTempUses.AddRange(armNodes);
        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, tempLocal, allowedTempUses)
            || arms.Any(arm => !ArmLocalReferencesAreOwned(function, arm))
            || !DuplicateTypesAreGuarded(arms))
        {
            return false;
        }

        switchExpression = new UnionSwitchExpression(
            (IrExpression)unionValue.Clone(),
            arms.Select(arm => new UnionSwitchExpressionArm(
                arm.PatternType,
                arm.LocalIndex,
                (IrExpression)arm.Value.Clone(),
                arm.Guard is null ? null : (IrExpression)arm.Guard.Clone())),
            (IrExpression)defaultValue.Clone());
        return true;
    }

    static bool TryMatchDefaultChainAt(
        IrFunction function,
        IReadOnlyList<IrNode> children,
        int start,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        if (start + 2 >= children.Count
            || children[start] is not StoreLocal { Value: LoadProperty unionValue } valueStore
            || !IsUnionValueProperty(function, unionValue))
        {
            return false;
        }

        IrExpression defaultValue;
        IReadOnlyList<IrNode> armNodes;
        if (TryFinalFallbackDefault(children, start + 1, out var finalDefault))
        {
            defaultValue = finalDefault;
            armNodes = children.Skip(start + 1).ToArray();
        }
        else if (children[^1] is Return { Value: { } trailingDefault })
        {
            defaultValue = trailingDefault;
            armNodes = children.Skip(start + 1).Take(children.Count - start - 2).ToArray();
        }
        else
        {
            return false;
        }

        int tempLocal = valueStore.Index;
        if (!TryDefaultArms(armNodes, tempLocal, defaultValue, out var arms))
            return false;

        var allowedTempUses = new List<IrNode> { valueStore };
        allowedTempUses.AddRange(armNodes);
        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, tempLocal, allowedTempUses)
            || arms.Any(arm => !ArmLocalReferencesAreOwned(function, arm))
            || !DuplicateTypesAreGuarded(arms))
        {
            return false;
        }

        switchExpression = new UnionSwitchExpression(
            (IrExpression)unionValue.Clone(),
            arms.Select(arm => new UnionSwitchExpressionArm(
                arm.PatternType,
                arm.LocalIndex,
                (IrExpression)arm.Value.Clone(),
                arm.Guard is null ? null : (IrExpression)arm.Guard.Clone())),
            (IrExpression)defaultValue.Clone());
        return true;
    }

    static bool TryFinalFallbackDefault(IReadOnlyList<IrNode> children, int armStart, out IrExpression defaultValue)
    {
        defaultValue = null!;
        if (children.Count - armStart < 3
            || children[^3] is not StoreLocal { Value: IsInstance }
            || children[^2] is not IfStatement nullGuard
            || nullGuard.HasElse
            || nullGuard.Then.Children is not [Return { Value: { } fallback }]
            || children[^1] is not Return)
        {
            return false;
        }

        defaultValue = fallback;
        return true;
    }

    static bool TryMatchClassNullGuardAt(
        IrFunction function,
        IReadOnlyList<IrNode> children,
        int start,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        if (start + 3 != children.Count
            || children[start] is not IfStatement { HasElse: false } nullGuard
            || children[start + 1] is not ExpressionStatement throwStatement
            || children[start + 2] is not Return throwReturn
            || !IsThrowSwitchExpression(throwStatement)
            || !ThrowArgumentMatches(throwStatement, nullGuard.Condition)
            || !ReturnsLocal(throwReturn, out int resultLocal))
        {
            return false;
        }

        var body = nullGuard.Then.Children;
        if (body.Count is not (2 or 3)
            || body[0] is not StoreLocal { Value: LoadProperty unionValue } valueStore
            || !IsUnionValueProperty(function, unionValue)
            || !PlaceIdentity.SameVariable(unionValue.Instance, nullGuard.Condition))
        {
            return false;
        }

        int tempLocal = valueStore.Index;
        if (!TryFirstArmWithElse(body, tempLocal, resultLocal, out var firstIf, out var firstArm, out var firstExtraUse))
            return false;

        var allowedTempUses = firstExtraUse is null
            ? (IReadOnlyList<IrNode>)[valueStore, firstIf]
            : [valueStore, firstExtraUse, firstIf];
        return TryBuildSwitch(function, unionValue, tempLocal, resultLocal, firstIf.Then.Children, firstArm, allowedTempUses, out switchExpression);
    }

    static bool TryMatchAt(
        IrFunction function,
        IReadOnlyList<IrNode> children,
        int start,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        if (start + 3 >= children.Count
            || children[start] is not StoreLocal { Value: LoadProperty unionValue } valueStore
            || !IsUnionValueProperty(function, unionValue))
        {
            return false;
        }

        // The switch-expression dispatch is terminal. Prefix statements are OK;
        // trailing statements would be unreachable or unrelated and stay flat.
        int tempLocal = valueStore.Index;
        Arm firstArm;
        if (children[start + 1] is StoreLocal { Value: IsInstance firstAs } firstStore)
        {
            if (start + 5 != children.Count
                || !IsTempTypeTest(firstAs, tempLocal)
                || children[start + 2] is not IfStatement firstIf
                || !IsNotLocal(firstIf.Condition, firstStore.Index))
            {
                return false;
            }
            if (!TryStoreReturn(children, start + 3, out int resultLocal, out var firstValue))
            {
                return false;
            }
            firstArm = new Arm(firstAs.Type, firstStore.Index, firstValue, [firstStore, firstIf.Condition, firstValue]);
            return TryBuild(function, unionValue, tempLocal, resultLocal, firstIf, firstArm, firstStore, out switchExpression);
        }

        if (children[start + 1] is IfStatement noLocalIf
            && noLocalIf.Condition is LogicalNot { Operand: IsInstance firstTest }
            && IsTempTypeTest(firstTest, tempLocal))
        {
            if (start + 4 != children.Count)
                return false;
            if (!TryStoreReturn(children, start + 2, out int resultLocal, out var firstValue))
                return false;
            firstArm = new Arm(firstTest.Type, LocalIndex: null, firstValue, []);
            return TryBuild(function, unionValue, tempLocal, resultLocal, noLocalIf, firstArm, extraTempUse: null, out switchExpression);
        }

        return false;
    }

    static bool TryMatchExhaustiveGuardedChainAt(
        IrFunction function,
        IReadOnlyList<IrNode> children,
        int start,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        if (start + 5 >= children.Count
            || children[start] is not StoreLocal { Value: LoadProperty unionValue } valueStore
            || !IsUnionValueProperty(function, unionValue)
            || !TryFinalThrowArm(children, start + 1, out int resultLocal, out var finalArm, out var finalNodes))
        {
            return false;
        }

        int tempLocal = valueStore.Index;
        if (!IsTempLocal(finalArm, tempLocal))
            return false;

        var leadingNodes = children.Skip(start + 1).Take(children.Count - start - 1 - finalNodes.Count).ToArray();
        if (!TryExhaustiveGuardedArms(leadingNodes, tempLocal, resultLocal, out var leadingArms))
            return false;

        var arms = leadingArms.Append(finalArm).ToArray();
        var allowedTempUses = new List<IrNode> { valueStore };
        allowedTempUses.AddRange(leadingNodes);
        allowedTempUses.AddRange(finalNodes);
        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, tempLocal, allowedTempUses)
            || arms.Any(arm => !ArmLocalReferencesAreOwned(function, arm))
            || !DuplicateTypesAreGuarded(arms))
        {
            return false;
        }

        switchExpression = new UnionSwitchExpression(
            (IrExpression)unionValue.Clone(),
            arms.Select(arm => new UnionSwitchExpressionArm(
                arm.PatternType,
                arm.LocalIndex,
                (IrExpression)arm.Value.Clone(),
                arm.Guard is null ? null : (IrExpression)arm.Guard.Clone())));
        return true;
    }

    static bool TryExhaustiveGuardedArms(IReadOnlyList<IrNode> nodes, int tempLocal, int resultLocal, out IReadOnlyList<Arm> arms)
    {
        var builder = new List<Arm>();
        foreach (var node in nodes)
        {
            if (node is not IfStatement armIf
                || !TryArmPattern(armIf.Condition, tempLocal, out var patternType, out var localIndex, out var patternRoots)
                || !TryExhaustiveArmBody(armIf.Then.Children, localIndex, resultLocal, out var matchedArms))
            {
                arms = [];
                return false;
            }

            builder.AddRange(BuildArms(patternType, localIndex, patternRoots, matchedArms));
        }

        arms = builder;
        return arms.Count > 0;
    }

    static bool TryExhaustiveArmBody(
        IReadOnlyList<IrNode> nodes,
        int? localIndex,
        int resultLocal,
        out IReadOnlyList<ArmBody> arms)
    {
        arms = [];
        if (nodes is [IfStatement { HasElse: false } guardIf, StoreLocal fallbackStore, Return fallbackReturn]
            && guardIf.Then.Children is [StoreLocal guardedStore, Return guardedReturn]
            && StoreReturnMatch(guardedStore, guardedReturn, resultLocal)
            && StoreReturnMatch(fallbackStore, fallbackReturn, resultLocal))
        {
            bool fallbackKeepsLocal = localIndex is { } local && ReferencesLocal(fallbackStore.Value, local);
            arms =
            [
                new ArmBody(guardedStore.Value, guardIf.Condition, [guardIf.Condition], KeepsLocal: true),
                new ArmBody(fallbackStore.Value, Guard: null, GuardRoots: [], fallbackKeepsLocal),
            ];
            return true;
        }

        if (nodes is [StoreLocal store, Return ret]
            && StoreReturnMatch(store, ret, resultLocal))
        {
            arms = [new ArmBody(store.Value, Guard: null, GuardRoots: [], KeepsLocal: localIndex is { } armLocal && ReferencesLocal(store.Value, armLocal))];
            return true;
        }

        return false;
    }

    static bool TryFinalThrowArm(
        IReadOnlyList<IrNode> children,
        int armStart,
        out int resultLocal,
        out Arm arm,
        out IReadOnlyList<IrNode> consumed)
    {
        resultLocal = -1;
        arm = null!;
        consumed = [];
        if (children.Count - armStart < 4
            || children[^4] is not StoreLocal { Value: IsInstance test } store
            || children[^3] is not IfStatement nullGuard
            || nullGuard.HasElse
            || !IsNotLocal(nullGuard.Condition, store.Index)
            || nullGuard.Then.Children is not [ExpressionStatement throwStatement, Return throwReturn]
            || !IsThrowSwitchExpression(throwStatement)
            || children[^2] is not StoreLocal valueStore
            || children[^1] is not Return valueReturn
            || !StoreReturnMatch(valueStore, valueReturn, valueStore.Index)
            || !ReturnsLocal(throwReturn, valueStore.Index))
        {
            return false;
        }

        resultLocal = valueStore.Index;
        arm = new Arm(test.Type, store.Index, valueStore.Value, [store, nullGuard.Condition, valueStore.Value]);
        consumed = [children[^4], children[^3], children[^2], children[^1]];
        return true;
    }

    static bool StoreReturnMatch(StoreLocal store, Return ret, int local)
        => store.Index == local && ReturnsLocal(ret, local);

    static bool IsTempLocal(Arm arm, int tempLocal)
        => arm.LocalRoots.OfType<StoreLocal>().FirstOrDefault()?.Value is IsInstance test
            && IsTempTypeTest(test, tempLocal);

    static bool TryBuild(
        IrFunction function,
        LoadProperty unionValue,
        int tempLocal,
        int resultLocal,
        IfStatement firstIf,
        Arm firstArm,
        IrNode? extraTempUse,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        if (firstIf.HasElse
            || firstIf.Then.Children.Count < 3
            || firstIf.Then.Children[^2] is not ExpressionStatement throwStatement
            || firstIf.Then.Children[^1] is not Return throwReturn
            || !IsThrowSwitchExpression(throwStatement)
            || !ReturnsLocal(throwReturn, resultLocal))
        {
            return false;
        }

        // The cached Value local exists only to feed the tests this switch owns.
        var allowedTempUses = extraTempUse is null
            ? (IReadOnlyList<IrNode>)[unionValue.Parent!, firstIf]
            : [unionValue.Parent!, extraTempUse, firstIf];
        return TryBuildSwitch(function, unionValue, tempLocal, resultLocal, firstIf.Then.Children.Take(firstIf.Then.Children.Count - 2), firstArm, allowedTempUses, out switchExpression);
    }

    static bool TryBuildSwitch(
        IrFunction function,
        LoadProperty unionValue,
        int tempLocal,
        int resultLocal,
        IEnumerable<IrNode> innerArmNodes,
        Arm firstArm,
        IReadOnlyList<IrNode> allowedTempUses,
        out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        if (!TryInnerArms(innerArmNodes, tempLocal, resultLocal, out var innerArms))
            return false;

        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, tempLocal, allowedTempUses)
            || !ArmLocalReferencesAreOwned(function, firstArm)
            || innerArms.Any(arm => !ArmLocalReferencesAreOwned(function, arm)))
        {
            return false;
        }

        var arms = new[] { firstArm }.Concat(innerArms).ToArray();
        if (arms.Select(arm => arm.PatternType).Distinct().Count() != arms.Length)
            return false;

        switchExpression = new UnionSwitchExpression(
            (IrExpression)unionValue.Clone(),
            arms.Select(arm => new UnionSwitchExpressionArm(
                arm.PatternType,
                arm.LocalIndex,
                (IrExpression)arm.Value.Clone(),
                arm.Guard is null ? null : (IrExpression)arm.Guard.Clone())));
        return true;
    }

    static bool TryDefaultArm(IfStatement armIf, int tempLocal, IrExpression defaultValue, out IReadOnlyList<Arm> arms)
    {
        arms = [];
        if (armIf.HasElse)
            return false;

        if (TryArmPattern(armIf.Condition, tempLocal, out var patternType, out var localIndex, out var patternRoots)
            && TryArmBody(armIf.Then.Children, localIndex, defaultValue, out var matchedArms))
        {
            arms = BuildArms(patternType, localIndex, patternRoots, matchedArms);
            return true;
        }

        return false;
    }

    sealed record ArmBody(IrExpression Value, IrExpression? Guard, IReadOnlyList<IrNode> GuardRoots, bool KeepsLocal);

    static bool TryArmPattern(IrExpression condition, int tempLocal, out TypeRef patternType, out int? localIndex, out IReadOnlyList<IrNode> roots)
    {
        switch (condition)
        {
            case IsPattern pattern when IsTempLoad(pattern.Value, tempLocal):
                patternType = pattern.Type;
                localIndex = pattern.LocalIndex;
                roots = [pattern];
                return true;
            case IsInstance test when IsTempTypeTest(test, tempLocal):
                patternType = test.Type;
                localIndex = null;
                roots = [];
                return true;
            default:
                patternType = null!;
                localIndex = null;
                roots = [];
                return false;
        }
    }

    static bool TryArmBody(
        IReadOnlyList<IrNode> nodes,
        int? localIndex,
        IrExpression defaultValue,
        out IReadOnlyList<ArmBody> arms)
    {
        arms = [];

        if (nodes is [Return { Value: { } direct }])
        {
            arms = [new ArmBody(direct, Guard: null, GuardRoots: [], KeepsLocal: localIndex is not null && ReferencesLocal(direct, localIndex.Value))];
            return true;
        }

        if (nodes is [IfStatement { HasElse: false } guardIf, Return fallback]
            && fallback.Value is { } fallbackValue
            && guardIf.Then.Children is [Return { Value: { } guardedValue }])
        {
            if (PlaceIdentity.SameOperand(fallbackValue, defaultValue))
            {
                arms = [new ArmBody(guardedValue, guardIf.Condition, [guardIf.Condition], KeepsLocal: true)];
                return true;
            }

            if (PlaceIdentity.SameOperand(guardedValue, defaultValue))
            {
                arms = [new ArmBody(fallbackValue, Conditions.Negate((IrExpression)guardIf.Condition.Clone()), [guardIf.Condition], KeepsLocal: localIndex is not null)];
                return true;
            }

            if (localIndex is not { } fallbackLocal || !ReferencesLocal(fallbackValue, fallbackLocal))
            {
                arms =
                [
                    new ArmBody(guardedValue, guardIf.Condition, [guardIf.Condition], KeepsLocal: true),
                    new ArmBody(fallbackValue, Guard: null, GuardRoots: [], KeepsLocal: false),
                ];
                return true;
            }

            arms =
            [
                new ArmBody(guardedValue, guardIf.Condition, [guardIf.Condition], KeepsLocal: true),
                new ArmBody(fallbackValue, Guard: null, GuardRoots: [], KeepsLocal: true),
            ];
            return true;
        }

        if (nodes is [IfStatement { HasElse: false } invertedGuardIf, Return { Value: { } invertedValue }]
            && invertedGuardIf.Then.Children is [Return { Value: { } nestedFallback }])
        {
            if (PlaceIdentity.SameOperand(nestedFallback, defaultValue))
            {
                arms = [new ArmBody(invertedValue, Conditions.Negate((IrExpression)invertedGuardIf.Condition.Clone()), [invertedGuardIf.Condition], KeepsLocal: true)];
                return true;
            }

            if (PlaceIdentity.SameOperand(invertedValue, defaultValue))
            {
                arms = [new ArmBody(nestedFallback, invertedGuardIf.Condition, [invertedGuardIf.Condition], KeepsLocal: true)];
                return true;
            }

            if (localIndex is not { } invertedFallbackLocal || !ReferencesLocal(nestedFallback, invertedFallbackLocal))
            {
                arms =
                [
                    new ArmBody(invertedValue, Conditions.Negate((IrExpression)invertedGuardIf.Condition.Clone()), [invertedGuardIf.Condition], KeepsLocal: true),
                    new ArmBody(nestedFallback, Guard: null, GuardRoots: [], KeepsLocal: false),
                ];
                return true;
            }

            arms =
            [
                new ArmBody(invertedValue, Conditions.Negate((IrExpression)invertedGuardIf.Condition.Clone()), [invertedGuardIf.Condition], KeepsLocal: true),
                new ArmBody(nestedFallback, Guard: null, GuardRoots: [], KeepsLocal: true),
            ];
            return true;
        }

        return false;
    }

    static IReadOnlyList<Arm> BuildArms(
        TypeRef patternType,
        int? localIndex,
        IReadOnlyList<IrNode> patternRoots,
        IReadOnlyList<ArmBody> bodies)
    {
        var sharedLocalRoots = new List<IrNode>(patternRoots);
        foreach (var body in bodies)
        {
            sharedLocalRoots.Add(body.Value);
            sharedLocalRoots.AddRange(body.GuardRoots);
        }

        return bodies.Select(body =>
        {
            IReadOnlyList<IrNode> localRoots;
            if (body.KeepsLocal && localIndex is not null)
            {
                localRoots = sharedLocalRoots;
            }
            else
            {
                var roots = new List<IrNode>(patternRoots) { body.Value };
                roots.AddRange(body.GuardRoots);
                localRoots = roots;
            }
            return new Arm(patternType, body.KeepsLocal ? localIndex : null, body.Value, localRoots, body.Guard);
        }).ToArray();
    }

    static bool ReferencesLocal(IrExpression expression, int local)
        => ReferenceOwnership.SubtreeReferencesLocal(expression, local);

    static bool TryInnerArms(IEnumerable<IrNode> nodes, int tempLocal, int resultLocal, out IReadOnlyList<Arm> arms)
    {
        var builder = new List<Arm>();
        foreach (var node in nodes)
        {
            if (node is not IfStatement armIf || !TryArm(armIf, tempLocal, resultLocal, out var arm))
            {
                arms = [];
                return false;
            }
            builder.Add(arm);
        }

        arms = builder;
        return arms.Count > 0;
    }

    static bool TryDefaultArms(IReadOnlyList<IrNode> nodes, int tempLocal, IrExpression defaultValue, out IReadOnlyList<Arm> arms)
    {
        var builder = new List<Arm>();
        for (int i = 0; i < nodes.Count;)
        {
            if (nodes[i] is IfStatement armIf && TryDefaultArm(armIf, tempLocal, defaultValue, out var arm))
            {
                builder.AddRange(arm);
                i++;
                continue;
            }

            if (i + 2 < nodes.Count
                && nodes[i] is StoreLocal { Value: IsInstance asCast } store
                && IsTempTypeTest(asCast, tempLocal)
                && nodes[i + 1] is IfStatement nullGuard
                && !nullGuard.HasElse
                && IsNotLocal(nullGuard.Condition, store.Index)
                && nullGuard.Then.Children is [Return { Value: { } fallback }]
                && PlaceIdentity.SameOperand(fallback, defaultValue)
                && nodes[i + 2] is Return { Value: { } value })
            {
                builder.Add(new Arm(asCast.Type, store.Index, value, [store, nullGuard.Condition, value]));
                i += 3;
                continue;
            }

            arms = [];
            return false;
        }

        arms = builder;
        return arms.Count > 0;
    }

    static bool TryArm(IfStatement armIf, int tempLocal, int resultLocal, out Arm arm)
    {
        arm = null!;
        if (armIf.HasElse || armIf.Then.Children.Count != 2)
            return false;

        if (!TryStoreReturn(armIf.Then.Children, 0, out int armResultLocal, out var value)
            || armResultLocal != resultLocal)
        {
            return false;
        }

        switch (armIf.Condition)
        {
            case IsPattern pattern when IsTempLoad(pattern.Value, tempLocal):
                arm = new Arm(pattern.Type, pattern.LocalIndex, value, [pattern, value]);
                return true;
            case IsInstance test when IsTempTypeTest(test, tempLocal):
                arm = new Arm(test.Type, LocalIndex: null, value, []);
                return true;
            default:
                return false;
        }
    }

    static bool TryFirstArmWithElse(
        IReadOnlyList<IrNode> body,
        int tempLocal,
        int resultLocal,
        out IfStatement firstIf,
        out Arm firstArm,
        out IrNode? firstExtraUse)
    {
        firstIf = null!;
        firstArm = null!;
        firstExtraUse = null;
        if (body[1] is StoreLocal { Value: IsInstance firstAs } firstStore)
        {
            if (!IsTempTypeTest(firstAs, tempLocal)
                || body.Count != 3
                || body[2] is not IfStatement { HasElse: true } withLocalIf
                || !IsNotLocal(withLocalIf.Condition, firstStore.Index)
                || withLocalIf.Else is not { } elseArm
                || elseArm.Children.Count != 2
                || !TryStoreReturn(elseArm.Children, 0, out int firstResultLocal, out var firstValue)
                || firstResultLocal != resultLocal)
            {
                return false;
            }

                firstIf = withLocalIf;
                firstArm = new Arm(firstAs.Type, firstStore.Index, firstValue, [firstStore, withLocalIf.Condition, firstValue]);
                firstExtraUse = firstStore;
                return true;
        }

        if (body[1] is IfStatement { HasElse: true } noLocalIf
                && noLocalIf.Condition is LogicalNot { Operand: IsInstance firstTest }
                && IsTempTypeTest(firstTest, tempLocal)
                && noLocalIf.Else is { } noLocalElse
                && noLocalElse.Children.Count == 2
                && TryStoreReturn(noLocalElse.Children, 0, out int noLocalResultLocal, out var noLocalValue)
                && noLocalResultLocal == resultLocal)
        {
                firstIf = noLocalIf;
                firstArm = new Arm(firstTest.Type, LocalIndex: null, noLocalValue, []);
                return true;
        }

        return false;
    }

    static bool TryStoreReturn(IReadOnlyList<IrNode> nodes, int index, out int local, out IrExpression value)
    {
        local = -1;
        value = null!;
        if (index + 1 >= nodes.Count
            || nodes[index] is not StoreLocal store
            || nodes[index + 1] is not Return ret
            || !ReturnsLocal(ret, store.Index))
        {
            return false;
        }

        local = store.Index;
        value = store.Value;
        return true;
    }

    static bool ReturnsLocal(Return ret, int local)
        => ret.Value is LoadLocal load && load.Index == local;

    static bool ReturnsLocal(Return ret, out int local)
    {
        if (ret.Value is LoadLocal load)
        {
            local = load.Index;
            return true;
        }

        local = -1;
        return false;
    }

    static bool IsNotLocal(IrExpression expression, int local)
        => expression is LogicalNot { Operand: LoadLocal load } && load.Index == local;

    static bool IsTempTypeTest(IsInstance test, int tempLocal)
        => IsTempLoad(test.Operand, tempLocal);

    static bool IsTempLoad(IrExpression expression, int tempLocal)
        => expression is LoadLocal load && load.Index == tempLocal;

    static bool ArmLocalReferencesAreOwned(IrFunction function, Arm arm)
        => arm.LocalIndex is not { } local
            || ReferenceOwnership.LocalReferencesOnlyWithin(function, local, arm.LocalRoots);

    static bool DuplicateTypesAreGuarded(IReadOnlyList<Arm> arms)
    {
        for (int i = 0; i < arms.Count; i++)
        {
            for (int j = i + 1; j < arms.Count; j++)
            {
                if (arms[i].PatternType.Equals(arms[j].PatternType)
                    && arms[i].Guard is null)
                {
                    return false;
                }
            }
        }
        return true;
    }

    static bool IsThrowSwitchExpression(ExpressionStatement statement)
        => statement.Expression is Call
        {
            Callee:
            {
                Name: "ThrowSwitchExpressionException",
                DeclaringType.Name: "<PrivateImplementationDetails>",
            },
        };

    static bool ThrowArgumentMatches(ExpressionStatement statement, IrExpression receiver)
        => statement.Expression is Call { Arguments: [var argument] }
        && PlaceIdentity.SameVariable(argument, receiver);

    static bool IsUnionValueProperty(IrFunction function, LoadProperty property)
        => property.PropertyName == "Value"
        && property.IndexArguments.Count == 0
        && function.UnionTypes.Contains(NamedDefinition(property.Accessor.DeclaringType))
        && IsSimpleUnionValueReceiver(property.Instance);

    static bool IsSimpleUnionValueReceiver(IrExpression? receiver) => receiver switch
    {
        LoadArgumentAddress or LoadArgument or LoadLocalAddress or LoadLocal => true,
        LoadFieldAddress field => field.Instance is null || IsSimpleUnionValueReceiver(field.Instance),
        LoadField field => field.Instance is null || IsSimpleUnionValueReceiver(field.Instance),
        _ => false,
    };

    static TypeRef NamedDefinition(TypeRef type)
        => type is { Kind: TypeRefKind.GenericInstance, ElementType: { } definition } ? definition : type;
}
