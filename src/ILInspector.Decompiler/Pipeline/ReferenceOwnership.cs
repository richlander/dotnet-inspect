namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Shared reference-scope atoms for proof-backed rewrites that consume compiler
/// scaffolds and must prove their temporaries do not escape the owned shape.
/// Intentionally small: this is not a general ownership framework, only the
/// repeated local/stack-slot location proof the passes already composed by hand.
/// </summary>
public static class ReferenceOwnership
{
    public static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    public static bool IsInsideAny(IrNode node, IEnumerable<IrNode> roots)
        => roots.Any(root => IsInside(node, root));

    public static bool ReferencesLocal(IrNode node, int index) => node switch
    {
        LoadLocal load => load.Index == index,
        StoreLocal store => store.Index == index,
        LoadLocalAddress address => address.Index == index,
        _ => false,
    };

    public static bool ReferencesStackSlot(IrNode node, int slot) => node switch
    {
        LoadStackSlot load => load.Slot == slot,
        StoreStackSlot store => store.Slot == slot,
        _ => false,
    };

    public static bool SubtreeReferencesLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => ReferencesLocal(node, index));

    public static bool SubtreeStoresLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => node is StoreLocal store && store.Index == index);

    public static bool LocalReferencesOnlyWithin(IrFunction function, int index, IReadOnlyCollection<IrNode> allowed)
        => ReferencesOnlyWithin(function, node => ReferencesLocal(node, index), allowed);

    public static bool StackSlotReferencesOnlyWithin(IrFunction function, int slot, IReadOnlyCollection<IrNode> allowed)
        => ReferencesOnlyWithin(function, node => ReferencesStackSlot(node, slot), allowed);

    public static bool ReferencesOnlyWithin(IrFunction function, Func<IrNode, bool> isReference, IReadOnlyCollection<IrNode> allowed)
    {
        foreach (var node in function.Descendants)
        {
            if (isReference(node) && !IsInsideAny(node, allowed))
                return false;
        }
        return true;
    }
}
