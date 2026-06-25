using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs classic async state-machine kickoffs (runtime-async=off) back to
/// async bodies. The source logic lives in <c>&lt;M&gt;d__N.MoveNext</c>; the public
/// kickoff only initializes the state machine and returns the builder's task.
/// This pass imports the sibling <c>MoveNext</c>, recovers the fixture-backed
/// await shapes, and replaces the kickoff body with the source-shaped body.
/// </summary>
public sealed class ClassicAsyncReconstructionPass : IIrPass
{
    public string Name => "classic-async-reconstruction";

    public void Run(IrFunction function, PassContext context)
    {
        if (TryAcknowledgeSupportMethod(function, context))
            return;

        if (context.ImportMethodBody is null)
            return;
        if (!TryGetKickoff(function, out var kickoff))
            return;

        var moveNextMethod = new MethodRef(
            kickoff.StateMachineType,
            "MoveNext",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: true);
        var moveNextPasses = IrPasses.Default
            .Where(static pass => pass is not ClassicAsyncReconstructionPass)
            .ToImmutableArray();
        if (!context.TryImportAndRunMethodBody(moveNextMethod, moveNextPasses, out var moveNext)
            || moveNext is null)
            return;

        if (!TryReconstruct(moveNext, function, kickoff, out var body, out var locals, out var localNames))
            return;

        context.Stepper.StepOver($"reconstruct classic async '{function.Name}' from {kickoff.StateMachineType.Name}.MoveNext");
        function.ResetLocals(locals, localNames);
        function.RequiresAsyncBodyModifier = true;
        function.Body.DetachChildren();
        foreach (var block in body.Blocks.ToList())
        {
            block.Detach();
            function.Body.Add(block);
        }
    }

    sealed record Kickoff(TypeRef StateMachineType, int StateMachineLocal, int SourceOffset);

    sealed class LocalBuilder
    {
        readonly ImmutableArray<TypeRef>.Builder _locals = ImmutableArray.CreateBuilder<TypeRef>();
        readonly ImmutableArray<string?>.Builder _names = ImmutableArray.CreateBuilder<string?>();

        public int Add(TypeRef type, string? name)
        {
            var index = _locals.Count;
            _locals.Add(type);
            _names.Add(name);
            return index;
        }

        public ImmutableArray<TypeRef> Locals => _locals.ToImmutable();
        public ImmutableArray<string?> Names => _names.ToImmutable();
    }

    static bool TryAcknowledgeSupportMethod(IrFunction function, PassContext context)
    {
        if (function.Name is not ("MoveNext" or "SetStateMachine"))
            return false;
        if (function.DeclaringTypeCompilerGenerated != MetadataFactState.Yes)
            return false;
        if (!LooksLikeClassicAsyncStateMachine(function))
            return false;

        context.Stepper.StepOver($"acknowledge generated classic async support method '{function.DeclaringType.Name}.{function.Name}'");
        function.ResetLocals([], []);
        function.Body.DetachChildren();
        var block = new Block(0);
        block.Add(new Return(null));
        function.Body.Add(block);
        return true;
    }

    static bool LooksLikeClassicAsyncStateMachine(IrFunction function)
        => IsStateMachineType(function.DeclaringType)
            && function.Descendants.Any(static node => node switch
            {
                LoadField { Field.Name: "<>t__builder" } => true,
                LoadFieldAddress { Field.Name: "<>t__builder" } => true,
                StoreField { Field.Name: "<>t__builder" } => true,
                _ => false,
            });

