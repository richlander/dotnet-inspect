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

    sealed record Arm(TypeRef PatternType, int? LocalIndex, IrExpression Value, IReadOnlyList<IrNode> LocalRoots);
    sealed record Match(int StartIndex, UnionSwitchExpression SwitchExpression);

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (container.Blocks is [var block] && TryMatch(function, block, out var match))
            {
                block.SetChild(match.StartIndex, new Return(match.SwitchExpression));
                for (int i = block.Children.Count - 1; i > match.StartIndex; i--)
                    block.Children[i].Detach();
                context.Stepper.StepOver("raise union type-test dispatch to switch expression", block);
                continue;
            }
        }
    }

    static bool TryMatch(IrFunction function, Block block, out Match match)
    {
        match = null!;
        var children = block.Children;
        for (int start = 0; start < children.Count; start++)
        {
            if (TryMatchClassNullGuardAt(function, children, start, out var guardedSwitch))
            {
                match = new Match(start, guardedSwitch);
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
            arms.Select(arm => new UnionSwitchExpressionArm(arm.PatternType, arm.LocalIndex, (IrExpression)arm.Value.Clone())));
        return true;
    }

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
