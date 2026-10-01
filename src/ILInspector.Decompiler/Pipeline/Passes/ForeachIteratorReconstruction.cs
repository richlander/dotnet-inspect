using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs a <b>foreach-delegation</b> iterator — the most common iterator shape,
/// <c>foreach (var x in source) { … yield … }</c>. Its <c>MoveNext</c> is doubly beyond
/// the generic structurer: the state dispatch is irreducible (the resume edge jumps into
/// the loop), <em>and</em> the enumerator's disposal lowers to the iterator-specific split
/// idiom — a <c>fault</c> region whose handler calls <c>Dispose()</c>, plus a
/// <c>&lt;&gt;m__Finally1()</c> call on the normal-completion path — which is neither a
/// reducible graph nor the <c>try/finally</c> the <see cref="UsingStatementPass"/> expects.
///
/// <para>This is the <em>transform-then-restructure</em> path (the sibling of
/// <see cref="ReducibleIteratorReconstruction"/>) extended for the enumerator loop. On the
/// flat raw blocks it:</para>
/// <list type="number">
/// <item>drops the leading state dispatch and the "no state matched" default exit;</item>
/// <item>materializes the hoisted enumerator field (<c>&lt;&gt;7__wrap1</c>) as a fresh
/// hidden local, so <c>e = source.GetEnumerator()</c> / <c>e.MoveNext()</c> / <c>e.Current</c>
/// read an ordinary local;</item>
/// <item>turns each <c>&lt;&gt;2__current = X; … return true;</c> seam into
/// <c>yield return X;</c>, and drops the <c>&lt;&gt;1__state = …</c> stores, the bool
/// return-value local stores, the <c>leave</c>/<c>return</c> markers, and the terminal
/// <c>return</c>;</item>
/// <item>drops the disposal scaffolding — the <c>&lt;&gt;m__Finally1()</c> call, the
/// <c>&lt;&gt;7__wrap1 = null</c> reset, and the entire fault-handler block — and clears the
/// region, because <c>foreach</c> re-implies the disposal.</item>
/// </list>
///
/// <para>The stripped body is now a reducible enumerator loop, so the structurer forms
/// <c>e = source.GetEnumerator(); while (e.MoveNext()) { … }</c>; a final raise folds that
/// hidden-enumerator loop into a <see cref="ForeachStatement"/>. Sound by validation: if the
/// restructured body still carries raw control flow, an unresolved state-machine field, no
/// yield, or no recovered <c>foreach</c>, the match is rejected and the iterator falls
/// through to honest acknowledgment.</para>
///
/// <para>The same owner also handles one narrowly authenticated authored-<c>using</c>
/// iterator shape: exactly two enumerators acquired once in source order, advanced by one
/// bitwise lockstep loop, and disposed by two compiler helpers in reverse order. The
/// helpers' restored states, the state-machine <c>Dispose</c> routing, field resets, and
/// terminal state must all agree before the resources become nested
/// <see cref="UsingStatement"/> nodes. Any missing evidence or user-authored cleanup
/// declines without mutating the kickoff.</para>
///
/// <para>The other two-resource path recovers nested foreach delegation inside an indexed
/// outer loop. It authenticates two distinct positive yield states against the state-machine
/// <c>Dispose</c> routes: the inner yield must dispose both live enumerators, while the direct
/// outer yield disposes only the outer enumerator. After the exact helper/resource, resume,
/// reset, and exception-region correspondence is proven, ordinary structuring recovers the
/// indexed <c>for</c> and both <c>foreach</c> statements.</para>
/// </summary>
internal static class ForeachIteratorReconstruction
{
    sealed record DisposalResource(
        FieldRef Field,
        MethodRef Finally,
        MethodRef Dispose,
        int RestoredState);

    sealed record DelegationResource(
        FieldRef Field,
        MethodRef Finally,
        MethodRef Dispose,
        int RestoredState,
        int ActiveState);

