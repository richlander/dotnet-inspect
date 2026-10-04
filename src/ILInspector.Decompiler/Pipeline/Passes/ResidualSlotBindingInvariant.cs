using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Checks the residual binding step, independently of the rewrite: ordered
/// node identity and arity survive; every outer slot node becomes a fresh local
/// reference whose index is shared by exactly the nodes the declared binding
/// partitions together and whose type equals the type that binding declares.
/// A sibling of <see cref="SlotMaterializationInvariant"/>, not a reuse of it:
/// one slot may legitimately become several locals here. The discharge that
/// follows binding is checked by <see cref="CoercionInvariant"/>, not here.
/// </summary>
sealed class ResidualSlotBindingInvariant
{
    readonly IrFunction _function;
    readonly ImmutableArray<TypeRef> _locals;
    readonly Node[] _nodes;
    readonly ResidualSlotPlan _plan;

    readonly record struct Node(IrNode Original, int ChildCount, ResidualSlotPlan.PieceKey? Key);

    ResidualSlotBindingInvariant(IrFunction function, ResidualSlotPlan plan)
    {
        _function = function;
        _locals = function.Locals;
        _plan = plan;
        _nodes = function.Descendants.Prepend(function)
            .Select(node => new Node(
                node,
                node.Children.Count,
                plan.KeyOf.TryGetValue(node, out var key) ? key : null))
            .ToArray();
    }

    internal static ResidualSlotBindingInvariant Capture(IrFunction function, ResidualSlotPlan plan) => new(function, plan);

    internal void Check()
    {
        var locals = _function.Locals;
        if (locals.Length < _locals.Length || !_locals.SequenceEqual(locals.Take(_locals.Length)))
            Fail("existing local types changed");
        if (locals.Length - _locals.Length != _plan.Pieces.Count)
            Fail("appended locals do not correspond one-to-one with declared pieces");

        var declaredTypes = _plan.Pieces.ToDictionary(static piece => piece.Key, static piece => piece.Type);
        var bindings = new Dictionary<ResidualSlotPlan.PieceKey, int>();
        var owners = new Dictionary<int, ResidualSlotPlan.PieceKey>();
        using var current = _function.Descendants.Prepend(_function).GetEnumerator();
        foreach (var node in _nodes)
        {
            if (!current.MoveNext())
                Fail("ordered tree lost nodes");
            var after = current.Current;
            if (node.ChildCount != after.Children.Count)
                Fail($"child count changed at {node.Original.Describe()}");

            if (node.Key is { } key)
            {
                (int index, TypeRef type) = after switch
                {
                    StoreLocal store when node.Original is StoreStackSlot => (store.Index, store.Type),
                    LoadLocal load when node.Original is LoadStackSlot => (load.Index, load.Type),
                    _ => throw new InvalidOperationException(
                        $"{_function.Name}: residual slot binding broke its contract: slot node was not bound at {node.Original.Describe()}."),
                };
                if (index < _locals.Length || index >= locals.Length)
                    Fail($"S_{key.Slot} does not refer to a fresh local");
                if (!type.Equals(locals[index]))
                    Fail($"S_{key.Slot} reference disagrees with its local table type");
                if (declaredTypes[key] is not { } declared || !type.Equals(declared))
                    Fail($"S_{key.Slot} local type disagrees with its declared binding");
                if (bindings.TryGetValue(key, out int previous) && previous != index)
                    Fail($"S_{key.Slot} ({key.TypeKey}) has inconsistent bindings");
                bindings[key] = index;
                if (owners.TryGetValue(index, out var owner) && owner != key)
                    Fail($"local {index} is shared by different pieces S_{owner.Slot} and S_{key.Slot}");
                owners[index] = key;
            }
            else if (!ReferenceEquals(node.Original, after))
            {
                Fail($"ordered node identity changed at {node.Original.Describe()}");
            }
        }
        if (current.MoveNext())
            Fail("ordered tree gained nodes");
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    void Fail(string message) =>
        throw new InvalidOperationException($"{_function.Name}: residual slot binding broke its contract: {message}.");
}
