namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers runtime-async awaiter scaffolds emitted for awaitables that cannot
/// use the direct <c>AsyncHelpers.Await</c> route. The defining method flag,
/// exact CoreLib helper, one correlated awaiter local, and exclusive three-block
/// CFG ownership are all required before the scaffold becomes an await.
/// </summary>
public sealed class RuntimeAsyncAwaiterPass : IIrPass
{
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");

    public string Name => "runtime-async-awaiter";

    public void Run(IrFunction function, PassContext context)
    {
        if (function.IsRuntimeAsync != MetadataFactState.Yes)
            return;

        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            var blocks = container.Blocks;
            for (int index = 0; index + 2 < blocks.Count; index++)
            {
                if (TryMatch(function, blocks, index) is { } match)
                {
                    Fold(container, index, match, stepper);
                    return true;
                }
            }
        }
        return false;
    }

    readonly record struct AwaiterShape(
        StoreLocal AwaitableStore,
        StoreLocal AwaiterStore,
        ConditionalBranch Branch,
        ExpressionStatement HelperStatement,
        Call GetResult);

    static AwaiterShape? TryMatch(IrFunction function, IReadOnlyList<Block> blocks, int index)
    {
        var head = blocks[index];
        var helperBlock = blocks[index + 1];
        var merge = blocks[index + 2];
        if (head.Children.Count < 3
            || head.Children[^3] is not StoreLocal awaitableStore
            || head.Children[^2] is not StoreLocal awaiterStore
            || head.Children[^1] is not ConditionalBranch branch
            || branch.TargetOffset != merge.StartOffset
            || helperBlock.Children is not [ExpressionStatement { Expression: Call helperCall } helperStatement]
            || !MemberIdentity.IsAsyncHelpersAwaiter(helperCall, out var awaiterType)
            || helperCall.Arguments is not [LoadLocal helperAwaiter]
            || helperAwaiter.Index != awaiterStore.Index
            || !awaiterStore.Type.Equals(awaiterType)
            || !TryGetAwaitable(awaitableStore, awaiterStore, awaiterType)
            || !IsCompletedTest(branch.Condition, awaiterStore.Index, awaiterType)
            || !TryGetResult(merge, awaiterStore.Index, awaiterType, out var getResult)
            || !HasExclusiveControlFlow(function, branch, helperBlock.StartOffset, merge.StartOffset)
            || !LocalDefinitionRangeOwned(
                function,
                awaitableStore.Index,
                awaitableStore,
                [awaitableStore, awaiterStore])
            || !LocalDefinitionRangeOwned(
                function,
                awaiterStore.Index,
                awaiterStore,
                [awaiterStore, branch, helperStatement, getResult]))
        {
            return null;
        }

        return new AwaiterShape(awaitableStore, awaiterStore, branch, helperStatement, getResult);
    }

    static bool TryGetAwaitable(StoreLocal awaitableStore, StoreLocal awaiterStore, TypeRef awaiterType)
    {
        if (awaiterStore.Value is not Call
            {
                IsVirtual: false,
                Callee:
                {
                    HasThis: true,
                    Name: "GetAwaiter",
                    TypeArguments.IsEmpty: true,
                    ParameterTypes.IsEmpty: true,
                    ReturnType: var returnType,
                    DeclaringType: var declaringType,
                },
                Arguments: [LoadLocalAddress receiver],
            })
        {
            return false;
        }

        return receiver.Index == awaitableStore.Index
            && receiver.Type.Equals(awaitableStore.Type)
            && declaringType.Equals(awaitableStore.Type)
            && returnType.Equals(awaiterType);
    }

    static bool IsCompletedTest(IrExpression condition, int awaiterIndex, TypeRef awaiterType)
        => condition is LoadProperty
        {
            IsVirtual: false,
            Accessor:
            {
                HasThis: true,
                Name: "get_IsCompleted",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                ReturnType: var returnType,
                DeclaringType: var declaringType,
            },
            Instance: LoadLocalAddress receiver,
            IndexArguments.Count: 0,
        }
        && receiver.Index == awaiterIndex
        && receiver.Type.Equals(awaiterType)
        && declaringType.Equals(awaiterType)
        && returnType.Equals(s_bool);

    static bool TryGetResult(Block merge, int awaiterIndex, TypeRef awaiterType, out Call getResult)
    {
        getResult = null!;
        if (merge.Children.Count == 0)
            return false;

        var calls = merge.Children[0].Descendants
            .Prepend(merge.Children[0])
            .OfType<Call>()
            .Where(call => call.Callee.Name == "GetResult")
            .ToList();
        if (calls is not [var candidate]
            || candidate.IsVirtual
            || candidate.Callee is not
            {
                HasThis: true,
                Name: "GetResult",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                DeclaringType: var declaringType,
            }
            || candidate.Arguments is not [LoadLocalAddress receiver]
            || receiver.Index != awaiterIndex
            || !receiver.Type.Equals(awaiterType)
            || !declaringType.Equals(awaiterType))
        {
            return false;
        }

        getResult = candidate;
        return true;
    }

    static bool LocalDefinitionRangeOwned(
        IrFunction function,
        int local,
        StoreLocal definition,
        IReadOnlyCollection<IrNode> allowed)
    {
        var references = function.Descendants
            .Where(node => !ReferenceOwnership.IsInsideNestedFunctionBody(node)
                && ReferenceOwnership.ReferencesLocal(node, local))
            .ToList();
        int definitionIndex = references.FindIndex(node => ReferenceEquals(node, definition));
        if (definitionIndex < 0)
            return false;

        int index = definitionIndex;
        while (index < references.Count && ReferenceOwnership.IsInsideAny(references[index], allowed))
            index++;

        // The compiler may reuse one local for a later await. That starts a new
        // live range with another store; any read/address before that store is
        // an escape from the scaffold this fold would consume.
        return index == references.Count || references[index] is StoreLocal;
    }

    static bool HasExclusiveControlFlow(
        IrFunction function,
        ConditionalBranch ownedBranch,
        int helperOffset,
        int mergeOffset)
    {
        foreach (var node in function.Descendants)
        {
            foreach (int target in Targets(node))
            {
                if (target == helperOffset)
                    return false;
                if (target == mergeOffset && !ReferenceEquals(node, ownedBranch))
                    return false;
            }
        }
        return true;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch @switch => @switch.TargetOffsets,
        Leave leave => [leave.TargetOffset],
        _ => [],
    };

    static void Fold(BlockContainer container, int index, AwaiterShape match, Stepper stepper)
    {
        var blocks = container.Blocks.ToList();
        var awaited = match.AwaitableStore.Value;
        awaited.Detach();

        var awaitExpression = new AwaitExpression(awaited, match.GetResult.Callee.ReturnType);
        awaitExpression.InheritSourceOffset(match.GetResult);
        match.GetResult.ReplaceWith(awaitExpression);

        var foldedHead = new Block(blocks[index].StartOffset);
        foreach (var statement in blocks[index].DetachChildren())
        {
            if (!ReferenceEquals(statement, match.AwaitableStore)
                && !ReferenceEquals(statement, match.AwaiterStore)
                && !ReferenceEquals(statement, match.Branch))
            {
                foldedHead.Add(statement);
            }
        }

        foreach (var block in blocks)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int i = 0; i < index; i++)
            rebuilt.Add(blocks[i]);
        rebuilt.Add(foldedHead);
        for (int i = index + 2; i < blocks.Count; i++)
            rebuilt.Add(blocks[i]);

        stepper.StepOver("recover await from runtime-async awaiter helper scaffold", container);
        container.ReplaceWith(rebuilt);
    }
}
