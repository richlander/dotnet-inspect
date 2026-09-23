using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Checks the storage rewrite, independently of its admission plan. Ordered
/// node identity and arity must survive, except that an outer slot web may
/// become one fresh, testified local. This is not a general IR equivalence
/// checker: non-child metadata and later passes are outside its comparison.
/// </summary>
internal sealed class SlotMaterializationInvariant
{
    readonly IrFunction _function;
    readonly ImmutableArray<TypeRef> _locals;
    readonly Node[] _nodes;
    readonly Dictionary<int, CoercionSinks.SlotTypeTestimony> _testimony;
    readonly Dictionary<int, TypeRef> _producerOnlyManagedReferences;
    readonly (int Destination, int Source)[] _copies;

    readonly record struct Node(IrNode Original, int ChildCount, bool OuterSlot);

    SlotMaterializationInvariant(IrFunction function)
    {
        _function = function;
        _locals = function.Locals;
        var outerSlots = CoercionSinks.ScopeNodes(function.Body)
            .Where(static node => node is StoreStackSlot or LoadStackSlot)
            .ToHashSet();
        _nodes = function.Descendants.Prepend(function)
            .Select(node => new Node(node, node.Children.Count, outerSlots.Contains(node)))
            .ToArray();
        _testimony = CoercionSinks.AnalyzeSlotTypeTestimony(
            function.Body, function.Signature.ReturnType, function.TypeShapes,
            recoverBooleanIdentity: true, recoverElementIdentity: true,
            enumUnderlyingTypes: function.EnumUnderlyingTypes);
        var loadedSlots = outerSlots
            .OfType<LoadStackSlot>()
            .Select(static load => load.Slot)
            .ToHashSet();
        _producerOnlyManagedReferences = outerSlots
            .OfType<StoreStackSlot>()
            .Where(store => !loadedSlots.Contains(store.Slot))
            .GroupBy(static store => store.Slot)
            .Select(group => (
                Slot: group.Key,
                Types: group
                    .Select(static store => store.Value.ResultType)
                    .Distinct()
                    .ToArray()))
            .Where(static candidate => candidate.Types is
                [
                    {
                        Kind: TypeRefKind.ByRef,
                    },
                ])
            .ToDictionary(
                static candidate => candidate.Slot,
                static candidate => candidate.Types[0]!);
        _copies = outerSlots.OfType<StoreStackSlot>()
            .Where(static store => store.Value is LoadStackSlot)
            .Select(static store => (store.Slot, ((LoadStackSlot)store.Value).Slot))
            .ToArray();
    }

    internal static SlotMaterializationInvariant Capture(IrFunction function) => new(function);

    internal void Check()
    {
        var locals = _function.Locals;
        if (locals.Length < _locals.Length || !_locals.SequenceEqual(locals.Take(_locals.Length)))
            Fail("existing local types changed");

        // Infer the correspondence from the two trees, not BuildPlan or the
        // rewrite's slot-to-local dictionary. Null means the web stayed a slot.
        var bindings = new Dictionary<int, int?>();
        var localOwners = new Dictionary<int, int>();
        using var current = _function.Descendants.Prepend(_function).GetEnumerator();
        foreach (var node in _nodes)
        {
            if (!current.MoveNext())
                Fail("ordered tree lost nodes");
            var after = current.Current;
            if (node.ChildCount != after.Children.Count)
                Fail($"child count changed at {node.Original.Describe()}");

            if (node.OuterSlot && node.Original is StoreStackSlot store && after is StoreLocal localStore)
                Bind(store.Slot, localStore.Index, localStore.Type);
            else if (node.OuterSlot && node.Original is LoadStackSlot load && after is LoadLocal localLoad)
                Bind(load.Slot, localLoad.Index, localLoad.Type);
            else
            {
                if (!ReferenceEquals(node.Original, after))
                    Fail($"ordered node identity changed at {node.Original.Describe()}");
                if (node.OuterSlot)
                {
                    int slot = node.Original is StoreStackSlot originalStore
                        ? originalStore.Slot : ((LoadStackSlot)node.Original).Slot;
                    RecordBinding(slot, null);
                }
            }
        }
        if (current.MoveNext())
            Fail("ordered tree gained nodes");
        if (locals.Length - _locals.Length != localOwners.Count)
            Fail("appended locals do not correspond one-to-one with converted webs");
        foreach (var (destination, source) in _copies)
        {
            if (bindings[destination].HasValue != bindings[source].HasValue)
                Fail($"direct-copy component was only partly materialized: S_{source} -> S_{destination}");
        }

        void Bind(int slot, int index, TypeRef type)
        {
            if (index < _locals.Length || index >= locals.Length)
                Fail($"S_{slot} does not refer to a fresh local");
            if (!type.Equals(locals[index]))
                Fail($"S_{slot} reference disagrees with its local table type");
            bool observerTestified =
                _testimony.TryGetValue(slot, out var testimony)
                && testimony.Status
                    == CoercionSinks.SlotTypeTestimonyStatus.Decided
                && type.Equals(testimony.Type);
            bool producerTestified =
                _producerOnlyManagedReferences.TryGetValue(
                    slot,
                    out var producerType)
                && type.Equals(producerType);
            if (!observerTestified && !producerTestified)
                Fail($"S_{slot} local type disagrees with pre-rewrite testimony");
            RecordBinding(slot, index);
            if (localOwners.TryGetValue(index, out int owner) && owner != slot)
                Fail($"local {index} is shared by different slots S_{owner} and S_{slot}");
            localOwners[index] = slot;
        }

        void RecordBinding(int slot, int? index)
        {
            if (bindings.TryGetValue(slot, out int? previous) && previous != index)
                Fail($"S_{slot} has inconsistent storage bindings");
            bindings[slot] = index;
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    void Fail(string message) =>
        throw new InvalidOperationException($"Slot materialization invariant in {_function.Name}: {message}.");
}
