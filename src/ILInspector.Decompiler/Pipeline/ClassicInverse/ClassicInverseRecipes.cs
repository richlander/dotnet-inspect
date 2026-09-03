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
        ClassicInverseShellFacts shell)
    {
        var candidates = new List<ClassicInverseCandidate>();
        IrFunction execution = request.ExecutionBody;

        Call? setResult = FinalSetResult(execution);
        if (setResult is null)
            return candidates;
        List<Call> getResults = GetResultCalls(execution, shell);

        Add(candidates, TryTryFinally(request, shell, setResult, getResults));
        Add(candidates, TryLoop(request, shell, setResult, getResults));
        Add(candidates, TryConditional(request, shell, setResult, getResults));
        Add(candidates, TrySequentialVoid(request, shell, setResult, getResults));
        Add(candidates, TrySingleAwaitVoid(request, shell, setResult, getResults));
        Add(candidates, TrySingleAwaitReturn(request, shell, setResult, getResults));
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
        ClassicInverseRequest request,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults)
    {
        IrFunction execution = request.ExecutionBody;
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
        if (store is null)
            return null;

        var candidate = new ClassicInverseCandidate("classic-await-return")
        {
            ResultLocal = result.Index,
        };
        var rewriter = new ClassicInverseRewriter(request, shell, candidate);
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
        ClassicInverseRequest request,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults)
    {
        IrFunction execution = request.ExecutionBody;
        if (setResult.Arguments.Count != 1
            || getResults.Count != 1
            || HasTryFinally(execution)
            || HasForeachHoist(execution))
        {
            return null;
        }

        if (getResults[0].Parent is not ExpressionStatement statement)
            return null;

        var candidate = new ClassicInverseCandidate("classic-await-void");
        var rewriter = new ClassicInverseRewriter(request, shell, candidate);
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
        ClassicInverseRequest request,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults)
    {
        IrFunction execution = request.ExecutionBody;
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
        if (tail is null)
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

        var rewriter = new ClassicInverseRewriter(request, shell, candidate);

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
        ClassicInverseRequest request,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults)
    {
        IrFunction execution = request.ExecutionBody;
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

        List<ConditionalBranch> zeroBranches = [.. execution.Body.Descendants
            .OfType<ConditionalBranch>()
            .Where(branch =>
                branch.TargetOffset == zeroBlock.StartOffset
                && branch.Condition is LogicalNot { Operand: LoadField field }
                && field.Instance is LoadArgument { Index: 0 }
                && ClassicInverseNodeFacts.IsMachineField(field.Field, shell.Machine))];
        if (zeroBranches is not [ConditionalBranch zeroBranch])
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
        List<Branch> joins = [.. execution.Body.Descendants.OfType<Branch>()];
        if (joins.Count > 1)
            return null;

        var candidate = new ClassicInverseCandidate("classic-await-conditional")
        {
            ResultLocal = result.Index,
        };
        var rewriter = new ClassicInverseRewriter(request, shell, candidate);

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
            awaitStore,
            thenOutput,
            ClassicInverseRealizationRule.AwaitResult);
        candidate.Claim(
            zeroStore,
            elseOutput,
            ClassicInverseRealizationRule.ResultStore);
        candidate.Claim(
            finalStore,
            ret,
            ClassicInverseRealizationRule.ResultStore);

        foreach (Branch join in joins)
            candidate.DeclareProtocol(join, "conditional-join");

        Block? thenBlock = EnclosingBlock(awaitStore);
        Block? operandBlock = EnclosingBlock(getResults[0]) is { } resultBlock
            ? OperandBlock(request, shell, getResults[0])
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

    static Block? OperandBlock(
        ClassicInverseRequest request,
        ClassicInverseShellFacts shell,
        Call getResult)
        => AwaitedOperand(request.ExecutionBody, getResult) is { } operand
            ? EnclosingBlock(operand)
            : null;

    // ---- Recipe: await inside a foreach over an array --------------------

    static ClassicInverseCandidate? TryLoop(
        ClassicInverseRequest request,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults)
    {
        IrFunction execution = request.ExecutionBody;
        if (setResult.Arguments is not [_, LoadLocal finalResult]
            || getResults is not [Call getResult]
            || HasTryFinally(execution))
        {
            return null;
        }

        StoreField? collectionHoist = execution.Body.Descendants.OfType<StoreField>()
            .FirstOrDefault(store =>
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
                && IsTaskLike(element));
        if (collectionHoist?.Value is not LoadField collectionField
            || collectionField.Field.Type.ElementType is not { } taskType)
        {
            return null;
        }
        if (!HasField(execution, "<>7__wrap2") || !HasField(execution, "<>7__wrap3"))
            return null;

        if (execution.Body.Descendants.OfType<StoreLocal>()
                .Where(store => Contains(store.Value, getResult)).ToList()
            is not [StoreLocal resultStore])
        {
            return null;
        }
        if (execution.Body.Descendants.OfType<StoreLocal>()
                .Where(store => store.Value is Binary { Kind: BinaryKind.Add } add
                    && IsAccumulatorRead(add.Left, shell)
                    && add.Right is LoadLocal read
                    && read.Index == resultStore.Index).ToList()
            is not [StoreLocal accumulatorStore])
        {
            return null;
        }

        IrExpression? operand = AwaitedOperand(execution, getResult);
        if (operand is not LoadStackSlot spilledElement)
            return null;
        if (execution.Body.Descendants.OfType<StoreStackSlot>()
                .Where(store => store.Slot == spilledElement.Slot).ToList()
            is not [StoreStackSlot elementSpill])
        {
            return null;
        }

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

        var candidate = new ClassicInverseCandidate("classic-await-foreach-array")
        {
            ResultLocal = finalResult.Index,
        };
        var locals = new ClassicInverseLocalTable();
        TypeRef sumType = accumulatorStore.Type;
        int sumIndex = locals.Add(sumType, "sum");
        int taskIndex = locals.Add(taskType, "task");

        candidate.Locals = locals.Types;
        candidate.LocalNames = locals.Names;
        candidate.MapHoistedLocal("<>7__wrap3", sumIndex, sumType);
        candidate.MapLocal(accumulatorStore.Index, sumIndex);
        candidate.MapLocal(finalResult.Index, sumIndex);

        var rewriter = new ClassicInverseRewriter(request, shell, candidate);
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

        foreach (IrNode node in execution.Body.Descendants)
        {
            switch (node)
            {
                case StoreField { Field.Name: "<>7__wrap2", Value: Constant { Value: 0 } } index
                    when index.Instance is LoadArgument { Index: 0 }
                        && ClassicInverseNodeFacts.IsMachineField(
                            index.Field,
                            shell.Machine):
                    candidate.DeclareProtocol(index, "foreach-index-init");
                    break;

                case StoreField
                {
                    Field.Name: "<>7__wrap2",
                    Value: Binary
                    {
                        Kind: BinaryKind.Add,
                        Left: LoadField { Field.Name: "<>7__wrap2" },
                        Right: Constant { Value: 1 },
                    },
                } advance
                    when advance.Instance is LoadArgument { Index: 0 }
                        && ClassicInverseNodeFacts.IsMachineField(
                            advance.Field,
                            shell.Machine):
                    candidate.DeclareProtocol(advance, "foreach-index-advance");
                    break;

                case StoreField { Field.Name: "<>7__wrap1", Value: Constant { Value: null } } release
                    when release.Instance is LoadArgument { Index: 0 }
                        && ClassicInverseNodeFacts.IsMachineField(
                            release.Field,
                            shell.Machine):
                    candidate.DeclareProtocol(release, "foreach-collection-release");
                    break;

                case StoreField { Field.Name: "<>7__wrap3", Value: LoadLocal spill } hoistSum
                    when spill.Index == accumulatorStore.Index
                        && hoistSum.Instance is LoadArgument { Index: 0 }
                        && ClassicInverseNodeFacts.IsMachineField(
                            hoistSum.Field,
                            shell.Machine):
                    candidate.DeclareProtocol(hoistSum, "hoisted-local-transfer");
                    break;

                case ConditionalBranch
                {
                    Condition: Comparison
                    {
                        Kind: ComparisonKind.LessThan,
                        Left: LoadField { Field.Name: "<>7__wrap2" },
                        Right: ArrayLength { Array: LoadField { Field.Name: "<>7__wrap1" } },
                    },
                } test:
                    candidate.DeclareProtocol(test, "foreach-bound-test");
                    break;

                case Branch entry:
                    candidate.DeclareProtocol(entry, "foreach-entry");
                    break;
            }
        }

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

    static bool IsAccumulatorRead(IrExpression expression, ClassicInverseShellFacts shell)
        => expression is LoadField { Field.Name: "<>7__wrap3" } load
            && load.Instance is LoadArgument { Index: 0 }
            && ClassicInverseNodeFacts.IsMachineField(load.Field, shell.Machine);

    static bool HasField(IrFunction execution, string name)
        => execution.Body.Descendants.Any(node => node switch
        {
            LoadField { Field.Name: var field } => field == name,
            StoreField { Field.Name: var field } => field == name,
            LoadFieldAddress { Field.Name: var field } => field == name,
            _ => false,
        });

    // ---- Recipe: await inside try/finally --------------------------------

    static ClassicInverseCandidate? TryTryFinally(
        ClassicInverseRequest request,
        ClassicInverseShellFacts shell,
        Call setResult,
        List<Call> getResults)
    {
        IrFunction execution = request.ExecutionBody;
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
        if (resultStore is null)
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
        var rewriter = new ClassicInverseRewriter(request, shell, candidate);
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
