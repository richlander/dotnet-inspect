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

        bool recovered = false;
        while (FoldOne(function, context.Stepper))
        {
            recovered = true;
        }
        if (recovered)
            function.RequiresAsyncBodyModifier = true;
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
        StoreLocal? AwaitableStore,
        StoreLocal AwaiterStore,
        ConditionalBranch Branch,
        ExpressionStatement HelperStatement,
        Call GetResult,
        IrExpression Awaited);

    static AwaiterShape? TryMatch(IrFunction function, IReadOnlyList<Block> blocks, int index)
    {
        var head = blocks[index];
        var helperBlock = blocks[index + 1];
        var merge = blocks[index + 2];
        if (head.Children.Count < 2
            || head.Children[^2] is not StoreLocal awaiterStore
            || head.Children[^1] is not ConditionalBranch branch
            || branch.TargetOffset != merge.StartOffset
            || helperBlock.Children is not [ExpressionStatement { Expression: Call helperCall } helperStatement]
            || !MemberIdentity.IsAsyncHelpersAwaiter(helperCall, out var awaiterType)
            || helperCall.Arguments is not [LoadLocal helperAwaiter]
            || helperAwaiter.Index != awaiterStore.Index
            || !awaiterStore.Type.Equals(awaiterType)
            || !TryGetAwaitedOperand(
                head,
                awaiterStore,
                awaiterType,
                out var awaitableStore,
                out var awaited)
            || !IsCompletedTest(branch.Condition, awaiterStore.Index, awaiterType)
            || !TryGetResult(merge, awaiterStore.Index, awaiterType, out var getResult)
            || !HasExclusiveControlFlow(function, branch, helperBlock.StartOffset, merge.StartOffset)
            || awaitableStore is not null
                && !LocalDefinitionRangeOwned(
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

        return new AwaiterShape(
            awaitableStore,
            awaiterStore,
            branch,
            helperStatement,
            getResult,
            awaited);
    }

    static bool TryGetAwaitedOperand(
        Block head,
        StoreLocal awaiterStore,
        TypeRef awaiterType,
        out StoreLocal? awaitableStore,
        out IrExpression awaited)
    {
        awaitableStore = null;
        awaited = null!;
        if (awaiterStore.Value is not Call
            {
                Callee:
                {
                    Name: "GetAwaiter",
                    TypeArguments.IsEmpty: true,
                    ReturnType: var returnType,
                },
            } getAwaiter
            || !returnType.Equals(awaiterType)
            || !TryGetAwaitableReceiver(getAwaiter, out var receiver))
        {
            return false;
        }

        if (receiver is LoadLocalAddress local
            && head.Children.Count >= 3
            && head.Children[^3] is StoreLocal store
            && store.Index == local.Index
            && store.Type.Equals(local.Type))
        {
            awaitableStore = store;
            awaited = store.Value;
            return true;
        }

        awaited = receiver switch
        {
            LoadLocalAddress address => new LoadLocal(address.Index, address.Type),
            LoadArgumentAddress address => new LoadArgument(address.Index, address.Name, address.Type),
            _ => receiver,
        };
        return true;
    }

    static bool TryGetAwaitableReceiver(Call getAwaiter, out IrExpression receiver)
    {
        receiver = null!;
        if (getAwaiter.Callee is
            {
                HasThis: true,
                ParameterTypes.IsEmpty: true,
                DeclaringType: var declaringType,
            }
            && getAwaiter.Arguments is [var instance]
            && AwaitableReceiverType(instance) is { } instanceType
            && declaringType.Equals(instanceType))
        {
            receiver = instance;
            return true;
        }

        if (!getAwaiter.IsVirtual
            && getAwaiter.Callee is
            {
                HasThis: false,
                IsExtension: MetadataFactState.Yes,
                ParameterTypes: [var parameterType],
            }
            && getAwaiter.Arguments is [var argument]
            && AwaitableReceiverType(argument) is { } argumentType
            && parameterType.Equals(argumentType))
        {
            receiver = argument;
            return true;
        }

        return false;
    }

    static TypeRef? AwaitableReceiverType(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress address => address.Type,
        LoadArgumentAddress address => address.Type,
        _ => receiver.ResultType,
    };

    static bool IsCompletedTest(IrExpression condition, int awaiterIndex, TypeRef awaiterType)
        => condition is LoadProperty
        {
            Accessor:
            {
                HasThis: true,
                Name: "get_IsCompleted",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                ReturnType: var returnType,
                DeclaringType: var declaringType,
            },
            Instance: var receiver,
            IndexArguments.Count: 0,
        }
        && receiver is not null
        && IsAwaiterReceiver(receiver, awaiterIndex, awaiterType)
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
            || candidate.Callee is not
            {
                HasThis: true,
                Name: "GetResult",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                DeclaringType: var declaringType,
            }
            || candidate.Arguments is not [var receiver]
            || receiver is null
            || !IsAwaiterReceiver(receiver, awaiterIndex, awaiterType)
            || !declaringType.Equals(awaiterType))
        {
            return false;
        }

        getResult = candidate;
        return true;
    }

    static bool IsAwaiterReceiver(IrExpression receiver, int awaiterIndex, TypeRef awaiterType)
        => receiver switch
        {
            LoadLocal local => local.Index == awaiterIndex && local.Type.Equals(awaiterType),
            LoadLocalAddress address => address.Index == awaiterIndex && address.Type.Equals(awaiterType),
            _ => false,
        };

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
        var awaited = match.Awaited;
        if (awaited.Parent is not null)
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