    public static bool TryReconstructNestedDelegation(
        IrFunction work,
        IrFunction kickoff,
        NewObject handoff,
        PassContext context,
        out BlockContainer body)
    {
        body = null!;

        if (!TryGetNestedDelegationResources(
                work,
                handoff,
                context,
                out var resources,
                out var yieldRunningStates))
        {
            return false;
        }

        var blocks = work.Body.Blocks;
        if (blocks.Count < 3
            || blocks[0].Children is not
            [
                StoreLocal
            {
                Index: var stateLocal,
                Value: LoadField
                {
                    Instance: LoadArgument { Index: 0 },
                    Field.Name: "<>1__state",
                },
            },
                SwitchBranch
            {
                Value: LoadLocal switchState,
                TargetOffsets: var dispatchTargets,
            },
            ]
            || switchState.Index != stateLocal
            || dispatchTargets.Length != 3)
        {
            return false;
        }

        var returnLocal = -1;
        foreach (var block in blocks)
            if (block.Children.Count > 0 && block.Children[^1] is Return { Value: LoadLocal terminal })
                returnLocal = terminal.Index;
        if (returnLocal < 0
            || !IsDefaultExit(blocks[1], returnLocal)
            || dispatchTargets[0] != blocks[2].StartOffset)
        {
            return false;
        }

        var yieldStores = work.Descendants.OfType<StoreField>()
            .Where(store => store.Field.Name == "<>2__current"
                && IsStateMachineReceiver(work, store.Instance))
            .ToHashSet();
        if (yieldStores.Count != 2
            || !TryValidateNestedYieldStateRoutes(
                blocks,
                returnLocal,
                dispatchTargets,
                yieldStores,
                yieldRunningStates))
        {
            return false;
        }

        var locals = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal);
        foreach (var resource in resources)
            locals.Add(resource.Field.Name, (work.AddLocal(resource.Field.Type, null), resource.Field.Type));
        foreach (var node in work.Descendants)
        {
            var field = node switch
            {
                StoreField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                LoadField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                _ => null,
            };
            if (field is null
                || !GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name)
                || locals.ContainsKey(field.Name))
            {
                continue;
            }
            locals.Add(field.Name, (work.AddLocal(field.Type, ExtractSourceName(field.Name)), field.Type));
        }

        var disposalHelpers = resources.Select(resource => resource.Finally).ToHashSet();
        var container = new BlockContainer();
        for (var i = 2; i < blocks.Count; i++)
        {
            if (blocks[i].Children is
                [
                    ExpressionStatement
                {
                    Expression: Call
                    {
                        Callee.Name: "System.IDisposable.Dispose",
                        Arguments: [LoadArgument { Index: 0 }],
                    },
                },
                    EndFinally,
                ])
            {
                continue;
            }

            var rebuilt = new Block(blocks[i].StartOffset);
            foreach (var statement in blocks[i].Children.ToList())
            {
                statement.Detach();
                switch (statement)
                {
                    case StoreField
                    {
                        Instance: LoadArgument { Index: 0 },
                        Field.Name: "<>1__state",
                    }:
                        continue;
                    case StoreField { Field.Name: "<>2__current" } yieldStore
                        when yieldStores.Contains(yieldStore):
                        if (!TryRemap(yieldStore.Value, kickoff, locals, receiver: null, out var yielded))
                            return false;
                        rebuilt.Add(new YieldReturn(yielded));
                        continue;
                    case StoreField
                    {
                        Instance: LoadArgument { Index: 0 },
                        Field: var slotField,
                    } slotStore
                        when locals.TryGetValue(slotField.Name, out var slot):
                        if (resources.Any(resource => Equals(resource.Field, slotField))
                            && slotStore.Value is Constant { Value: null })
                        {
                            continue;
                        }
                        if (!TryRemap(slotStore.Value, kickoff, locals, receiver: null, out var stored))
                            return false;
                        rebuilt.Add(new StoreLocal(slot.Index, slotField.Type, stored));
                        continue;
                    case ExpressionStatement { Expression: Call { Callee: var callee } }
                        when disposalHelpers.Contains(callee):
                        continue;
                    case StoreLocal returnStore
                        when returnStore.Index == returnLocal
                            && returnStore.Value is Constant:
                        continue;
                    case Leave:
                    case Return:
                        continue;
                    default:
                        if (!TryRemapInPlace(statement, kickoff, locals, receiver: null))
                            return false;
                        rebuilt.Add(statement);
                        continue;
                }
            }
            container.Add(rebuilt);
        }
        while (container.Blocks.Count > 0 && container.Blocks[^1].Children.Count == 0)
            container.Blocks[^1].Detach();

        work.Regions = ImmutableArray<HandlerRegion>.Empty;
        work.ClearImportedExceptionFacts();
        work.Body.ReplaceWith(container);
        IrPasses.Run(work, IrPasses.Default, context);

        foreach (var resource in resources.AsEnumerable().Reverse())
        {
            int enumeratorIndex = locals[resource.Field.Name].Index;
            if (work.DescendantsOutsideNestedFunctions.Any(
                    node => ReferenceOwnership.ReferencesOrBindsLocal(node, enumeratorIndex))
                && !RaiseForeach(work, resource.Dispose, context, enumeratorIndex))
            {
                return false;
            }
        }
        IrPasses.Run(work, IrPasses.Default, context);

        if (work.Body.Descendants.Any(IsUnstructured)
            || work.Descendants.OfType<YieldReturn>().Count() != 2
            || work.Descendants.OfType<ForeachStatement>().Count() != 2
            || work.Descendants.OfType<ForLoop>().Count() != 1
            || work.Descendants.Any(IsStateMachineField))
        {
            return false;
        }

        body = (BlockContainer)work.Body;
        foreach (var block in body.Blocks)
            foreach (var statement in block.Children)
                Reanchor(statement, handoff.SourceOffset);

        return true;
    }

    static bool TryValidateNestedYieldStateRoutes(
        IReadOnlyList<Block> blocks,
        int returnLocal,
        ImmutableArray<int> dispatchTargets,
        IReadOnlySet<StoreField> yieldStores,
        IReadOnlyDictionary<int, int> yieldRunningStates)
    {
        if (dispatchTargets.Length != yieldRunningStates.Count + 1
            || !yieldRunningStates.Keys.Order().SequenceEqual(Enumerable.Range(1, yieldRunningStates.Count)))
        {
            return false;
        }

        var blockIndices = blocks
            .Select((block, index) => (block.StartOffset, index))
            .ToDictionary(pair => pair.StartOffset, pair => pair.index);
        var matchedStates = new HashSet<int>();
        foreach (var yieldStore in yieldStores)
        {
            if (yieldStore.Parent is not Block yieldBlock
                || !blockIndices.TryGetValue(yieldBlock.StartOffset, out var yieldBlockIndex))
            {
                return false;
            }
            var yieldIndex = yieldBlock.Children.ToList().IndexOf(yieldStore);
            if (yieldIndex < 0
                || yieldBlock.Children.Skip(yieldIndex).ToList() is not
                [
                    StoreField,
                    StoreField
                {
                    Instance: LoadArgument { Index: 0 },
                    Field.Name: "<>1__state",
                    Value: Constant { Value: int yieldState },
                },
                    StoreLocal
                {
                    Index: var storedReturnLocal,
                    Value: Constant { Value: true or 1 },
                },
                    Leave,
                ]
                || storedReturnLocal != returnLocal
                || !yieldRunningStates.TryGetValue(yieldState, out var expectedRunningState)
                || !matchedStates.Add(yieldState)
                || !blockIndices.TryGetValue(dispatchTargets[yieldState], out var resumeIndex)
                || resumeIndex != yieldBlockIndex + 1
                || blocks[resumeIndex].Children is not
                [
                    StoreField
                {
                    Instance: LoadArgument { Index: 0 },
                    Field.Name: "<>1__state",
                    Value: Constant { Value: int runningState },
                },
                ]
                || runningState != expectedRunningState)
            {
                return false;
            }
        }

        return matchedStates.SetEquals(yieldRunningStates.Keys);
    }

    public static bool TryReconstructUsingResources(
        IrFunction work,
        IrFunction kickoff,
        NewObject handoff,
        PassContext context,
        out BlockContainer body)
    {
        body = null!;

        var blocks = work.Body.Blocks;
        if (blocks.Count == 0)
            return false;
        if (!TryGetUsingDisposalResources(
                work,
                handoff,
                context,
                out var resources,
                out var disposeYieldState))
        {
            return false;
        }

        var stateStore = blocks[0].Children.OfType<StoreLocal>()
            .FirstOrDefault(s => s.Value is LoadField
            {
                Instance: LoadArgument { Index: 0 },
                Field.Name: "<>1__state",
            });
        if (stateStore is null)
            return false;
        var stateLocal = stateStore.Index;

        var returnLocal = -1;
        foreach (var block in blocks)
            if (block.Children.Count > 0 && block.Children[^1] is Return { Value: LoadLocal terminal })
                returnLocal = terminal.Index;
        if (returnLocal < 0)
            return false;

        var dispatchEnd = 0;
        while (dispatchEnd < blocks.Count && TestsState(blocks[dispatchEnd], stateLocal))
            dispatchEnd++;
        if (dispatchEnd == 0)
            return false;
        var stateDispatchEnd = dispatchEnd;
        if (dispatchEnd < blocks.Count && IsDefaultExit(blocks[dispatchEnd], returnLocal))
            dispatchEnd++;
        if (dispatchEnd >= blocks.Count)
            return false;

        var locals = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal);
        foreach (var node in work.Descendants)
        {
            var field = node switch
            {
                StoreField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                LoadField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                _ => null,
            };
            if (field is null || !GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name) || locals.ContainsKey(field.Name))
                continue;
            locals[field.Name] = (work.AddLocal(field.Type, ExtractSourceName(field.Name)), field.Type);
        }
        if (resources.Any(resource => !locals.ContainsKey(resource.Field.Name)))
            return false;

        var yieldStores = work.Descendants.OfType<StoreField>()
            .Where(store => store.Field.Name == "<>2__current"
                && IsStateMachineReceiver(work, store.Instance))
            .ToHashSet();
        var expectedYieldCount = yieldStores.Count;
        if (expectedYieldCount == 0)
            return false;
        if (!TryValidateYieldStateRoutes(
                blocks,
                stateDispatchEnd,
                dispatchEnd,
                stateLocal,
                returnLocal,
                yieldStores,
                disposeYieldState))
        {
            return false;
        }

        var disposalHelpers = resources.Select(resource => resource.Finally).ToHashSet();
        var container = new BlockContainer();
        for (var i = dispatchEnd; i < blocks.Count; i++)
        {
            if (blocks[i].Children is
                [
                    ExpressionStatement
                    {
                        Expression: Call
                        {
                            Callee.Name: "System.IDisposable.Dispose",
                            Arguments: [LoadArgument { Index: 0 }],
                        },
                    },
                    EndFinally,
                ])
            {
                continue;
            }

            var rebuilt = new Block(blocks[i].StartOffset);
            foreach (var statement in blocks[i].Children.ToList())
            {
                statement.Detach();
                switch (statement)
                {
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" }:
                        continue;
                    case StoreField { Field.Name: "<>2__current" } yieldStore
                        when yieldStores.Contains(yieldStore):
                        if (!TryRemap(yieldStore.Value, kickoff, locals, receiver: null, out var yielded))
                            return false;
                        rebuilt.Add(new YieldReturn(yielded));
                        continue;
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field: var slotField } slotStore
                        when locals.TryGetValue(slotField.Name, out var slot):
                        if (resources.Any(resource => Equals(resource.Field, slotField))
                            && slotStore.Value is Constant { Value: null })
                        {
                            return false;
                        }
                        if (!TryRemap(slotStore.Value, kickoff, locals, receiver: null, out var stored))
                            return false;
                        rebuilt.Add(new StoreLocal(slot.Index, slotField.Type, stored));
                        continue;
                    case ExpressionStatement { Expression: Call { Callee: var callee } }
                        when disposalHelpers.Contains(callee):
                        continue;
                    case StoreLocal returnStore when returnStore.Index == returnLocal && returnStore.Value is Constant:
                        continue;
                    case Leave:
                    case Return:
                        continue;
                    default:
                        if (!TryRemapInPlace(statement, kickoff, locals, receiver: null))
                            return false;
                        rebuilt.Add(statement);
                        continue;
                }
            }
            container.Add(rebuilt);
        }
        while (container.Blocks.Count > 0 && container.Blocks[^1].Children.Count == 0)
            container.Blocks[^1].Detach();

        work.Regions = ImmutableArray<HandlerRegion>.Empty;
        work.ClearImportedExceptionFacts();
        work.Body.ReplaceWith(container);
        IrPasses.Run(work, IrPasses.Default, context);
        if (!TryRaiseLockstepLoop(work, resources, locals))
            return false;
        IrPasses.Run(work, IrPasses.Default, context);

        if (!RaiseNestedUsingResources(work, resources, locals))
            return false;
        if (work.Body.Descendants.Any(IsUnstructured))
            return false;
        if (work.Descendants.OfType<YieldReturn>().Count() != expectedYieldCount)
            return false;
        if (work.Descendants.Any(IsStateMachineField))
            return false;
        if (work.Descendants.OfType<UsingStatement>().Count() != resources.Count)
            return false;

        body = (BlockContainer)work.Body;
        foreach (var block in body.Blocks)
            foreach (var statement in block.Children)
                Reanchor(statement, handoff.SourceOffset);

        return true;
    }

    static bool TryValidateYieldStateRoutes(
        IReadOnlyList<Block> blocks,
        int stateDispatchEnd,
        int bodyStart,
        int stateLocal,
        int returnLocal,
        IReadOnlySet<StoreField> yieldStores,
        int disposeYieldState)
    {
        var dispatchTargets = new Dictionary<int, int>();
        for (var i = 0; i < stateDispatchEnd; i++)
        {
            var branches = blocks[i].Children.OfType<ConditionalBranch>().ToList();
            if (branches is not [var branch]
                || !TryGetTestedState(branch.Condition, stateLocal, out var state)
                || !dispatchTargets.TryAdd(state, branch.TargetOffset))
            {
                return false;
            }
        }
        if (dispatchTargets.Count != yieldStores.Count + 1
            || !dispatchTargets.TryGetValue(0, out var initialTarget)
            || initialTarget != blocks[bodyStart].StartOffset)
        {
            return false;
        }

        var blockIndices = blocks
            .Select((block, index) => (block.StartOffset, index))
            .ToDictionary(pair => pair.StartOffset, pair => pair.index);
        var matchedStates = new HashSet<int>();
        foreach (var yieldStore in yieldStores)
        {
            if (yieldStore.Parent is not Block yieldBlock
                || !blockIndices.TryGetValue(yieldBlock.StartOffset, out var yieldBlockIndex))
            {
                return false;
            }
            var yieldIndex = yieldBlock.Children.ToList().IndexOf(yieldStore);
            if (yieldIndex < 0
                || yieldBlock.Children.Skip(yieldIndex).ToList() is not
                [
                    StoreField,
                    StoreField
                    {
                        Instance: LoadArgument { Index: 0 },
                        Field.Name: "<>1__state",
                        Value: Constant { Value: int yieldState },
                    },
                    StoreLocal
                    {
                        Index: var storedReturnLocal,
                        Value: Constant { Value: true or 1 },
                    },
                    Leave,
                ]
                || storedReturnLocal != returnLocal
                || yieldState <= 0
                || yieldState != disposeYieldState
                || !matchedStates.Add(yieldState)
                || !dispatchTargets.TryGetValue(yieldState, out var resumeTarget)
                || !blockIndices.TryGetValue(resumeTarget, out var resumeIndex)
                || resumeIndex != yieldBlockIndex + 1
                || resumeIndex + 1 >= blocks.Count
                || blocks[resumeIndex].Children is not
                [
                    StoreField
                    {
                        Instance: LoadArgument { Index: 0 },
                        Field.Name: "<>1__state",
                        Value: Constant { Value: int runningState },
                    },
                ]
                || runningState >= 0)
            {
                return false;
            }
        }

        return dispatchTargets.Keys
            .Where(state => state != 0)
            .ToHashSet()
            .SetEquals(matchedStates);
    }

    static bool TryGetTestedState(IrExpression condition, int stateLocal, out int state)
    {
        state = 0;
        if (condition is LogicalNot { Operand: LoadLocal zero } && zero.Index == stateLocal)
            return true;
        if (condition is Comparison
            {
                Kind: ComparisonKind.Equal,
                Left: LoadLocal load,
                Right: Constant { Value: int value },
            } && load.Index == stateLocal)
        {
            state = value;
            return true;
        }
        return false;
    }

    static bool TryRaiseLockstepLoop(
        IrFunction work,
        IReadOnlyList<DisposalResource> resources,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals)
    {
        var blocks = work.Body.Blocks;
        if (resources.Count != 2
            || blocks.Count < 3
            || blocks[0].Children.Count == 0
            || blocks[0].Children[^1] is not Branch entryBranch)
        {
            return false;
        }

        var conditionIndex = -1;
        for (var i = 1; i < blocks.Count; i++)
            if (blocks[i].StartOffset == entryBranch.TargetOffset)
            {
                conditionIndex = i;
                break;
            }
        if (conditionIndex <= 1 || conditionIndex != blocks.Count - 1)
            return false;

        var conditionBlock = blocks[conditionIndex];
        if (conditionBlock.Children is not
            [
                StoreLocal { Value: Call { Callee.Name: "MoveNext" } firstMove } firstTemp,
                StoreLocal { Value: LoadLocal firstTempRead } firstResult,
                StoreLocal { Value: Call { Callee.Name: "MoveNext" } secondMove } secondTemp,
                StoreLocal { Value: LoadLocal secondTempRead } secondResult,
                ConditionalBranch
                {
                    Condition: Binary
                    {
                        Kind: BinaryKind.Or,
                        Left: LoadLocal firstCondition,
                        Right: LoadLocal secondCondition,
                    },
                } backBranch,
            ])
        {
            return false;
        }

        var firstLocal = locals[resources[0].Field.Name];
        var secondLocal = locals[resources[1].Field.Name];
        if (firstMove.Arguments is not [LoadLocal firstReceiver]
            || !IsMoveNextCall(firstMove)
            || firstReceiver.Index != firstLocal.Index
            || secondMove.Arguments is not [LoadLocal secondReceiver]
            || !IsMoveNextCall(secondMove)
            || secondReceiver.Index != secondLocal.Index
            || firstTemp.Index == secondTemp.Index
            || firstResult.Index == secondResult.Index
            || !MemberIdentity.IsCoreLibraryType(firstResult.Type, "System", "Boolean")
            || !MemberIdentity.IsCoreLibraryType(secondResult.Type, "System", "Boolean")
            || firstTempRead.Index != firstTemp.Index
            || firstCondition.Index != firstTemp.Index
            || secondTempRead.Index != secondTemp.Index
            || secondCondition.Index != secondTemp.Index
            || backBranch.TargetOffset != blocks[1].StartOffset)
        {
            return false;
        }

        foreach (var block in blocks.Skip(1).Take(conditionIndex - 1))
            if (block.Children.Any(IsUnstructured))
                return false;

        var loopBody = new Block(blocks[1].StartOffset);
        loopBody.Add(new StoreLocal(
            firstResult.Index,
            firstResult.Type,
            (IrExpression)firstMove.Clone()));
        loopBody.Add(new StoreLocal(
            secondResult.Index,
            secondResult.Type,
            (IrExpression)secondMove.Clone()));

        var breakBody = new Block(conditionBlock.StartOffset);
        breakBody.Add(new Break());
        loopBody.Add(new IfStatement(
            new LogicalNot(new Binary(
                BinaryKind.Or,
                isChecked: false,
                isUnsigned: false,
                new LoadLocal(firstResult.Index, firstResult.Type),
                new LoadLocal(secondResult.Index, secondResult.Type))),
            breakBody,
            elseArm: null));

        foreach (var block in blocks.Skip(1).Take(conditionIndex - 1))
            foreach (var statement in block.Children.ToList())
            {
                statement.Detach();
                if (statement is StoreStackSlot slot
                    && !work.Descendants.OfType<LoadStackSlot>().Any(load => load.Slot == slot.Slot))
                {
                    continue;
                }
                loopBody.Add(statement);
            }

        entryBranch.Detach();
        blocks[0].Add(new WhileLoop(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            loopBody));
        foreach (var block in blocks.Skip(1).ToList())
            block.Detach();

        return true;
    }

    public static bool TryReconstruct(IrFunction work, IrFunction kickoff, NewObject handoff, PassContext context, out BlockContainer body)
    {
        body = null!;

        var blocks = work.Body.Blocks;
        if (blocks.Count == 0)
            return false;

        // The dispatch reads the state into a local: `Vs = this.<>1__state;`.
        var stateStore = blocks[0].Children.OfType<StoreLocal>()
            .FirstOrDefault(s => s.Value is LoadField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" });
        if (stateStore is null)
            return false;
        var stateLocal = stateStore.Index;

        // The bool return-value local, returned by the terminal `return V;`. The EH
        // lowering routes every exit through it (a `leave` cannot carry a value).
        var returnLocal = -1;
        foreach (var block in blocks)
            if (block.Children.Count > 0 && block.Children[^1] is Return { Value: LoadLocal terminal })
                returnLocal = terminal.Index;
        if (returnLocal < 0)
            return false;

        // The hoisted enumerator field, assigned from a GetEnumerator() call. Its presence
        // (with a region) is the foreach-delegation signature; without it this is some other
        // shape and we decline so the dedicated matchers or acknowledgment own it.
        FieldRef? enumeratorField = null;
        foreach (var node in work.Descendants)
            if (node is StoreField { Instance: LoadArgument { Index: 0 }, Field: var f, Value: Call { Callee.Name: "GetEnumerator" } })
                enumeratorField = f;
        if (enumeratorField is null)
            return false;
        if (!TryGetEnumeratorDisposalFinally(work, enumeratorField, out var disposalFinally))
            return false;
        if (context.ImportMethodBody is null)
            return false;
        var disposalBody = context.ImportMethodBody(disposalFinally);
        if (disposalBody is null
            || !TryGetDisposalMethod(disposalBody, out var dispose)
            || UnsafeAwaitOperand.MethodRequiresUnsafe(
                dispose,
                work.UsesUpdatedMemorySafetyRules))
            return false;

        // field name -> (local index, type): the enumerator, plus any hoisted loop fields.
        var locals = new Dictionary<string, (int Index, TypeRef Type)>(StringComparer.Ordinal)
        {
            [enumeratorField.Name] = (work.AddLocal(enumeratorField.Type, null), enumeratorField.Type),
        };
        foreach (var node in work.Descendants)
        {
            var field = node switch
            {
                StoreField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                LoadField { Instance: LoadArgument { Index: 0 }, Field: var f } => f,
                _ => null,
            };
            if (field is null || !GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name) || locals.ContainsKey(field.Name))
                continue;
            locals[field.Name] = (work.AddLocal(field.Type, ExtractSourceName(field.Name)), field.Type);
        }

        // The dispatch is the maximal leading run of state-testing blocks, plus the
        // "no state matched" default — `return false;` flows through the return local as
        // `V = false; leave terminal;` here (a `leave` cannot carry the constant).
        var dispatchEnd = 0;
        while (dispatchEnd < blocks.Count && TestsState(blocks[dispatchEnd], stateLocal))
            dispatchEnd++;
        if (dispatchEnd == 0)
            return false;
        if (dispatchEnd < blocks.Count && IsDefaultExit(blocks[dispatchEnd], returnLocal))
            dispatchEnd++;
        if (dispatchEnd >= blocks.Count)
            return false;

        var receiver = FindCapturedReceiver(work, kickoff, handoff);
        StoreLocal? receiverStore = null;
        if (work.Descendants.Any(node =>
                node is LoadFieldAddress { Field.Name: "<>4__this" } address
                    && Equals(address.Field.DeclaringType, work.DeclaringType)
                || receiver is not null && node is StoreField store && Equals(store.Field, receiver.Field)))
            return false;

        // Keep the immutable receiver alias from the entry block. Other useful
        // initialization still belongs to an unsupported dispatch shape.
        foreach (var statement in blocks.Take(dispatchEnd).SelectMany(block => block.Children))
        {
            if (ReferenceEquals(statement, stateStore)
                || statement is ConditionalBranch or Branch or Leave
                || statement is StoreLocal { Value: Constant } marker && marker.Index == returnLocal)
                continue;
            if (receiver is not null
                && receiverStore is null
                && statement is StoreLocal alias
                && ReferenceEquals(alias.Parent, blocks[0])
                && alias.Index != stateLocal && alias.Index != returnLocal
                && Equals(alias.Type, receiver.Field.Type)
                && receiver.Matches(alias.Value)
                && work.DescendantsOutsideNestedFunctions
                    .Where(node => ReferenceOwnership.ReferencesOrBindsLocal(node, alias.Index))
                    .All(node => ReferenceEquals(node, alias) || node is LoadLocal))
            {
                receiverStore = alias;
                continue;
            }
            return false;
        }

        // Rebuild the surviving blocks: strip the scaffolding, sew the yields, drop the
        // disposal idiom, and remap the state-machine fields to the fresh locals/arguments.
        var container = new BlockContainer();
        for (var i = dispatchEnd; i < blocks.Count; i++)
        {
            // The fault handler (`Dispose(); endfinally`) is disposal scaffolding — drop it.
            if (blocks[i].Children.Count > 0 && blocks[i].Children[^1] is EndFinally)
                continue;

            var rebuilt = new Block(blocks[i].StartOffset);
            if (i == dispatchEnd && receiverStore is not null && receiver is not null)
                rebuilt.Add(new StoreLocal(receiverStore.Index, receiverStore.Type,
                    new LoadArgument(0, receiver.Target)));
            foreach (var statement in blocks[i].Children.ToList())
            {
                statement.Detach();
                switch (statement)
                {
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>1__state" }:
                        continue;  // a state advance / resume marker
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field.Name: "<>2__current" } yieldStore:
                        if (!TryRemap(yieldStore.Value, kickoff, locals, receiver, out var yielded))
                            return false;
                        rebuilt.Add(new YieldReturn(yielded));
                        continue;
                    case StoreField { Instance: LoadArgument { Index: 0 }, Field: var slotField } slotStore
                        when locals.TryGetValue(slotField.Name, out var slot):
                        // `<>7__wrap1 = null` is the disposal reset — drop it.
                        if (slotStore.Value is Constant { Value: null })
                            continue;
                        if (!TryRemap(slotStore.Value, kickoff, locals, receiver, out var stored))
                            return false;
                        rebuilt.Add(new StoreLocal(slot.Index, slotField.Type, stored));
                        continue;
                    case ExpressionStatement { Expression: Call { Callee.Name: var callee } }
                        when callee.StartsWith("<>m__Finally", StringComparison.Ordinal):
                        continue;  // the dispose-on-normal-completion call
                    case StoreLocal returnStore when returnStore.Index == returnLocal && returnStore.Value is Constant:
                        continue;  // the bool return-value marker
                    case Leave:
                    case Return:
                        continue;  // the leave-to-terminal and terminal `return V` markers
                    default:
                        if (!TryRemapInPlace(statement, kickoff, locals, receiver))
                            return false;
                        rebuilt.Add(statement);
                        continue;
                }
            }
            container.Add(rebuilt);
        }

        work.Regions = ImmutableArray<HandlerRegion>.Empty;
        work.ClearImportedExceptionFacts();
        work.Body.ReplaceWith(container);

        // Re-run the pipeline: the enumerator loop is reducible now, so the structurer
        // forms the `while (e.MoveNext())` loop with the yields riding along.
        IrPasses.Run(work, IrPasses.Default, context);

        // The normal pipeline may already have raised this enumerator when its
        // exact-named Current local survived. Otherwise use the single-use matcher.
        int enumeratorIndex = locals[enumeratorField.Name].Index;
        if (work.DescendantsOutsideNestedFunctions.Any(
                node => ReferenceOwnership.ReferencesOrBindsLocal(node, enumeratorIndex))
            && !RaiseForeach(work, dispose, context, enumeratorIndex))
            return false;

        // Validate: fully structured, keeps a yield, recovers the foreach, and leaves no
        // unspeakable state-machine field behind.
        if (work.Body.Descendants.Any(IsUnstructured))
            return false;
        if (!work.Descendants.OfType<YieldReturn>().Any())
            return false;
        if (!work.Descendants.OfType<ForeachStatement>().Any())
            return false;
        if (work.Descendants.Any(IsStateMachineField))
            return false;

        body = (BlockContainer)work.Body;
        foreach (var block in body.Blocks)
            foreach (var statement in block.Children)
                Reanchor(statement, handoff.SourceOffset);

        return true;
    }

    static bool IsStateMachineReceiver(IrFunction work, IrExpression? expression)
    {
        if (expression is LoadArgument { Index: 0 })
            return true;
        if (expression is not LoadStackSlot slot)
            return false;

        var stores = work.Descendants.OfType<StoreStackSlot>()
            .Where(store => store.Slot == slot.Slot)
            .ToList();
        if (stores is not [{ Value: LoadArgument { Index: 0 } }])
            return false;

        return work.Descendants.OfType<LoadStackSlot>()
            .Count(load => load.Slot == slot.Slot) == 1;
    }

    static bool TryGetNestedDelegationResources(
        IrFunction work,
        NewObject handoff,
        PassContext context,
        out List<DelegationResource> resources,
        out Dictionary<int, int> yieldRunningStates)
    {
        resources = [];
        yieldRunningStates = [];
        if (context.ImportMethodBody is null
            || !HasNestedDelegationFaultShell(work))
        {
            return false;
        }

        var acquisitions = work.Descendants.OfType<StoreField>()
            .Where(store => store is
            {
                Instance: LoadArgument { Index: 0 },
                Value: Call
                {
                    Callee:
                    {
                        Name: "GetEnumerator",
                        HasThis: true,
                        ReturnType: var returnType,
                    },
                    Arguments.Count: 1,
                } getEnumerator,
            } && Equals(store.Field.Type, returnType)
                && MemberIdentity.IsCoreLibraryType(
                    getEnumerator.Callee.DeclaringType,
                    "System.Collections.Generic",
                    "IEnumerable`1"))
            .ToList();
        if (acquisitions.Count != 2
            || acquisitions.Select(acquisition => acquisition.Field).Distinct().Count() != 2)
        {
            return false;
        }

        var helperCalls = work.Descendants.OfType<ExpressionStatement>()
            .Select(statement => statement.Expression)
            .OfType<Call>()
            .Where(call => call.Callee.Name.StartsWith("<>m__Finally", StringComparison.Ordinal))
            .ToList();
        if (helperCalls.Count != acquisitions.Count
            || helperCalls.Any(call => call.Arguments is not [LoadArgument { Index: 0 }]))
        {
            return false;
        }

        var helpersByField = new Dictionary<FieldRef, (MethodRef Finally, MethodRef Dispose, int RestoredState)>();
        foreach (var call in helperCalls)
        {
            var disposalBody = context.ImportMethodBody(call.Callee);
            if (disposalBody is null
                || !TryGetDisposalResource(disposalBody, out var field, out var dispose, out var restoredState)
                || UnsafeAwaitOperand.MethodRequiresUnsafe(dispose, work.UsesUpdatedMemorySafetyRules)
                || !helpersByField.TryAdd(field, (call.Callee, dispose, restoredState)))
            {
                return false;
            }
        }

        foreach (var acquisition in acquisitions)
        {
            if (!helpersByField.TryGetValue(acquisition.Field, out var helper)
                || !TryGetStateAfter(acquisition, out var activeState))
            {
                return false;
            }
            resources.Add(new DelegationResource(
                acquisition.Field,
                helper.Finally,
                helper.Dispose,
                helper.RestoredState,
                activeState));
        }
        var matchedResources = resources;
        if (!helperCalls.Select(call => call.Callee)
                .SequenceEqual(matchedResources.AsEnumerable().Reverse().Select(resource => resource.Finally))
            || matchedResources[0].RestoredState != -1
            || matchedResources[1].RestoredState != matchedResources[0].ActiveState)
        {
            return false;
        }

        foreach (var resource in matchedResources)
        {
            var call = FindSingleHelperCall(helperCalls, resource.Finally);
            if (call?.Parent is not ExpressionStatement statement
                || statement.Parent is not Block block)
            {
                return false;
            }
            var callIndex = block.Children.ToList().IndexOf(statement);
            if (callIndex < 0
                || callIndex + 1 >= block.Children.Count
                || block.Children[callIndex + 1] is not StoreField
                {
                    Instance: LoadArgument { Index: 0 },
                    Field: var resetField,
                    Value: Constant { Value: null },
                }
                || !Equals(resetField, resource.Field))
            {
                return false;
            }
        }

        var stateMachineDispose = context.ImportMethodBody(
            handoff.Constructor with { Name = "System.IDisposable.Dispose" });
        if (stateMachineDispose is null
            || !TryValidateNestedStateMachineDisposeRouting(
                stateMachineDispose,
                matchedResources,
                out yieldRunningStates))
        {
            return false;
        }

        var resetFields = stateMachineDispose.Descendants.OfType<StoreField>()
            .Where(store => store is
            {
                Instance: LoadArgument { Index: 0 },
                Value: Constant { Value: null },
            })
            .Select(store => store.Field)
            .ToList();
        if (resetFields.Distinct().Count() != resetFields.Count
            || matchedResources.Any(resource => resetFields.Count(field => Equals(field, resource.Field)) != 1)
            || resetFields.Any(field => !matchedResources.Any(resource => Equals(resource.Field, field))
                && !GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name)))
        {
            return false;
        }

        var allowedHelpers = matchedResources.Select(resource => resource.Finally).ToHashSet();
        if (stateMachineDispose.Descendants.OfType<Call>().Any(call => !allowedHelpers.Contains(call.Callee)))
            return false;
        if (stateMachineDispose.Descendants.OfType<StoreField>().Any(store =>
                store.Field.Name != "<>1__state"
                && store.Value is not Constant { Value: null }))
        {
            return false;
        }
        var disposeStateStores = stateMachineDispose.Descendants.OfType<StoreField>()
            .Where(store => store is
            {
                Instance: LoadArgument { Index: 0 },
                Field.Name: "<>1__state",
            })
            .ToList();
        if (disposeStateStores is not [{ Value: Constant { Value: -2 } }])
            return false;
        if (stateMachineDispose.Body.Blocks[^1].Children.Count != resetFields.Count + 2
            || stateMachineDispose.Body.Blocks[^1].Children[^1] is not Return { Value: null }
            || stateMachineDispose.Body.Blocks[^1].Children
                .Take(stateMachineDispose.Body.Blocks[^1].Children.Count - 1)
                .Any(child => child is not StoreField))
        {
            return false;
        }

        return true;
    }

    static bool HasNestedDelegationFaultShell(IrFunction work)
    {
        if (work.Regions is not
            [
                {
                    Kind: HandlerKind.Fault,
                    FilterOffset: -1,
                    CatchType: null,
                } region,
            ]
            || work.ExceptionInstructions?.Instructions is not { Length: > 0 } instructions
            || work.Body.Blocks is not [var first, ..])
        {
            return false;
        }

        int handlerIndex = work.Body.IndexOfOffset(region.HandlerOffset);
        if (handlerIndex < 1
            || handlerIndex != work.Body.Blocks.Count - 2
            || work.Body.Blocks[handlerIndex].Children is not
            [
                ExpressionStatement
                {
                    Expression: Call
                    {
                        Callee.Name: "System.IDisposable.Dispose",
                        Arguments: [LoadArgument { Index: 0 }],
                    },
                },
                EndFinally,
            ])
        {
            return false;
        }

        var handler = work.Body.Blocks[handlerIndex];
        long tryEnd = (long)region.TryOffset + region.TryLength;
        long handlerEnd = (long)region.HandlerOffset + region.HandlerLength;
        if (region.TryLength <= 0
            || region.HandlerLength <= 0
            || region.TryOffset != first.StartOffset
            || tryEnd != region.HandlerOffset
            || region.HandlerOffset != handler.StartOffset
            || handlerEnd != work.Body.Blocks[handlerIndex + 1].StartOffset
            || handlerEnd >= instructions[^1].NextOffset)
        {
            return false;
        }

        return work.Body.Blocks
            .Take(handlerIndex)
            .All(block => block.StartOffset >= region.TryOffset
                && block.StartOffset < tryEnd);
    }

    static Call? FindSingleHelperCall(IEnumerable<Call> calls, MethodRef helper)
    {
        Call? match = null;
        foreach (var call in calls)
        {
            if (!Equals(call.Callee, helper))
                continue;
            if (match is not null)
                return null;
            match = call;
        }
        return match;
    }

    static bool TryValidateNestedStateMachineDisposeRouting(
        IrFunction dispose,
        IReadOnlyList<DelegationResource> resources,
        out Dictionary<int, int> yieldRunningStates)
    {
        yieldRunningStates = [];
        if (resources.Count != 2
            || dispose.Regions.Length != 2
            || dispose.Regions.Any(region => region.Kind != HandlerKind.Finally)
            || dispose.Body.Blocks is not
            [
                var entryActiveRange,
                var entryYieldRange,
                var outerEntry,
                var innerStateTest,
                var innerYieldTest,
                var outerLeave,
                var innerEntry,
                var innerLeave,
                var innerHandler,
                var outerHandler,
                var terminal,
            ]
            || outerEntry.Children.Count != 0
            || innerEntry.Children.Count != 0)
        {
            return false;
        }

        if (entryActiveRange.Children is not
            [
                StoreLocal
            {
                Index: var stateLocal,
                Value: LoadField
                {
                    Instance: LoadArgument { Index: 0 },
                    Field.Name: "<>1__state",
                },
            },
                ConditionalBranch activeRangeBranch,
            ]
            || !TryGetAdjacentStateRange(
                activeRangeBranch.Condition,
                stateLocal,
                out var innerActiveState)
            || activeRangeBranch.TargetOffset != outerEntry.StartOffset)
        {
            return false;
        }
        int outerActiveState = innerActiveState + 1;
        if (resources[0].ActiveState != outerActiveState
            || resources[1].ActiveState != innerActiveState)
        {
            return false;
        }

        if (entryYieldRange.Children is not [ConditionalBranch yieldRangeBranch]
            || !TryGetAdjacentPositiveStateRange(
                yieldRangeBranch.Condition,
                stateLocal,
                out var innerYieldState)
            || yieldRangeBranch.TargetOffset != terminal.StartOffset)
        {
            return false;
        }
        int outerYieldState = innerYieldState + 1;
        if (innerYieldState <= 0)
            return false;
        yieldRunningStates.Add(innerYieldState, innerActiveState);
        yieldRunningStates.Add(outerYieldState, outerActiveState);

        if (innerStateTest.Children is not [ConditionalBranch innerStateBranch]
            || !IsStateComparison(
                innerStateBranch.Condition,
                stateLocal,
                ComparisonKind.Equal,
                innerActiveState)
            || innerStateBranch.TargetOffset != innerEntry.StartOffset
            || innerYieldTest.Children is not [ConditionalBranch innerYieldBranch]
            || !IsStateComparison(
                innerYieldBranch.Condition,
                stateLocal,
                ComparisonKind.Equal,
                innerYieldState)
            || innerYieldBranch.TargetOffset != innerEntry.StartOffset
            || outerLeave.Children is not [Leave { TargetOffset: var outerTarget }]
            || innerLeave.Children is not [Leave { TargetOffset: var innerTarget }]
            || outerTarget != terminal.StartOffset
            || innerTarget != terminal.StartOffset)
        {
            return false;
        }

        if (innerHandler.Children is not
            [
                ExpressionStatement
            {
                Expression: Call
                {
                    Callee: var innerFinally,
                    Arguments: [LoadArgument { Index: 0 }],
                },
            },
                EndFinally,
            ]
            || outerHandler.Children is not
            [
                ExpressionStatement
            {
                Expression: Call
                {
                    Callee: var outerFinally,
                    Arguments: [LoadArgument { Index: 0 }],
                },
            },
                EndFinally,
            ]
            || !Equals(innerFinally, resources[1].Finally)
            || !Equals(outerFinally, resources[0].Finally))
        {
            return false;
        }

        var innerRegion = dispose.Regions.SingleOrDefault(
            region => region.HandlerOffset == innerHandler.StartOffset);
        var outerRegion = dispose.Regions.SingleOrDefault(
            region => region.HandlerOffset == outerHandler.StartOffset);
        if (innerRegion is null
            || outerRegion is null
            || innerRegion.TryOffset != innerLeave.StartOffset
            || innerRegion.TryOffset + innerRegion.TryLength != innerHandler.StartOffset
            || outerRegion.TryOffset != innerStateTest.StartOffset
            || outerRegion.TryOffset + outerRegion.TryLength != outerHandler.StartOffset
            || outerRegion.TryOffset > innerRegion.TryOffset
            || outerRegion.TryOffset + outerRegion.TryLength
                < innerRegion.TryOffset + innerRegion.TryLength)
        {
            return false;
        }

        return true;
    }

    static bool TryGetAdjacentPositiveStateRange(
        IrExpression condition,
        int stateLocal,
        out int firstState)
    {
        firstState = 0;
        if (condition is not Comparison
            {
                Kind: ComparisonKind.GreaterThan,
                IsUnsigned: true,
                Left: Binary
                {
                    Kind: BinaryKind.Subtract,
                    IsChecked: false,
                    IsUnsigned: false,
                    Left: LoadLocal load,
                    Right: Constant { Value: int value },
                },
                Right: Constant { Value: 1 },
            }
            || load.Index != stateLocal)
        {
            return false;
        }

        firstState = value;
        return true;
    }

    static bool TryGetStateAfter(StoreField acquisition, out int state)
    {
        state = 0;
        if (acquisition.Parent is not Block block)
            return false;
        var index = block.Children.ToList().IndexOf(acquisition);
        if (index < 0
            || index + 1 >= block.Children.Count
            || block.Children[index + 1] is not StoreField
            {
                Instance: LoadArgument { Index: 0 },
                Field.Name: "<>1__state",
                Value: Constant { Value: int value },
            })
        {
            return false;
        }
        state = value;
        return true;
    }

    static bool TryGetUsingDisposalResources(
        IrFunction work,
        NewObject handoff,
        PassContext context,
        out List<DisposalResource> resources,
        out int disposeYieldState)
    {
        resources = [];
        disposeYieldState = 0;
        if (context.ImportMethodBody is null
            || work.Regions is not [{ Kind: HandlerKind.Fault }]
            || work.Body.Blocks.Count(block => block.Children is
            [
                ExpressionStatement
                {
                    Expression: Call
                    {
                        Callee.Name: "System.IDisposable.Dispose",
                        Arguments: [LoadArgument { Index: 0 }],
                    },
                },
                EndFinally,
            ]) != 1)
        {
            return false;
        }

        var acquisitions = work.Descendants.OfType<StoreField>()
            .Where(store => store is
            {
                Instance: LoadArgument { Index: 0 },
                Value: Call
                {
                    Callee:
                    {
                        Name: "GetEnumerator",
                        HasThis: true,
                        ReturnType: var returnType,
                    },
                    Arguments.Count: 1,
                } getEnumerator,
            } && Equals(store.Field.Type, returnType)
                && MemberIdentity.IsCoreLibraryType(
                    getEnumerator.Callee.DeclaringType,
                    "System.Collections.Generic",
                    "IEnumerable`1"))
            .ToList();
        if (acquisitions.Count != 2
            || acquisitions.Select(acquisition => acquisition.Field).Distinct().Count() != acquisitions.Count)
        {
            return false;
        }

        var normalFinallyCalls = work.Descendants.OfType<ExpressionStatement>()
            .Select(statement => statement.Expression)
            .OfType<Call>()
            .Where(call => call.Callee.Name.StartsWith("<>m__Finally", StringComparison.Ordinal))
            .ToList();
        if (normalFinallyCalls.Count != acquisitions.Count
            || normalFinallyCalls.Any(call => call.Arguments is not [LoadArgument { Index: 0 }]))
        {
            return false;
        }

        foreach (var call in normalFinallyCalls)
        {
            var disposalBody = context.ImportMethodBody(call.Callee);
            if (disposalBody is null
                || !TryGetDisposalResource(disposalBody, out var field, out var dispose, out var restoredState)
                || UnsafeAwaitOperand.MethodRequiresUnsafe(dispose, work.UsesUpdatedMemorySafetyRules))
            {
                return false;
            }
            resources.Add(new DisposalResource(field, call.Callee, dispose, restoredState));
        }

        resources.Reverse();
        var matchedResources = resources;
        for (var i = 0; i < matchedResources.Count; i++)
        {
            if (!Equals(matchedResources[i].Field, acquisitions[i].Field)
                || !TryGetStateBefore(acquisitions[i], out var state)
                || matchedResources[i].RestoredState != state)
            {
                return false;
            }
        }

        var stateMachineDispose = context.ImportMethodBody(
            handoff.Constructor with { Name = "System.IDisposable.Dispose" });
        if (stateMachineDispose is null
            || stateMachineDispose.Regions.Length != matchedResources.Count
            || stateMachineDispose.Regions.Any(region => region.Kind != HandlerKind.Finally))
        {
            return false;
        }

        var disposeFinallyCalls = stateMachineDispose.Descendants.OfType<Call>()
            .Where(call => call.Callee.Name.StartsWith("<>m__Finally", StringComparison.Ordinal))
            .ToList();
        if (disposeFinallyCalls.Any(call => call.Arguments is not [LoadArgument { Index: 0 }])
            || !disposeFinallyCalls.Select(call => call.Callee)
                .SequenceEqual(matchedResources.AsEnumerable().Reverse().Select(resource => resource.Finally)))
        {
            return false;
        }
        if (stateMachineDispose.Regions.Any(region =>
                disposeFinallyCalls.Count(call => call.SourceOffset >= region.HandlerOffset
                    && call.SourceOffset < region.HandlerOffset + region.HandlerLength) != 1)
            || disposeFinallyCalls.Any(call =>
                stateMachineDispose.Regions.Count(region => call.SourceOffset >= region.HandlerOffset
                    && call.SourceOffset < region.HandlerOffset + region.HandlerLength) != 1))
        {
            return false;
        }
        if (!TryValidateStateMachineDisposeRouting(
                stateMachineDispose,
                matchedResources,
                out disposeYieldState))
        {
            return false;
        }

        var resetFields = stateMachineDispose.Descendants.OfType<StoreField>()
            .Where(store => store is
            {
                Instance: LoadArgument { Index: 0 },
                Value: Constant { Value: null },
            })
            .Select(store => store.Field)
            .ToList();
        if (resetFields.Count != matchedResources.Count
            || matchedResources.Any(resource => resetFields.Count(field => Equals(field, resource.Field)) != 1))
        {
            return false;
        }

        var allowedHelpers = matchedResources.Select(resource => resource.Finally).ToHashSet();
        if (stateMachineDispose.Descendants.OfType<Call>().Any(call => !allowedHelpers.Contains(call.Callee)))
            return false;
        if (stateMachineDispose.Descendants.OfType<StoreField>().Any(store =>
                store.Field.Name != "<>1__state"
                && !matchedResources.Any(resource => Equals(resource.Field, store.Field)
                    && store.Value is Constant { Value: null })))
        {
            return false;
        }
        var disposeStateStores = stateMachineDispose.Descendants.OfType<StoreField>()
            .Where(store => store is
            {
                Instance: LoadArgument { Index: 0 },
                Field.Name: "<>1__state",
            })
            .ToList();
        if (disposeStateStores is not
            [
                {
                    Value: Constant { Value: -2 },
                },
            ])
        {
            return false;
        }

        return true;
    }

    static bool TryValidateStateMachineDisposeRouting(
        IrFunction dispose,
        IReadOnlyList<DisposalResource> resources,
        out int yieldState)
    {
        yieldState = 0;
        if (resources.Count != 2
            || dispose.Body.Blocks is not
            [
                var entryRange,
                var entryYield,
                var outerEntry,
                var innerStateTest,
                var yieldStateTest,
                var outerLeave,
                var innerEntry,
                var innerLeave,
                var innerHandler,
                var outerHandler,
                var terminal,
            ]
            || outerEntry.Children.Count != 0
            || innerEntry.Children.Count != 0)
        {
            return false;
        }

        if (entryRange.Children is not
            [
                StoreLocal
                {
                    Index: var stateLocal,
                    Value: LoadField
                    {
                        Instance: LoadArgument { Index: 0 },
                        Field.Name: "<>1__state",
                    },
                },
                ConditionalBranch rangeBranch,
            ]
            || !TryGetAdjacentStateRange(
                rangeBranch.Condition,
                stateLocal,
                out var innerActiveState)
            || innerActiveState + 1 != resources[1].RestoredState
            || rangeBranch.TargetOffset != outerEntry.StartOffset)
        {
            return false;
        }

        if (entryYield.Children is not [ConditionalBranch entryYieldBranch]
            || !TryGetStateComparison(
                entryYieldBranch.Condition,
                stateLocal,
                ComparisonKind.NotEqual,
                out yieldState)
            || yieldState <= 0
            || entryYieldBranch.TargetOffset != terminal.StartOffset
            || innerStateTest.Children is not [ConditionalBranch innerStateBranch]
            || !IsStateComparison(
                innerStateBranch.Condition,
                stateLocal,
                ComparisonKind.Equal,
                innerActiveState)
            || innerStateBranch.TargetOffset != innerEntry.StartOffset
            || yieldStateTest.Children is not [ConditionalBranch yieldStateBranch]
            || !IsStateComparison(
                yieldStateBranch.Condition,
                stateLocal,
                ComparisonKind.Equal,
                yieldState)
            || yieldStateBranch.TargetOffset != innerEntry.StartOffset
            || outerLeave.Children is not [Leave { TargetOffset: var outerTarget }]
            || innerLeave.Children is not [Leave { TargetOffset: var innerTarget }]
            || outerTarget != terminal.StartOffset
            || innerTarget != terminal.StartOffset)
        {
            return false;
        }

        if (innerHandler.Children is not
            [
                ExpressionStatement
                {
                    Expression: Call
                    {
                        Callee: var innerFinally,
                        Arguments: [LoadArgument { Index: 0 }],
                    },
                },
                EndFinally,
            ]
            || outerHandler.Children is not
            [
                ExpressionStatement
                {
                    Expression: Call
                    {
                        Callee: var outerFinally,
                        Arguments: [LoadArgument { Index: 0 }],
                    },
                },
                EndFinally,
            ]
            || !Equals(innerFinally, resources[1].Finally)
            || !Equals(outerFinally, resources[0].Finally))
        {
            return false;
        }

        var innerRegion = dispose.Regions.SingleOrDefault(
            region => region.HandlerOffset == innerHandler.StartOffset);
        var outerRegion = dispose.Regions.SingleOrDefault(
            region => region.HandlerOffset == outerHandler.StartOffset);
        if (innerRegion is null
            || outerRegion is null
            || innerRegion.TryOffset != innerLeave.StartOffset
            || innerRegion.TryOffset + innerRegion.TryLength != innerHandler.StartOffset
            || outerRegion.TryOffset != innerStateTest.StartOffset
            || outerRegion.TryOffset + outerRegion.TryLength != outerHandler.StartOffset
            || outerRegion.TryOffset > innerRegion.TryOffset
            || outerRegion.TryOffset + outerRegion.TryLength
                < innerRegion.TryOffset + innerRegion.TryLength)
        {
            return false;
        }

        return true;
    }

    static bool TryGetAdjacentStateRange(
        IrExpression condition,
        int stateLocal,
        out int firstState)
    {
        firstState = 0;
        if (condition is not Comparison
            {
                Kind: ComparisonKind.LessThanOrEqual,
                IsUnsigned: true,
                Left: Binary
                {
                    Kind: BinaryKind.Subtract,
                    IsChecked: false,
                    IsUnsigned: false,
                    Left: LoadLocal load,
                    Right: Constant { Value: int value },
                },
                Right: Constant { Value: 1 },
            }
            || load.Index != stateLocal)
        {
            return false;
        }

        firstState = value;
        return true;
    }

    static bool TryGetStateComparison(
        IrExpression condition,
        int stateLocal,
        ComparisonKind kind,
        out int state)
    {
        state = 0;
        if (condition is not Comparison
            {
                Kind: var actualKind,
                IsUnsigned: false,
                Left: LoadLocal load,
                Right: Constant { Value: int value },
            }
            || actualKind != kind
            || load.Index != stateLocal)
        {
            return false;
        }

        state = value;
        return true;
    }

    static bool IsStateComparison(
        IrExpression condition,
        int stateLocal,
        ComparisonKind kind,
        int state)
        => TryGetStateComparison(condition, stateLocal, kind, out var actualState)
            && actualState == state;

    static bool TryGetDisposalResource(
        IrFunction disposalBody,
        out FieldRef field,
        out MethodRef dispose,
        out int restoredState)
    {
        field = null!;
        dispose = null!;
        restoredState = 0;

        if (disposalBody.Body.Blocks is not
            [
                {
                    Children:
                    [
                        StoreField
                        {
                            Instance: LoadArgument { Index: 0 },
                            Field.Name: "<>1__state",
                            Value: Constant { Value: int state },
                        },
                        ConditionalBranch
                        {
                            Condition: LogicalNot
                            {
                                Operand: LoadField
                                {
                                    Instance: LoadArgument { Index: 0 },
                                    Field: var guardedField,
                                },
                            },
                        } guard,
                    ],
                },
                {
                    Children:
                    [
                        ExpressionStatement
                        {
                            Expression: Call
                            {
                                Callee.Name: "Dispose",
                                Arguments:
                                [
                                    LoadField
                                    {
                                        Instance: LoadArgument { Index: 0 },
                                        Field: var disposedField,
                                    },
                                ],
                            } call,
                        },
                    ],
                },
                {
                    StartOffset: var terminalOffset,
                    Children: [Return { Value: null }],
                },
            ])
        {
            return false;
        }

        if (guard.TargetOffset != terminalOffset
            || !Equals(guardedField, disposedField)
            || !MemberIdentity.IsIDisposableDispose(call))
            return false;

        field = disposedField;
        dispose = call.Callee;
        restoredState = state;
        return true;
    }

    static bool IsMoveNextCall(Call call)
        => call.Callee is
        {
            Name: "MoveNext",
            HasThis: true,
            ParameterTypes.IsEmpty: true,
            ReturnType: var returnType,
        } && MemberIdentity.IsCoreLibraryType(returnType, "System", "Boolean");

    static bool TryGetStateBefore(StoreField acquisition, out int state)
    {
        state = 0;
        if (acquisition.Parent is not Block block)
            return false;
        var index = -1;
        for (var i = 0; i < block.Children.Count; i++)
            if (ReferenceEquals(block.Children[i], acquisition))
            {
                index = i;
                break;
            }
        if (index < 0)
            return false;
        for (var i = index - 1; i >= 0; i--)
            if (block.Children[i] is StoreField
                {
                    Instance: LoadArgument { Index: 0 },
                    Field.Name: "<>1__state",
                    Value: Constant { Value: int value },
                })
            {
                state = value;
                return true;
            }
        return false;
    }

    static bool RaiseNestedUsingResources(
        IrFunction work,
        IReadOnlyList<DisposalResource> resources,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals)
    {
        if (resources.Count != 2)
            return false;
        if (work.Body.Children is not [Block block])
            return false;

        var stores = new List<StoreLocal>();
        foreach (var resource in resources)
        {
            var local = locals[resource.Field.Name];
            var candidates = work.DescendantsOutsideNestedFunctions.OfType<StoreLocal>()
                .Where(store => store.Index == local.Index)
                .ToList();
            if (candidates is not
                [
                    {
                        Parent: Block parent,
                        Value: Call { Callee.Name: "GetEnumerator" },
                    } store,
                ]
                || !ReferenceEquals(parent, block))
            {
                return false;
            }
            stores.Add(store);
        }

        if (block.Children.Count <= resources.Count
            || !ReferenceEquals(block.Children[0], stores[0])
            || !ReferenceEquals(block.Children[1], stores[1]))
        {
            return false;
        }

        var innerBody = new BlockContainer();
        var innerBlock = new Block(stores[1].SourceOffset);
        foreach (var statement in block.Children.Skip(2).ToList())
        {
            statement.Detach();
            innerBlock.Add(statement);
        }
        innerBody.Add(innerBlock);

        var innerResource = (IrExpression)stores[1].DetachChildren()[0];
        stores[1].Detach();
        var innerLocal = locals[resources[1].Field.Name];
        var innerUsing = new UsingStatement(
            innerLocal.Index,
            innerLocal.Type,
            innerResource,
            innerBody,
            consumedMemberRefs: [resources[1].Dispose]);

        var outerBody = new BlockContainer();
        var outerBlock = new Block(stores[0].SourceOffset);
        outerBlock.Add(innerUsing);
        outerBody.Add(outerBlock);

        var outerResource = (IrExpression)stores[0].DetachChildren()[0];
        stores[0].Detach();
        var outerLocal = locals[resources[0].Field.Name];
        block.Add(new UsingStatement(
            outerLocal.Index,
            outerLocal.Type,
            outerResource,
            outerBody,
            consumedMemberRefs: [resources[0].Dispose]));
        return true;
    }

    // Raises `e = collection.GetEnumerator(); while (e.MoveNext()) { [item = e.Current;] BODY }`
    // — e a compiler-hidden enumerator local — into `foreach (item in collection) BODY`. The
    // copy-propagated single-use form (where `item = e.Current` folded into the body) is
    // recovered by introducing a fresh loop variable for the surviving `e.Current` reads.
    static bool RaiseForeach(
        IrFunction work,
        MethodRef dispose,
        PassContext context,
        int enumeratorIndex)
    {
        foreach (var block in work.Body.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (var i = 0; i + 1 < children.Count; i++)
            {
                if (children[i] is not StoreLocal enumeratorStore
                    || enumeratorStore.Index != enumeratorIndex
                    || enumeratorStore.Value is not Call { Callee.Name: "GetEnumerator" } getEnumerator
                    || getEnumerator.Arguments.Count != 1
                    || children[i + 1] is not WhileLoop loop
                    || loop.Condition is not Call { Callee.Name: "MoveNext" } moveNext
                    || moveNext.Arguments is not [LoadLocal conditionReceiver]
                    || conditionReceiver.Index != enumeratorStore.Index)
                {
                    continue;
                }

                var loopBody = loop.Body;

                int loopVariable;
                TypeRef elementType;
                MethodRef currentAccessor;
                if (loopBody.Children.Count > 0
                    && loopBody.Children[0] is StoreLocal currentStore
                    && currentStore.Value is LoadProperty current
                    && IsCurrentOf(current, enumeratorIndex))
                {
                    // `item = e.Current` survived (multi-use) — adopt it as the loop variable.
                    loopVariable = currentStore.Index;
                    elementType = currentStore.Type;
                    currentAccessor = current.Accessor;
                    currentStore.Detach();
                }
                else
                {
                    // Single-use: `e.Current` folded into the body — reintroduce a loop variable.
                    var read = loopBody.Descendants.OfType<LoadProperty>()
                        .FirstOrDefault(p => IsCurrentOf(p, enumeratorIndex));
                    if (read is null)
                        return false;
                    elementType = read.ResultType!;
                    currentAccessor = read.Accessor;
                    loopVariable = work.AddSynthesizedLocal(elementType, "item");
                    foreach (var currentProperty in loopBody.Descendants.OfType<LoadProperty>().ToList())
                        if (IsCurrentOf(currentProperty, enumeratorIndex))
                            currentProperty.ReplaceWith(new LoadLocal(loopVariable, elementType));
                }

                // The enumerator local must not leak past its Current/MoveNext uses.
                if (loopBody.Descendants.Any(n => n is LoadLocal load && load.Index == enumeratorIndex))
                    return false;

                var collection = getEnumerator.Arguments[0];
                collection.Detach();
                loopBody.Detach();
                var foreachStatement = new ForeachStatement(
                    loopVariable,
                    elementType,
                    collection,
                    loopBody,
                    [getEnumerator.Callee, moveNext.Callee, currentAccessor, dispose]);
                context.Stepper.StepOver("raise hidden-enumerator loop to foreach", loop);
                loop.ReplaceWith(foreachStatement);
                enumeratorStore.Detach();
                return true;
            }
        }
        return false;
    }

    static bool IsCurrentOf(IrExpression expression, int enumeratorIndex)
        => expression is LoadProperty { PropertyName: "Current", Instance: LoadLocal receiver }
            && receiver.Index == enumeratorIndex;

    static bool TryGetEnumeratorDisposalFinally(
        IrFunction work,
        FieldRef enumeratorField,
        out MethodRef disposalFinally)
    {
        disposalFinally = null!;
        if (work.Regions is not [{ Kind: HandlerKind.Fault }])
            return false;

        var finallyCalls = work.Descendants.OfType<ExpressionStatement>()
            .Select(statement => statement.Expression)
            .OfType<Call>()
            .Where(call => call.Callee.Name.StartsWith("<>m__Finally", StringComparison.Ordinal))
            .ToList();
        if (finallyCalls.Count != 1)
            return false;

        var call = finallyCalls[0];
        var statement = (ExpressionStatement)call.Parent!;
        if (statement.Parent is not Block block
            || !block.Children.Any(child => child is StoreField
            {
                Instance: LoadArgument { Index: 0 },
                Field: var field,
                Value: Constant { Value: null },
            } && Equals(field, enumeratorField)))
        {
            return false;
        }
        disposalFinally = call.Callee;
        return true;
    }

    static bool TryGetDisposalMethod(IrFunction disposalBody, out MethodRef dispose)
    {
        dispose = null!;
        var calls = disposalBody.Descendants
            .OfType<Call>()
            .Where(call => call.Callee.Name == "Dispose")
            .ToList();
        if (calls is not [var call])
            return false;
        dispose = call.Callee;
        return true;
    }

    static bool IsDefaultExit(Block block, int returnLocal)
        => block.Children is [Return { Value: Constant }]
            or [Branch]
            || (block.Children is [StoreLocal { Value: Constant } store, Leave] && store.Index == returnLocal);

    static bool TestsState(Block block, int stateLocal)
        => block.Children.OfType<ConditionalBranch>().Any(branch =>
            branch.Condition.Descendants.Prepend(branch.Condition)
                .Any(node => node is LoadLocal load && load.Index == stateLocal));

    static bool IsUnstructured(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter or UnsupportedNode;

    static bool IsStateMachineField(IrNode node)
        => (node is LoadField { Field.Name: var loaded } && GeneratedCodeIdentity.IsGeneratedFieldName(loaded))
            || (node is StoreField { Field.Name: var stored } && GeneratedCodeIdentity.IsGeneratedFieldName(stored));

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(offset >= 0 ? offset : -1);
    }

    static bool TryRemap(IrExpression expression, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals,
        CapturedReceiver? receiver, out IrExpression result)
    {
        var clone = (IrExpression)expression.Clone();
        if (!TryRemapInPlace(clone, kickoff, locals, receiver, out var replacedRoot))
        {
            result = null!;
            return false;
        }
        result = (IrExpression)(replacedRoot ?? clone);
        return true;
    }

    static bool TryRemapInPlace(IrNode node, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals, CapturedReceiver? receiver)
        => TryRemapInPlace(node, kickoff, locals, receiver, out _);

    static bool TryRemapInPlace(IrNode node, IrFunction kickoff,
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> locals,
        CapturedReceiver? receiver, out IrNode? replacedRoot)
    {
        replacedRoot = null;
        var ok = true;
        var swaps = new List<(IrNode Old, IrNode New)>();
        Visit(node);
        if (!ok)
            return false;

        foreach (var (old, replacement) in swaps)
        {
            if (ReferenceEquals(old, node))
                replacedRoot = replacement;
            else
                old.ReplaceWith(replacement);
        }
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            switch (current)
            {
                case LoadField load when receiver is not null && receiver.Matches(load):
                    swaps.Add((current, new LoadArgument(0, receiver.Target)));
                    return;
                case LoadField { Instance: LoadArgument { Index: 0 }, Field: var field }:
                    if (locals.TryGetValue(field.Name, out var slot))
                        swaps.Add((current, new LoadLocal(slot.Index, field.Type)));
                    else if (TryGetParameter(kickoff, field.Name, out var index, out var parameter))
                        swaps.Add((current, new LoadArgument(index, parameter)));
                    else
                        ok = false;
                    return;  // never descend into a swapped field's `this` receiver
                case LoadField { Field.Name: var name } when GeneratedCodeIdentity.IsGeneratedFieldName(name):
                    ok = false;  // a state-machine field reached through some other path
                    return;
                case LoadArgument argument when receiver is not null
                    && ReferenceEquals(argument.Parameter, receiver.Source):
                    ok = false;
                    return;
                default:
                    foreach (var child in current.Children)
                        Visit(child);
                    return;
            }
        }
    }

    sealed record CapturedReceiver(FieldRef Field, Parameter Source, Parameter Target)
    {
        public bool Matches(IrNode node)
            => node is LoadField { Instance: LoadArgument { Index: 0 } instance, Field: var field }
                && ReferenceEquals(instance.Parameter, Source)
                && Equals(field, Field);
    }

    static CapturedReceiver? FindCapturedReceiver(IrFunction work, IrFunction kickoff, NewObject handoff)
    {
        if (kickoff.ReceiverParameter is not { } target
            || work.ReceiverParameter is not { } source
            || !Equals(work.DeclaringType, handoff.Constructor.DeclaringType)
            || handoff.Parent is not ObjectInitializerExpression { IsCollection: false } initializer)
            return null;

        var captures = initializer.Entries
            .Where(entry => entry.ConsumedField?.Name == "<>4__this").ToArray();
        if (captures is not [{ ConsumedField: { } field, Arguments: [LoadArgument value] }]
            || !Equals(field.DeclaringType, work.DeclaringType)
            || field.Type.DeclaredValueTypeHint != ValueTypeHint.ReferenceType
            || !Equals(field.Type, target.Type)
            || !Equals(value.Type, field.Type)
            || value.Index != 0
            || !ReferenceEquals(value.Parameter, target))
            return null;

        return new CapturedReceiver(field, source, target);
    }

    static bool TryGetParameter(IrFunction kickoff, string name, out int index, out Parameter parameter)
    {
        var parameters = kickoff.Signature.Parameters;
        var argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        for (var i = 0; i < parameters.Length; i++)
            if (parameters[i].Name == name)
            {
                index = argumentBase + i;
                parameter = parameters[i];
                return true;
            }

        index = -1;
        parameter = null!;
        return false;
    }

    static string ExtractSourceName(string fieldName)
    {
        var close = fieldName.IndexOf('>');
        return close > 1 ? fieldName[1..close] : "i";
    }
}
