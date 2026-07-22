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

    /// <summary>
    /// Whether the node sits inside a nested <see cref="Lambda"/> or
    /// <see cref="LocalFunctionStatement"/> body. Nested bodies carry their own
    /// slot numbering and return types (the per-body-scope rule), so a
    /// function-scope fact map — slot testimony, locals, return type — must not
    /// be consulted for nodes past this boundary.
    /// </summary>
    public static bool IsInsideNestedFunctionBody(IrNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is Lambda or LocalFunctionStatement)
                return true;
        }
        return false;
    }

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

    /// <summary>
    /// Whether the node binds the local <paramref name="index"/> through a
    /// designation that carries the index directly — a pattern binding, a
    /// foreach / using / fixed header, a deconstruction target, a catch
    /// variable, or a null-coalescing-assignment target — rather than an
    /// explicit <see cref="LoadLocal"/>/<see cref="StoreLocal"/>/<see
    /// cref="LoadLocalAddress"/>. Mirrors the designation set the C# printer
    /// treats as owning or declaring a local, so a confinement proof cannot be
    /// blinded by a reference expressed through one of these node kinds.
    /// </summary>
    public static bool BindsLocal(IrNode node, int index) => node switch
    {
        NullCoalescingAssignment n => n.LocalIndex == index,
        ForeachStatement f => f.LocalIndex == index,
        UsingStatement u => u.LocalIndex == index,
        Fixed fx => fx.LocalIndex == index,
        IsPattern p => p.LocalIndex == index,
        RecursivePropertyDeclarationPattern r => r.LocalIndex == index,
        UnionSwitchExpressionArm a => a.LocalIndex == index,
        PatternSwitchExpressionArm p => p.LocalIndex == index || p.Subpattern?.LocalIndex == index,
        CatchClause c => c.VariableIndex == index,
        DeconstructionAssignment d => d.LocalIndices.Contains(index),
        _ => false,
    };

    public static bool ReferencesOrBindsLocal(IrNode node, int index)
        => ReferencesLocal(node, index) || BindsLocal(node, index);

    public static bool SubtreeReferencesLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => ReferencesLocal(node, index));

    public static bool SubtreeReferencesOrBindsLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => ReferencesOrBindsLocal(node, index));

    public static bool SubtreeStoresLocal(IrNode root, int index)
        => root.Descendants.Prepend(root).Any(node => node is StoreLocal store && store.Index == index);

    public static bool LocalReferencesOnlyWithin(IrFunction function, int index, IReadOnlyCollection<IrNode> allowed)
        => ReferencesOnlyWithin(function, node => ReferencesLocal(node, index), allowed);

    public static bool LocalReferencedOrBoundOnlyWithin(IrFunction function, int index, IReadOnlyCollection<IrNode> allowed)
        => ReferencesOnlyWithin(function, node => ReferencesOrBindsLocal(node, index), allowed);

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
