namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's <c>using</c> lowering into a <see cref="UsingStatement"/>. Three
/// shapes are recognized, after EH and guard structuring plus return sinking:
/// <code>
///   // reference type — null-guarded interface dispose
///   T V_0 = resource;
///   try { BODY }
///   finally { if (V_0) ((IDisposable)V_0).Dispose(); }
///
///   // value type — unguarded constrained dispose (no null check)
///   T V_0 = resource;
///   try { BODY }
///   finally { V_0.Dispose(); }
///
///   // pattern dispose — unguarded instance Dispose on a same-assembly value type
///   T V_0 = resource;
///   try { BODY }
///   finally { V_0.Dispose(); }
/// </code>
/// all become <c>using (T V_0 = resource) { BODY }</c>. The dispose receiver is
/// a <c>LoadLocal</c> (reference type) or <c>LoadLocalAddress</c> (value type,
/// constrained callvirt).
/// </summary>
public sealed class UsingStatementPass : IIrPass
{
    public string Name => "using-statement";

    sealed record Match(StoreLocal StoreResource, TryFinally TryFinally);
    sealed record AwaitMatch(
        StoreLocal StoreResource,
        StoreLocal StoreException,
        StoreLocal StoreState,
        TryCatch TryCatch,
        IfStatement DisposeIf,
        IfStatement RethrowIf,
        IfStatement ReturnIf,
        Throw? FailThrow);

    public void Run(IrFunction function, PassContext context)
    {
        while (TransformOne(function, context.Stepper))
        {
        }
    }

