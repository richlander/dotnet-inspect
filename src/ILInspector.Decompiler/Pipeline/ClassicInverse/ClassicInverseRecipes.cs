using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The closed set of accepted classic-async recipes. Each is an inverse of one
/// named Roslyn lowering shell, and each publishes the claims, protocol
/// declarations, and control regions that
/// <see cref="ClassicInverseAccountant"/> re-checks. A recipe never licenses
/// its own result: matching only produces a candidate.
/// <para>
/// The preconditions below are mutually exclusive by construction (presence of
/// a <c>try/finally</c>, of the compiler's <c>&lt;&gt;7__wrap</c> foreach
/// triple, of a two-armed conditional temporary, and the arity of the
/// builder's completion call). The core still detects a multiple match and
/// declines, so registration order can never decide an outcome.
/// </para>
/// </summary>
internal static class ClassicInverseRecipes
{
    internal static List<ClassicInverseCandidate> Match(
        ClassicInverseRequest request,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        ClassicInverseBudget budget)
    {
        var candidates = new List<ClassicInverseCandidate>();
        IrFunction execution = planning.ExecutionBody;
        foreach (IrNode _ in execution.Body.Descendants.Prepend(execution.Body))
        {
            if (!budget.Charge())
                return candidates;
        }

        Call? setResult = FinalSetResult(execution);
        if (setResult is null)
            return candidates;
        TypeRef builder = ClassicInverseNodeFacts.Definition(
            setResult.Callee.DeclaringType);
        if (builder is not
            {
                Namespace: "System.Runtime.CompilerServices",
                Name: "AsyncTaskMethodBuilder"
                    or "AsyncTaskMethodBuilder`1",
            })
        {
            return candidates;
        }
        List<Call> getResults = GetResultCalls(execution, shell);

        Add(candidates, TryTryFinally(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            getResults,
            budget));
        Add(candidates, TryLoop(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            getResults,
            budget));
        Add(candidates, TryConditional(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            getResults,
            budget));
        Add(candidates, TrySequentialVoid(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            getResults,
            budget));
        Add(candidates, TrySingleAwaitVoid(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            getResults,
            budget));
        Add(candidates, TrySingleAwaitReturn(
            request.ExecutionBody,
            planning,
            shell,
            setResult,
            getResults,
            budget));
        return candidates;
    }

    static void Add(
        List<ClassicInverseCandidate> candidates,
        ClassicInverseCandidate? candidate)
    {
        if (candidate is not null)
            candidates.Add(candidate);
    }

    // ---- Shared shell queries ------------------------------------------

    static Call? FinalSetResult(IrFunction execution)
        => execution.Body.Descendants.OfType<Call>().LastOrDefault(
            static call => call.Callee.Name == "SetResult"
                && ClassicInverseNodeFacts.IsAsyncMethodBuilder(
                    call.Callee.DeclaringType));

    static List<Call> GetResultCalls(
        IrFunction execution,
        ClassicInverseShellFacts shell)
        => [.. execution.Body.Descendants.OfType<Call>().Where(
            call => ClassicInverseRealizationRules.IsAwaiterGetResult(call, shell))];

    static bool HasTryFinally(IrFunction execution)
        => execution.Body.Descendants.OfType<TryFinally>().Any();

    static bool HasForeachHoist(IrFunction execution)
        => execution.Body.Descendants.Any(static node => node switch
        {
            LoadField { Field.Name: var name } =>
                name.StartsWith("<>7__wrap", StringComparison.Ordinal),
            StoreField { Field.Name: var name } =>
                name.StartsWith("<>7__wrap", StringComparison.Ordinal),
            _ => false,
        });

    /// <summary>
    /// The user expression the compiler passed to <c>GetAwaiter</c> for one
    /// <c>GetResult</c> call. The cached-awaiter restore is skipped explicitly.
    /// </summary>
    static IrExpression? AwaitedOperand(IrFunction execution, Call getResult)
    {
        if (getResult.Arguments is not [LoadLocalAddress awaiterAddress])
            return null;

        List<IrNode> nodes = [.. execution.Body.Descendants];
        int position = nodes.IndexOf(getResult);
        if (position < 0)
            return null;

        StoreLocal? bind = null;
        for (int i = 0; i < position; i++)
        {
            if (nodes[i] is StoreLocal { Value: Call { Callee.Name: "GetAwaiter" } call } store
                && store.Index == awaiterAddress.Index
                && call.Arguments.Count == 1)
            {
                bind = store;
            }
        }

        return bind?.Value is Call { Arguments: [IrExpression operand] } ? operand : null;
    }

    static Block? EnclosingBlock(IrNode node)
    {
        IrNode? current = node;
        while (current is not null)
        {
            if (current is Block block)
                return block;
            current = current.Parent;
        }
        return null;
    }

    static string SourceName(string fieldName)
    {
        int close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "value";
    }

    static bool IsTaskLike(TypeRef type)
    {
        TypeRef definition = ClassicInverseNodeFacts.Definition(type);
        return definition is
        {
            Namespace: "System.Threading.Tasks",
            Name: "Task" or "Task`1" or "ValueTask" or "ValueTask`1",
        };
    }

    // ---- Recipe: return await -------------------------------------------

    static ClassicInverseCandidate? TrySingleAwaitReturn(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1
            || HasTryFinally(execution)
            || HasForeachHoist(execution))
        {
            return null;
        }

        StoreLocal? store = execution.Body.Descendants.OfType<StoreLocal>()
            .LastOrDefault(candidate =>
                candidate.Index == result.Index
                && Contains(candidate.Value, getResults[0]));
        if (store is null
            || !ProvesCompletionTransfer(
                rawExecution,
                store,
                setResult,
                budget))
            return null;

        var candidate = new ClassicInverseCandidate("classic-await-return")
        {
            ResultLocal = result.Index,
        };
        var rewriter = new ClassicInverseRewriter(planning, shell, candidate);
        IrNode? value = rewriter.Rewrite(store.Value);
        if (value is not IrExpression expression)
            return null;

