namespace ILInspector.Decompiler.Pipeline;

internal static class UnsafeAwaitOperand
{
    public static bool ContainsAwait(IrNode root)
        => IsAwaitSyntax(root) || root.Descendants.Any(IsAwaitSyntax);

    static bool IsAwaitSyntax(IrNode node)
        => node is AwaitExpression
            or UsingStatement { IsAwait: true }
            or ForeachStatement { IsAwait: true };

    public static bool WouldPlaceAwaitInUnsafeContext(
        IrNode root,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit = false)
        => ContainsAwait(root)
            && RequiresUnsafeContext(
                root,
                usesUpdatedMemorySafetyRules,
                skipLocalsInit);

    public static bool RequiresUnsafeContext(
        IrNode root,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit = false)
        => ContainsUnsafeOperation(
            root,
            usesUpdatedMemorySafetyRules,
            skipLocalsInit);

    static bool ContainsUnsafeOperation(
        IrNode root,
        bool usesUpdatedMemorySafetyRules,
        bool skipLocalsInit)
    {
        var evidence = new List<ConsumedMemberEvidence>();
        foreach (var node in root.DescendantsAndSelfOutsideNestedFunctions)
        {
            if (OperationMemorySafetyContract.RequiresUnsafe(
                    node,
                    usesUpdatedMemorySafetyRules,
                    skipLocalsInit,
                    evidence))
                return true;
            if (!usesUpdatedMemorySafetyRules && IsLegacyPointerOperation(node))
                return true;
        }
        return false;
    }

    internal static bool IsLegacyPointerOperation(IrNode node)
        => OperationMemorySafetyContract.IsLegacyPointerOperation(node);

    internal static bool CanScopeLegacyPointerLocal(
        IrFunction function,
        StoreLocal store)
        => !ReferencesLocal(store.Value, store.Index)
            && ReferencesStayInAwaitFreeStoreRange(
                function,
                store,
                candidate => candidate is StoreLocal local
                    && local.Index == store.Index
                    || candidate is LoadLocal load
                        && load.Index == store.Index
                    || candidate is LoadLocalAddress address
                        && address.Index == store.Index);

    internal static bool CanScopeLegacyPointerStackSlot(
        IrFunction function,
        StoreStackSlot store)
        => !ReferencesStackSlot(store.Value, store.Slot)
            && ReferencesStayInAwaitFreeStoreRange(
                function,
                store,
                candidate => candidate is StoreStackSlot slotStore
                    && slotStore.Slot == store.Slot
                    || candidate is LoadStackSlot load
                        && load.Slot == store.Slot);

    static bool ReferencesStayInAwaitFreeStoreRange(
        IrFunction function,
        IrNode store,
        Func<IrNode, bool> isReference)
    {
        if (!TryGetDirectBlockChild(
                store,
                out var block,
                out var storeStatement,
                out int storeIndex))
            return false;

        var references = function.DescendantsOutsideNestedFunctions
            .Where(isReference)
            .ToList();
        if (!ReferenceEquals(storeStatement, store)
            && references.Any(reference => !IsInside(reference, storeStatement)))
        {
            return false;
        }
        int lastReference = storeIndex;
        foreach (var reference in references)
        {
            int statementIndex = DirectChildIndex(block, reference);
            if (statementIndex < storeIndex)
                return false;
            lastReference = Math.Max(lastReference, statementIndex);
        }

        var range = block.Children
            .Skip(storeIndex)
            .Take(lastReference - storeIndex + 1)
            .ToList();
        return !range.Any(ContainsAwait)
            && !range.SelectMany(
                    statement => statement.DescendantsAndSelfOutsideNestedFunctions)
                .Any(node => node is Branch
                    or ConditionalBranch
                    or SwitchBranch
                    or Leave);
    }

    static bool TryGetDirectBlockChild(
        IrNode node,
        out Block block,
        out IrNode statement,
        out int childIndex)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
        {
            if (current.Parent is not Block parent || current.ChildIndex < 0)
                continue;
            block = parent;
            statement = current;
            childIndex = current.ChildIndex;
            return true;
        }
        block = null!;
        statement = null!;
        childIndex = -1;
        return false;
    }

    static bool IsInside(IrNode node, IrNode ancestor)
    {
        for (IrNode? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    static int DirectChildIndex(Block block, IrNode node)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, block))
                return current.ChildIndex;
        }
        return -1;
    }

    static bool ReferencesLocal(IrNode node, int index)
        => node.DescendantsAndSelfOutsideNestedFunctions.Any(candidate =>
            candidate is StoreLocal store && store.Index == index
            || candidate is LoadLocal load && load.Index == index
            || candidate is LoadLocalAddress address && address.Index == index);

    static bool ReferencesStackSlot(IrNode node, int slot)
        => node.DescendantsAndSelfOutsideNestedFunctions.Any(candidate =>
            candidate is StoreStackSlot store && store.Slot == slot
            || candidate is LoadStackSlot load && load.Slot == slot);

    internal static bool MethodRequiresUnsafe(
        MethodRef method,
        bool usesUpdatedMemorySafetyRules)
        => OperationMemorySafetyContract.MethodRequiresUnsafe(
            method,
            usesUpdatedMemorySafetyRules);

    internal static bool ContainsPointer(TypeRef? type)
        => OperationMemorySafetyContract.ContainsPointer(type);

}