    static bool TransformOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i + 6 < children.Count; i++)
            {
                if ((TryMatchAwaitUsing(function, children, i) ?? TryMatchNestedAwaitUsing(function, children, i)) is not { } match)
                    continue;

                if (ReferenceOwnership.SubtreeReferencesLocal(match.StoreResource.Value, match.StoreResource.Index)
                    || ReferenceOwnership.SubtreeStoresLocal(match.TryCatch, match.StoreResource.Index)
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, match.StoreResource.Index, [match.StoreResource, match.TryCatch, match.DisposeIf]))
                {
                    continue;
                }

                if (!RewriteAwaitUsingBody(match.TryCatch.TryBody, match.StoreState.Index, match.ReturnIf))
                    continue;

                var resource = (IrExpression)match.StoreResource.DetachChildren()[0];
                var body = match.TryCatch.TryBody;
                body.Detach();
                var usingStatement = new UsingStatement(match.StoreResource.Index, match.StoreResource.Type, resource, body, isAwait: true);
                stepper.StepOver("raise await dispose scaffold to await using", match.TryCatch);
                match.StoreResource.ReplaceWith(usingStatement);
                match.StoreException.Detach();
                match.StoreState.Detach();
                match.TryCatch.Detach();
                match.DisposeIf.Detach();
                match.RethrowIf.Detach();
                match.ReturnIf.Detach();
                match.FailThrow?.Detach();
                new StructuringPass().Run(function, new PassContext(stepper));
                return true;
            }

            for (int i = 0; i + 1 < children.Count; i++)
            {
                if (TryMatch(function, children[i], children[i + 1]) is not { } match)
                    continue;

                if (ReferenceOwnership.SubtreeReferencesLocal(match.StoreResource.Value, match.StoreResource.Index)
                    || ReferenceOwnership.SubtreeStoresLocal(match.TryFinally, match.StoreResource.Index)
                    || !ReferenceOwnership.LocalReferencesOnlyWithin(function, match.StoreResource.Index, [match.StoreResource, match.TryFinally]))
                {
                    continue;
                }

                var resource = (IrExpression)match.StoreResource.DetachChildren()[0];
                var body = match.TryFinally.TryBody;
                body.Detach();
                var usingStatement = new UsingStatement(match.StoreResource.Index, match.StoreResource.Type, resource, body);
                stepper.StepOver("raise dispose try/finally to using", match.TryFinally);
                match.TryFinally.ReplaceWith(usingStatement);
                match.StoreResource.Detach();
                return true;
            }
        }
        return false;
    }

    static AwaitMatch? TryMatchAwaitUsing(IrFunction function, IReadOnlyList<IrNode> children, int i)
    {
        if (i + 7 >= children.Count)
            return null;

        if (children[i] is not StoreLocal storeResource
            || children[i + 1] is not StoreLocal storeException
            || children[i + 2] is not StoreLocal storeState
            || children[i + 3] is not TryCatch tryCatch
            || children[i + 4] is not IfStatement disposeIf
            || children[i + 5] is not IfStatement rethrowIf
            || children[i + 6] is not IfStatement returnIf
            || children[i + 7] is not Throw failThrow)
        {
            return null;
        }

        if (storeException.Value is not Constant { Value: null }
            || storeState.Value is not Constant { Value: 0 }
            || failThrow.Value is not Constant { Value: null })
        {
            return null;
        }

        if (tryCatch.Clauses is not [{ ExceptionType: { Namespace: "System", Name: "Object" }, VariableIndex: var exceptionIndex, Body.Blocks: [{ Children.Count: 0 }] }]
            || exceptionIndex != storeException.Index)
        {
            return null;
        }

        if (disposeIf is not
            {
                Else: null,
                Condition: LoadLocal disposeGuard,
                Then.Children: [ExpressionStatement { Expression: AwaitExpression awaitedDispose }],
            }
            || disposeGuard.Index != storeResource.Index
            || awaitedDispose.Operand is not Call dispose
            || !IsAsyncDisposeOf(dispose, storeResource))
        {
            return null;
        }

        if (!IsExceptionRethrowScaffold(rethrowIf, storeException.Index))
            return null;

        if (!IsReturnState(returnIf, storeState.Index))
            return null;

        return new AwaitMatch(storeResource, storeException, storeState, tryCatch, disposeIf, rethrowIf, returnIf, failThrow);
    }

    static AwaitMatch? TryMatchNestedAwaitUsing(IrFunction function, IReadOnlyList<IrNode> children, int i)
    {
        if (children[i] is not StoreLocal storeResource
            || children[i + 1] is not StoreLocal storeException
            || children[i + 2] is not StoreLocal storeState
            || children[i + 3] is not TryCatch tryCatch
            || children[i + 4] is not IfStatement disposeIf
            || children[i + 5] is not IfStatement rethrowIf
            || children[i + 6] is not IfStatement returnIf)
        {
            return null;
        }

        if (storeException.Value is not Constant { Value: null }
            || storeState.Value is not Constant { Value: 0 })
        {
            return null;
        }

        if (tryCatch.Clauses is not [{ ExceptionType: { Namespace: "System", Name: "Object" }, VariableIndex: var exceptionIndex, Body.Blocks: [{ Children.Count: 0 }] }]
            || exceptionIndex != storeException.Index)
        {
            return null;
        }

        if (disposeIf is not
            {
                Else: null,
                Condition: LoadLocal disposeGuard,
                Then.Children: [ExpressionStatement { Expression: AwaitExpression awaitedDispose }],
            }
            || disposeGuard.Index != storeResource.Index
            || awaitedDispose.Operand is not Call dispose
            || !IsAsyncDisposeOf(dispose, storeResource))
        {
            return null;
        }

        if (!IsExceptionRethrowScaffold(rethrowIf, storeException.Index))
            return null;

        if (!IsNestedReturnState(returnIf, storeState.Index))
            return null;

        return new AwaitMatch(storeResource, storeException, storeState, tryCatch, disposeIf, rethrowIf, returnIf, FailThrow: null);
    }

    static bool IsAsyncDisposeOf(Call dispose, StoreLocal storeResource)
    {
        if (dispose.Arguments is not [var receiver]
            || !IsResourceReceiver(receiver, storeResource.Index))
        {
            return false;
        }

        return dispose.Callee is
        {
            HasThis: true,
            Name: "DisposeAsync",
            TypeArguments.IsEmpty: true,
            ParameterTypes.IsEmpty: true,
            ReturnType: { Assembly: TypeRef.CoreLibrary, Namespace: "System.Threading.Tasks", Name: "ValueTask" },
        } && (dispose.Callee.DeclaringType.Equals(storeResource.Type) || IsIAsyncDisposable(dispose.Callee.DeclaringType));
    }

    static bool IsIAsyncDisposable(TypeRef type)
        => type is { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IAsyncDisposable" };

    static bool IsExceptionRethrowScaffold(IfStatement rethrowIf, int exceptionLocal)
        => rethrowIf is
        {
            Else: null,
            Condition: LoadLocal condition,
            Then.Children:
            [
                StoreStackSlot { Slot: var isInstanceSlot, Value: IsInstance { Type: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Exception" }, Operand: LoadLocal isInstanceOperand } },
                StoreStackSlot { Slot: var exceptionSlot, Value: LoadStackSlot copiedException },
                IfStatement
                {
                    Else: null,
                    Condition: LogicalNot,
                    Then.Children: [ExpressionStatement { Expression: LoadStackSlot discardedException }, Throw { Value: LoadLocal throwOriginal }],
                },
                ExpressionStatement { Expression: Call { Callee: var throwMethod, Arguments: [Call { Callee: var captureMethod, Arguments: [LoadStackSlot capturedException] }] } },
            ],
        }
        && condition.Index == exceptionLocal
        && isInstanceOperand.Index == exceptionLocal
        && throwOriginal.Index == exceptionLocal
        && copiedException.Slot == isInstanceSlot
        && discardedException.Slot == exceptionSlot
        && capturedException.Slot == exceptionSlot
        && throwMethod is { HasThis: true, Name: "Throw", ParameterTypes.IsEmpty: true, ReturnType: { Namespace: "System", Name: "Void" } }
        && captureMethod is { HasThis: false, Name: "Capture", ParameterTypes.Length: 1 };

    static bool IsReturnState(IfStatement returnIf, int stateLocal)
        => returnIf is
        {
            Else: null,
            Condition: Comparison
            {
                Kind: ComparisonKind.Equal,
                Left: LoadLocal state,
                Right: Constant { Value: 1 },
            },
            Then.Children: [Return],
        } && state.Index == stateLocal;

    static bool IsNestedReturnState(IfStatement returnIf, int stateLocal)
        => returnIf is
        {
            Condition: Comparison
            {
                Kind: ComparisonKind.Equal,
                Left: LoadLocal state,
                Right: Constant { Value: 1 },
            },
            Then.Children: [StoreLocal { Value: LoadLocal }],
            Else.Children: [Leave],
        } && state.Index == stateLocal;

    static bool RewriteAwaitUsingBody(BlockContainer tryBody, int stateLocal, IfStatement returnIf)
    {
        if (tryBody.Blocks is not [{ Children.Count: >= 2 } block])
            return false;
        if (block.Children[^1] is not StoreLocal { Index: var storedState, Value: Constant { Value: 1 } } stateStore
            || storedState != stateLocal)
        {
            return false;
        }

        if (block.Children[^2] is StoreLocal resultStore
            && returnIf.Then.Children is [Return { Value: LoadLocal resultLoad }]
            && resultLoad.Index == resultStore.Index)
        {
            var result = (IrExpression)resultStore.DetachChildren()[0];
            resultStore.ReplaceWith(new Return(result));
            stateStore.Detach();
            return true;
        }

        if (block.Children[^2] is StoreLocal nestedResultStore
            && returnIf.Then.Children is [StoreLocal { Value: LoadLocal nestedResultLoad }]
            && nestedResultLoad.Index == nestedResultStore.Index)
        {
            var result = (IrExpression)nestedResultStore.DetachChildren()[0];
            nestedResultStore.ReplaceWith(new Return(result));
            stateStore.Detach();
            return true;
        }

        if (block.Children[^2] is UsingStatement { IsAwait: true } nestedUsing
            && nestedUsing.Descendants.OfType<Return>().Any()
            && returnIf.Then.Children is [Return])
        {
            stateStore.Detach();
            return true;
        }

        return false;
    }

    static Match? TryMatch(IrFunction function, IrNode first, IrNode second)
    {
        if (first is not StoreLocal storeResource || second is not TryFinally tryFinally)
            return null;

        if (tryFinally.FinallyBody.Blocks is not [{ Children: [var only] }])
            return null;

        bool disposes = only switch
        {
            // Reference type: finally { if (V_0) ((IDisposable)V_0).Dispose(); }
            IfStatement
            {
                Else: null,
                Condition: LoadLocal guardLoad,
                Then.Children: [ExpressionStatement { Expression: Call guardedDispose }],
            } => guardLoad.Index == storeResource.Index && IsDisposeOf(function, guardedDispose, storeResource),
            // Value type: finally { V_0.Dispose(); } — no null guard.
            ExpressionStatement { Expression: Call bareDispose } => IsDisposeOf(function, bareDispose, storeResource),
            _ => false,
        };

        return disposes ? new Match(storeResource, tryFinally) : null;
    }

    /// <summary>An <c>IDisposable.Dispose()</c> or pattern <c>Dispose()</c> call whose receiver is the resource local.</summary>
    static bool IsDisposeOf(IrFunction function, Call dispose, StoreLocal storeResource)
    {
        if (dispose.Arguments is not [var receiver])
            return false;

        if (!IsResourceReceiver(receiver, storeResource.Index))
            return false;

        return MemberIdentity.IsIDisposableDispose(dispose)
            || IsPatternDispose(function, dispose, storeResource);
    }

    static bool IsResourceReceiver(IrExpression receiver, int index) => receiver switch
    {
        LoadLocal load => load.Index == index,
        LoadLocalAddress address => address.Index == index,
        _ => false,
    };

    static bool IsPatternDispose(IrFunction function, Call dispose, StoreLocal storeResource)
    {
        if (dispose.IsVirtual
            || dispose.Callee is not
            {
                HasThis: true,
                Name: "Dispose",
                TypeArguments.IsEmpty: true,
                ParameterTypes.IsEmpty: true,
                ReturnType: var returnType,
            }
            || !returnType.Equals(TypeRef.CoreLib("System", "Void"))
            || !dispose.Callee.DeclaringType.Equals(storeResource.Type))
        {
            return false;
        }

        return function.TypeShapes.GetValueOrDefault(storeResource.Type) is TypeShape.ValueType or TypeShape.Enum;
    }

}
