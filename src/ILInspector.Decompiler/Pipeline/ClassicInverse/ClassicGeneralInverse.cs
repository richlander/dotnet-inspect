using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The general classic async inverse: recognizes the Roslyn classic lowering
/// protocol by its physical roles in the raw <c>MoveNext</c> import, removes
/// those roles, splices the continuations by evaluating every state test under
/// the running state <c>-1</c>, rebinds machine storage to the kickoff's
/// parameters, <c>this</c>, and fresh locals, hands the body to the ordinary
/// pass pipeline, and validates the result after the pipeline instead of
/// matching the user code before it.
/// <para>Owning design: <c>docs/design/classic-async-reconstruction.md</c>,
/// section "General inverse".</para>
/// </summary>
internal static class ClassicGeneralInverse
{
    internal abstract record Outcome
    {
        internal sealed record Reconstructed(IrFunction Body, int AwaitCount) : Outcome;

        internal sealed record Declined(string Reason, IrFunction? Body = null) : Outcome;
    }

    /// <summary>
    /// Attempts the general inverse over a fresh clone of the unmodified
    /// execution import. <paramref name="runPasses"/> runs a pass list over a
    /// body inside the host's cross-method pipeline scope.
    /// </summary>
    internal static Outcome TryReconstruct(
        IrFunction rawExecution,
        IrFunction kickoff,
        TypeRef machine,
        Action<IrFunction, ImmutableArray<IIrPass>> runPasses)
    {
        var work = (IrFunction)rawExecution.Clone();
        ImmutableArray<IIrPass> all = IrPasses.ForReconstruction<ClassicAsyncReconstructionPass>();
        int ehIndex = all.IndexOf(all.First(static pass => pass is EhStructuringPass));
        ImmutableArray<IIrPass> prefix = all.Take(ehIndex + 1).ToImmutableArray();
        ImmutableArray<IIrPass> rest = all.Skip(ehIndex + 1).ToImmutableArray();

        runPasses(work, prefix);
        if (!work.Regions.IsEmpty)
            return new Outcome.Declined("exception regions did not structure");

        var transform = new Transform(work, kickoff, machine);
        string? failure = transform.Run();
        if (failure is not null)
            return new Outcome.Declined(failure, work);
        // The spliced returns carry the async result, not MoveNext's void: the
        // later passes type a returned value (a short-circuit's constants) from
        // the function's return type.
        if (transform.ResultType is { } resultType)
            work.Signature = work.Signature with { ReturnType = resultType };
        runPasses(work, rest);

        if (work.Body.Descendants.FirstOrDefault(IsUnstructured) is { } transfer)
            return new Outcome.Declined($"a transfer survived the pipeline ({transfer.Describe()} in {transfer.Parent?.Describe()})", work);
        if (work.Descendants.Any(node => IsMachineFieldAccess(node, machine)))
            return new Outcome.Declined("a state-machine field survived the pipeline", work);
        if (work.Descendants.OfType<AwaitExpression>().FirstOrDefault(static node => node.Operand.ResultType?.Kind == TypeRefKind.ByRef) is { } byRef)
            return new Outcome.Declined($"an await operand is an address ({byRef.Operand.Describe()})", work);
        if (work.Descendants.OfType<Throw>().FirstOrDefault(static thrown => IsObjectHolder(thrown.Value)) is { } untyped)
            return new Outcome.Declined($"a throw of an object-typed holder survived ({untyped.Value.Describe()}): an await inside a catch that rethrows keeps the pending exception in an object local", work);
        int awaits = work.Descendants.OfType<AwaitExpression>().Count();
        if (awaits != transform.SuspensionCount)
            return new Outcome.Declined($"{awaits} awaits survived for {transform.SuspensionCount} proven suspensions");
        return new Outcome.Reconstructed(work, awaits);
    }

    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");

