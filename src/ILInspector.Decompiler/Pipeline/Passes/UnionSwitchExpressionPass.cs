namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the two-arm union switch-expression lowering Roslyn currently emits:
/// one cached <c>union.Value</c>, two ordered type tests, value arms, and the
/// compiler's unreachable <c>ThrowSwitchExpressionException</c> fallback. This is
/// deliberately narrower than general pattern-switch reconstruction.
/// </summary>
public sealed class UnionSwitchExpressionPass : IIrPass
{
    public string Name => "union-switch-expression";

    sealed record Arm(TypeRef PatternType, int? LocalIndex, IrExpression Value, IReadOnlyList<IrNode> LocalRoots);

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (container.Blocks is [var block] && TryMatch(function, block, out var switchExpression))
            {
                foreach (var child in block.Children.ToList())
                    child.Detach();
                block.Add(new Return(switchExpression));
                context.Stepper.StepOver("raise union type-test dispatch to switch expression", block);
                return;
            }
        }
    }

    static bool TryMatch(IrFunction function, Block block, out UnionSwitchExpression switchExpression)
    {
        switchExpression = null!;
        var children = block.Children;
        if (children.Count < 4
            || children[0] is not StoreLocal { Value: LoadProperty unionValue } valueStore
            || !IsUnionValueProperty(function, unionValue))
        {
            return false;
        }

        int tempLocal = valueStore.Index;
        Arm firstArm;
        if (children[1] is StoreLocal { Value: IsInstance firstAs } firstStore)
        {
            if (children.Count != 5
                || !IsTempTypeTest(firstAs, tempLocal)
                || children[2] is not IfStatement firstIf
                || !IsNotLocal(firstIf.Condition, firstStore.Index))
            {
                return false;
            }
            if (!TryStoreReturn(children, 3, out int resultLocal, out var firstValue))
            {
                return false;
            }
            firstArm = new Arm(firstAs.Type, firstStore.Index, firstValue, [firstStore, firstIf.Condition, firstValue]);
            return TryBuild(function, unionValue, tempLocal, resultLocal, firstIf, firstArm, firstStore, out switchExpression);
        }

        if (children[1] is IfStatement noLocalIf
            && noLocalIf.Condition is LogicalNot { Operand: IsInstance firstTest }
            && IsTempTypeTest(firstTest, tempLocal))
        {
            if (children.Count != 4)
                return false;
            if (!TryStoreReturn(children, 2, out int resultLocal, out var firstValue))
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
            || firstIf.Then.Children is not [IfStatement secondIf, ExpressionStatement throwStatement, Return throwReturn]
            || !IsThrowSwitchExpression(throwStatement)
            || !ReturnsLocal(throwReturn, resultLocal)
            || !TrySecondArm(secondIf, tempLocal, resultLocal, out var secondArm))
        {
            return false;
        }

        // The cached Value local exists only to feed the tests this switch owns.
        var allowedTempUses = extraTempUse is null
            ? (IReadOnlyList<IrNode>)[unionValue.Parent!, firstIf]
            : [unionValue.Parent!, extraTempUse, firstIf];
        if (!ReferenceOwnership.LocalReferencesOnlyWithin(function, tempLocal, allowedTempUses)
            || !ArmLocalReferencesAreOwned(function, firstArm)
            || !ArmLocalReferencesAreOwned(function, secondArm))
        {
            return false;
        }

        switchExpression = new UnionSwitchExpression(
            (IrExpression)unionValue.Clone(),
            [
                new UnionSwitchExpressionArm(firstArm.PatternType, firstArm.LocalIndex, (IrExpression)firstArm.Value.Clone()),
                new UnionSwitchExpressionArm(secondArm.PatternType, secondArm.LocalIndex, (IrExpression)secondArm.Value.Clone()),
            ]);
        return true;
    }

    static bool TrySecondArm(IfStatement secondIf, int tempLocal, int resultLocal, out Arm arm)
    {
        arm = null!;
        if (secondIf.HasElse || secondIf.Then.Children.Count != 2)
            return false;

        if (!TryStoreReturn(secondIf.Then.Children, 0, out int secondResultLocal, out var value)
            || secondResultLocal != resultLocal)
        {
            return false;
        }

        switch (secondIf.Condition)
        {
            case IsPattern pattern when IsTempLoad(pattern.Value, tempLocal) && ReferencesLocalOnly(value, pattern.LocalIndex):
                arm = new Arm(pattern.Type, pattern.LocalIndex, value, [pattern, value]);
                return true;
            case IsInstance test when IsTempTypeTest(test, tempLocal):
                arm = new Arm(test.Type, LocalIndex: null, value, []);
                return true;
            default:
                return false;
        }
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

    static bool IsNotLocal(IrExpression expression, int local)
        => expression is LogicalNot { Operand: LoadLocal load } && load.Index == local;

    static bool IsTempTypeTest(IsInstance test, int tempLocal)
        => IsTempLoad(test.Operand, tempLocal);

    static bool IsTempLoad(IrExpression expression, int tempLocal)
        => expression is LoadLocal load && load.Index == tempLocal;

    static bool ReferencesLocalOnly(IrExpression expression, int local)
        => expression.Descendants.Prepend(expression)
            .OfType<LoadLocal>()
            .All(load => load.Index == local);

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
