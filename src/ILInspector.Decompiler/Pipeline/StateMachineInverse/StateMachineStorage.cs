using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The storage and state roles every Roslyn state-machine <c>MoveNext</c>
/// shares, whichever lowering produced it: the entry copies of the state and
/// <c>this</c> fields, state tests that fold under a known state, and machine
/// fields that rebind to the kickoff's parameters, <c>this</c>, and fresh
/// locals. The classic async inverse and the iterator inverse each add their
/// own protocol (awaiter triples, yield seams) on top.
/// <para>Owning design: <c>docs/design/classic-async-reconstruction.md</c>,
/// section "General inverse"; the iterator consumer is owned by
/// <c>docs/design/iterator-reconstruction.md</c>.</para>
/// </summary>
internal sealed class StateMachineStorage(IrFunction work, IrFunction kickoff, TypeRef machine)
{
    internal const string StateField = "<>1__state";
    internal const string ThisField = "<>4__this";

    readonly IrFunction _work = work;
    readonly IrFunction _kickoff = kickoff;
    readonly TypeRef _machine = machine;
    readonly Dictionary<string, (int Index, TypeRef Type)> _hoisted = new(StringComparer.Ordinal);
    readonly HashSet<int> _removedLocals = [];
    readonly HashSet<IrNode> _kickoffBound = new(ReferenceEqualityComparer.Instance);
    int _stateLocal = -1;
    int _thisLocal = -1;

    /// <summary>Fresh locals issued for hoisted machine fields, by field name.</summary>
    internal IReadOnlyDictionary<string, (int Index, TypeRef Type)> HoistedLocals => _hoisted;

    /// <summary>Protocol locals whose every use must be gone when <see cref="Rebind"/> runs.</summary>
    internal HashSet<int> RemovedLocals => _removedLocals;

    /// <summary>The local the entry block copies the state field into, or -1.</summary>
    internal int StateLocal => _stateLocal;

    // The entry block: V_state = this.<>1__state, optionally V_this = this.<>4__this.
    internal string? ReadEntry()
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

    internal bool IsHoistedField(FieldRef field)
        => ClassicInverseNodeFacts.IsMachineField(field, _machine)
            && (GeneratedCodeIdentity.IsHoistedLocalFieldName(field.Name)
                || field.Name.StartsWith("<>7__wrap", StringComparison.Ordinal)
                || field.Name.StartsWith("<>s__", StringComparison.Ordinal)
                || field.Name.StartsWith("<>8__", StringComparison.Ordinal));

    internal bool ReadsState(IrNode node)
        => node.Descendants.Prepend(node).Any(n => n is LoadLocal load && load.Index == _stateLocal);

    internal static bool IsTrue(object value) => value switch { bool b => b, int i => i != 0, _ => false };

    internal object? Evaluate(IrExpression expression, int state)
    {
        switch (expression)
        {
            case LoadLocal load when load.Index == _stateLocal:
                return state;
            case Constant { Value: int i }:
                return i;
            case Constant { Value: bool b }:
                return b;
            case LogicalNot not:
                return Evaluate(not.Operand, state) is { } inner ? !IsTrue(inner) : null;
            case Comparison comparison:
                {
                    if (Evaluate(comparison.Left, state) is not int left || Evaluate(comparison.Right, state) is not int right)
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
                    if (Evaluate(binary.Left, state) is not int left || Evaluate(binary.Right, state) is not int right)
                        return null;
                    return binary.Kind == BinaryKind.Add ? left + right : left - right;
                }
            case LogicalBinary logical:
                {
                    if (Evaluate(logical.Left, state) is not { } left || Evaluate(logical.Right, state) is not { } right)
                        return null;
                    return logical.Kind == LogicalKind.And ? IsTrue(left) && IsTrue(right) : IsTrue(left) || IsTrue(right);
                }
            default:
                return null;
        }
    }

    // Machine storage becomes parameters, this, and fresh locals; no protocol storage may remain.
    internal string? Rebind()
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
                            var argument = new LoadArgument(index, parameter!)
                            {
                                IsDynamic = parameter!.IsDynamic,
                                ArrayElementIsDynamic = parameter.ArrayElementIsDynamic,
                            };
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
            // An element read typed its dynamic fact from the machine field;
            // the kickoff parameter's metadata now owns it.
            if (actual is LoadArgument { Parent: LoadElement element } && element.Array == actual)
                element.ResultIsDynamic = IrImporter.ArrayElementDynamicFact(element.Array);
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

    internal bool TryReceiver(out IrExpression? receiver)
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

    // Housekeeping clears outside the completion catch and tail: a null or
    // default store to a compiler temporary from which no read of that field
    // is reachable before another store to it, and the second of two
    // adjacent clears of one hoisted field. Everything else is carried.
    internal string? RemoveDeadClears()
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
    // from every point of its try: the walk enters each enclosing try's
    // handlers and finally from the store, and a try statement it reaches
    // later is read as a whole), so a `true` result is a sound deadness
    // proof and a `false` result only keeps the clear.
    bool IsDeadStore(Block block, int index, FieldRef field)
    {
        var offsets = new Dictionary<int, Block>();
        foreach (Block candidate in _work.Body.Descendants.OfType<Block>())
            offsets.TryAdd(candidate.StartOffset, candidate);
        var visited = new HashSet<(Block, int)>();
        var pending = new Stack<(Block Block, int Index)>();
        pending.Push((block, index + 1));
        for (IrNode? node = block.Parent; node is not null; node = node.Parent)
        {
            if (node is not BlockContainer body)
                continue;
            if (body.Parent is TryCatch tryCatch && ReferenceEquals(tryCatch.TryBody, body))
            {
                foreach (CatchClause clause in tryCatch.Clauses)
                {
                    if (clause.Filter is { } filter && ReadsField(filter, field))
                        return false;
                    EnqueueEntry(clause.Body);
                }
            }
            else if (body.Parent is TryFinally tryFinally && ReferenceEquals(tryFinally.TryBody, body))
            {
                EnqueueEntry(tryFinally.FinallyBody);
            }
        }
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
}
