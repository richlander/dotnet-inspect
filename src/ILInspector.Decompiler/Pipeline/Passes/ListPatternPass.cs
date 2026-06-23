namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's single-element string-array list-pattern lowering back to
/// <c>args is ["a" or "b"]</c>. The compiler shape is a pre-structuring run:
/// null/length guards to the false arm, an unnamed element-zero temp, an
/// exact-BCL <c>string.op_Equality</c> OR chain, then an unnamed bool result
/// diamond consumed by a following branch.
/// </summary>
public sealed class ListPatternPass : IIrPass
{
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");

    public string Name => "list-pattern";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            var blocks = container.Blocks;
            for (int p = 0; p + 6 < blocks.Count; p++)
            {
                if (TryMatch(function, blocks, p, out var match)
                    && NoExternalEntry(blocks, p, consumedCount: 7))
                {
                    Fold(container, blocks, p, match, stepper);
                    return true;
                }
            }
        }
        return false;
    }

    static bool TryMatch(IrFunction function, IReadOnlyList<Block> blocks, int p, out Match match)
    {
        match = default!;

        if (blocks[p].Children is not [ConditionalBranch lengthGuard]
            || !TryNullOrLengthFailure(lengthGuard.Condition, out var array)
            || blocks[p + 1].Children is not [StoreLocal elementStore, ConditionalBranch equalityGuard]
            || lengthGuard.TargetOffset != equalityGuard.TargetOffset
            || !TryElementZeroStore(function, elementStore, array, out int elementLocal)
            || equalityGuard.Condition is not LogicalNot equalityNot)
        {
            return false;
        }

        var alternatives = new List<Constant>();
        if (!TryCollectStringEqualityAlternatives(equalityNot.Operand, elementLocal, alternatives)
            || alternatives.Count < 2)
        {
            return false;
        }

        if (blocks[p + 2].Children is not [StoreLocal trueStore, Branch trueBranch]
            || blocks[p + 3].StartOffset != lengthGuard.TargetOffset
            || blocks[p + 3].Children is not [StoreLocal falseStore]
            || blocks[p + 4].StartOffset != trueBranch.TargetOffset
            || blocks[p + 4].Children is not [ConditionalBranch resultGuard]
            || resultGuard.Condition is not LogicalNot { Operand: LoadLocal resultLoad }
            || blocks[p + 5].Children is not [Return trueReturn]
            || blocks[p + 6].StartOffset != resultGuard.TargetOffset
            || blocks[p + 6].Children is not [Return falseReturn]
            || !TryBoolStore(function, trueStore, value: true, out int resultLocal)
            || !TryBoolStore(function, falseStore, value: false, out int falseLocal)
            || falseLocal != resultLocal
            || resultLoad.Index != resultLocal
            || !ReferenceOwnership.LocalReferencesOnlyWithin(function, elementLocal, [elementStore, equalityGuard.Condition])
            || !ReferenceOwnership.LocalReferencesOnlyWithin(function, resultLocal, [trueStore, falseStore, resultGuard.Condition]))
        {
            return false;
        }

        match = new Match(array, alternatives, trueReturn, falseReturn);
        return true;
    }

    static bool TryNullOrLengthFailure(IrExpression condition, out IrExpression array)
    {
        array = null!;
        return condition is LogicalBinary { Kind: LogicalKind.Or } logical
            && TryNullFailure(logical.Left, out array)
            && TryLengthNotOneFailure(logical.Right, array);
    }

    static bool TryNullFailure(IrExpression condition, out IrExpression array)
    {
        array = null!;
        if (condition is LogicalNot { Operand: LoadArgument or LoadLocal } not)
        {
            array = not.Operand;
            return true;
        }
        return false;
    }

    static bool TryLengthNotOneFailure(IrExpression condition, IrExpression array)
        => condition is Comparison
        {
            Kind: ComparisonKind.NotEqual,
            IsUnsigned: false,
            Left: ArrayLength length,
            Right: Constant { Value: 1, Type: var type },
        }
        && type.Equals(s_int)
        && PlaceIdentity.SameVariable(length.Array, array);

    static bool TryElementZeroStore(IrFunction function, StoreLocal store, IrExpression array, out int elementLocal)
    {
        elementLocal = -1;
        if (HasSourceLocalName(function, store.Index)
            || !store.Type.Equals(s_string)
            || store.Value is not LoadElement
            {
                Array: var elementArray,
                Index: Constant { Value: 0, Type: var indexType },
                ResultType: var elementType,
            }
            || !indexType.Equals(s_int)
            || elementType is null
            || !elementType.Equals(s_string)
            || !PlaceIdentity.SameVariable(elementArray, array))
        {
            return false;
        }

        elementLocal = store.Index;
        return true;
    }

    static bool TryCollectStringEqualityAlternatives(IrExpression condition, int elementLocal, List<Constant> alternatives)
    {
        if (condition is LogicalBinary { Kind: LogicalKind.Or } logical)
        {
            return TryCollectStringEqualityAlternatives(logical.Left, elementLocal, alternatives)
                && TryCollectStringEqualityAlternatives(logical.Right, elementLocal, alternatives);
        }

        if (condition is Call { Arguments: var args } call
            && MemberIdentity.IsStringEquality(call))
        {
            if (args[0] is LoadLocal left && left.Index == elementLocal && args[1] is Constant { Value: string } right)
            {
                alternatives.Add((Constant)right.Clone());
                return true;
            }

            if (args[1] is LoadLocal rightLoad && rightLoad.Index == elementLocal && args[0] is Constant { Value: string } leftConstant)
            {
                alternatives.Add((Constant)leftConstant.Clone());
                return true;
            }
        }

        return false;
    }

    static bool TryBoolStore(IrFunction function, StoreLocal store, bool value, out int local)
    {
        local = -1;
        if (HasSourceLocalName(function, store.Index)
            || !store.Type.Equals(s_bool)
            || store.Value is not Constant { Value: bool actual }
            || actual != value)
        {
            return false;
        }

        local = store.Index;
        return true;
    }

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
        && index < function.LocalNames.Length
        && !string.IsNullOrEmpty(function.LocalNames[index]);

    static bool NoExternalEntry(IReadOnlyList<Block> blocks, int p, int consumedCount)
    {
        var consumedInnerOffsets = blocks
            .Skip(p + 1)
            .Take(consumedCount - 1)
            .Select(block => block.StartOffset)
            .ToHashSet();

        for (int i = 0; i < blocks.Count; i++)
        {
            if (i >= p && i < p + consumedCount)
                continue;

            foreach (var node in blocks[i].Descendants.Prepend(blocks[i]))
                foreach (int target in Targets(node))
                    if (consumedInnerOffsets.Contains(target))
                        return false;
        }
        return true;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        Leave leave => [leave.TargetOffset],
        _ => [],
    };

    static void Fold(BlockContainer container, IReadOnlyList<Block> blocks, int p, Match match, Stepper stepper)
    {
        var rebuilt = new BlockContainer();
        for (int i = 0; i < p; i++)
            rebuilt.Add((Block)blocks[i].Clone());

        var raised = new Block(blocks[p].StartOffset);
        var then = new Block(blocks[p + 5].StartOffset);
        then.Add(new Return(match.TrueReturn.Value is null ? null : (IrExpression)match.TrueReturn.Value.Clone()));
        raised.Add(new IfStatement(
            new SingleElementListPattern((IrExpression)match.Array.Clone(), match.Alternatives),
            then,
            elseArm: null));
        raised.Add(new Return(match.FalseReturn.Value is null ? null : (IrExpression)match.FalseReturn.Value.Clone()));
        rebuilt.Add(raised);

        for (int i = p + 7; i < blocks.Count; i++)
            rebuilt.Add((Block)blocks[i].Clone());

        stepper.StepOver("raise single-element string-array list pattern", container);
        container.ReplaceWith(rebuilt);
    }

    sealed record Match(
        IrExpression Array,
        IReadOnlyList<Constant> Alternatives,
        Return TrueReturn,
        Return FalseReturn);
}
