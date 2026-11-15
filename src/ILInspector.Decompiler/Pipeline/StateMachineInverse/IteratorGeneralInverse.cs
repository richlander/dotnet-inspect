using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The general iterator inverse: recognizes the Roslyn iterator lowering
/// protocol by its physical roles in the raw <c>MoveNext</c> import (the state
/// dispatch, each <c>current</c>/<c>state</c>/<c>return true</c> yield seam,
/// the <c>return false</c> completions, and machine storage), removes those
/// roles, joins each yield to the block its state resumes at, rebinds machine
/// storage to the kickoff's parameters, <c>this</c>, and fresh locals, hands
/// the body to the ordinary pass pipeline, and validates the result after the
/// pipeline instead of matching the user code before it.
/// <para>Owning design: <c>docs/design/iterator-reconstruction.md</c>.</para>
/// </summary>
internal static class IteratorGeneralInverse
{
    internal abstract record Outcome
    {
        internal sealed record Reconstructed(IrFunction Body, int YieldCount) : Outcome;

        internal sealed record Declined(string Reason) : Outcome;
    }

    const string CurrentField = "<>2__current";

    /// <summary>
    /// Attempts the inverse over a fresh clone of the unmodified execution
    /// import. <paramref name="runPasses"/> runs a pass list over a body inside
    /// the host's cross-method pipeline scope.
    /// </summary>
    internal static Outcome TryReconstruct(
        IrFunction rawExecution,
        IrFunction kickoff,
        TypeRef machine,
        Action<IrFunction, ImmutableArray<IIrPass>> runPasses)
    {
        // A yield inside a try block lowers to finally frames: a fault handler
        // over the body and <>m__Finally methods that Dispose replays by state.
        // That protocol is a later stage of the owning design.
        if (rawExecution.Regions.Any(static region => region.Kind == HandlerKind.Fault))
            return new Outcome.Declined("a yield inside a try block lowers to finally frames");

        var work = (IrFunction)rawExecution.Clone();
        ImmutableArray<IIrPass> all = IrPasses.ForReconstruction<IteratorReconstructionPass>();
        int ehIndex = all.IndexOf(all.First(static pass => pass is EhStructuringPass));
        ImmutableArray<IIrPass> prefix = all.Take(ehIndex + 1).ToImmutableArray();
        ImmutableArray<IIrPass> rest = all.Skip(ehIndex + 1).ToImmutableArray();

        runPasses(work, prefix);
        if (!work.Regions.IsEmpty)
            return new Outcome.Declined("exception regions did not structure");

        var transform = new Transform(work, kickoff, machine);
        string? failure = transform.Run();
        if (failure is not null)
            return new Outcome.Declined(failure);
        // The completions are plain returns while the pipeline structures the
        // body: no pass treats `yield break` as an exit, and every pass treats
        // a void return as one.
        work.Signature = work.Signature with { ReturnType = TypeRef.CoreLib("System", "Void") };
        runPasses(work, rest);

        if (work.Body.Descendants.FirstOrDefault(IsUnstructured) is { } transfer)
            return new Outcome.Declined($"a transfer survived the pipeline ({transfer.Describe()} in {transfer.Parent?.Describe()})");
        if (work.Descendants.FirstOrDefault(node => MachineField(node, machine) is not null) is { } survivor)
            return new Outcome.Declined($"a state-machine field survived the pipeline ({survivor.Describe()})");
        if (work.Descendants.OfType<Return>().Any(static ret => ret.Value is not null))
            return new Outcome.Declined("a value return survived the pipeline");
        int yields = work.Descendants.OfType<YieldReturn>().Count();
        if (yields != transform.YieldCount)
            return new Outcome.Declined($"{yields} yields survived for {transform.YieldCount} proven yield seams");

        RestoreCompletions(work, yields);
        return new Outcome.Reconstructed(work, yields);
    }

    // Every completion the pipeline kept becomes `yield break`. The one that
    // ends the body is the implicit end of the iterator; it stays only when
    // the body has no yield, where it is what makes the method an iterator.
    static void RestoreCompletions(IrFunction work, int yields)
    {
        if (yields > 0
            && work.Body.Blocks is [.., Block last]
            && last.Children is [.., Return tail])
        {
            tail.Detach();
        }
        foreach (Return completion in work.Descendants.OfType<Return>().ToList())
        {
            var stop = new YieldBreak();
            stop.InheritSourceOffset(completion);
            completion.ReplaceWith(stop);
        }
        if (yields == 0 && !work.Descendants.OfType<YieldBreak>().Any())
        {
            if (work.Body.Blocks is not [.., Block end])
            {
                end = new Block(0);
                work.Body.Add(end);
            }
            end.Add(new YieldBreak());
        }
    }

