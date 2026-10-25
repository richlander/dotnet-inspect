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
        runPasses(work, rest);

        if (work.Body.Descendants.FirstOrDefault(IsUnstructured) is { } transfer)
            return new Outcome.Declined($"a transfer survived the pipeline ({transfer.Describe()} in {transfer.Parent?.Describe()})", work);
        if (work.Descendants.Any(node => IsMachineFieldAccess(node, machine)))
            return new Outcome.Declined("a state-machine field survived the pipeline", work);
        int awaits = work.Descendants.OfType<AwaitExpression>().Count();
        if (awaits != transform.SuspensionCount)
            return new Outcome.Declined($"{awaits} awaits survived for {transform.SuspensionCount} proven suspensions");
        return new Outcome.Reconstructed(work, awaits);
    }

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
        const string StateField = "<>1__state";
        const string BuilderField = "<>t__builder";
        const string ThisField = "<>4__this";

        readonly IrFunction _work = work;
        readonly IrFunction _kickoff = kickoff;
        readonly TypeRef _machine = machine;
        readonly Dictionary<string, (int Index, TypeRef Type)> _hoisted = new(StringComparer.Ordinal);
        readonly HashSet<int> _removedLocals = [];
        readonly HashSet<IrNode> _kickoffBound = new(ReferenceEqualityComparer.Instance);
        int _stateLocal = -1;
        int _thisLocal = -1;
        int? _resultLocal;
        TypeRef? _builder;

        internal int SuspensionCount { get; private set; }

        internal string? Run()
        {
            return ReadEntry()
                ?? ReadBuilder()
                ?? DeclineUnspellableHandlers()
                ?? RemoveCompletion()
                ?? FoldDispatch()
                ?? SpliceSuspensions()
                ?? RemoveDeadClears()
                ?? RebindStorage();
        }

        // Housekeeping clears outside the completion catch and tail: a null or
        // default store to a compiler temporary from which no read of that field
        // is reachable before another store to it, and the second of two
        // adjacent clears of one hoisted field. Everything else is carried.
        string? RemoveDeadClears()
        {
            foreach (Block block in _work.Body.Descendants.OfType<Block>().ToList())
            {
                var statements = block.Children.ToList();
                for (int i = 0; i < statements.Count; i++)
                {
                    if (ClearedField(statements[i]) is not { } field)
                        continue;
                    if (i + 1 < statements.Count && ClearedField(statements[i + 1]) is { } next && next.Name == field.Name)
                    {
                        statements[i + 1].Detach();
                        statements.RemoveAt(i + 1);
                        i--;
                        continue;
                    }
                    if (IsCompilerTemporary(field) && IsDeadStore(block, i, field))
                    {
                        statements[i].Detach();
                        statements.RemoveAt(i);
                        i--;
                    }
                }
            }
            return null;
        }

        FieldRef? ClearedField(IrNode node)
            => node switch
            {
                StoreField { Field: var field, Instance: LoadArgument { Index: 0 }, Value: Constant { Value: null } or DefaultValue }
                    when IsHoistedField(field) => field,
                InitObject { Address: LoadFieldAddress { Field: var field, Instance: LoadArgument { Index: 0 } } }
                    when IsHoistedField(field) => field,
                _ => null,
            };

        // Fields no source statement can name: spill and wrap temporaries and
        // the display-class environment, whose scope-end clear Roslyn emits
        // like a local's.
        static bool IsCompilerTemporary(FieldRef field)
            => field.Name.StartsWith("<>7__wrap", StringComparison.Ordinal)
                || field.Name.StartsWith("<>s__", StringComparison.Ordinal)
                || field.Name.StartsWith("<>8__", StringComparison.Ordinal);

        bool ReadsField(IrNode node, FieldRef field)
            => node.Descendants.Prepend(node).Any(n => n switch
            {
                LoadField { Field: var f, Instance: LoadArgument { Index: 0 } } => f.Name == field.Name,
                LoadFieldAddress { Field: var f, Instance: LoadArgument { Index: 0 } } => f.Name == field.Name && n.Parent is not InitObject,
                _ => false,
            });

        static bool StoresField(IrNode node, FieldRef field)
            => node switch
            {
                StoreField { Field: var f, Instance: LoadArgument { Index: 0 } } => f.Name == field.Name,
                InitObject { Address: LoadFieldAddress { Field: var f, Instance: LoadArgument { Index: 0 } } } => f.Name == field.Name,
                _ => false,
            };

        // Over-approximates successors (every handler and finally is reachable
        // from every point of its try), so a `true` result is a sound deadness
        // proof and a `false` result only keeps the clear.
        bool IsDeadStore(Block block, int index, FieldRef field)
        {
            var offsets = new Dictionary<int, Block>();
            foreach (Block candidate in _work.Body.Descendants.OfType<Block>())
                offsets.TryAdd(candidate.StartOffset, candidate);
            var visited = new HashSet<(Block, int)>();
            var pending = new Stack<(Block Block, int Index)>();
            pending.Push((block, index + 1));
            while (pending.Count > 0)
            {
                (Block current, int start) = pending.Pop();
                if (!visited.Add((current, start)))
                    continue;
                var statements = current.Children.ToList();
                bool fellThrough = true;
                for (int i = start; i < statements.Count; i++)
                {
                    IrNode statement = statements[i];
                    if (statement is TryCatch or TryFinally)
                    {
                        if (ReadsField(statement, field))
                            return false;
                        // Enter the try; its exits reach the statements after it.
                        foreach (BlockContainer container in statement.Children.OfType<BlockContainer>())
                            EnqueueEntry(container);
                        foreach (CatchClause clause in statement.Children.OfType<CatchClause>())
                            EnqueueEntry(clause.Body);
                        fellThrough = false;
                        break;
                    }
                    if (ReadsField(statement, field))
                        return false;
                    if (StoresField(statement, field))
                    {
                        fellThrough = false;
                        break;
                    }
                    switch (statement)
                    {
                        case Branch jump:
                            EnqueueOffset(jump.TargetOffset, current);
                            fellThrough = false;
                            break;
                        case ConditionalBranch conditional:
                            EnqueueOffset(conditional.TargetOffset, current);
                            continue;
                        case SwitchBranch dispatch:
                            foreach (int target in dispatch.TargetOffsets)
                                EnqueueOffset(target, current);
                            continue;
                        case Leave leave:
                            EnqueueOffset(leave.TargetOffset, current);
                            fellThrough = false;
                            break;
                        case Return or Throw:
                            fellThrough = false;
                            break;
                        case EndFinally:
                            EnqueueExit(current);
                            fellThrough = false;
                            break;
                        default:
                            continue;
                    }
                    break;
                }
                if (fellThrough)
                {
                    if (current.Parent is BlockContainer container)
                    {
                        var siblings = container.Blocks.ToList();
                        int at = siblings.IndexOf(current);
                        if (at + 1 < siblings.Count)
                            pending.Push((siblings[at + 1], 0));
                        else
                            EnqueueExit(current);
                    }
                }
            }
            return true;

            void EnqueueEntry(BlockContainer container)
            {
                if (container.Blocks is [Block first, ..])
                    pending.Push((first, 0));
            }

            void EnqueueOffset(int offset, Block from)
            {
                if (offsets.TryGetValue(offset, out Block? target))
                    pending.Push((target, 0));
                else
                    EnqueueExit(from);
            }

            // Leaving a container: a try body falls into its finally, and every
            // container's exit continues after the statement that owns it.
            void EnqueueExit(Block from)
            {
                IrNode? node = from.Parent;
                while (node is not null and not Block)
                {
                    if (node is BlockContainer container && container.Parent is TryFinally tryFinally
                        && ReferenceEquals(tryFinally.TryBody, container))
                        EnqueueEntry(tryFinally.FinallyBody);
                    if (node.Parent is Block host)
                    {
                        var statements = host.Children.ToList();
                        int at = statements.IndexOf(node);
                        if (at >= 0)
                        {
                            pending.Push((host, at + 1));
                            return;
                        }
                    }
                    node = node.Parent;
                }
                // Reached the function body: the method exits.
            }
        }

        // The entry block: V_state = this.<>1__state, optionally V_this = this.<>4__this.
        string? ReadEntry()
        {
            if (_work.Body.Blocks is not [Block entry, ..])
                return "the body has no entry block";
            foreach (IrNode statement in entry.Children.ToList())
            {
                if (statement is StoreLocal { Value: LoadField { Field: var field, Instance: LoadArgument { Index: 0 } } } store
                    && ClassicInverseNodeFacts.IsMachineField(field, _machine))
                {
                    if (field.Name == StateField && _stateLocal < 0)
                    {
                        _stateLocal = store.Index;
                        _removedLocals.Add(store.Index);
                        store.Detach();
                        continue;
                    }
                    if (field.Name == ThisField && _thisLocal < 0)
                    {
                        _thisLocal = store.Index;
                        _removedLocals.Add(store.Index);
                        store.Detach();
                        continue;
                    }
                }
                break;
            }
            return _stateLocal < 0 ? "the entry block does not load the state field" : null;
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
                    => IsHoistedField(field),
                InitObject { Address: LoadFieldAddress { Field: var field, Instance: LoadArgument { Index: 0 } } }
                    => IsHoistedField(field),
                _ => false,
            };

        bool IsHoistedField(FieldRef field)
            => ClassicInverseNodeFacts.IsMachineField(field, _machine)
                && (GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name)
                    || field.Name.StartsWith("<>7__wrap", StringComparison.Ordinal)
                    || field.Name.StartsWith("<>s__", StringComparison.Ordinal)
                    || field.Name.StartsWith("<>8__", StringComparison.Ordinal));

        // Every branch whose condition depends only on V_state folds under V_state = -1.
        string? FoldDispatch()
        {
            foreach (IrNode node in _work.Body.Descendants.ToList())
            {
                switch (node)
                {
                    case ConditionalBranch branch when ReadsState(branch.Condition):
                        {
                            if (Evaluate(branch.Condition) is not { } value)
                                return "a state test does not fold under the running state";
                            if (IsTrue(value))
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
                    case SwitchBranch dispatch when ReadsState(dispatch.Value):
                        {
                            if (Evaluate(dispatch.Value) is not int selector)
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

        bool ReadsState(IrNode node)
            => node.Descendants.Prepend(node).Any(n => n is LoadLocal load && load.Index == _stateLocal);

        static bool IsTrue(object value) => value switch { bool b => b, int i => i != 0, _ => false };

        object? Evaluate(IrExpression expression)
        {
            switch (expression)
            {
                case LoadLocal load when load.Index == _stateLocal:
                    return -1;
                case Constant { Value: int i }:
                    return i;
                case Constant { Value: bool b }:
                    return b;
                case LogicalNot not:
                    return Evaluate(not.Operand) is { } inner ? !IsTrue(inner) : null;
                case Comparison comparison:
                    {
                        if (Evaluate(comparison.Left) is not int left || Evaluate(comparison.Right) is not int right)
                            return null;
                        return comparison.Kind switch
                        {
                            ComparisonKind.Equal => left == right,
                            ComparisonKind.NotEqual => left != right,
                            ComparisonKind.LessThan => comparison.IsUnsigned ? (uint)left < (uint)right : left < right,
                            ComparisonKind.LessThanOrEqual => comparison.IsUnsigned ? (uint)left <= (uint)right : left <= right,
                            ComparisonKind.GreaterThan => comparison.IsUnsigned ? (uint)left > (uint)right : left > right,
                            ComparisonKind.GreaterThanOrEqual => comparison.IsUnsigned ? (uint)left >= (uint)right : left >= right,
                            _ => null,
                        };
                    }
                case Binary { Kind: BinaryKind.Add or BinaryKind.Subtract } binary:
                    {
                        if (Evaluate(binary.Left) is not int left || Evaluate(binary.Right) is not int right)
                            return null;
                        return binary.Kind == BinaryKind.Add ? left + right : left - right;
                    }
                case LogicalBinary logical:
                    {
                        if (Evaluate(logical.Left) is not { } left || Evaluate(logical.Right) is not { } right)
                            return null;
                        return logical.Kind == LogicalKind.And ? IsTrue(left) && IsTrue(right) : IsTrue(left) || IsTrue(right);
                    }
                default:
                    return null;
            }
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
                consumedMemberRefs: [getAwaiter.Callee, isCompleted, getResult.Callee],
                provesClassicCompletionPaths: true);
            await.InheritSourceOffset(getResult);
            getResult.ReplaceWith(await);

            bind.Detach();
            completed.Detach();
            suspension.Detach();
            resume.Detach();
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

        // Machine storage becomes parameters, this, and fresh locals; no protocol storage may remain.
        string? RebindStorage()
        {
            if (_work.Descendants.Any(node => node is LoadLocal load && load.Index == _stateLocal))
                return "a read of the state local survived dispatch folding";
            if (_work.Descendants.Any(node => (node is StoreLocal store && store.Index == _stateLocal)))
                return "a store to the state local survived";

            var swaps = new List<(IrNode Old, Func<IrNode> Replacement)>();
            foreach (IrNode node in _work.Descendants)
            {
                switch (node)
                {
                    case LoadLocal load when load.Index == _thisLocal:
                        if (!TryReceiver(out IrExpression? receiver))
                            return "the this copy has no kickoff receiver";
                        swaps.Add((node, () => receiver!));
                        break;
                    case LoadLocalAddress address when address.Index == _thisLocal:
                        return "the this copy is used by address";
                    case StoreLocal store when store.Index == _thisLocal:
                        return "the this copy is stored outside the entry";
                    case LoadField { Field: var field, Instance: LoadArgument { Index: 0 } } when ClassicInverseNodeFacts.IsMachineField(field, _machine):
                        {
                            if (field.Name == ThisField)
                            {
                                if (!TryReceiver(out IrExpression? hoistedReceiver))
                                    return "the hoisted this has no kickoff receiver";
                                swaps.Add((node, () => hoistedReceiver!));
                            }
                            else if (TryParameter(field.Name, out int index, out Parameter? parameter))
                            {
                                var argument = new LoadArgument(index, parameter!);
                                _kickoffBound.Add(argument);
                                swaps.Add((node, () => argument));
                            }
                            else if (IsHoistedField(field))
                            {
                                (int local, TypeRef type) = Hoisted(field);
                                swaps.Add((node, () => new LoadLocal(local, type)));
                            }
                            else
                            {
                                return $"protocol field '{field.Name}' survived as a load";
                            }
                            break;
                        }
                    case StoreField { Field: var field, Instance: LoadArgument { Index: 0 } } store when ClassicInverseNodeFacts.IsMachineField(field, _machine):
                        {
                            if (TryParameter(field.Name, out int index, out Parameter? parameter))
                            {
                                IrExpression value = store.Value;
                                swaps.Add((node, () =>
                                {
                                    value.Detach();
                                    return new StoreArgument(index, parameter!, value);
                                }));
                            }
                            else if (IsHoistedField(field))
                            {
                                (int local, TypeRef type) = Hoisted(field);
                                IrExpression value = store.Value;
                                swaps.Add((node, () =>
                                {
                                    value.Detach();
                                    return new StoreLocal(local, type, value);
                                }));
                            }
                            else
                            {
                                return $"protocol field '{field.Name}' survived as a store";
                            }
                            break;
                        }
                    case LoadFieldAddress { Field: var field, Instance: LoadArgument { Index: 0 } } when ClassicInverseNodeFacts.IsMachineField(field, _machine):
                        {
                            if (IsHoistedField(field))
                            {
                                (int local, TypeRef type) = Hoisted(field);
                                swaps.Add((node, () => new LoadLocalAddress(local, type)));
                            }
                            else if (TryParameter(field.Name, out int index, out Parameter? parameter))
                            {
                                swaps.Add((node, () => new LoadArgumentAddress(index, parameter!)));
                            }
                            else
                            {
                                return $"protocol field '{field.Name}' survived as an address";
                            }
                            break;
                        }
                    case InitObject { Address: LoadFieldAddress { Field: var field, Instance: LoadArgument { Index: 0 } } } when ClassicInverseNodeFacts.IsMachineField(field, _machine) && !IsHoistedField(field):
                        return $"protocol field '{field.Name}' survived as an init";
                }
            }
            foreach ((IrNode old, Func<IrNode> replacement) in swaps)
            {
                IrNode actual = replacement();
                actual.InheritSourceOffset(old);
                old.ReplaceWith(actual);
            }
            IrNode? receiverUse = _work.Descendants.FirstOrDefault(node =>
                node is LoadArgument { Index: 0 }
                && !_kickoffBound.Contains(node)
                && node.Parent is not (LoadField or StoreField or LoadFieldAddress));
            if (receiverUse is not null)
                return $"the state-machine receiver is used outside a field access ({receiverUse.Parent?.Describe()} in {receiverUse.Parent?.Parent?.Describe()})";
            foreach (int index in _removedLocals)
            {
                IrNode? use = _work.Descendants.FirstOrDefault(node => node switch
                {
                    LoadLocal load => load.Index == index,
                    LoadLocalAddress address => address.Index == index,
                    StoreLocal store => store.Index == index,
                    _ => false,
                });
                if (use is not null)
                    return $"protocol local {index} survived as {use.Describe()} in {use.Parent?.Describe()}";
            }
            return null;
        }

        (int Index, TypeRef Type) Hoisted(FieldRef field)
        {
            if (!_hoisted.TryGetValue(field.Name, out var slot))
            {
                slot = (_work.AddLocal(field.Type, SourceName(field.Name)), field.Type);
                _hoisted[field.Name] = slot;
            }
            return slot;
        }

        static string? SourceName(string fieldName)
        {
            if (!GeneratedCodeIdentity.IsHoistedLocalFieldName(fieldName))
                return null;
            int close = fieldName.IndexOf('>');
            return close > 1 ? fieldName[1..close] : null;
        }

        bool TryReceiver(out IrExpression? receiver)
        {
            receiver = null;
            if (!_kickoff.Signature.HasThis || _kickoff.ReceiverParameter is not { } parameter)
                return false;
            receiver = new LoadArgument(0, parameter);
            _kickoffBound.Add(receiver);
            return true;
        }

        bool TryParameter(string name, out int index, out Parameter? parameter)
        {
            ImmutableArray<Parameter> parameters = _kickoff.Signature.Parameters;
            int argumentBase = _kickoff.Signature.HasThis ? 1 : 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].Name == name)
                {
                    index = argumentBase + i;
                    parameter = parameters[i];
                    return true;
                }
            }
            index = -1;
            parameter = null;
            return false;
        }
    }
}