        var ret = new Return(expression);
        candidate.Statements.Add(ret);
        candidate.Claim(store, ret, ClassicInverseRealizationRule.ResultStore);
        return candidate;
    }

    // ---- Recipe: await a void-returning operation -----------------------

    static ClassicInverseCandidate? TrySingleAwaitVoid(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments.Count != 1
            || getResults.Count != 1
            || HasTryFinally(execution)
            || HasForeachHoist(execution))
        {
            return null;
        }

        if (getResults[0].Parent is not ExpressionStatement statement
            || !ProvesCompletionTransfer(
                rawExecution,
                statement,
                setResult,
                budget))
            return null;

        var candidate = new ClassicInverseCandidate("classic-await-void");
        var rewriter = new ClassicInverseRewriter(planning, shell, candidate);
        IrNode? rewritten = rewriter.Rewrite(statement);
        if (rewritten is not ExpressionStatement output)
            return null;

        candidate.Statements.Add(output);
        candidate.Statements.Add(new Return(null));
        candidate.Claim(statement, output, ClassicInverseRealizationRule.Statement);
        return candidate;
    }

    // ---- Recipe: two sequential awaits, then one statement ---------------

    static ClassicInverseCandidate? TrySequentialVoid(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments.Count != 1
            || getResults.Count != 2
            || HasTryFinally(execution)
            || HasForeachHoist(execution))
        {
            return null;
        }

        StoreLocal? firstResultStore = execution.Body.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store => Contains(store.Value, getResults[0]));
        if (firstResultStore is null)
            return null;

        StoreField? hoist = execution.Body.Descendants.OfType<StoreField>()
            .FirstOrDefault(store =>
                ClassicInverseNodeFacts.IsHoistedLocalField(store.Field.Name)
                && ClassicInverseNodeFacts.IsMachineField(store.Field, shell.Machine)
                && store.Instance is LoadArgument { Index: 0 }
                && store.Value is LoadLocal local
                && local.Index == firstResultStore.Index);
        if (hoist is null || hoist.Field.Type is not { } firstType)
            return null;

        StoreLocal? secondStore = execution.Body.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store => Contains(store.Value, getResults[1]));
        if (secondStore is null)
            return null;

        ExpressionStatement? tail = execution.Body.Descendants
            .OfType<ExpressionStatement>()
            .FirstOrDefault(static statement =>
                statement.Expression is Call { Callee.Name: "KeepAlive" });
        if (tail is null
            || !ProvesCompletionTransfer(
                rawExecution,
                tail,
                setResult,
                budget))
            return null;

        var candidate = new ClassicInverseCandidate("classic-sequential-await-void");
        var locals = new ClassicInverseLocalTable();
        int firstIndex = locals.Add(firstType, SourceName(hoist.Field.Name));
        string? secondName =
            secondStore.Index >= 0 && secondStore.Index < execution.LocalNames.Length
                ? execution.LocalNames[secondStore.Index]
                : null;
        int secondIndex = locals.Add(secondStore.Type, secondName);

        candidate.Locals = locals.Types;
        candidate.LocalNames = locals.Names;
        candidate.MapHoistedLocal(hoist.Field.Name, firstIndex, firstType);
        candidate.MapLocal(firstResultStore.Index, firstIndex);
        candidate.MapLocal(secondStore.Index, secondIndex);

        var rewriter = new ClassicInverseRewriter(planning, shell, candidate);

        IrNode? firstValue = rewriter.Rewrite(firstResultStore.Value);
        IrNode? secondValue = rewriter.Rewrite(secondStore.Value);
        IrNode? tailStatement = rewriter.Rewrite(tail);
        if (firstValue is not IrExpression first
            || secondValue is not IrExpression second
            || tailStatement is not ExpressionStatement tailOutput)
        {
            return null;
        }

        var firstOutput = new StoreLocal(firstIndex, firstType, first);
        var secondOutput = new StoreLocal(secondIndex, secondStore.Type, second);
        candidate.Statements.Add(firstOutput);
        candidate.Statements.Add(secondOutput);
        candidate.Statements.Add(tailOutput);
        candidate.Statements.Add(new Return(null));

        candidate.Claim(
            firstResultStore,
            firstOutput,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            secondStore,
            secondOutput,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(tail, tailOutput, ClassicInverseRealizationRule.Statement);

        // The compiler hoists the first user local into the state machine so it
        // survives the suspension point. The hoist writes the same value into
        // the same variable the output local already denotes.
        candidate.DeclareProtocol(hoist, "hoisted-local-transfer");
        return candidate;
    }

    // ---- Recipe: conditional await --------------------------------------

    static ClassicInverseCandidate? TryConditional(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1
            || HasTryFinally(execution)
            || HasForeachHoist(execution))
        {
            return null;
        }

        List<StoreLocal> temporaries = [.. execution.Body.Descendants
            .OfType<StoreLocal>()
            .Where(store => store.Index != result.Index)];
        if (temporaries.Where(store => Contains(store.Value, getResults[0])).ToList()
            is not [StoreLocal awaitStore])
        {
            return null;
        }
        if (temporaries.Where(store =>
                store.Index == awaitStore.Index
                && store.Value is Constant { Value: 0 }).ToList()
            is not [StoreLocal zeroStore])
        {
            return null;
        }
        if (zeroStore.Parent is not Block zeroBlock)
            return null;

        ConditionalBranch? zeroBranch = execution.Body.Descendants
            .OfType<ConditionalBranch>()
            .FirstOrDefault(branch =>
                branch.TargetOffset == zeroBlock.StartOffset
                && branch.Condition is LogicalNot);
        if (zeroBranch is null)
            return null;

        List<StoreLocal> finalStores = [.. execution.Body.Descendants
            .OfType<StoreLocal>()
            .Where(store =>
                store.Index == result.Index
                && store.Value is LoadLocal load
                && load.Index == awaitStore.Index)];
        if (finalStores is not [StoreLocal finalStore])
            return null;

        // The compiler's join branch after the awaited arm.
        if (finalStore.Parent is not Block finalBlock)
            return null;
        List<Branch> joins = [.. execution.Body.Descendants.OfType<Branch>()];
        List<Branch> conditionalJoins = [.. joins.Where(
            branch => branch.TargetOffset == finalBlock.StartOffset)];
        if (conditionalJoins is not [Branch conditionalJoin]
            || zeroBranch.Parent is not Block conditionBlock
            || awaitStore.Parent is not Block awaitContinuation
            || OperandBlock(planning, shell, getResults[0])
                is not Block awaitEntry
            || !TryGetConditionalControlIdentity(
                conditionBlock,
                zeroBranch,
                awaitEntry,
                awaitContinuation,
                zeroBlock,
                finalBlock,
                conditionalJoin,
                budget,
                out string planningControl)
            || !TryGetRawConditionalControlIdentity(
                rawExecution,
                zeroBranch.SourceOffset,
                awaitEntry.StartOffset,
                awaitContinuation.StartOffset,
                zeroBlock.StartOffset,
                finalBlock.StartOffset,
                conditionalJoin.SourceOffset,
                budget,
                out string rawControl)
            || planningControl != rawControl
            || !ProvesCompletionTransfer(
                rawExecution,
                finalStore,
                setResult,
                budget))
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-await-conditional")
        {
            ResultLocal = result.Index,
        };
        var rewriter = new ClassicInverseRewriter(planning, shell, candidate);
        rewriter.AttributeAwaitTo(getResults[0], awaitStore);

        IrNode? condition =
            rewriter.Rewrite(((LogicalNot)zeroBranch.Condition).Operand);
        IrNode? whenTrue = rewriter.Rewrite(awaitStore.Value);
        IrNode? whenFalse = rewriter.Rewrite(zeroStore.Value);
        if (condition is not IrExpression conditionOutput
            || whenTrue is not IrExpression thenOutput
            || whenFalse is not IrExpression elseOutput)
        {
            return null;
        }

        var conditional = new Conditional(conditionOutput, thenOutput, elseOutput);
        var ret = new Return(conditional);
        candidate.Statements.Add(ret);

        candidate.MapLocalValue(awaitStore.Index, conditional);
        candidate.Claim(
            zeroBranch,
            conditional,
            ClassicInverseRealizationRule.ControlCondition);
        candidate.Claim(
            zeroStore,
            elseOutput,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            finalStore,
            ret,
            ClassicInverseRealizationRule.ResultStore);

        candidate.DeclareProtocol(conditionalJoin, "conditional-join");

        Block? thenBlock = EnclosingBlock(awaitStore);
        Block? operandBlock = EnclosingBlock(getResults[0]) is { } resultBlock
            ? OperandBlock(planning, shell, getResults[0])
            : null;
        var thenRoots = new List<IrNode>();
        if (thenBlock is not null)
            thenRoots.Add(thenBlock);
        if (operandBlock is not null && !ReferenceEquals(operandBlock, thenBlock))
            thenRoots.Add(operandBlock);
        if (thenRoots.Count > 0)
            candidate.DeclareControlRegion("conditional-then", thenRoots, thenOutput);
        candidate.DeclareControlRegion("conditional-else", [zeroBlock], elseOutput);

        return candidate;
    }

    static bool TryGetConditionalControlIdentity(
        Block condition,
        ConditionalBranch test,
        Block awaitEntry,
        Block awaitContinuation,
        Block whenFalse,
        Block merge,
        Branch join,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        if (condition.Parent is not BlockContainer container
            || !ReferenceEquals(awaitEntry.Parent, container)
            || !ReferenceEquals(awaitContinuation.Parent, container)
            || !ReferenceEquals(whenFalse.Parent, container)
            || !ReferenceEquals(merge.Parent, container)
            || !ReferenceEquals(test.Parent, condition)
            || !ReferenceEquals(condition.Children.LastOrDefault(), test)
            || !ReferenceEquals(join.Parent, awaitContinuation)
            || !ReferenceEquals(
                awaitContinuation.Children.LastOrDefault(),
                join))
        {
            return false;
        }

        IReadOnlyList<Block> blocks = container.Blocks;
        int conditionIndex = BlockIndex(blocks, condition, budget);
        int awaitEntryIndex = BlockIndex(blocks, awaitEntry, budget);
        int awaitContinuationIndex = BlockIndex(
            blocks,
            awaitContinuation,
            budget);
        int falseIndex = BlockIndex(blocks, whenFalse, budget);
        int mergeIndex = BlockIndex(blocks, merge, budget);
        if (conditionIndex < 0
            || awaitEntryIndex < 0
            || awaitContinuationIndex < 0
            || falseIndex < 0
            || mergeIndex < 0
            || test.TargetOffset != whenFalse.StartOffset
            || join.TargetOffset != merge.StartOffset
            || !ClassicInverseCfg.TryBuild(blocks, budget, out var edges)
            || !HasOnlySuccessors(
                edges[conditionIndex],
                budget,
                awaitEntryIndex,
                falseIndex)
            || !HasOnlySuccessors(
                edges[awaitContinuationIndex],
                budget,
                mergeIndex)
            || !HasOnlySuccessors(
                edges[falseIndex],
                budget,
                mergeIndex)
            || !HasNoSuccessors(edges[mergeIndex])
            || !HasOnlyPredecessors(
                edges,
                awaitEntryIndex,
                budget,
                conditionIndex)
            || !HasOnlyPredecessors(
                edges,
                falseIndex,
                budget,
                conditionIndex)
            || !HasOnlyPredecessors(
                edges,
                mergeIndex,
                budget,
                awaitContinuationIndex,
                falseIndex))
        {
            return false;
        }

        identity =
            $"condition:{condition.StartOffset}/{test.SourceOffset}"
            + $"->{awaitEntry.StartOffset}|{whenFalse.StartOffset};"
            + $"await:{awaitContinuation.StartOffset}/{join.SourceOffset}"
            + $"->{merge.StartOffset};"
            + $"false:{whenFalse.StartOffset}->{merge.StartOffset};"
            + $"merge:{merge.StartOffset}";
        return true;
    }

    static bool TryGetRawConditionalControlIdentity(
        IrFunction raw,
        int testSourceOffset,
        int awaitEntryOffset,
        int awaitContinuationOffset,
        int falseOffset,
        int mergeOffset,
        int joinSourceOffset,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        var tests = new List<ConditionalBranch>();
        var joins = new List<Branch>();
        var awaitEntries = new List<Block>();
        var awaitContinuations = new List<Block>();
        var falseBlocks = new List<Block>();
        var mergeBlocks = new List<Block>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge())
                return false;
            switch (node)
            {
                case ConditionalBranch candidateTest
                    when candidateTest.SourceOffset == testSourceOffset:
                    tests.Add(candidateTest);
                    break;
                case Branch candidateJoin
                    when candidateJoin.SourceOffset == joinSourceOffset:
                    joins.Add(candidateJoin);
                    break;
                case Block block when block.StartOffset == awaitEntryOffset:
                    awaitEntries.Add(block);
                    break;
                case Block block
                    when block.StartOffset == awaitContinuationOffset:
                    awaitContinuations.Add(block);
                    break;
                case Block block when block.StartOffset == falseOffset:
                    falseBlocks.Add(block);
                    break;
                case Block block when block.StartOffset == mergeOffset:
                    mergeBlocks.Add(block);
                    break;
            }
        }

        if (tests is not [ConditionalBranch test]
            || joins is not [Branch join]
            || awaitEntries is not [Block awaitEntry]
            || awaitContinuations is not [Block awaitContinuation]
            || falseBlocks is not [Block whenFalse]
            || mergeBlocks is not [Block merge]
            || test.Parent is not Block condition)
        {
            return false;
        }

        return TryGetConditionalControlIdentity(
            condition,
            test,
            awaitEntry,
            awaitContinuation,
            whenFalse,
            merge,
            join,
            budget,
            out identity);
    }

    static int BlockIndex(
        IReadOnlyList<Block> blocks,
        Block target,
        ClassicInverseBudget budget)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!budget.Charge())
                return -1;
            if (ReferenceEquals(blocks[i], target))
                return i;
        }
        return -1;
    }

    static bool HasOnlySuccessors(
        ILInspector.ControlFlow.BlockEdges block,
        ClassicInverseBudget budget,
        params int[] expected)
    {
        if (block.ExitsMethod
            || block.LeavesRegion
            || block.ExternalTargets.Count != 0
            || block.Successors.Count != expected.Length)
        {
            return false;
        }
        foreach (int successor in block.Successors)
        {
            if (!budget.Charge() || !expected.Contains(successor))
                return false;
        }
        return true;
    }

    static bool HasNoSuccessors(
        ILInspector.ControlFlow.BlockEdges block)
        => block.ExternalTargets.Count == 0
            && block.Successors.Count == 0;

    static bool HasOnlyPredecessors(
        IReadOnlyList<ILInspector.ControlFlow.BlockEdges> edges,
        int block,
        ClassicInverseBudget budget,
        params int[] expected)
    {
        var actual = new List<int>();
        for (int source = 0; source < edges.Count; source++)
        {
            if (!budget.Charge())
                return false;
            foreach (int successor in edges[source].Successors)
            {
                if (!budget.Charge())
                    return false;
                if (successor == block)
                    actual.Add(source);
            }
        }
        return actual.Count == expected.Length
            && actual.All(expected.Contains);
    }

    static bool ProvesCompletionTransfer(
        IrFunction raw,
        IrNode planningEndpoint,
        Call planningSetResult,
        ClassicInverseBudget budget)
    {
        if (planningEndpoint.Parent is not Block planningBlock
            || planningBlock.Parent is not BlockContainer planningContainer
            || !ClassicInverseCfg.TryBuild(
                planningContainer.Blocks,
                budget,
                out var planningEdges))
        {
            return false;
        }

        int planningIndex = BlockIndex(
            planningContainer.Blocks,
            planningBlock,
            budget);
        if (planningIndex < 0
            || !HasNoSuccessors(planningEdges[planningIndex]))
        {
            return false;
        }

        var rawEndpoints = new List<Block>();
        var rawSetResults = new List<Call>();
        var rawLeaves = new List<Leave>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge())
                return false;
            switch (node)
            {
                case Block block
                    when block.StartOffset == planningBlock.StartOffset:
                    rawEndpoints.Add(block);
                    break;
                case Call call
                    when call.SourceOffset == planningSetResult.SourceOffset
                        && call.Callee.Name == "SetResult":
                    rawSetResults.Add(call);
                    break;
                case Leave leave:
                    rawLeaves.Add(leave);
                    break;
            }
        }

        return rawEndpoints is [Block rawEndpoint]
            && rawSetResults is [Call { Parent.Parent: Block setResultBlock }]
            && rawEndpoint.Children.LastOrDefault() is Leave success
            && success.TargetOffset == setResultBlock.StartOffset
            && rawLeaves.Count(
                leave => leave.TargetOffset == setResultBlock.StartOffset) == 1;
    }

    static Block? OperandBlock(
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call getResult)
        => AwaitedOperand(planning.ExecutionBody, getResult) is { } operand
            ? EnclosingBlock(operand)
            : null;

    // ---- Recipe: await inside a foreach over an array --------------------

    static ClassicInverseCandidate? TryLoop(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal finalResult]
            || getResults is not [Call getResult]
            || HasTryFinally(execution))
        {
            return null;
        }

        List<StoreField> collectionHoists = [.. execution.Body.Descendants
            .OfType<StoreField>()
            .Where(store =>
                store.Field.Name == "<>7__wrap1"
                && store.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(store.Field, shell.Machine)
                && store.Value is LoadField collection
                && collection.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(
                    collection.Field,
                    shell.Machine)
                && collection.Field.Type is
                    { Kind: TypeRefKind.SzArray, ElementType: { } element }
                && IsTaskLike(element))];
        if (collectionHoists is not
            [StoreField { Value: LoadField collectionField } collectionHoist]
            || collectionField.Field.Type.ElementType is not { } taskType)
        {
            return null;
        }
        FieldRef hoistedCollection = collectionHoist.Field;

        // The loop index is whatever machine field the bound test compares
        // against this exact hoisted array's length — never a field recognized
        // by the compiler's '<>7__wrap' name family.
        List<ConditionalBranch> boundTests = [.. execution.Body.Descendants
            .OfType<ConditionalBranch>()
            .Where(branch => branch.Condition is Comparison
            {
                Kind: ComparisonKind.LessThan,
                Left: LoadField { Instance: LoadArgument { Index: 0 } } index,
                Right: ArrayLength
                {
                    Array: LoadField { Instance: LoadArgument { Index: 0 } } array,
                },
            }
                && array.Field == hoistedCollection
                && index.Field != hoistedCollection
                && ClassicInverseNodeFacts.IsMachineField(
                    index.Field,
                    shell.Machine))];
        if (boundTests is not
            [ConditionalBranch
            {
                Parent: Block boundTestBlock,
                Condition: Comparison { Left: LoadField boundIndex },
            } boundTest])
        {
            return null;
        }
        FieldRef loopIndex = boundIndex.Field;

        if (execution.Body.Descendants.OfType<StoreLocal>()
                .Where(store => Contains(store.Value, getResult)).ToList()
            is not [StoreLocal resultStore])
        {
            return null;
        }
        List<StoreLocal> accumulatorStores = [.. execution.Body.Descendants
            .OfType<StoreLocal>()
            .Where(store => store.Value is Binary { Kind: BinaryKind.Add } add
                && IsAccumulatorRead(add.Left, shell, hoistedCollection, loopIndex)
                && add.Right is LoadLocal read
                && read.Index == resultStore.Index)];
        if (accumulatorStores is not
            [StoreLocal
            {
                Value: Binary { Left: LoadField accumulatorRead },
            } accumulatorStore])
        {
            return null;
        }
        FieldRef loopAccumulator = accumulatorRead.Field;
        var storage = new ClassicInverseLoopStorage(
            hoistedCollection,
            loopIndex,
            loopAccumulator);
        List<StoreField> accumulatorHoists = [.. execution.Body.Descendants
            .OfType<StoreField>()
            .Where(store => store.Field == loopAccumulator
                && store.Instance is LoadArgument { Index: 0 }
                && store.Value is LoadLocal load
                && load.Index == accumulatorStore.Index)];
        List<StoreField> collectionReleases = [.. execution.Body.Descendants
            .OfType<StoreField>()
            .Where(store => store.Field == hoistedCollection
                && store.Instance is LoadArgument { Index: 0 }
                && store.Value is Constant { Value: null })];
        int collectionWriteCount = execution.Body.Descendants.Count(
            node => IsMachineFieldWrite(
                node,
                hoistedCollection,
                shell.Machine));
        int accumulatorWriteCount = execution.Body.Descendants.Count(
            node => IsMachineFieldWrite(
                node,
                loopAccumulator,
                shell.Machine));
        if (accumulatorHoists is not [StoreField accumulatorHoist]
            || collectionReleases is not [StoreField collectionRelease]
            || collectionWriteCount != 2
            || accumulatorWriteCount != 1)
        {
            return null;
        }

        IrExpression? operand = AwaitedOperand(execution, getResult);
        List<LoadStackSlot> spilledElements = operand is null
            ? []
            : [.. operand.Descendants.Prepend(operand).OfType<LoadStackSlot>()];
        if (spilledElements is not [LoadStackSlot spilledElement])
            return null;
        if (execution.Body.Descendants.OfType<StoreStackSlot>()
                .Where(store => store.Slot == spilledElement.Slot).ToList()
            is not [StoreStackSlot elementSpill])
        {
            return null;
        }
        if (!storage.IsElementLoad(elementSpill.Value, shell.Machine))
            return null;

        StoreLocal? seed = execution.Body.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store =>
                store.Index == accumulatorStore.Index
                && store.Value is Constant { Value: 0 });
        StoreLocal? finalStore = execution.Body.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store =>
                store.Index == finalResult.Index
                && store.Value is LoadLocal load
                && load.Index == accumulatorStore.Index);
        if (seed is null || finalStore is null)
            return null;

        List<StoreField> advances = [.. execution.Body.Descendants
            .OfType<StoreField>()
            .Where(advance => advance is
            {
                Value: Binary
                {
                    Kind: BinaryKind.Add,
                    IsChecked: false,
                    IsUnsigned: false,
                    Left: LoadField advanceRead,
                    Right: Constant { Value: 1 },
                },
                Instance: LoadArgument { Index: 0 },
            }
                && advance.Field == loopIndex
                && advanceRead.Field == loopIndex
                && advanceRead.Instance is LoadArgument { Index: 0 })];
        List<IrNode> indexWrites = [.. execution.Body.Descendants
            .Where(node => IsMachineFieldWrite(
                node,
                loopIndex,
                shell.Machine))];
        List<StoreField> indexInitializers = [.. indexWrites
            .OfType<StoreField>()
            .Where(static store => store.Value is Constant { Value: 0 })];
        if (advances is not [StoreField advance]
            || indexInitializers is not [StoreField indexInitializer]
            || indexWrites.Count != 2
            || EnclosingBlock(elementSpill) is not { } bodyBlock
            || EnclosingBlock(advance) is not { } advanceBlock)
        {
            return null;
        }

        List<Branch> entries = [.. execution.Body.Descendants
            .OfType<Branch>()
            .Where(branch =>
                branch.TargetOffset == boundTestBlock.StartOffset)];
        if (entries is not [Branch entry]
            || entry.Parent is not Block entryBlock
            || !ReferenceEquals(indexInitializer.Parent, entryBlock)
            || indexInitializer.ChildIndex >= entry.ChildIndex
            || !TryGetForeachControlIdentity(
                boundTestBlock,
                boundTest,
                entryBlock,
                bodyBlock,
                advanceBlock,
                indexInitializer,
                collectionHoist,
                elementSpill,
                accumulatorHoist,
                resultStore,
                accumulatorStore,
                collectionRelease,
                finalStore,
                budget,
                out string planningControl)
            || !TryGetRawForeachControlIdentity(
                rawExecution,
                shell.Machine,
                storage,
                boundTest.SourceOffset,
                entry.SourceOffset,
                elementSpill.SourceOffset,
                advance.SourceOffset,
                indexInitializer.SourceOffset,
                collectionHoist.SourceOffset,
                accumulatorHoist.SourceOffset,
                resultStore.SourceOffset,
                accumulatorStore.SourceOffset,
                collectionRelease.SourceOffset,
                finalStore.SourceOffset,
                collectionField.Field,
                accumulatorStore.Index,
                finalResult.Index,
                budget,
                out string rawControl)
            || planningControl != rawControl)
        {
            return null;
        }
        if (!ProvesCompletionTransfer(
                rawExecution,
                finalStore,
                setResult,
                budget))
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-await-foreach-array")
        {
            ResultLocal = finalResult.Index,
            LoopStorage = storage,
        };
        var locals = new ClassicInverseLocalTable();
        TypeRef sumType = accumulatorStore.Type;
        int sumIndex = locals.Add(sumType, "sum");
        int taskIndex = locals.Add(taskType, "task");

        candidate.Locals = locals.Types;
        candidate.LocalNames = locals.Names;
        candidate.MapHoistedLocal(loopAccumulator.Name, sumIndex, sumType);
        candidate.MapLocal(accumulatorStore.Index, sumIndex);
        candidate.MapLocal(finalResult.Index, sumIndex);

        var rewriter = new ClassicInverseRewriter(planning, shell, candidate);
        IrNode? collectionOutput = rewriter.Rewrite(collectionField);
        if (collectionOutput is not IrExpression collectionExpression)
            return null;

        var awaited = new AwaitExpression(
            new LoadLocal(taskIndex, taskType),
            getResult.Callee.ReturnType,
            getResult.Callee.ReturnIsDynamic);
        var accumulate = new StoreLocal(
            sumIndex,
            sumType,
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new LoadLocal(sumIndex, sumType),
                awaited));
        var body = new Block(0);
        body.Add(accumulate);
        var loop = new ForeachStatement(
            taskIndex,
            taskType,
            collectionExpression,
            body);

        var seedOutput = new StoreLocal(sumIndex, sumType, new Constant(0, sumType));
        var ret = new Return(new LoadLocal(sumIndex, sumType));
        candidate.Statements.Add(seedOutput);
        candidate.Statements.Add(loop);
        candidate.Statements.Add(ret);

        candidate.MapLocalValue(resultStore.Index, awaited);
        candidate.Claim(seed, seedOutput, ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            collectionField,
            collectionExpression,
            ClassicInverseRealizationRule.LoopCollection);
        candidate.Claim(elementSpill, body, ClassicInverseRealizationRule.LoopElement);
        candidate.Claim(
            spilledElement,
            awaited.Operand,
            ClassicInverseRealizationRule.LoopElement);
        candidate.Claim(
            resultStore,
            awaited,
            ClassicInverseRealizationRule.AwaitResult);
        candidate.Claim(
            accumulatorStore,
            accumulate,
            ClassicInverseRealizationRule.LoopAccumulator);
        candidate.Claim(finalStore, ret, ClassicInverseRealizationRule.ResultStore);

        candidate.DeclareContainer(
            collectionHoist,
            ClassicInverseAncestorKind.Protocol,
            "foreach-collection-hoist",
            outputContext: null);

        candidate.DeclareProtocol(
            collectionRelease,
            "foreach-collection-release");
        candidate.DeclareProtocol(
            accumulatorHoist,
            "hoisted-local-transfer");
        candidate.DeclareProtocol(indexInitializer, "foreach-index-init");
        candidate.DeclareProtocol(advance, "foreach-index-advance");
        candidate.DeclareProtocol(boundTest, "foreach-bound-test");
        candidate.DeclareProtocol(entry, "foreach-entry");

        var loopRoots = new List<IrNode>();
        if (EnclosingBlock(elementSpill) is { } spillBlock)
            loopRoots.Add(spillBlock);
        if (EnclosingBlock(resultStore) is { } resultBlock
            && !loopRoots.Contains(resultBlock))
        {
            loopRoots.Add(resultBlock);
        }
        candidate.DeclareControlRegion("foreach-body", loopRoots, body);
        return candidate;
    }

    /// <summary>
    /// Proves the exact loop skeleton represented by the source-level
    /// <c>foreach</c>: the entry and advance reach the bound test, whose taken
    /// edge enters the body and whose fall-through exits the loop. Predecessor
    /// closure prevents another edge from silently entering either arm.
    /// </summary>
    static bool TryGetForeachControlIdentity(
        Block bound,
        ConditionalBranch test,
        Block entry,
        Block body,
        Block advance,
        StoreField indexInitializer,
        StoreField collectionHoist,
        StoreStackSlot elementSpill,
        StoreField accumulatorHoist,
        StoreLocal awaitedResultStore,
        StoreLocal accumulatorStore,
        StoreField collectionRelease,
        StoreLocal finalStore,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        if (bound.Parent is not BlockContainer container
            || !ReferenceEquals(entry.Parent, container)
            || !ReferenceEquals(body.Parent, container)
            || !ReferenceEquals(advance.Parent, container)
            || !ReferenceEquals(indexInitializer.Parent, entry)
            || !ReferenceEquals(collectionHoist.Parent, entry)
            || !ReferenceEquals(elementSpill.Parent, body)
            || !ReferenceEquals(accumulatorHoist.Parent, body)
            || !ReferenceEquals(awaitedResultStore.Parent, advance)
            || !ReferenceEquals(accumulatorStore.Parent, advance)
            || finalStore.Parent is not Block finalBlock
            || !ReferenceEquals(finalBlock.Parent, container)
            || indexInitializer.ChildIndex < 0
            || indexInitializer.ChildIndex >= entry.Children.Count - 1
            || collectionHoist.ChildIndex < 0
            || collectionHoist.ChildIndex >= indexInitializer.ChildIndex
            || elementSpill.ChildIndex < 0
            || accumulatorHoist.ChildIndex <= elementSpill.ChildIndex
            || accumulatorHoist.ChildIndex >= body.Children.Count - 1
            || awaitedResultStore.ChildIndex < 0
            || accumulatorStore.ChildIndex <= awaitedResultStore.ChildIndex
            || accumulatorStore.ChildIndex >= advance.Children.Count - 1
            || bound.Children is not [.., var terminator]
            || !ReferenceEquals(terminator, test))
        {
            return false;
        }

        IReadOnlyList<Block> blocks = container.Blocks;
        int boundIndex = BlockIndex(blocks, bound, budget);
        int entryIndex = BlockIndex(blocks, entry, budget);
        int bodyIndex = BlockIndex(blocks, body, budget);
        int advanceIndex = BlockIndex(blocks, advance, budget);
        int finalIndex = BlockIndex(blocks, finalBlock, budget);
        if (boundIndex < 0
            || entryIndex < 0
            || bodyIndex < 0
            || advanceIndex < 0
            || finalIndex < 0)
        {
            return false;
        }

        if (!ClassicInverseCfg.TryBuild(blocks, budget, out var edges))
            return false;
        if (!HasOnlySuccessors(edges[entryIndex], budget, boundIndex)
            || !HasOnlySuccessors(edges[advanceIndex], budget, boundIndex)
            || edges[boundIndex].Successors.Count != 2
            || !edges[boundIndex].Successors.Contains(bodyIndex))
        {
            return false;
        }

        int exitIndex = edges[boundIndex].Successors.SingleOrDefault(
            successor => successor != bodyIndex,
            -1);
        if (exitIndex < 0
            || test.TargetOffset != body.StartOffset
            || !ReferenceEquals(
                collectionRelease.Parent,
                blocks[exitIndex])
            || collectionRelease.ChildIndex < 0
            || finalStore.ChildIndex < 0
            || (exitIndex == finalIndex
                && collectionRelease.ChildIndex >= finalStore.ChildIndex)
            || !AllPathsReachTarget(
                edges,
                exitIndex,
                finalIndex,
                budget)
            || !HasOnlyPredecessors(
                edges,
                bodyIndex,
                budget,
                boundIndex)
            || !HasOnlyPredecessors(
                edges,
                exitIndex,
                budget,
                boundIndex)
            || !HasOnlyPredecessors(
                edges,
                boundIndex,
                budget,
                entryIndex,
                advanceIndex))
        {
            return false;
        }

        identity =
            $"collection:{collectionHoist.SourceOffset}"
            + $"@{entry.StartOffset}:{collectionHoist.ChildIndex};"
            + $"init:{indexInitializer.SourceOffset}@{entry.StartOffset}"
            + $":{indexInitializer.ChildIndex};"
            + $"entry:{entry.StartOffset}->{bound.StartOffset};"
            + $"element:{elementSpill.SourceOffset}@{body.StartOffset}"
            + $":{elementSpill.ChildIndex};"
            + $"accumulator:{accumulatorHoist.SourceOffset}@{body.StartOffset}"
            + $":{accumulatorHoist.ChildIndex};"
            + $"result:{awaitedResultStore.SourceOffset}@{advance.StartOffset}"
            + $":{awaitedResultStore.ChildIndex};"
            + $"accumulate:{accumulatorStore.SourceOffset}"
            + $"@{advance.StartOffset}:{accumulatorStore.ChildIndex};"
            + $"advance:{advance.StartOffset}->{bound.StartOffset};"
            + $"bound:{bound.StartOffset}/{test.SourceOffset}"
            + $"->{body.StartOffset}|{blocks[exitIndex].StartOffset};"
            + $"body:{body.StartOffset};"
            + $"release:{collectionRelease.SourceOffset}"
            + $"@{blocks[exitIndex].StartOffset}:{collectionRelease.ChildIndex};"
            + $"final:{finalStore.SourceOffset}"
            + $"@{finalBlock.StartOffset}:{finalStore.ChildIndex};"
            + $"exit:{blocks[exitIndex].StartOffset}";
        return true;
    }

    static bool AllPathsReachTarget(
        IReadOnlyList<ILInspector.ControlFlow.BlockEdges> edges,
        int start,
        int target,
        ClassicInverseBudget budget)
    {
        var state = new byte[edges.Count];
        var stack = new Stack<(int Block, int NextSuccessor)>();
        stack.Push((start, 0));
        while (stack.Count > 0)
        {
            var (block, nextSuccessor) = stack.Pop();
            if (block == target)
                continue;

            ILInspector.ControlFlow.BlockEdges edge = edges[block];
            if (nextSuccessor == 0)
            {
                if (!budget.Charge()
                    || state[block] == 1
                    || edge.ExitsMethod
                    || edge.LeavesRegion
                    || edge.ExternalTargets.Count != 0
                    || edge.Successors.Count == 0)
                {
                    return false;
                }
                if (state[block] == 2)
                    continue;
                state[block] = 1;
            }

            if (nextSuccessor < edge.Successors.Count)
            {
                if (!budget.Charge())
                    return false;
                stack.Push((block, nextSuccessor + 1));
                int successor = edge.Successors[nextSuccessor];
                if (successor != target)
                {
                    if (state[successor] == 1)
                        return false;
                    if (state[successor] == 0)
                        stack.Push((successor, 0));
                }
                continue;
            }

            state[block] = 2;
        }
        return true;
    }

    static bool TryGetRawForeachControlIdentity(
        IrFunction raw,
        TypeRef machine,
        ClassicInverseLoopStorage storage,
        int boundSourceOffset,
        int entrySourceOffset,
        int bodySourceOffset,
        int advanceSourceOffset,
        int initializerSourceOffset,
        int collectionHoistSourceOffset,
        int accumulatorHoistSourceOffset,
        int awaitedResultStoreSourceOffset,
        int accumulatorStoreSourceOffset,
        int collectionReleaseSourceOffset,
        int finalStoreSourceOffset,
        FieldRef sourceCollection,
        int accumulatorLocal,
        int finalResultLocal,
        ClassicInverseBudget budget,
        out string identity)
    {
        identity = "";
        if (boundSourceOffset < 0
            || entrySourceOffset < 0
            || bodySourceOffset < 0
            || advanceSourceOffset < 0
            || initializerSourceOffset < 0
            || collectionHoistSourceOffset < 0
            || accumulatorHoistSourceOffset < 0
            || awaitedResultStoreSourceOffset < 0
            || accumulatorStoreSourceOffset < 0
            || collectionReleaseSourceOffset < 0
            || finalStoreSourceOffset < 0)
        {
            return false;
        }

        var bounds = new List<ConditionalBranch>();
        var entries = new List<Branch>();
        var bodyAnchors = new List<StoreStackSlot>();
        var advances = new List<StoreField>();
        var initializers = new List<StoreField>();
        var indexWrites = new List<IrNode>();
        var collectionHoists = new List<StoreField>();
        var accumulatorHoists = new List<StoreField>();
        var collectionReleases = new List<StoreField>();
        var finalStores = new List<StoreLocal>();
        var awaitedResultStores = new List<StoreLocal>();
        var accumulatorStores = new List<StoreLocal>();
        var collectionWrites = new List<IrNode>();
        var accumulatorWrites = new List<IrNode>();
        foreach (IrNode node in raw.Body.Descendants)
        {
            if (!budget.Charge())
                return false;
            switch (node)
            {
                case ConditionalBranch branch
                    when branch.SourceOffset == boundSourceOffset
                        && IsExactLoopBound(branch.Condition, storage, machine):
                    bounds.Add(branch);
                    break;
                case Branch branch
                    when branch.SourceOffset == entrySourceOffset:
                    entries.Add(branch);
                    break;
                case StoreStackSlot store
                    when store.SourceOffset == bodySourceOffset
                        && storage.IsElementLoad(store.Value, machine):
                    bodyAnchors.Add(store);
                    break;
                case StoreField
                {
                    Field: var advanceField,
                    Instance: LoadArgument { Index: 0 },
                    Value: Binary
                    {
                        Kind: BinaryKind.Add,
                        IsChecked: false,
                        IsUnsigned: false,
                        Left: LoadField
                        {
                            Field: var advanceRead,
                            Instance: LoadArgument { Index: 0 },
                        },
                        Right: Constant { Value: 1 },
                    },
                } store
                    when store.SourceOffset == advanceSourceOffset
                        && advanceField == storage.Index
                        && advanceRead == storage.Index
                        && ClassicInverseNodeFacts.IsMachineField(
                            advanceField,
                            machine):
                    advances.Add(store);
                    break;
                case StoreField
                {
                    Field: var hoistedCollection,
                    Instance: LoadArgument { Index: 0 },
                    Value: LoadField
                    {
                        Field: var source,
                        Instance: LoadArgument { Index: 0 },
                    },
                } store
                    when store.SourceOffset == collectionHoistSourceOffset
                        && hoistedCollection == storage.Collection
                        && source == sourceCollection:
                    collectionHoists.Add(store);
                    break;
                case StoreField
                {
                    Field: var accumulator,
                    Instance: LoadArgument { Index: 0 },
                    Value: LoadLocal { Index: var local },
                } store
                    when store.SourceOffset == accumulatorHoistSourceOffset
                        && accumulator == storage.Accumulator
                        && local == accumulatorLocal:
                    accumulatorHoists.Add(store);
                    break;
                case StoreField
                {
                    Field: var collection,
                    Instance: LoadArgument { Index: 0 },
                    Value: Constant { Value: null },
                } store
                    when store.SourceOffset == collectionReleaseSourceOffset
                        && collection == storage.Collection:
                    collectionReleases.Add(store);
                    break;
                case StoreLocal
                {
                    Index: var target,
                    Value: LoadLocal { Index: var source },
                } store
                    when store.SourceOffset == finalStoreSourceOffset
                        && target == finalResultLocal
                        && source == accumulatorLocal:
                    finalStores.Add(store);
                    break;
                case StoreLocal store
                    when store.SourceOffset == awaitedResultStoreSourceOffset:
                    awaitedResultStores.Add(store);
                    break;
                case StoreLocal store
                    when store.SourceOffset == accumulatorStoreSourceOffset:
                    accumulatorStores.Add(store);
                    break;
            }
            if (IsMachineFieldWrite(node, storage.Index, machine))
            {
                indexWrites.Add(node);
                if (node is StoreField
                    {
                        Value: Constant { Value: 0 },
                    } indexInitializer
                    && indexInitializer.SourceOffset == initializerSourceOffset)
                {
                    initializers.Add(indexInitializer);
                }
            }
            if (IsMachineFieldWrite(node, storage.Collection, machine))
                collectionWrites.Add(node);
            if (IsMachineFieldWrite(node, storage.Accumulator, machine))
                accumulatorWrites.Add(node);
        }
        if (bounds is not [ConditionalBranch bound]
            || entries is not [Branch entry]
            || bodyAnchors is not [StoreStackSlot bodyAnchor]
            || advances is not [StoreField advance]
            || initializers is not [StoreField initializer]
            || indexWrites.Count != 2
            || collectionHoists is not [StoreField collectionHoist]
            || accumulatorHoists is not [StoreField accumulatorHoist]
            || collectionReleases is not [StoreField collectionRelease]
            || finalStores is not [StoreLocal finalStore]
            || awaitedResultStores is not
                [StoreLocal awaitedResultStore]
            || accumulatorStores is not [StoreLocal accumulatorStore]
            || collectionWrites.Count != 2
            || accumulatorWrites.Count != 1
            || bound.Parent is not Block boundBlock
            || entry.Parent is not Block entryBlock
            || bodyAnchor.Parent is not Block bodyBlock
            || advance.Parent is not Block advanceBlock)
        {
            return false;
        }

        return TryGetForeachControlIdentity(
            boundBlock,
            bound,
            entryBlock,
            bodyBlock,
            advanceBlock,
            initializer,
            collectionHoist,
            bodyAnchor,
            accumulatorHoist,
            awaitedResultStore,
            accumulatorStore,
            collectionRelease,
            finalStore,
            budget,
            out identity);
    }

    static bool IsMachineFieldWrite(
        IrNode node,
        FieldRef expectedField,
        TypeRef machine)
        => node switch
        {
            StoreField
            {
                Field: var field,
            } => field == expectedField
                && ClassicInverseNodeFacts.IsMachineField(field, machine),
            InitObject
            {
                Address: LoadFieldAddress
                {
                    Field: var field,
                },
            } => field == expectedField
                && ClassicInverseNodeFacts.IsMachineField(field, machine),
            _ => false,
        };

    static bool IsExactLoopBound(
        IrExpression expression,
        ClassicInverseLoopStorage storage,
        TypeRef machine)
    {
        if (expression is not Comparison
            {
                Kind: ComparisonKind.LessThan,
                IsUnsigned: false,
                Left: LoadField
                {
                    Field: var index,
                    Instance: LoadArgument { Index: 0 },
                },
            } comparison
            || index != storage.Index
            || !ClassicInverseNodeFacts.IsMachineField(index, machine))
        {
            return false;
        }

        IrExpression length = comparison.Right;
        if (length is Convert
            {
                Target: var target,
                IsChecked: false,
                IsUnsigned: false,
                Operand: var converted,
            })
        {
            if (!MemberIdentity.IsCoreLibraryType(
                    target,
                    "System",
                    "Int32"))
                return false;
            length = converted;
        }

        return length is ArrayLength
            {
                Array: LoadField
                {
                    Field: var collection,
                    Instance: LoadArgument { Index: 0 },
                },
            }
            && collection == storage.Collection
            && ClassicInverseNodeFacts.IsMachineField(collection, machine);
    }

    /// <summary>
    /// A read of the loop accumulator: a machine field that is neither the
    /// hoisted collection nor the loop index the same recipe already bound.
    /// </summary>
    static bool IsAccumulatorRead(
        IrExpression expression,
        ClassicInverseShellFacts shell,
        FieldRef collection,
        FieldRef index)
        => expression is LoadField { Instance: LoadArgument { Index: 0 } } load
            && ClassicInverseNodeFacts.IsMachineField(load.Field, shell.Machine)
            && load.Field != collection
            && load.Field != index;

    // ---- Recipe: await inside try/finally --------------------------------

    static ClassicInverseCandidate? TryTryFinally(
        IrFunction rawExecution,
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults,
        ClassicInverseBudget budget)
    {
        IrFunction execution = planning.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1)
        {
            return null;
        }

        if (execution.Body.Descendants.OfType<TryFinally>().ToList()
            is not [TryFinally tryFinally])
        {
            return null;
        }

        StoreLocal? resultStore = tryFinally.TryBody.Descendants.OfType<StoreLocal>()
            .LastOrDefault(store =>
                store.Index == result.Index && Contains(store.Value, getResults[0]));
        if (resultStore is null
            || !ProvesCompletionTransfer(
                rawExecution,
                resultStore,
                setResult,
                budget))
            return null;

        List<IfStatement> guards = [.. tryFinally.FinallyBody.Blocks
            .SelectMany(static block => block.Children)
            .OfType<IfStatement>()];
        if (guards is not [IfStatement guard]
            || guard.Then.Children is not [ExpressionStatement guarded]
            || guard.HasElse)
        {
            return null;
        }

        var candidate = new ClassicInverseCandidate("classic-await-try-finally")
        {
            ResultLocal = result.Index,
        };
        var rewriter = new ClassicInverseRewriter(planning, shell, candidate);
        IrNode? value = rewriter.Rewrite(resultStore.Value);
        IrNode? finallyStatement = rewriter.Rewrite(guarded);
        if (value is not IrExpression returned
            || finallyStatement is not ExpressionStatement finallyOutput)
        {
            return null;
        }

        var ret = new Return(returned);
        var output = new TryFinally(Container(ret), Container(finallyOutput));
        candidate.Statements.Add(output);

        candidate.Claim(
            resultStore,
            ret,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            guarded,
            finallyOutput,
            ClassicInverseRealizationRule.Statement);

        candidate.DeclareContainer(
            tryFinally,
            ClassicInverseAncestorKind.Reproduced,
            "try-finally",
            output);
        candidate.DeclareContainer(
            tryFinally.TryBody,
            ClassicInverseAncestorKind.Reproduced,
            "try-body",
            output.TryBody);
        candidate.DeclareContainer(
            tryFinally.FinallyBody,
            ClassicInverseAncestorKind.Reproduced,
            "finally-body",
            output.FinallyBody);

        // The compiler runs the user's finally body only when the machine is
        // not suspended. That guard is the shell's, and the reproduced C#
        // `finally` accounts for its semantics.
        if (IsFinallyStateGuard(guard.Condition, shell))
        {
            candidate.DeclareContainer(
                guard,
                ClassicInverseAncestorKind.Protocol,
                "finally-state-guard",
                outputContext: null);
            candidate.DeclareProtocol(guard.Condition, "finally-state-guard-test");
        }

        return candidate;
    }

    static bool IsFinallyStateGuard(
        IrExpression condition,
        ClassicInverseShellFacts shell)
        => shell.StateLocal >= 0
            && condition is Comparison
            {
                Kind: ComparisonKind.LessThan,
                IsUnsigned: false,
                Left: LoadLocal load,
                Right: Constant { Value: 0 },
            }
            && load.Index == shell.StateLocal;

    static BlockContainer Container(IrNode statement)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(statement);
        container.Add(block);
        return container;
    }

    static bool Contains(IrNode root, IrNode target)
        => ReferenceEquals(root, target)
            || root.Descendants.Any(node => ReferenceEquals(node, target));
}

/// <summary>Accumulates the output local table a recipe introduces.</summary>
internal sealed class ClassicInverseLocalTable
{
    readonly ImmutableArray<TypeRef>.Builder _types =
        ImmutableArray.CreateBuilder<TypeRef>();
    readonly ImmutableArray<string?>.Builder _names =
        ImmutableArray.CreateBuilder<string?>();

    internal int Add(TypeRef type, string? name)
    {
        int index = _types.Count;
        _types.Add(type);
        _names.Add(name);
        return index;
    }

    internal ImmutableArray<TypeRef> Types => _types.ToImmutable();

    internal ImmutableArray<string?> Names => _names.ToImmutable();
}