    static bool IsUnstructured(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter or UnsupportedNode;

    static FieldRef? MachineField(IrNode node, TypeRef machine)
    {
        FieldRef? field = node switch
        {
            LoadField load => load.Field,
            StoreField store => store.Field,
            LoadFieldAddress address => address.Field,
            _ => null,
        };
        return field is not null && ClassicInverseNodeFacts.IsMachineField(field, machine) ? field : null;
    }

    sealed class Transform(IrFunction work, IrFunction kickoff, TypeRef machine)
    {
        readonly IrFunction _work = work;
        readonly IrFunction _kickoff = kickoff;
        readonly TypeRef _machine = machine;
        readonly StateMachineStorage _storage = new(work, kickoff, machine);
        readonly HashSet<IrNode> _dispatch = new(ReferenceEqualityComparer.Instance);

        internal int YieldCount { get; private set; }

        internal string? Run()
        {
            return _storage.ReadEntry()
                ?? PropagateReceiverSlots()
                ?? SpliceYields()
                ?? EnterAtInitialState()
                ?? RemoveStateStores()
                ?? LowerCompletions()
                ?? Tidy()
                ?? _storage.RemoveDeadClears()
                ?? BindValueReceiver()
                ?? _storage.Rebind();
        }

        bool IsMachineReceiver(IrNode? node)
            => node is LoadArgument { Index: 0 };

        bool IsMachineStore(IrNode node, string name, out StoreField store)
        {
            store = null!;
            if (node is not StoreField { Field: var field, Instance: var instance } candidate
                || field.Name != name
                || !IsMachineReceiver(instance)
                || !ClassicInverseNodeFacts.IsMachineField(field, _machine))
                return false;
            store = candidate;
            return true;
        }

        // The importer carries the machine receiver across a branch in a stack
        // slot (`S = this; ... this.<n>5__3 = S ? a : b`). A slot stored once,
        // with the receiver, reads as the receiver everywhere.
        string? PropagateReceiverSlots()
        {
            foreach (StoreStackSlot store in _work.Descendants.OfType<StoreStackSlot>()
                .Where(store => IsMachineReceiver(store.Value) && store.Parent is Block)
                .ToList())
            {
                int slot = store.Slot;
                if (_work.Descendants.OfType<StoreStackSlot>().Count(other => other.Slot == slot) != 1)
                    continue;
                foreach (LoadStackSlot load in _work.Descendants.OfType<LoadStackSlot>().Where(load => load.Slot == slot).ToList())
                {
                    IrNode receiver = store.Value.Clone();
                    receiver.InheritSourceOffset(load);
                    load.ReplaceWith(receiver);
                }
                store.Detach();
            }
            return null;
        }

        // The block a resumed MoveNext reaches for one state: the walk from the
        // entry through every test that depends only on the state local. Each
        // test it evaluates is dispatch, and is removed with the entry.
        string? Resolve(int state, out Block target)
        {
            target = null!;
            var blocks = _work.Body.Blocks.ToList();
            var byOffset = new Dictionary<int, int>();
            for (int i = 0; i < blocks.Count; i++)
                byOffset.TryAdd(blocks[i].StartOffset, i);
            var visited = new HashSet<int>();
            int at = 0;
            while (at < blocks.Count)
            {
                if (!visited.Add(at))
                    return "the state dispatch does not terminate";
                Block block = blocks[at];
                int? jump = null;
                int position = 0;
                foreach (IrNode statement in block.Children)
                {
                    switch (statement)
                    {
                        case ConditionalBranch branch when _storage.ReadsState(branch.Condition):
                            {
                                if (_storage.Evaluate(branch.Condition, state) is not { } value)
                                    return "a state test does not fold under a state the machine stores";
                                _dispatch.Add(branch);
                                if (StateMachineStorage.IsTrue(value))
                                    jump = branch.TargetOffset;
                                break;
                            }
                        case SwitchBranch dispatch when _storage.ReadsState(dispatch.Value):
                            {
                                if (_storage.Evaluate(dispatch.Value, state) is not int selector)
                                    return "a state switch does not fold under a state the machine stores";
                                _dispatch.Add(dispatch);
                                if (selector >= 0 && selector < dispatch.TargetOffsets.Length)
                                    jump = dispatch.TargetOffsets[selector];
                                break;
                            }
                        case Branch branch when position > 0 || block.Children.Count == 1:
                            // The transfer that ends a dispatch block, or a
                            // block that only forwards.
                            if (position > 0)
                                _dispatch.Add(branch);
                            jump = branch.TargetOffset;
                            break;
                        default:
                            if (position > 0)
                                return "the state dispatch reaches the middle of a block";
                            target = block;
                            return null;
                    }
                    if (jump is not null)
                        break;
                    position++;
                }
                if (jump is { } offset)
                {
                    if (!byOffset.TryGetValue(offset, out at))
                        return "the state dispatch leaves the body";
                }
                else
                {
                    at++;
                }
            }
            return "the state dispatch falls off the body";
        }

        // this.<>2__current = value; this.<>1__state = k; return true;
        // becomes `yield return value;` joined to the block state k resumes at.
        string? SpliceYields()
        {
            var topLevel = _work.Body.Blocks.ToHashSet<IrNode>(ReferenceEqualityComparer.Instance);
            foreach (IrNode node in _work.Descendants.ToList())
            {
                if (!IsMachineStore(node, CurrentField, out StoreField current))
                    continue;
                if (current.Parent is not Block block || !topLevel.Contains(block))
                    return "a yield seam is not in the body's own blocks";
                var statements = block.Children.ToList();
                int at = statements.IndexOf(current);
                if (at + 3 != statements.Count
                    || !IsMachineStore(statements[at + 1], StateMachineStorage.StateField, out StoreField advance)
                    || advance.Value is not Constant { Value: int state }
                    || statements[at + 2] is not Return { Value: Constant { Value: true } } suspend)
                    return "a current store is not followed by its state store and return true";
                string? failure = Resolve(state, out Block resume);
                if (failure is not null)
                    return failure;

                IrExpression value = current.Value;
                value.Detach();
                // The current field was the value's typed sink; `yield return`
                // has none of its own, so the sink travels with the value (an
                // int counter yielded as an enum keeps its conversion).
                TypeRef element = current.Field.Type;
                if (value.ResultType is { } produced && !produced.Equals(element))
                {
                    var sink = new Coerce(element, value);
                    sink.InheritSourceOffset(value);
                    value = sink;
                }
                var yield = new YieldReturn(value);
                yield.InheritSourceOffset(current);
                current.ReplaceWith(yield);
                advance.Detach();
                var join = new Branch(resume.StartOffset);
                join.InheritSourceOffset(suspend);
                suspend.ReplaceWith(join);
                YieldCount++;
            }
            return null;
        }

        // A fresh machine starts in state 0: the entry reaches that block, and
        // what only another state reaches is gone.
        string? EnterAtInitialState()
        {
            string? failure = Resolve(0, out Block start);
            if (failure is not null)
                return failure;
            if (_work.Body.Blocks is not [Block entry, ..])
                return "the body has no entry block";
            foreach (IrNode statement in _dispatch)
                statement.Detach();
            if (!ReferenceEquals(entry, start))
            {
                if (entry.Children.Count != 0)
                    return "the entry block carries statements beyond the dispatch";
                entry.Add(new Branch(start.StartOffset));
            }

            var blocks = _work.Body.Blocks.ToList();
            var byOffset = new Dictionary<int, Block>();
            foreach (Block block in blocks)
                byOffset.TryAdd(block.StartOffset, block);
            var reachable = new HashSet<Block>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<Block>();
            pending.Push(entry);
            while (pending.Count > 0)
            {
                Block block = pending.Pop();
                if (!reachable.Add(block))
                    continue;
                foreach (IrNode node in block.Descendants)
                {
                    foreach (int offset in Targets(node))
                    {
                        if (byOffset.TryGetValue(offset, out Block? target))
                            pending.Push(target);
                    }
                }
                if (FallsThrough(block))
                {
                    int next = blocks.IndexOf(block) + 1;
                    if (next < blocks.Count)
                        pending.Push(blocks[next]);
                }
            }
            foreach (Block block in blocks)
            {
                if (!reachable.Contains(block))
                    block.Detach();
            }
            return null;
        }

        static IEnumerable<int> Targets(IrNode node)
            => node switch
            {
                Branch branch => [branch.TargetOffset],
                ConditionalBranch branch => [branch.TargetOffset],
                SwitchBranch dispatch => dispatch.TargetOffsets,
                Leave leave => [leave.TargetOffset],
                _ => [],
            };

        static bool FallsThrough(Block block)
            => block.Children.Count == 0
                || block.Children[^1] is not (Return or Branch or Leave or Throw or SwitchBranch or EndFinally);

        // The running marker (-1) and every advance are protocol; a state store
        // that is not a constant is not this lowering.
        string? RemoveStateStores()
        {
            foreach (IrNode node in _work.Descendants.ToList())
            {
                if (!IsMachineStore(node, StateMachineStorage.StateField, out StoreField store))
                    continue;
                if (store.Value is not Constant { Value: int })
                    return "a state store does not store a constant";
                if (store.Parent is not Block)
                    return "a state store is not a statement";
                store.Detach();
            }
            return null;
        }

        // `return false` ends the sequence. A `return true` that no yield seam
        // owned is not this lowering.
        string? LowerCompletions()
        {
            foreach (Return ret in _work.Descendants.OfType<Return>().ToList())
            {
                if (ret.Value is not Constant { Value: false })
                    return "a return is neither a yield seam nor a completion";
                var completion = new Return(null);
                completion.InheritSourceOffset(ret);
                ret.ReplaceWith(completion);
            }
            return null;
        }

        // Removing the protocol leaves what redundant-branch elimination has
        // already cleaned from an ordinary import: resume blocks that held only
        // the running marker, and joins to the block that follows. Only the
        // dispatch and the joins ever targeted a resume block.
        string? Tidy()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var blocks = _work.Body.Blocks.ToList();
                for (int i = 0; i + 1 < blocks.Count; i++)
                {
                    Block block = blocks[i];
                    Block next = blocks[i + 1];
                    if (block.Children is [.., Branch join] && join.TargetOffset == next.StartOffset)
                    {
                        join.Detach();
                        changed = true;
                    }
                    if (block.Children.Count != 0)
                        continue;
                    foreach (Branch branch in _work.Descendants.OfType<Branch>().Where(branch => branch.TargetOffset == block.StartOffset).ToList())
                    {
                        var forwarded = new Branch(next.StartOffset);
                        forwarded.InheritSourceOffset(branch);
                        branch.ReplaceWith(forwarded);
                    }
                    if (_work.Descendants.Any(node => Targets(node).Contains(block.StartOffset)))
                        continue;
                    block.Detach();
                    changed = true;
                    break;
                }
            }
            return null;
        }