    static bool TryGetKickoff(IrFunction function, out Kickoff kickoff)
    {
        kickoff = null!;
        if (function.Body.Blocks is not [var block])
            return false;

        StoreField? builderStore = null;
        ExpressionStatement? startStatement = null;
        Return? returnTask = null;

        foreach (var statement in block.Children)
        {
            if (statement is StoreField { Field.Name: "<>t__builder", Instance: LoadLocalAddress builderLocal } store
                && builderStore is null)
            {
                builderStore = store;
                if (builderLocal.Index < 0 || builderLocal.Index >= function.Locals.Length)
                    return false;
                continue;
            }

            if (statement is ExpressionStatement { Expression: Call { Callee.Name: "Start" } } expression)
                startStatement = expression;
            else if (statement is Return { Value: LoadProperty { PropertyName: "Task" } } ret)
                returnTask = ret;
        }

        if (builderStore?.Instance is not LoadLocalAddress stateMachineAddress
            || startStatement is null
            || returnTask is null)
        {
            return false;
        }

        var stateMachineType = function.Locals[stateMachineAddress.Index];
        if (IsStateMachineType(stateMachineType))
        {
            kickoff = new Kickoff(stateMachineType, stateMachineAddress.Index, builderStore.SourceOffset);
            return true;
        }

        return false;
    }

    static bool IsStateMachineType(TypeRef type)
    {
        var name = MetadataName(type);
        return name.StartsWith("<", StringComparison.Ordinal)
            && name.Contains(">d__", StringComparison.Ordinal);
    }

