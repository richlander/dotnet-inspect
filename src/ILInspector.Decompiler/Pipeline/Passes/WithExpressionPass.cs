namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's record <c>with</c> lowering back into
/// <c>receiver with { X = value, ... }</c>. The supported shape is a synthesized
/// record clone stored to one stack slot, followed by member stores on that slot
/// and exactly one downstream load of the mutated clone.
/// </summary>
public sealed class WithExpressionPass : IIrPass
{
    public string Name => "with-expression";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var seed in function.Descendants.OfType<StoreStackSlot>().Reverse().ToList())
        {
            if (seed.Parent is not Block || seed.Value is not Call clone)
                continue;

            if (TryBuild(function, seed, clone) is not { } plan)
                continue;

            context.Stepper.StepOver($"raise record with-expression for stack slot {seed.Slot}", seed);
            Apply(plan);
        }
    }

    sealed record Plan(
        IReadOnlyList<IrNode> Consumed,
        IrExpression Receiver,
        IReadOnlyList<InitializerEntry> Entries,
        LoadStackSlot Use);

    static Plan? TryBuild(IrFunction function, StoreStackSlot seed, Call clone)
    {
        if (!IsRecordCloneCall(clone))
            return null;

        var statements = seed.Parent!.Children;
        var entries = new List<InitializerEntry>();
        var consumed = new List<IrNode> { seed };

        for (int i = seed.ChildIndex + 1; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (TryMemberStore(statement, seed.Slot) is { } member)
            {
                entries.Add(member);
                consumed.Add(statement);
                continue;
            }

            break;
        }

        if (ReferencesSlot(clone.Arguments[0], seed.Slot))
            return null;

        if (entries.Count == 0 || HasDuplicateMembers(entries))
            return null;

        foreach (var entry in entries)
            if (ReferencesSlot(entry.Arguments[0], seed.Slot))
                return null;

        var consumedSet = consumed.ToHashSet();
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
            if (store.Slot == seed.Slot && !consumedSet.Contains(store) && !HasAncestorIn(store, consumedSet))
                return null;

        var outsideUses = function.Descendants.OfType<LoadStackSlot>()
            .Where(load => load.Slot == seed.Slot && !HasAncestorIn(load, consumedSet))
            .ToList();
        if (outsideUses.Count != 1)
            return null;

        IrNode escapeStatement = outsideUses[0];
        while (escapeStatement.Parent is { } parent && !ReferenceEquals(parent, seed.Parent))
            escapeStatement = parent;
        if (!ReferenceEquals(escapeStatement.Parent, seed.Parent))
            return null;

        for (int i = seed.ChildIndex + 1; i < escapeStatement.ChildIndex; i++)
            if (!consumedSet.Contains(statements[i]))
                return null;

        return new Plan(consumed, clone.Arguments[0], entries, outsideUses[0]);
    }

    static bool IsRecordCloneCall(Call clone)
    {
        if (!GeneratedCodeIdentity.IsRecordCloneMethod(clone.Callee) || clone.Arguments is not [var receiver])
            return false;

        var receiverType = receiver.ResultType;
        return receiverType is not null
            && receiverType.Equals(clone.Callee.DeclaringType)
            && receiverType.Equals(clone.Callee.ReturnType);
    }

    static InitializerEntry? TryMemberStore(IrNode statement, int slot) => statement switch
    {
        StoreProperty { HasInstance: true, Instance: LoadStackSlot receiver } property
            when receiver.Slot == slot
                && property.IndexArguments.Count == 0
                && ObjectInitializerPass.IsInitializerSpellable(property)
            => new InitializerEntry(property.PropertyName, [property.Value]),

        StoreField { Instance: LoadStackSlot receiver } field
            when receiver.Slot == slot && CSharpNaming.IsUsableIdentifier(field.Field.Name)
            => new InitializerEntry(field.Field.Name, [field.Value]),

        _ => null,
    };

    static bool HasDuplicateMembers(IEnumerable<InitializerEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (entry.Member is null || !seen.Add(entry.Member))
                return true;
        return false;
    }

    static bool ReferencesSlot(IrNode node, int slot)
        => (node is LoadStackSlot load && load.Slot == slot)
            || node.Descendants.OfType<LoadStackSlot>().Any(descendant => descendant.Slot == slot);

    static bool HasAncestorIn(IrNode node, HashSet<IrNode> set)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (set.Contains(parent))
                return true;
        return false;
    }

    static void Apply(Plan plan)
    {
        foreach (var statement in plan.Consumed)
            statement.Detach();

        plan.Receiver.Detach();
        foreach (var entry in plan.Entries)
            entry.Arguments[0].Detach();

        var withExpression = new WithExpression(plan.Receiver, plan.Entries);
        withExpression.InheritSourceOffset(plan.Receiver);
        plan.Use.ReplaceWith(withExpression);
    }
}