        // A value-type iterator copies the receiver into <>4__this and reads
        // its members through that field's address. The source reads them
        // through `this`, which the kickoff holds as an address: its capture
        // of the receiver is the indirect load that proves it.
        string? BindValueReceiver()
        {
            bool valueReceiver = _kickoff.Signature.HasThis
                && _kickoff.Descendants.OfType<LoadIndirect>().Any(static load => load.Address is LoadArgument { Index: 0 });
            foreach (IrNode node in _work.Descendants.ToList())
            {
                FieldRef? field = node switch
                {
                    LoadFieldAddress address when IsMachineReceiver(address.Instance) => address.Field,
                    LoadField load when IsMachineReceiver(load.Instance) => load.Field,
                    _ => null,
                };
                if (field is null
                    || field.Name != StateMachineStorage.ThisField
                    || !ClassicInverseNodeFacts.IsMachineField(field, _machine))
                    continue;
                if (node is LoadField)
                {
                    if (valueReceiver)
                        return "the hoisted value receiver is read as a value";
                    continue;
                }
                if (!valueReceiver)
                    return "the hoisted receiver is read by address but the kickoff does not capture a value receiver";
                if (node.Parent is not (LoadField or Call or LoadProperty))
                    return "the hoisted value receiver's address is used outside a member read";
                if (!_storage.TryReceiver(out IrExpression? receiver))
                    return "the hoisted value receiver has no kickoff receiver";
                receiver!.InheritSourceOffset(node);
                node.ReplaceWith(receiver);
            }
            return null;
        }
    }
}