    static string MetadataName(TypeRef type)
    {
        var name = type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } definition
            ? definition.Name
            : type.Name;
        var nested = name.LastIndexOf('+');
        return nested >= 0 ? name[(nested + 1)..] : name;
    }

    static bool TryReconstruct(
        IrFunction moveNext,
        IrFunction kickoff,
        Kickoff kickoffShape,
        out BlockContainer body,
        out ImmutableArray<TypeRef> locals,
        out ImmutableArray<string?> localNames)
    {
        body = null!;
        locals = [];
        localNames = [];

        var localBuilder = new LocalBuilder();
        if (!TryBuildStatements(moveNext, kickoff, localBuilder, out var statements))
            return false;

        var block = new Block(0);
        foreach (var statement in statements)
        {
            Reanchor(statement, kickoffShape.SourceOffset);
            block.Add(statement);
        }

        body = new BlockContainer();
        body.Add(block);
        locals = localBuilder.Locals;
        localNames = localBuilder.Names;
        return true;
    }

    static bool TryBuildStatements(
        IrFunction moveNext,
        IrFunction kickoff,
        LocalBuilder locals,
        out List<IrNode> statements)
    {
        statements = [];

        var setResult = FinalSetResult(moveNext);
        var getResults = GetResultCalls(moveNext);
        if (setResult is null)
            return false;

        if (TryBuildTryFinally(moveNext, kickoff, setResult, getResults, out var tryFinally))
        {
            statements.Add(tryFinally);
            return true;
        }

        if (TryBuildLoop(moveNext, kickoff, setResult, locals, out var loopStatements))
        {
            statements.AddRange(loopStatements);
            return true;
        }

        if (TryBuildConditional(moveNext, kickoff, setResult, getResults, out var conditionalReturn))
        {
            statements.Add(conditionalReturn);
            return true;
        }

        if (TryBuildSequentialVoid(moveNext, kickoff, setResult, getResults, locals, out var sequential))
        {
            statements.AddRange(sequential);
            return true;
        }

        if (TryBuildSingleAwaitVoid(moveNext, kickoff, setResult, getResults, out var voidStatements))
        {
            statements.AddRange(voidStatements);
            return true;
        }

        if (TryBuildSingleAwaitReturn(moveNext, kickoff, setResult, getResults, out var ret))
        {
            statements.Add(ret);
            return true;
        }

        return false;
    }

    static Call? FinalSetResult(IrFunction moveNext)
        => moveNext.Descendants.OfType<Call>()
            .LastOrDefault(static call => call.Callee.Name == "SetResult" && IsAsyncMethodBuilder(call.Callee.DeclaringType));

    static List<Call> GetResultCalls(IrFunction moveNext)
        => [.. moveNext.Descendants.OfType<Call>().Where(static call => call.Callee.Name == "GetResult")];

    static bool TryBuildSingleAwaitReturn(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out Return ret)
    {
        ret = null!;
        if (setResult.Arguments is not [_, LoadLocal result]
            || getResults.Count != 1)
        {
            return false;
        }
        if (HasUnexpectedExpressionStatement(moveNext) || HasHoistedUserState(moveNext))
            return false;

        var store = moveNext.Descendants.OfType<StoreLocal>()
            .LastOrDefault(s => s.Index == result.Index && ContainsNode(s.Value, getResults[0]));
        if (store is null)
            return false;
        if (HasUnexpectedStore(moveNext, store))
            return false;

        var value = CloneWithAwaitsAndRemap(store.Value, moveNext, kickoff);
        if (value is null)
            return false;

        ret = new Return(value);
        return true;
    }

    static bool TryBuildSingleAwaitVoid(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out List<IrNode> statements)
    {
        statements = [];
        if (setResult.Arguments.Count != 1 || getResults.Count != 1)
            return false;
        if (HasHoistedUserState(moveNext))
            return false;

        var awaited = AwaitForGetResult(moveNext, kickoff, getResults[0]);
        if (awaited is null)
            return false;
        var getResultStatement = getResults[0].Parent as ExpressionStatement;
        if (getResultStatement is null || HasUnexpectedExpressionStatement(moveNext, getResultStatement))
            return false;
        if (HasUnexpectedStore(moveNext))
            return false;

        statements.Add(new ExpressionStatement(awaited));
        statements.Add(new Return(null));
        return true;
    }

    static bool TryBuildSequentialVoid(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        LocalBuilder locals,
        out List<IrNode> statements)
    {
        statements = [];
        if (setResult.Arguments.Count != 1 || getResults.Count != 2)
            return false;

        var firstResultStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store => ContainsNode(store.Value, getResults[0]));
        if (firstResultStore is null)
            return false;

        var firstStore = moveNext.Descendants.OfType<StoreField>()
            .FirstOrDefault(store => IsHoistedLocal(store.Field.Name)
                && store.Value is LoadLocal local
                && local.Index == firstResultStore.Index);
        if (firstStore is null || firstStore.Field.Type is not { } firstType)
            return false;

        var firstName = ExtractSourceName(firstStore.Field.Name);
        var firstIndex = locals.Add(firstType, firstName);
        var firstAwait = AwaitForGetResult(moveNext, kickoff, getResults[0]);
        if (firstAwait is null)
            return false;
        statements.Add(new StoreLocal(firstIndex, firstType, firstAwait));

        var secondStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store => ContainsNode(store.Value, getResults[1]));
        if (secondStore is null)
            return false;
        var secondName = "y";
        var secondIndex = locals.Add(secondStore.Type, secondName);
        var secondAwait = AwaitForGetResult(moveNext, kickoff, getResults[1]);
        if (secondAwait is null)
            return false;
        statements.Add(new StoreLocal(secondIndex, secondStore.Type, secondAwait));

        var keepAlive = moveNext.Descendants.OfType<ExpressionStatement>()
            .FirstOrDefault(static statement => statement.Expression is Call { Callee.Name: "KeepAlive" });
        if (keepAlive is not { Expression: Call call })
            return false;
        if (HasUnexpectedExpressionStatement(moveNext, keepAlive))
            return false;

        var hoisted = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal)
        {
            [firstStore.Field.Name] = (firstIndex, firstType),
        };
        var replacements = new Dictionary<int, (int Index, TypeRef Type)> { [secondStore.Index] = (secondIndex, secondStore.Type) };
        var mapped = CloneAndRemap(call, kickoff, hoisted, replacements);
        if (mapped is null)
            return false;
        statements.Add(new ExpressionStatement(mapped));
        statements.Add(new Return(null));
        return true;
    }

    static bool TryBuildConditional(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out Return ret)
    {
        ret = null!;
        if (setResult.Arguments is not [_, LoadLocal result] || getResults.Count != 1)
            return false;
        if (HasUnexpectedExpressionStatement(moveNext) || HasHoistedUserState(moveNext))
            return false;

        var flag = moveNext.Descendants.OfType<LoadField>()
            .FirstOrDefault(static field => field.Field.Type is { Name: "Boolean", Namespace: "System" }
                && field.Instance is LoadArgument { Index: 0 }
                && !field.Field.Name.StartsWith("<", StringComparison.Ordinal));
        if (flag is null)
            return false;
        if (!moveNext.Descendants.OfType<ConditionalBranch>().Any(branch => ContainsEquivalentField(branch.Condition, flag.Field.Name)))
            return false;

        var tempStores = moveNext.Descendants.OfType<StoreLocal>().Where(store => store.Index != result.Index).ToList();
        var awaitStore = tempStores.SingleOrDefault(store => ContainsNode(store.Value, getResults[0]));
        if (awaitStore is null)
            return false;
        var zeroStore = tempStores.SingleOrDefault(store => store.Index == awaitStore.Index
            && store.Value is Constant { Value: 0 });
        if (zeroStore is null)
            return false;
        if (zeroStore.Parent is not Block zeroBlock
            || !moveNext.Descendants.OfType<ConditionalBranch>().Any(branch =>
                branch.TargetOffset == zeroBlock.StartOffset
                && branch.Condition is LogicalNot not
                && ContainsEquivalentField(not.Operand, flag.Field.Name)))
        {
            return false;
        }
        var finalStore = moveNext.Descendants.OfType<StoreLocal>()
            .SingleOrDefault(store => store.Index == result.Index
                && store.Value is LoadLocal load
                && load.Index == awaitStore.Index);
        if (finalStore is null)
            return false;
        if (HasUnexpectedStore(moveNext, awaitStore, zeroStore, finalStore))
            return false;

        var condition = CloneAndRemap((IrExpression)flag, kickoff);
        var awaited = AwaitForGetResult(moveNext, kickoff, getResults[0]);
        if (condition is null || awaited is null)
            return false;

        ret = new Return(new Conditional(condition, awaited, new Constant(0, TypeRef.CoreLib("System", "Int32"))));
        return true;
    }

    static bool TryBuildLoop(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        LocalBuilder locals,
        out List<IrNode> statements)
    {
        statements = [];
        if (setResult.Arguments is not [_, LoadLocal])
            return false;
        if (HasUnexpectedExpressionStatement(moveNext))
            return false;

        var tasksField = moveNext.Descendants.OfType<LoadField>()
            .FirstOrDefault(static field => field.Field.Name == "tasks"
                && field.Field.Type is { Kind: TypeRefKind.SzArray, ElementType: { } element }
                && IsTaskLike(element));
        if (tasksField is null || tasksField.Field.Type.ElementType is not { } taskType)
            return false;

        var getResult = GetResultCalls(moveNext).SingleOrDefault();
        if (getResult is null)
            return false;
        if (!HasField(moveNext, "<>7__wrap1")
            || !HasField(moveNext, "<>7__wrap2")
            || !HasField(moveNext, "<>7__wrap3"))
        {
            return false;
        }
        if (!moveNext.Descendants.OfType<StoreLocal>().Any(static store => store.Value is Constant { Value: 0 }))
            return false;
        var resultStore = moveNext.Descendants.OfType<StoreLocal>()
            .SingleOrDefault(store => ContainsNode(store.Value, getResult));
        if (resultStore is null)
            return false;
        var accumulatorStore = moveNext.Descendants.OfType<StoreLocal>()
            .SingleOrDefault(store => store.Value is Binary { Kind: BinaryKind.Add } binary
                && IsWrap3Load(binary.Left)
                && binary.Right is LoadLocal load
                && load.Index == resultStore.Index);
        if (accumulatorStore is null)
            return false;

        var sumType = accumulatorStore.Type;
        var sumIndex = locals.Add(sumType, "sum");
        var taskIndex = locals.Add(taskType, "task");

        statements.Add(new StoreLocal(sumIndex, sumType, new Constant(0, sumType)));
        var body = new Block(0);
        var awaited = new AwaitExpression(new LoadLocal(taskIndex, taskType), getResult.Callee.ReturnType);
        body.Add(new StoreLocal(
            sumIndex,
            sumType,
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadLocal(sumIndex, sumType), awaited)));

        var collection = CloneAndRemap((IrExpression)tasksField, kickoff);
        if (collection is null)
            return false;

        statements.Add(new ForeachStatement(taskIndex, taskType, collection, body));
        statements.Add(new Return(new LoadLocal(sumIndex, sumType)));
        return true;
    }

    static bool TryBuildTryFinally(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out TryFinally tryFinally)
    {
        tryFinally = null!;
        if (setResult.Arguments is not [_, LoadLocal result] || getResults.Count != 1)
            return false;

        var originalTryFinally = moveNext.Descendants.OfType<TryFinally>().FirstOrDefault();
        if (originalTryFinally is null)
            return false;

        var resultStore = originalTryFinally.TryBody.Descendants.OfType<StoreLocal>()
            .LastOrDefault(store => store.Index == result.Index && ContainsNode(store.Value, getResults[0]));
        if (resultStore is null)
            return false;
        var resultValue = CloneWithAwaitsAndRemap(resultStore.Value, moveNext, kickoff);
        if (resultValue is null)
            return false;

        var finallyStatements = originalTryFinally.FinallyBody.Blocks
            .SelectMany(block => block.Children)
            .OfType<IfStatement>()
            .SelectMany(ifStatement => ifStatement.Then.Children)
            .OfType<ExpressionStatement>()
            .ToList();
        if (finallyStatements.Count != 1)
            return false;
        if (HasUnexpectedExpressionStatement(moveNext, finallyStatements[0]))
            return false;

        var mappedFinally = CloneAndRemap(finallyStatements[0], kickoff);
        if (mappedFinally is null)
            return false;

        tryFinally = new TryFinally(
            Container(new Return(resultValue)),
            Container(mappedFinally));
        return true;
    }

    static AwaitExpression? AwaitForGetResult(IrFunction moveNext, IrFunction kickoff, Call getResult)
    {
        if (getResult.Arguments is not [LoadLocalAddress awaiterAddress])
            return null;

        var nodes = moveNext.Descendants.ToList();
        var getResultPosition = nodes.IndexOf(getResult);
        if (getResultPosition < 0)
            return null;

        StoreLocal? awaiterStore = null;
        for (var i = 0; i < getResultPosition; i++)
        {
            if (nodes[i] is StoreLocal { Index: var index, Value: Call { Callee.Name: "GetAwaiter" } call } store
                && index == awaiterAddress.Index
                && call.Arguments.Count == 1)
            {
                if (store.Value is Call { Arguments: [LoadField { Field.Name: var maybeAwaiterField }] }
                    && maybeAwaiterField.StartsWith("<>u__", StringComparison.Ordinal))
                {
                    continue;
                }
                awaiterStore = store;
            }
        }

        if (awaiterStore?.Value is not Call { Arguments: [var awaitedOperand] })
            return null;

        var operand = CloneAndRemap(awaitedOperand, kickoff);
        return operand is null ? null : new AwaitExpression(operand, getResult.Callee.ReturnType);
    }

    static bool HasUnexpectedExpressionStatement(IrFunction moveNext, params ExpressionStatement[] allowed)
    {
        var allowedSet = allowed.ToHashSet();
        foreach (var statement in moveNext.Descendants.OfType<ExpressionStatement>())
        {
            if (allowedSet.Contains(statement))
                continue;
            if (statement.Expression is Call { Callee.Name: "AwaitUnsafeOnCompleted" or "SetException" or "SetResult" })
                continue;
            return true;
        }
        return false;
    }

    static bool HasUnexpectedStore(IrFunction moveNext, params IrNode[] allowed)
    {
        var allowedSet = allowed.ToHashSet();
        var stateLocal = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(static store => store.Value is LoadField { Field.Name: "<>1__state" })
            ?.Index;

        foreach (var node in moveNext.Descendants)
        {
            switch (node)
            {
                case StoreField store:
                    if (allowedSet.Contains(store) || store.Field.Name.StartsWith("<>", StringComparison.Ordinal))
                        continue;
                    return true;
                case StoreProperty or StoreElement or StoreIndirect or StoreArgument:
                    if (!allowedSet.Contains(node))
                        return true;
                    break;
                case StoreLocal store:
                    if (allowedSet.Contains(store))
                        continue;
                    if (stateLocal is { } state && store.Index == state
                        && store.Value is Constant or LoadStackSlot or LoadField { Field.Name: "<>1__state" })
                        continue;
                    if (store.Value is Call { Callee.Name: "GetAwaiter" }
                        || store.Value is LoadField { Field.Name: var fieldName } && fieldName.StartsWith("<>u__", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    return true;
            }
        }
        return false;
    }

    static bool HasHoistedUserState(IrFunction moveNext)
        => moveNext.Descendants.OfType<StoreField>().Any(static store =>
            IsHoistedLocal(store.Field.Name) || store.Field.Name.StartsWith("<>7__wrap", StringComparison.Ordinal));

    static bool HasField(IrFunction moveNext, string name)
        => moveNext.Descendants.Any(node => node switch
        {
            LoadField { Field.Name: var fieldName } => fieldName == name,
            StoreField { Field.Name: var fieldName } => fieldName == name,
            LoadFieldAddress { Field.Name: var fieldName } => fieldName == name,
            _ => false,
        });

    static bool ContainsEquivalentField(IrNode node, string fieldName)
        => node.Descendants.Prepend(node).Any(candidate =>
            candidate is LoadField { Field.Name: var name, Instance: LoadArgument { Index: 0 } } && name == fieldName);

    static bool IsWrap3Load(IrExpression expression)
        => expression is LoadField { Field.Name: "<>7__wrap3", Instance: LoadArgument { Index: 0 } };

    static IrExpression? CloneWithAwaitsAndRemap(IrExpression expression, IrFunction moveNext, IrFunction kickoff)
    {
        var clone = (IrExpression)expression.Clone();
        var originalGetResults = expression.Descendants.Prepend(expression).OfType<Call>()
            .Where(static call => call.Callee.Name == "GetResult")
            .ToList();
        var clonedGetResults = clone.Descendants.Prepend(clone).OfType<Call>()
            .Where(static call => call.Callee.Name == "GetResult")
            .ToList();
        if (originalGetResults.Count != clonedGetResults.Count)
            return null;

        IrExpression? rootReplacement = null;
        for (var i = 0; i < originalGetResults.Count; i++)
        {
            var awaited = AwaitForGetResult(moveNext, kickoff, originalGetResults[i]);
            if (awaited is null)
                return null;
            if (ReferenceEquals(clonedGetResults[i], clone))
                rootReplacement = awaited;
            else
                clonedGetResults[i].ReplaceWith(awaited);
        }

        var result = rootReplacement ?? clone;
        return RemapInPlace(result, kickoff) ? result : null;
    }

    static IrExpression? CloneAndRemap(IrExpression expression, IrFunction kickoff)
    {
        var clone = (IrExpression)expression.Clone();
        if (clone is LoadField { Instance: LoadArgument { Index: 0, Name: "this" }, Field: var field }
            && TryGetParameter(kickoff, field.Name, out var argIndex, out var parameter))
        {
            return new LoadArgument(argIndex, parameter.Name, parameter.Type);
        }
        if (clone is LoadFieldAddress { Instance: LoadArgument { Index: 0, Name: "this" }, Field: var addressField }
            && TryGetParameter(kickoff, addressField.Name, out var addressArgIndex, out var addressParameter))
        {
            return new LoadArgument(addressArgIndex, addressParameter.Name, addressParameter.Type);
        }

        return RemapInPlace(clone, kickoff) ? clone : null;
    }

    static T? CloneAndRemap<T>(T node, IrFunction kickoff) where T : IrNode
    {
        var clone = (T)node.Clone();
        return RemapInPlace(clone, kickoff) ? clone : null;
    }

    static Call? CloneAndRemap(
        Call call,
        IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> hoisted,
        IReadOnlyDictionary<int, (int Index, TypeRef Type)> locals)
    {
        var clone = (Call)call.Clone();
        return RemapInPlace(clone, kickoff, hoisted, locals) ? clone : null;
    }

    static bool RemapInPlace(
        IrNode node,
        IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)>? hoisted = null,
        IReadOnlyDictionary<int, (int Index, TypeRef Type)>? localReplacements = null)
    {
        var swaps = new List<(IrNode Old, IrNode New)>();
        var ok = true;
        Visit(node);
        if (!ok)
            return false;

        foreach (var (old, replacement) in swaps)
        {
            if (ReferenceEquals(old, node))
                return false;
            old.ReplaceWith(replacement);
        }
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            switch (current)
            {
                case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (hoisted is not null && hoisted.TryGetValue(field.Name, out var local))
                    {
                        swaps.Add((current, new LoadLocal(local.Index, local.Type)));
                    }
                    else if (TryGetParameter(kickoff, field.Name, out var argIndex, out var parameter))
                    {
                        swaps.Add((current, new LoadArgument(argIndex, parameter.Name, parameter.Type)));
                    }
                    else
                    {
                        ok = false;
                    }
                    return;
                case LoadFieldAddress { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (TryGetParameter(kickoff, field.Name, out var addressArgIndex, out var addressParameter))
                    {
                        swaps.Add((current, new LoadArgument(addressArgIndex, addressParameter.Name, addressParameter.Type)));
                    }
                    else
                    {
                        ok = false;
                    }
                    return;
                case LoadLocal load when localReplacements is not null && localReplacements.TryGetValue(load.Index, out var replacement):
                    swaps.Add((current, new LoadLocal(replacement.Index, replacement.Type)));
                    return;
                case LoadArgument { Index: 0, Name: "this" }:
                    ok = false;
                    return;
            }

            foreach (var child in current.Children)
                Visit(child);
        }
    }

    static bool TryGetParameter(IrFunction kickoff, string fieldName, out int index, out Parameter parameter)
    {
        var argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        for (var i = 0; i < kickoff.Signature.Parameters.Length; i++)
        {
            var candidate = kickoff.Signature.Parameters[i];
            if (candidate.Name == fieldName)
            {
                index = argumentBase + i;
                parameter = candidate;
                return true;
            }
        }

        index = -1;
        parameter = null!;
        return false;
    }

    static bool ContainsNode(IrNode root, IrNode target)
        => ReferenceEquals(root, target) || root.Descendants.Any(descendant => ReferenceEquals(descendant, target));

    static bool IsAsyncMethodBuilder(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is
        {
            Namespace: "System.Runtime.CompilerServices",
            Name: "AsyncTaskMethodBuilder" or "AsyncTaskMethodBuilder`1" or "AsyncValueTaskMethodBuilder`1",
        };
    }

    static bool IsTaskLike(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        return definition is
        {
            Namespace: "System.Threading.Tasks",
            Name: "Task" or "Task`1" or "ValueTask" or "ValueTask`1",
        };
    }

    static bool IsHoistedLocal(string fieldName)
        => fieldName.StartsWith("<", StringComparison.Ordinal)
            && !fieldName.StartsWith("<>", StringComparison.Ordinal)
            && fieldName.Contains(">5__", StringComparison.Ordinal);

    static string ExtractSourceName(string fieldName)
    {
        var close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "value";
    }

    static BlockContainer Container(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        container.Add(block);
        return container;
    }

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }
}