    // The await-in-catch rethrow throws the pending exception from the local or
    // field that holds it as object, which C# cannot spell. A user `throw;`
    // (CaughtException), `throw null;`, and a boxed generic exception are
    // ordinary throws the pipeline spells.
    static bool IsObjectHolder(IrExpression value)
        => value is LoadLocal or LoadField && ObjectType.Equals(value.ResultType);

    static bool IsUnstructured(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter or UnsupportedNode;

    static bool IsMachineFieldAccess(IrNode node, TypeRef machine)
        => node switch
        {
            LoadField load => ClassicInverseNodeFacts.IsMachineField(load.Field, machine),
            StoreField store => ClassicInverseNodeFacts.IsMachineField(store.Field, machine),
            LoadFieldAddress address => ClassicInverseNodeFacts.IsMachineField(address.Field, machine),
            _ => false,
        };

    sealed class Transform(IrFunction work, IrFunction kickoff, TypeRef machine)
    {
        const string StateField = StateMachineStorage.StateField;
        const string BuilderField = "<>t__builder";

        readonly IrFunction _work = work;
        readonly TypeRef _machine = machine;
        readonly StateMachineStorage _storage = new(work, kickoff, machine);
        IReadOnlyDictionary<string, (int Index, TypeRef Type)> _hoisted => _storage.HoistedLocals;
        HashSet<int> _removedLocals => _storage.RemovedLocals;
        int _stateLocal => _storage.StateLocal;
        int? _resultLocal;
        TypeRef? _builder;

        internal int SuspensionCount { get; private set; }

        internal string? Run()
        {
            return _storage.ReadEntry()
                ?? ReadBuilder()
                ?? DeclineUnspellableHandlers()
                ?? RemoveCompletion()
                ?? FoldDispatch()
                ?? SpliceSuspensions()
                ?? _storage.RemoveDeadClears()
                ?? _storage.Rebind()
                ?? FoldOperandSpills()
                ?? ScratchAwaitResults();
        }

        // The builder is whatever type the machine's <>t__builder field names:
        // Task, ValueTask, void, or a custom [AsyncMethodBuilder] builder.
        string? ReadBuilder()
        {
            FieldRef? builder = _work.Descendants
                .Select(static node => node switch
                {
                    LoadFieldAddress { Field: var field } => field,
                    LoadField { Field: var field } => field,
                    _ => null,
                })
                .FirstOrDefault(field => field is { Name: BuilderField } && ClassicInverseNodeFacts.IsMachineField(field, _machine));
            if (builder is null)
                return "the execution body never touches the builder";
            _builder = ClassicInverseNodeFacts.Definition(builder.Type);
            return null;
        }

        bool IsBuilderCall(Call call)
            => _builder is not null
                && ClassicInverseNodeFacts.Definition(call.Callee.DeclaringType).Equals(_builder);

        // The await-in-finally lowering catches `object` into a pending-exception
        // holder it later rethrows; C# cannot spell a used `catch (object e)`.
        string? DeclineUnspellableHandlers()
        {
            foreach (CatchClause clause in _work.Body.Descendants.OfType<CatchClause>())
            {
                if (MemberIdentity.IsCoreLibraryType(clause.ExceptionType, "System", "Object")
                    && clause.VariableIndex is { } variable
                    && clause.Body.Descendants.Any(node => node is LoadLocal load && load.Index == variable))
                {
                    return "an await inside a finally lowers to a used catch (object) handler that C# cannot spell";
                }
            }
            return null;
        }

        // The outer try/catch with the completion catch, followed by the completion tail.
        string? RemoveCompletion()
        {
            TryCatch? outer = null;
            foreach (Block block in _work.Body.Blocks)
            {
                foreach (IrNode statement in block.Children)
                {
                    if (statement is TryCatch tryCatch)
                    {
                        if (outer is not null)
                            return "more than one top-level try/catch";
                        outer = tryCatch;
                    }
                }
            }
            if (outer is null)
                return "no completion try/catch";
            if (outer.Clauses is not [CatchClause clause] || clause.Filter is not null
                || !MemberIdentity.IsCoreLibraryType(clause.ExceptionType, "System", "Exception"))
                return "the completion catch is not a single unfiltered catch (Exception)";

            // The completion catch body: state = -2; clears; SetException(ex); return.
            if (clause.Body.Blocks is not [Block catchBlock])
                return "the completion catch body is not one block";
            string? catchFailure = CheckCompletionStatements(catchBlock.Children, "SetException", expectResult: false, out _);
            if (catchFailure is not null)
                return "completion catch: " + catchFailure;

            // The completion tail follows the try/catch in its block, then any trailing blocks.
            var host = (Block)outer.Parent!;
            int position = host.Children.ToList().IndexOf(outer);
            List<IrNode> tail = [.. host.Children.Skip(position + 1)];
            int hostIndex = _work.Body.Blocks.ToList().IndexOf(host);
            List<Block> trailing = [.. _work.Body.Blocks.Skip(hostIndex + 1)];
            foreach (Block block in trailing)
                tail.AddRange(block.Children);
            string? tailFailure = CheckCompletionStatements(tail, "SetResult", expectResult: true, out _resultLocal);
            if (tailFailure is not null)
                return "completion tail: " + tailFailure;
            if (hostIndex != _work.Body.Blocks.Count - 1 - trailing.Count || position != host.Children.Count - 1 - (tail.Count - trailing.Sum(b => b.Children.Count)))
                return "the completion tail is not at the end of the body";

            // Every leave out of the try body is a return; a fallthrough off its end is one too.
            var tryBlocks = outer.TryBody.Blocks.ToList();
            var innerOffsets = outer.TryBody.Descendants.OfType<Block>().Select(static block => block.StartOffset).ToHashSet();
            foreach (Leave leave in outer.TryBody.Descendants.OfType<Leave>().ToList())
            {
                if (innerOffsets.Contains(leave.TargetOffset))
                    continue;
                var ret = new Return(ResultValue());
                ret.InheritSourceOffset(leave);
                leave.ReplaceWith(ret);
            }
            if (tryBlocks.Count == 0)
                return "the completion try body is empty";
            Block last = tryBlocks[^1];
            if (FallsThrough(last))
                last.Add(new Return(ResultValue()));

            // Hoist the try body: the entry statements stay first, the tail goes.
            foreach (IrNode statement in tail)
                statement.Detach();
            foreach (Block block in trailing)
                block.Detach();
            outer.Detach();
            var container = new BlockContainer();
            if (host.Children.Count > 0)
            {
                // Statements before the try (none in Roslyn output) keep their block.
                host.Detach();
                container.Add(host);
            }
            else
            {
                host.Detach();
            }
            foreach (Block block in _work.Body.Blocks.ToList())
            {
                block.Detach();
                container.Add(block);
            }
            foreach (Block block in tryBlocks)
            {
                block.Detach();
                container.Add(block);
            }
            _work.Body.ReplaceWith(container);
            return null;
        }

        internal TypeRef? ResultType
            => _resultLocal is { } result ? _work.Locals[result] : null;

        IrExpression? ResultValue()
            => _resultLocal is { } result ? new LoadLocal(result, _work.Locals[result]) : null;

        static bool FallsThrough(Block block)
            => block.Children.Count == 0
                || block.Children[^1] is not (Return or Branch or Leave or Throw or SwitchBranch or EndFinally);

        // state = -2; [hoisted clears]; builder.<callback>([result]); [return]
        string? CheckCompletionStatements(IReadOnlyList<IrNode> statements, string callback, bool expectResult, out int? resultLocal)
        {
            resultLocal = null;
            int i = 0;
            if (statements.Count == 0 || !IsStateStore(statements[0], -2))
                return "does not begin with the -2 state store";
            i++;
            while (i < statements.Count && IsHoistedClear(statements[i]))
                i++;
            if (i >= statements.Count
                || statements[i] is not ExpressionStatement { Expression: Call { Callee: { Name: var name, HasThis: true } } call }
                || name != callback
                || !IsBuilderCall(call))
                return $"does not call the builder's {callback}";
            IrExpression[] arguments = [.. call.Arguments];
            if (arguments is not [LoadFieldAddress { Field.Name: BuilderField, Instance: LoadArgument { Index: 0 } }, .. var rest])
                return $"{callback} is not called on the machine's builder";
            if (callback == "SetException")
            {
                if (rest is not [LoadLocal])
                    return "SetException does not pass the caught exception local";
            }
            else if (expectResult)
            {
                switch (rest)
                {
                    case []:
                        break;
                    case [LoadLocal result]:
                        resultLocal = result.Index;
                        break;
                    default:
                        return "SetResult does not pass a result local";
                }
            }
            i++;
            if (i < statements.Count && statements[i] is Return { Value: null } or Leave)
                i++;
            return i == statements.Count ? null : "carries statements beyond the protocol";
        }

        bool IsStateStore(IrNode node, int value)
            => node is StoreField { Field.Name: StateField, Instance: LoadArgument { Index: 0 }, Value: Constant { Value: int stored } }
                && stored == value;

        bool IsHoistedClear(IrNode node)
            => node switch
            {
                StoreField { Field: var field, Instance: LoadArgument { Index: 0 }, Value: Constant { Value: null } or DefaultValue }
                    => _storage.IsHoistedField(field),
                InitObject { Address: LoadFieldAddress { Field: var field, Instance: LoadArgument { Index: 0 } } }
                    => _storage.IsHoistedField(field),
                _ => false,
            };

        // Every branch whose condition depends only on V_state folds under V_state = -1.
        string? FoldDispatch()
        {
            foreach (IrNode node in _work.Body.Descendants.ToList())
            {
                switch (node)
                {
                    case ConditionalBranch branch when _storage.ReadsState(branch.Condition):
                        {
                            if (_storage.Evaluate(branch.Condition, -1) is not { } value)
                                return "a state test does not fold under the running state";
                            if (StateMachineStorage.IsTrue(value))
                            {
                                var jump = new Branch(branch.TargetOffset);
                                jump.InheritSourceOffset(branch);
                                branch.ReplaceWith(jump);
                            }
                            else
                            {
                                branch.Detach();
                            }
                            break;
                        }
                    case SwitchBranch dispatch when _storage.ReadsState(dispatch.Value):
                        {
                            if (_storage.Evaluate(dispatch.Value, -1) is not int selector)
                                return "a state switch does not fold under the running state";
                            if (selector >= 0 && selector < dispatch.TargetOffsets.Length)
                            {
                                var jump = new Branch(dispatch.TargetOffsets[selector]);
                                jump.InheritSourceOffset(dispatch);
                                dispatch.ReplaceWith(jump);
                            }
                            else
                            {
                                dispatch.Detach();
                            }
                            break;
                        }
                }
            }
            return null;
        }

        // Each suspension site collapses to the edge into its continuation.
        string? SpliceSuspensions()
        {
            foreach (BlockContainer container in _work.Body.Descendants.OfType<BlockContainer>().Prepend(_work.Body).ToList())
            {
                var blocks = container.Blocks.ToList();
                for (int i = 0; i < blocks.Count; i++)
                {
                    Block block = blocks[i];
                    if (block.Parent is null)
                        continue;
                    StoreLocal? bind = block.Children.OfType<StoreLocal>().FirstOrDefault(static store =>
                        store.Value is Call { Callee.Name: "GetAwaiter", Callee.HasThis: true });
                    if (bind is null)
                        continue;
                    string? failure = SpliceOne(container, bind);
                    if (failure is not null)
                        return failure;
                    blocks = container.Blocks.ToList();
                    i = Math.Max(-1, blocks.IndexOf(block) - 1);
                }
            }
            return null;
        }

        string? SpliceOne(BlockContainer container, StoreLocal bind)
        {
            var block = (Block)bind.Parent!;
            var getAwaiter = (Call)bind.Value;
            int awaiter = bind.Index;
            var statements = block.Children.ToList();
            int bindIndex = statements.IndexOf(bind);
            if (bindIndex + 1 >= statements.Count
                || statements[bindIndex + 1] is not ConditionalBranch completed
                || !IsCompletedTest(completed.Condition, awaiter)
                || bindIndex + 1 != statements.Count - 1)
                return "an awaiter bind is not followed by its IsCompleted test";

            var blocks = container.Blocks.ToList();
            int position = blocks.IndexOf(block);
            if (position + 3 > blocks.Count)
                return "a suspension site has no suspension, resume, and continuation blocks";
            Block suspension = blocks[position + 1];
            Block resume = blocks[position + 2];
            if (!IsSuspensionBlock(suspension, awaiter, out int state, out string? cacheField))
                return "a suspension block does not carry the protocol";
            if (!IsResumeBlock(resume, awaiter, cacheField!))
                return "a resume block does not carry the protocol";
            int continuationOffset = completed.TargetOffset;
            if (position + 3 >= blocks.Count || blocks[position + 3].StartOffset != continuationOffset)
                return "the continuation does not follow the resume block";
            Block continuation = blocks[position + 3];
            if (_work.Body.Descendants.Any(node => TargetsOffset(node, resume.StartOffset) || TargetsOffset(node, suspension.StartOffset)))
                return "a surviving transfer targets a suspension or resume block";

            // The awaited operand: the GetAwaiter receiver, with an adjacent struct spill folded.
            IrExpression operand = getAwaiter.Arguments[0];
            bool foldSpill = operand is LoadLocalAddress { Index: var spillIndex }
                && bindIndex > 0
                && statements[bindIndex - 1] is StoreLocal { Index: var spillStoreIndex }
                && spillStoreIndex == spillIndex
                && IsPureAwaitSpill(spillIndex);
            operand.Detach();
            if (foldSpill
                && operand is LoadLocalAddress { Index: var spill }
                && statements[bindIndex - 1] is StoreLocal spilled)
            {
                operand = spilled.Value;
                operand.Detach();
                spilled.Detach();
                _removedLocals.Add(spill);
            }
            else if (operand is LoadLocalAddress local)
            {
                operand = new LoadLocal(local.Index, local.Type);
            }
            else if (operand is LoadFieldAddress { Instance: LoadArgument { Index: 0 } receiver } hoisted
                && ClassicInverseNodeFacts.IsMachineField(hoisted.Field, _machine))
            {
                // A struct awaitable hoisted across an earlier await (a local or
                // a parameter) is awaited through its field's address; the await
                // takes the value, which RebindStorage then binds.
                receiver.Detach();
                operand = new LoadField(hoisted.Field, receiver);
            }

            // The continuation's GetResult(ref awaiter) becomes the await.
            Call? getResult = continuation.Descendants.OfType<Call>().FirstOrDefault(call =>
                call.Callee is { Name: "GetResult", HasThis: true }
                && call.Arguments.Count == 1
                && call.Arguments[0] is LoadLocalAddress { Index: var index }
                && index == awaiter);
            if (getResult is null)
                return "the continuation does not call GetResult on the awaiter";
            TypeRef resultType = getResult.Callee.ReturnType;
            MethodRef isCompleted = CompletedAccessor(completed.Condition)!;
            var await = new AwaitExpression(
                operand,
                resultType,
                getResult.Callee.ReturnIsDynamic,
                consumedMemberRefs: [getAwaiter.Callee, isCompleted, getResult.Callee],
                provesClassicCompletionPaths: true);
            await.InheritSourceOffset(getResult);
            getResult.ReplaceWith(await);

            bind.Detach();
            completed.Detach();
            suspension.Detach();
            resume.Detach();
            // The site now falls through into its continuation. When nothing else
            // enters the continuation, the two are one straight-line block in the
            // source, which the statement-sequence raises (an interpolated string
            // whose ToStringAndClear feeds the await) need.
            if (!_work.Body.Descendants.Any(node => TargetsOffset(node, continuation.StartOffset)))
            {
                foreach (IrNode statement in continuation.Children.ToList())
                {
                    statement.Detach();
                    block.Add(statement);
                }
                continuation.Detach();
            }
            // Roslyn shares one awaiter local across same-typed awaits; the
            // no-surviving-use check runs once every site is spliced.
            _removedLocals.Add(awaiter);
            SuspensionCount++;
            return null;
        }

        static bool TargetsOffset(IrNode node, int offset)
            => node switch
            {
                Branch branch => branch.TargetOffset == offset,
                ConditionalBranch branch => branch.TargetOffset == offset,
                SwitchBranch dispatch => dispatch.TargetOffsets.Contains(offset),
                Leave leave => leave.TargetOffset == offset,
                _ => false,
            };

        static bool IsCompletedTest(IrExpression condition, int awaiter)
            => CompletedAccessor(condition) is not null
                && condition.Children is [LoadLocalAddress { Index: var index }, ..]
                && index == awaiter;

        static MethodRef? CompletedAccessor(IrExpression condition)
            => condition switch
            {
                Call { Callee: { Name: "get_IsCompleted", HasThis: true } callee } => callee,
                LoadProperty { Accessor: { Name: "get_IsCompleted", HasThis: true } accessor } => accessor,
                _ => null,
            };

        // [S = N]; V_state = N; this.<>1__state = N; this.<>u__k = V_aw; builder.Await*OnCompleted(ref V_aw, ref this); return
        bool IsSuspensionBlock(Block block, int awaiter, out int state, out string? cacheField)
        {
            state = int.MinValue;
            cacheField = null;
            var statements = block.Children.ToList();
            int i = 0;
            int? slot = null;
            if (i < statements.Count && statements[i] is StoreStackSlot { Value: Constant { Value: int spilled } } spill)
            {
                slot = spill.Slot;
                state = spilled;
                i++;
            }
            if (i >= statements.Count || statements[i] is not StoreLocal { Index: var stateIndex } stateStore || stateIndex != _stateLocal
                || !ConstantOrSlot(stateStore.Value, slot, ref state))
                return false;
            i++;
            if (i >= statements.Count || statements[i] is not StoreField { Field.Name: StateField, Instance: LoadArgument { Index: 0 } } fieldStore
                || !ConstantOrSlot(fieldStore.Value, slot, ref state))
                return false;
            i++;
            if (i >= statements.Count || statements[i] is not StoreField { Field: var cache, Instance: LoadArgument { Index: 0 }, Value: LoadLocal { Index: var cached } }
                || cached != awaiter || !cache.Name.StartsWith("<>u__", StringComparison.Ordinal) || !ClassicInverseNodeFacts.IsMachineField(cache, _machine))
                return false;
            cacheField = cache.Name;
            i++;
            if (i >= statements.Count
                || statements[i] is not ExpressionStatement { Expression: Call { Callee: { Name: "AwaitUnsafeOnCompleted" or "AwaitOnCompleted", HasThis: true } } call }
                || ((IrExpression[])[.. call.Arguments]) is not [LoadFieldAddress { Field.Name: BuilderField, Instance: LoadArgument { Index: 0 } }, LoadLocalAddress { Index: var callbackAwaiter }, LoadArgument { Index: 0 }]
                || callbackAwaiter != awaiter
                || !IsBuilderCall(call))
                return false;
            i++;
            if (i >= statements.Count || statements[i] is not (Return { Value: null } or Leave))
                return false;
            i++;
            return i == statements.Count && state >= 0;
        }

        static bool ConstantOrSlot(IrExpression value, int? slot, ref int state)
        {
            switch (value)
            {
                case Constant { Value: int constant }:
                    if (state != int.MinValue && state != constant)
                        return false;
                    state = constant;
                    return true;
                case LoadStackSlot load when slot is { } expected && load.Slot == expected:
                    return true;
                default:
                    return false;
            }
        }

        // V_aw = this.<>u__k; this.<>u__k = default; [S = -1]; V_state = -1; this.<>1__state = -1; falls into the continuation
        bool IsResumeBlock(Block block, int awaiter, string cacheField)
        {
            var statements = block.Children.ToList();
            int i = 0;
            if (i >= statements.Count || statements[i] is not StoreLocal { Index: var restored, Value: LoadField { Field.Name: var restoredField, Instance: LoadArgument { Index: 0 } } }
                || restored != awaiter || restoredField != cacheField)
                return false;
            i++;
            if (i >= statements.Count || !IsCacheClear(statements[i], cacheField))
                return false;
            i++;
            int state = int.MinValue;
            int? slot = null;
            if (i < statements.Count && statements[i] is StoreStackSlot { Value: Constant { Value: int spilled } } spill)
            {
                slot = spill.Slot;
                state = spilled;
                i++;
            }
            if (i >= statements.Count || statements[i] is not StoreLocal { Index: var stateIndex } stateStore || stateIndex != _stateLocal
                || !ConstantOrSlot(stateStore.Value, slot, ref state))
                return false;
            i++;
            if (i >= statements.Count || statements[i] is not StoreField { Field.Name: StateField, Instance: LoadArgument { Index: 0 } } fieldStore
                || !ConstantOrSlot(fieldStore.Value, slot, ref state))
                return false;
            i++;
            return i == statements.Count && state == -1;
        }

        static bool IsCacheClear(IrNode node, string cacheField)
            => node switch
            {
                InitObject { Address: LoadFieldAddress { Field.Name: var cleared, Instance: LoadArgument { Index: 0 } } } => cleared == cacheField,
                StoreField { Field.Name: var cleared, Instance: LoadArgument { Index: 0 }, Value: DefaultValue or Constant { Value: null } } => cleared == cacheField,
                _ => false,
            };

        readonly Dictionary<int, bool> _pureSpills = [];

        // Roslyn spills a struct awaitable into one local it reuses across
        // same-typed awaits. The spill folds into the operand only when every
        // use of the local is such a store immediately feeding a GetAwaiter
        // bind, or that bind's address load.
        bool IsPureAwaitSpill(int index)
        {
            if (_pureSpills.TryGetValue(index, out bool pure))
                return pure;
            pure = true;
            foreach (IrNode use in _work.Descendants.Where(node => node switch
            {
                LoadLocal load => load.Index == index,
                LoadLocalAddress address => address.Index == index,
                StoreLocal store => store.Index == index,
                _ => false,
            }).ToList())
            {
                switch (use)
                {
                    case StoreLocal store when store.Parent is Block block:
                        {
                            var siblings = block.Children.ToList();
                            int at = siblings.IndexOf(store);
                            if (at + 1 >= siblings.Count
                                || siblings[at + 1] is not StoreLocal { Value: Call { Callee.Name: "GetAwaiter" } bind }
                                || bind.Arguments.Count != 1
                                || bind.Arguments[0] is not LoadLocalAddress { Index: var fed }
                                || fed != index)
                                pure = false;
                            break;
                        }
                    case LoadLocalAddress { Parent: Call { Callee.Name: "GetAwaiter", Parent: StoreLocal { Parent: Block } } }:
                        break;
                    default:
                        pure = false;
                        break;
                }
                if (!pure)
                    break;
            }
            _pureSpills[index] = pure;
            return pure;
        }

        int CountLocalUses(int index)
            => _work.Descendants.Count(node => node switch
            {
                LoadLocal load => load.Index == index,
                LoadLocalAddress address => address.Index == index,
                StoreLocal store => store.Index == index,
                _ => false,
            });

        // An operand evaluated before an await (the left side of `sum += await t`)
        // is spilled to a <>s__ or <>7__wrap field across the suspension. A spill of a local or
        // argument read once later in its block, with no write to that storage in
        // between, reads the same value there: the read takes the storage back.
        string? FoldOperandSpills()
        {
            foreach ((string name, (int spill, _)) in _hoisted)
            {
                if (!name.StartsWith("<>s__", StringComparison.Ordinal)
                    && !name.StartsWith("<>7__wrap", StringComparison.Ordinal))
                    continue;
                var uses = _work.Descendants.Where(node => node switch
                {
                    LoadLocal load => load.Index == spill,
                    LoadLocalAddress address => address.Index == spill,
                    StoreLocal store => store.Index == spill,
                    _ => false,
                }).ToList();
                if (uses is not [StoreLocal { Parent: Block block, Value: LoadLocal or LoadArgument } store, LoadLocal load])
                    continue;
                IrNode statement = load;
                while (statement.Parent is { } parent && parent != block)
                    statement = parent;
                if (statement.Parent != block || statement.ChildIndex <= store.ChildIndex)
                    continue;
                IrExpression value = store.Value;
                bool written = block.Children
                    .Skip(store.ChildIndex + 1)
                    .Take(statement.ChildIndex - store.ChildIndex)
                    .SelectMany(static node => node.Descendants.Prepend(node))
                    .Any(node => node != statement && Writes(node, value));
                if (written)
                    continue;
                value.Detach();
                value.InheritSourceOffset(load);
                load.ReplaceWith(value);
                store.Detach();
            }
            return null;
        }

        // Roslyn stores an await's GetResult in an unnamed MoveNext local when an
        // operand evaluated before the await (`sum` in `sum + await t`) has to be
        // loaded first. That local is evaluation-stack scratch, not a source
        // variable: carried as a stack slot, the inliner folds the await back
        // into its use behind the pure operand that precedes it.
        string? ScratchAwaitResults()
        {
            var hoisted = _hoisted.Values.Select(static slot => slot.Index).ToHashSet();
            int next = Math.Max(
                StoreStackSlot.DupSlotBase,
                _work.Descendants.Select(static node => node switch
                {
                    StoreStackSlot store => store.Slot + 1,
                    LoadStackSlot load => load.Slot + 1,
                    _ => 0,
                }).DefaultIfEmpty(0).Max());
            foreach (StoreLocal store in _work.Descendants.OfType<StoreLocal>().Where(static store => store is { Value: AwaitExpression, Parent: Block }).ToList())
            {
                int local = store.Index;
                if (hoisted.Contains(local)
                    || (local < _work.LocalNames.Length && !string.IsNullOrEmpty(_work.LocalNames[local])))
                    continue;
                var uses = _work.Descendants.Where(node => node switch
                {
                    LoadLocal load => load.Index == local,
                    LoadLocalAddress address => address.Index == local,
                    StoreLocal other => other.Index == local,
                    _ => false,
                }).ToList();
                if (uses is not [StoreLocal, LoadLocal load] || uses[0] != store)
                    continue;
                int slot = next++;
                IrExpression value = store.Value;
                value.Detach();
                var scratch = new StoreStackSlot(slot, value);
                scratch.InheritSourceOffset(store);
                store.ReplaceWith(scratch);
                var read = new LoadStackSlot(slot, store.Type);
                read.InheritSourceOffset(load);
                load.ReplaceWith(read);
            }
            return null;
        }

        static bool Writes(IrNode node, IrExpression storage)
            => (node, storage) switch
            {
                (StoreLocal store, LoadLocal local) => store.Index == local.Index,
                (LoadLocalAddress address, LoadLocal local) => address.Index == local.Index,
                (StoreArgument store, LoadArgument argument) => store.Index == argument.Index,
                (LoadArgumentAddress address, LoadArgument argument) => address.Index == argument.Index,
                _ => false,
            };
    }
}
