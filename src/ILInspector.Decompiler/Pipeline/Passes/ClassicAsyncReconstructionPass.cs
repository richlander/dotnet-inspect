using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs classic async state-machine kickoffs (runtime-async=off) back to
/// async bodies. The source logic lives in <c>&lt;M&gt;d__N.MoveNext</c>; the public
/// kickoff only initializes the state machine and returns the builder's task.
/// Product imports carry Metadata's authenticated relationship and exact
/// execution MethodDef; tokenless synthetic IR retains a shape-only test seam.
/// The pass recovers the fixture-backed await shapes and replaces the kickoff
/// body with the source-shaped body.
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
        ClassicAsyncRequestSeed? request =
            (function.ClassicAsyncRequest as
                ClassicAsyncRequestAdapterResult.RequestAvailable)?.Request;
        if (function.IsMetadataBacked && request is null)
            return;
        if (!TryGetKickoff(function, out var kickoff))
            return;
        if (request is not null
            && !MatchesStateMachine(
                kickoff.StateMachineType,
                request.Relationship.StateMachineType))
        {
            return;
        }

        var moveNextMethod = new MethodRef(
            kickoff.StateMachineType,
            "MoveNext",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: true);
        if (request is not null)
        {
            moveNextMethod = moveNextMethod with
            {
                ExactDefinitionAddress = request.ExecutionMethod,
                ExactDefinitionAcquisitionGuard =
                    request.AcquisitionGuard,
            };
        }
        var moveNextPasses = IrPasses.ForReconstruction<ClassicAsyncReconstructionPass>();
        if (!context.TryImportAndRunMethodBody(
                moveNextMethod,
                moveNextPasses,
                out var moveNext)
            || moveNext is null)
        {
            return;
        }

        var reconstruction = TryReconstruct(
            moveNext,
            function,
            kickoff,
            out var body,
            out var locals,
            out var localNames,
            out var synthesizedLocalNames);
        if (reconstruction == ReconstructionResult.UnconsumedExecutionRegion)
        {
            MarkUnconsumedExecutionRegion(function, kickoff, context);
            return;
        }
        if (reconstruction != ReconstructionResult.Reconstructed)
            return;

        context.Stepper.StepOver($"reconstruct classic async '{function.Name}' from {kickoff.StateMachineType.Name}.MoveNext");
        function.MergeTypeFactsFrom(moveNext);
        function.ResetLocals(
            locals,
            localNames,
            synthesizedNames: synthesizedLocalNames);
        function.RequiresAsyncBodyModifier = true;
        function.Body.DetachChildren();
        foreach (var block in body.Blocks.ToList())
        {
            block.Detach();
            function.Body.Add(block);
        }
    }

    sealed record Kickoff(TypeRef StateMachineType, int StateMachineLocal, int SourceOffset);

    static bool MatchesStateMachine(
        TypeRef observed,
        MetadataTypeDefinitionAddress expected)
    {
        if (observed.Kind == TypeRefKind.GenericInstance
            && observed.ElementType is { } definition)
        {
            observed = definition;
        }

        return observed.DefinitionModuleVersionId == expected.ModuleVersionId
            && !observed.DefinitionHandle.IsNil
            && MetadataTokens.GetToken(observed.DefinitionHandle)
                == expected.Definition.Value;
    }

    enum ReconstructionResult
    {
        NotRecognized,
        Reconstructed,
        UnconsumedExecutionRegion,
    }

    sealed class LocalBuilder
    {
        readonly ImmutableArray<TypeRef>.Builder _locals = ImmutableArray.CreateBuilder<TypeRef>();
        readonly ImmutableArray<string?>.Builder _names = ImmutableArray.CreateBuilder<string?>();
        readonly ImmutableArray<string?>.Builder _synthesizedNames = ImmutableArray.CreateBuilder<string?>();

        public int Add(TypeRef type, string? name)
        {
            var index = _locals.Count;
            _locals.Add(type);
            _names.Add(name);
            _synthesizedNames.Add(null);
            return index;
        }

        public int AddSynthesized(TypeRef type, string name)
        {
            var index = _locals.Count;
            _locals.Add(type);
            _names.Add(null);
            _synthesizedNames.Add(name);
            return index;
        }

        public ImmutableArray<TypeRef> Locals => _locals.ToImmutable();
        public ImmutableArray<string?> Names => _names.ToImmutable();
        public ImmutableArray<string?> SynthesizedNames
            => _synthesizedNames.Any(static name => name is not null)
                ? _synthesizedNames.ToImmutable()
                : [];
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

    static ReconstructionResult TryReconstruct(
        IrFunction moveNext,
        IrFunction kickoff,
        Kickoff kickoffShape,
        out BlockContainer body,
        out ImmutableArray<TypeRef> locals,
        out ImmutableArray<string?> localNames,
        out ImmutableArray<string?> synthesizedLocalNames)
    {
        body = null!;
        locals = [];
        localNames = [];
        synthesizedLocalNames = [];

        var localBuilder = new LocalBuilder();
        if (!TryBuildStatements(
                moveNext,
                kickoff,
                localBuilder,
                out var statements,
                out bool recipeHasUnconsumedStore))
        {
            return ReconstructionResult.NotRecognized;
        }
        if (recipeHasUnconsumedStore
            || HasUnconsumedExecutionStore(moveNext))
        {
            return ReconstructionResult.UnconsumedExecutionRegion;
        }

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
        synthesizedLocalNames = localBuilder.SynthesizedNames;
        return ReconstructionResult.Reconstructed;
    }

    static bool HasUnconsumedExecutionStore(IrFunction moveNext)
    {
        TypeRef machine = DefinitionType(moveNext.DeclaringType);
        foreach (IrNode node in moveNext.Descendants)
        {
            switch (node)
            {
                case StoreField store
                    when !IsMachineFieldStore(store, machine):
                case StoreProperty:
                case StoreElement:
                case StoreIndirect:
                case StoreArgument:
                case CopyBlock:
                case ChainedAssignment:
                case DeconstructionAssignment:
                case EventSubscription:
                case NullCoalescingAssignment:
                case NullCoalescingFieldAssignment:
                case NullCoalescingFieldAssignmentExpression:
                case NullCoalescingPropertyAssignment:
                case InitObject init
                    when !IsMachineStorageAddress(init.Address, machine):
                case Call call
                    when IsPotentialWriteAccessor(call.Callee):
                    return true;
            }
        }

        return false;
    }

    static bool IsMachineStorageAddress(
        IrExpression address,
        TypeRef machine)
        => address is LoadFieldAddress field
            && IsMachineField(field.Field, machine)
            && IsCompilerHousekeepingField(field.Field.Name);

    static bool IsPotentialWriteAccessor(MethodRef method)
        => method.AccessorKind is
                AccessorKind.PropertySet
                or AccessorKind.EventAdd
                or AccessorKind.EventRemove
            || (method.AccessorKind == AccessorKind.Unknown
                && (HasPropertySetterSignature(method)
                    || HasEventAccessorSignature(method)));

    static bool HasPropertySetterSignature(MethodRef method)
        => method.Name.StartsWith("set_", StringComparison.Ordinal)
            && method.Name.Length > "set_".Length
            && method.ParameterTypes.Length >= 1
            && method.ReturnType is
                { Namespace: "System", Name: "Void" }
            && method.TypeArguments.IsEmpty
            && (method.HasThis
                || method.ParameterTypes.Length == 1);

    static bool HasEventAccessorSignature(MethodRef method)
    {
        int prefixLength =
            method.Name.StartsWith("add_", StringComparison.Ordinal)
                ? "add_".Length
                : method.Name.StartsWith("remove_", StringComparison.Ordinal)
                    ? "remove_".Length
                    : 0;
        return prefixLength > 0
            && method.Name.Length > prefixLength
            && method.ParameterTypes.Length == 1
            && method.ReturnType is
                { Namespace: "System", Name: "Void" }
            && method.TypeArguments.IsEmpty;
    }

    internal static bool IsMachineFieldStore(
        StoreField store,
        TypeRef machine)
        => IsMachineField(store.Field, machine);

    static bool IsMachineField(
        FieldRef field,
        TypeRef machine)
    {
        TypeRef declaringType =
            DefinitionType(field.DeclaringType);
        machine = DefinitionType(machine);
        return !declaringType.DefinitionHandle.IsNil
            && declaringType.DefinitionHandle
                == machine.DefinitionHandle
            && declaringType.DefinitionModuleVersionId is { } declaringMvid
            && machine.DefinitionModuleVersionId is { } machineMvid
            && declaringMvid == machineMvid;
    }

    static TypeRef DefinitionType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } definition
                ? definition
                : type;

    static void MarkUnconsumedExecutionRegion(
        IrFunction function,
        Kickoff kickoff,
        PassContext context)
    {
        context.Stepper.StepOver(
            $"decline classic async '{function.Name}': execution region contains unconsumed user effects");

        IReadOnlyList<Block> originalBlocks = function.Body.Blocks;
        function.Body.DetachChildren();

        var block = new Block(originalBlocks[0].StartOffset);
        var marker = new UnsupportedNode(
            kickoff.SourceOffset,
            "classic async",
            "execution region contains unconsumed user effects; original kickoff preserved");
        marker.SetSourceOffset(kickoff.SourceOffset);
        var markerStatement = new ExpressionStatement(marker);
        markerStatement.SetSourceOffset(kickoff.SourceOffset);
        block.Add(markerStatement);

        foreach (Block originalBlock in originalBlocks)
        {
            foreach (IrNode statement in originalBlock.DetachChildren())
                block.Add(statement);
        }

        function.Body.Add(block);
        function.RequiresAsyncBodyModifier = false;
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct,
            "classic async reconstruction declined: execution region contains unconsumed user effects"));
    }

    static bool TryBuildStatements(
        IrFunction moveNext,
        IrFunction kickoff,
        LocalBuilder locals,
        out List<IrNode> statements,
        out bool hasUnconsumedStore)
    {
        statements = [];
        hasUnconsumedStore = false;

        var setResult = FinalSetResult(moveNext);
        var getResults = GetResultCalls(moveNext);
        if (setResult is null)
            return false;

        if (TryBuildTryFinally(
                moveNext,
                kickoff,
                setResult,
                getResults,
                out var tryFinally,
                out hasUnconsumedStore))
        {
            if (!hasUnconsumedStore)
                statements.Add(tryFinally);
            return true;
        }

        if (TryBuildLoop(
                moveNext,
                kickoff,
                setResult,
                locals,
                out var loopStatements,
                out hasUnconsumedStore))
        {
            if (!hasUnconsumedStore)
                statements.AddRange(loopStatements);
            return true;
        }

        if (TryBuildConditional(
                moveNext,
                kickoff,
                setResult,
                getResults,
                out var conditionalReturn,
                out hasUnconsumedStore))
        {
            if (!hasUnconsumedStore)
                statements.Add(conditionalReturn);
            return true;
        }

        if (TryBuildSequentialVoid(
                moveNext,
                kickoff,
                setResult,
                getResults,
                locals,
                out var sequential,
                out hasUnconsumedStore))
        {
            if (!hasUnconsumedStore)
                statements.AddRange(sequential);
            return true;
        }

        if (TryBuildSingleAwaitVoid(moveNext, kickoff, setResult, getResults, out var voidStatements))
        {
            statements.AddRange(voidStatements);
            return true;
        }

        if (TryBuildSingleAwaitReturn(moveNext, kickoff, setResult, getResults, locals, out var returnStatements))
        {
            statements.AddRange(returnStatements);
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
        LocalBuilder locals,
        out List<IrNode> statements)
    {
        statements = [];
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
            return TryBuildNamedAwaitReturn(moveNext, kickoff, setResult, result, getResults[0], locals, out statements);
        if (HasUnexpectedStore(moveNext, store))
            return false;

        var value = CloneWithAwaitsAndRemap(store.Value, moveNext, kickoff);
        if (value is null)
            return false;

        statements.Add(new Return(value));
        return true;
    }

    static bool TryBuildNamedAwaitReturn(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        LoadLocal completionResult,
        Call getResult,
        LocalBuilder locals,
        out List<IrNode> statements)
    {
        statements = [];
        if (getResult.Parent is not StoreLocal resultStore
            || resultStore.Value != getResult
            || resultStore.Index == completionResult.Index
            || resultStore.Index < 0
            || resultStore.Index >= moveNext.LocalNames.Length
            || moveNext.LocalNames[resultStore.Index] is not { } name
            || resultStore.Parent is not Block continuation
            || continuation.Children is not [var first, StoreLocal returnStore]
            || first != resultStore
            || returnStore.Index != completionResult.Index
            || !IsSingleAwaitContinuation(moveNext, continuation, getResult, setResult)
            || HasUnexpectedStore(moveNext, resultStore, returnStore))
        {
            return false;
        }

        IrNode? resultUse = null;
        foreach (var node in moveNext.Descendants)
        {
            if (node is LoadLocal load && load.Index == resultStore.Index
                || node is LoadLocalAddress address && address.Index == resultStore.Index)
            {
                if (resultUse is not null || !ContainsNode(returnStore.Value, node))
                    return false;
                resultUse = node;
            }
            if (node is LoadLocal result && result.Index == completionResult.Index && node != completionResult
                || node is LoadLocalAddress resultAddress && resultAddress.Index == completionResult.Index)
            {
                return false;
            }
        }
        if (resultUse?.Parent is not { } receiver
            || !(receiver is LoadProperty property && property.Instance == resultUse
                || receiver is LoadField field && field.Instance == resultUse
                || receiver is Call { Callee.HasThis: true } call && call.Arguments.FirstOrDefault() == resultUse)
            || returnStore.Value.Descendants.Prepend(returnStore.Value).Any(node => node switch
            {
                LoadLocal load => load.Index != resultStore.Index,
                LoadLocalAddress address => address.Index != resultStore.Index,
                _ => false,
            }))
        {
            return false;
        }

        var awaited = AwaitForGetResult(moveNext, kickoff, getResult);
        if (awaited is null)
            return false;

        int index = locals.Locals.Length;
        var replacements = new Dictionary<int, (int Index, TypeRef Type)>
        {
            [resultStore.Index] = (index, resultStore.Type),
        };
        var value = (IrExpression)returnStore.Value.Clone();
        if (!RemapInPlace(value, kickoff, localReplacements: replacements))
            return false;

        locals.Add(resultStore.Type, name);
        statements.Add(new StoreLocal(index, resultStore.Type, awaited));
        statements.Add(new Return(value));
        return true;
    }

    // The retained binder belongs to the same single-await completion shell,
    // not a user-guarded or multiply entered descendant that happens to match.
    internal static bool IsSingleAwaitContinuation(
        IrFunction moveNext,
        Block continuation,
        Call getResult,
        Call setResult)
    {
        if (getResult.Arguments is not [LoadLocalAddress awaiter]
            || StateLocalIndex(moveNext) is not { } state
            || continuation.Parent is not BlockContainer { Parent: TryCatch handler } body
            || handler.TryBody != body
            || body.Blocks is not [var dispatch, var acquire, var suspend, var resume, var last]
            || last != continuation
            || dispatch.Children is not [ConditionalBranch
            {
                Condition: LogicalNot { Operand: LoadLocal stateLoad },
            } stateBranch]
            || stateLoad.Index != state
            || stateBranch.TargetOffset != resume.StartOffset
            || acquire.Children is not [StoreLocal
            {
                Value: Call { Callee.Name: "GetAwaiter" },
            } awaiterStore, ConditionalBranch
            {
                Condition: LoadProperty { PropertyName: "IsCompleted", Instance: LoadLocalAddress completedAwaiter },
            } completedBranch]
            || awaiterStore.Index != awaiter.Index
            || completedAwaiter.Index != awaiter.Index
            || completedBranch.TargetOffset != continuation.StartOffset
            || suspend.Children.LastOrDefault() is not Return { Value: null }
            || resume.Children.LastOrDefault() is not StoreField { Field.Name: "<>1__state", Value: Constant { Value: -1 } }
            || moveNext.Body.Blocks is not [var root]
            || root.Children is not [StoreLocal, var rootHandler, StoreField, ExpressionStatement completion, Return { Value: null }]
            || rootHandler != handler
            || completion.Expression != setResult
            || handler.Clauses is not [var catchClause]
            || catchClause.Filter is not null
            || catchClause.Body.Blocks is not [var catchBlock]
            || catchBlock.Children is not [StoreField, ExpressionStatement
            {
                Expression: Call { Callee.Name: "SetException" } setException,
            }, Return { Value: null }]
            || !IsCompilerBuilderCallback(moveNext, setException))
        {
            return false;
        }

        // No extra transfer, nested region, or user condition may bypass the
        // continuation or change the number of executions of its two stores.
        return body.Blocks.All(block => block.Children.All(node => node switch
        {
            StoreLocal or StoreField or InitObject or ExpressionStatement => true,
            ConditionalBranch branch => branch == stateBranch || branch == completedBranch,
            Return { Value: null } => block == suspend,
            _ => false,
        }));
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
        out List<IrNode> statements,
        out bool hasUnconsumedStore)
    {
        statements = [];
        hasUnconsumedStore = false;
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
        string? secondName =
            secondStore.Index >= 0
            && secondStore.Index < moveNext.LocalNames.Length
                ? moveNext.LocalNames[secondStore.Index]
                : null;
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
        if (!IsRealizedAwaitResult(firstResultStore, getResults[0])
            || !IsRealizedAwaitResult(secondStore, getResults[1]))
        {
            hasUnconsumedStore = true;
            return true;
        }
        if (HasUnexpectedStore(
                moveNext,
                firstResultStore,
                firstStore,
                secondStore))
        {
            hasUnconsumedStore = true;
            return true;
        }

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

    static bool IsRealizedAwaitResult(
        StoreLocal store,
        Call getResult)
        => ReferenceEquals(store.Value, getResult)
            || store.Value is Convert
            {
                IsChecked: false,
                Target: var target,
                Operand: var operand,
            }
            && ReferenceEquals(operand, getResult)
            && target.Equals(store.Type)
            && CSharpConversionRules.IsImplicitNumericAssignment(
                getResult.Callee.ReturnType,
                target);

    static bool TryBuildConditional(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out Return ret,
        out bool hasUnconsumedStore)
    {
        ret = null!;
        hasUnconsumedStore = false;
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
        var awaitStores = tempStores.Where(store => ContainsNode(store.Value, getResults[0])).ToList();
        if (awaitStores is not [var awaitStore])
            return false;
        var zeroStores = tempStores.Where(store => store.Index == awaitStore.Index
            && store.Value is Constant { Value: 0 }).ToList();
        if (zeroStores is not [var zeroStore])
            return false;
        if (zeroStore.Parent is not Block zeroBlock)
        {
            return false;
        }
        var zeroBranches = moveNext.Descendants.OfType<ConditionalBranch>()
            .Where(branch =>
                branch.TargetOffset == zeroBlock.StartOffset
                && branch.Condition is LogicalNot not
                && ContainsEquivalentField(not.Operand, flag.Field.Name))
            .ToList();
        if (zeroBranches is not [var zeroBranch])
            return false;
        if (zeroBranch.Condition is not LogicalNot { Operand: LoadField conditionField }
            || conditionField.Field.Name != flag.Field.Name
            || conditionField.Instance is not LoadArgument { Index: 0 })
        {
            hasUnconsumedStore = true;
            return true;
        }
        var finalStores = moveNext.Descendants.OfType<StoreLocal>()
            .Where(store => store.Index == result.Index
                && store.Value is LoadLocal load
                && load.Index == awaitStore.Index)
            .ToList();
        if (finalStores is not [var finalStore])
            return false;
        if (!IsRealizedAwaitResult(awaitStore, getResults[0]))
        {
            hasUnconsumedStore = true;
            return true;
        }
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
        out List<IrNode> statements,
        out bool hasUnconsumedStore)
    {
        statements = [];
        hasUnconsumedStore = false;
        if (setResult.Arguments is not [_, LoadLocal finalResult])
            return false;
        if (HasUnexpectedExpressionStatement(moveNext))
            return false;

        var tasksField = moveNext.Descendants.OfType<LoadField>()
            .FirstOrDefault(static field => field.Field.Name == "tasks"
                && field.Field.Type is { Kind: TypeRefKind.SzArray, ElementType: { } element }
                && IsTaskLike(element));
        if (tasksField is null || tasksField.Field.Type.ElementType is not { } taskType)
            return false;

        var getResults = GetResultCalls(moveNext).ToList();
        if (getResults is not [var getResult])
            return false;
        if (!HasField(moveNext, "<>7__wrap1")
            || !HasField(moveNext, "<>7__wrap2")
            || !HasField(moveNext, "<>7__wrap3"))
        {
            return false;
        }
        var resultStores = moveNext.Descendants.OfType<StoreLocal>()
            .Where(store => ContainsNode(store.Value, getResult))
            .ToList();
        if (resultStores is not [var resultStore])
            return false;
        var accumulatorStores = moveNext.Descendants.OfType<StoreLocal>()
            .Where(store => store.Value is Binary { Kind: BinaryKind.Add } binary
                && IsWrap3Load(binary.Left)
                && binary.Right is LoadLocal load
                && load.Index == resultStore.Index)
            .ToList();
        if (accumulatorStores is not [var accumulatorStore])
            return false;
        var awaitedOperand = AwaitedOperandForGetResult(moveNext, getResult);
        if (awaitedOperand is null
            || !IsCurrentLoopElement(moveNext, awaitedOperand))
        {
            hasUnconsumedStore = true;
            return true;
        }
        var initialAccumulatorStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store =>
                store.Index == accumulatorStore.Index
                && store.Value is Constant { Value: 0 });
        var finalResultStore = moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(store =>
                store.Index == finalResult.Index
                && store.Value is LoadLocal load
                && load.Index == accumulatorStore.Index);
        if (initialAccumulatorStore is null
            || finalResultStore is null)
        {
            return false;
        }
        var expectedLoopFieldStores = moveNext.Descendants
            .OfType<StoreField>()
            .Where(store => IsExpectedLoopFieldStore(
                store,
                accumulatorStore.Index))
            .Cast<IrNode>();
        var allowedStores = new List<IrNode>
        {
            resultStore,
            accumulatorStore,
            initialAccumulatorStore,
            finalResultStore,
        };
        allowedStores.AddRange(expectedLoopFieldStores);
        if (HasUnexpectedStore(moveNext, [.. allowedStores]))
        {
            hasUnconsumedStore = true;
            return true;
        }

        var sumType = accumulatorStore.Type;
        var sumIndex = locals.AddSynthesized(sumType, "sum");
        var taskIndex = locals.AddSynthesized(taskType, "task");

        statements.Add(new StoreLocal(sumIndex, sumType, new Constant(0, sumType)));
        var body = new Block(0);
        var awaited = new AwaitExpression(
            new LoadLocal(taskIndex, taskType),
            getResult.Callee.ReturnType,
            getResult.Callee.ReturnIsDynamic);
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

    static bool IsCurrentLoopElement(
        IrFunction moveNext,
        IrExpression awaitedOperand)
    {
        if (awaitedOperand is not LoadStackSlot load)
            return false;

        var stores = moveNext.Descendants.OfType<StoreStackSlot>()
            .Where(store => store.Slot == load.Slot)
            .ToList();
        return stores is
        [
            {
                Value: LoadElement
                {
                    Array: LoadField
                    {
                        Field.Name: "<>7__wrap1",
                        Instance: LoadArgument { Index: 0 },
                    },
                    Index: LoadField
                    {
                        Field.Name: "<>7__wrap2",
                        Instance: LoadArgument { Index: 0 },
                    },
                },
            },
        ];
    }

    static bool IsExpectedLoopFieldStore(
        StoreField store,
        int accumulatorIndex)
        => store switch
        {
            { Field.Name: "<>7__wrap1", Value: LoadField { Field.Name: "tasks" } }
                or { Field.Name: "<>7__wrap1", Value: Constant { Value: null } }
                or { Field.Name: "<>7__wrap2", Value: Constant { Value: 0 } }
                or
                {
                    Field.Name: "<>7__wrap2",
                    Value: Binary
                    {
                        Kind: BinaryKind.Add,
                        Left: LoadField { Field.Name: "<>7__wrap2" },
                        Right: Constant { Value: 1 },
                    },
                } => true,
            {
                Field.Name: "<>7__wrap3",
                Value: LoadLocal load,
            } => load.Index == accumulatorIndex,
            _ => false,
        };

    static bool TryBuildTryFinally(
        IrFunction moveNext,
        IrFunction kickoff,
        Call setResult,
        IReadOnlyList<Call> getResults,
        out TryFinally tryFinally,
        out bool hasUnconsumedStore)
    {
        tryFinally = null!;
        hasUnconsumedStore = false;
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

        var finallyGuards = originalTryFinally.FinallyBody.Blocks
            .SelectMany(block => block.Children)
            .OfType<IfStatement>()
            .ToList();
        if (finallyGuards is not [var finallyGuard]
            || finallyGuard.Then.Children is not [ExpressionStatement finallyStatement])
        {
            return false;
        }
        if (!IsCompilerFinallyStateGuard(moveNext, finallyGuard.Condition))
        {
            hasUnconsumedStore = true;
            return true;
        }
        if (HasUnexpectedExpressionStatement(moveNext, finallyStatement))
            return false;

        var mappedFinally = CloneAndRemap(finallyStatement, kickoff);
        if (mappedFinally is null)
            return false;
        if (HasUnexpectedStore(moveNext, resultStore))
        {
            hasUnconsumedStore = true;
            return true;
        }

        tryFinally = new TryFinally(
            Container(new Return(resultValue)),
            Container(mappedFinally));
        return true;
    }

    static bool IsCompilerFinallyStateGuard(
        IrFunction moveNext,
        IrExpression condition)
    {
        var stateLocal = StateLocalIndex(moveNext);
        return stateLocal is { } state
            && condition is Comparison
            {
                Kind: ComparisonKind.LessThan,
                IsUnsigned: false,
                Left: LoadLocal load,
                Right: Constant { Value: 0 },
            }
            && load.Index == state;
    }

    static AwaitExpression? AwaitForGetResult(IrFunction moveNext, IrFunction kickoff, Call getResult)
    {
        var awaitedOperand = AwaitedOperandForGetResult(moveNext, getResult);
        if (awaitedOperand is null)
            return null;

        var operand = CloneAndRemap(awaitedOperand, kickoff);
        return operand is null
            ? null
            : new AwaitExpression(
                operand,
                getResult.Callee.ReturnType,
                getResult.Callee.ReturnIsDynamic);
    }

    static IrExpression? AwaitedOperandForGetResult(
        IrFunction moveNext,
        Call getResult)
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
        return awaitedOperand;
    }

    static bool HasUnexpectedExpressionStatement(IrFunction moveNext, params ExpressionStatement[] allowed)
    {
        var allowedSet = allowed.ToHashSet();
        foreach (var statement in moveNext.Descendants.OfType<ExpressionStatement>())
        {
            if (allowedSet.Contains(statement))
                continue;
            if (statement.Expression is Call call
                && IsCompilerBuilderCallback(moveNext, call))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    static bool IsCompilerBuilderCallback(
        IrFunction moveNext,
        Call call)
    {
        if (call.Callee.Name is not ("AwaitUnsafeOnCompleted" or "SetException" or "SetResult")
            || !IsAsyncMethodBuilder(call.Callee.DeclaringType)
            || call.Arguments.Count == 0
            || call.Arguments[0] is not LoadFieldAddress
            {
                Field: var field,
                Instance: LoadArgument { Index: 0 },
            }
            || field.Name != "<>t__builder")
        {
            return false;
        }

        return IsMachineField(
            field,
            DefinitionType(moveNext.DeclaringType));
    }

    static bool HasUnexpectedStore(IrFunction moveNext, params IrNode[] allowed)
    {
        var allowedSet = allowed.ToHashSet();
        var stateLocal = StateLocalIndex(moveNext);

        foreach (var node in moveNext.Descendants)
        {
            switch (node)
            {
                case StoreField store:
                    if (allowedSet.Contains(store)
                        || IsCompilerHousekeepingField(store.Field.Name))
                    {
                        continue;
                    }
                    return true;
                case StoreProperty or StoreElement or StoreIndirect or StoreArgument:
                    if (!allowedSet.Contains(node))
                        return true;
                    break;
                case InitObject init:
                    if (allowedSet.Contains(init)
                        || init.Address is LoadFieldAddress
                        {
                            Field.Name: var initFieldName,
                        }
                        && IsCompilerHousekeepingField(initFieldName))
                    {
                        continue;
                    }
                    return true;
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

    static int? StateLocalIndex(IrFunction moveNext)
        => moveNext.Descendants.OfType<StoreLocal>()
            .FirstOrDefault(static store =>
                store.Value is LoadField { Field.Name: "<>1__state" })
            ?.Index;

    static bool IsCompilerHousekeepingField(string name)
        => name is "<>1__state" or "<>t__builder" or "<>4__this"
            || name.StartsWith("<>u__", StringComparison.Ordinal);

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
            return ParameterLoad(argIndex, parameter);
        }
        if (clone is LoadFieldAddress { Instance: LoadArgument { Index: 0, Name: "this" }, Field: var addressField }
            && TryGetParameter(kickoff, addressField.Name, out var addressArgIndex, out var addressParameter))
        {
            return ParameterLoad(addressArgIndex, addressParameter);
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
        foreach (var load in node.Descendants.Prepend(node).OfType<LoadElement>())
            load.ResultIsDynamic = IrImporter.ArrayElementDynamicFact(load.Array);
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
                        swaps.Add((current, ParameterLoad(argIndex, parameter)));
                    }
                    else
                    {
                        ok = false;
                    }
                    return;
                case LoadFieldAddress { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (TryGetParameter(kickoff, field.Name, out var addressArgIndex, out var addressParameter))
                    {
                        swaps.Add((current, ParameterLoad(addressArgIndex, addressParameter)));
                    }
                    else
                    {
                        ok = false;
                    }
                    return;
                case LoadLocal load when localReplacements is not null && localReplacements.TryGetValue(load.Index, out var replacement):
                    swaps.Add((current, new LoadLocal(replacement.Index, replacement.Type)));
                    return;
                case LoadLocalAddress address when localReplacements is not null && localReplacements.TryGetValue(address.Index, out var replacement):
                    swaps.Add((current, new LoadLocalAddress(replacement.Index, replacement.Type)));
                    return;
                case LoadArgument { Index: 0, Name: "this" }:
                    ok = false;
                    return;
            }

            foreach (var child in current.Children)
                Visit(child);
        }
    }

    static LoadArgument ParameterLoad(int index, Parameter parameter)
        => new(index, parameter)
        {
            IsDynamic = parameter.IsDynamic,
            ArrayElementIsDynamic = parameter.ArrayElementIsDynamic,
        };

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
