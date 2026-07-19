namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's record <c>with</c> lowering back into
/// <c>receiver with { X = value, ... }</c>. The supported shape is a synthesized
/// record clone threaded through a stack-slot dup chain, followed by member
/// stores on those slots and exactly one downstream load of the mutated clone.
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
        var aliasSlots = new HashSet<int> { seed.Slot };
        var entries = new List<InitializerEntry>();
        var consumed = new List<IrNode> { seed };

        for (int i = seed.ChildIndex + 1; i < statements.Count; i++)
        {
            var statement = statements[i];

            if (statement is StoreStackSlot { Value: LoadStackSlot source } copy && aliasSlots.Contains(source.Slot))
            {
                aliasSlots.Add(copy.Slot);
                consumed.Add(copy);
                continue;
            }

            if (TryMemberStore(statement, aliasSlots) is { } member)
            {
                entries.Add(member);
                consumed.Add(statement);
                continue;
            }

            break;
        }

        if (ReferencesAnySlot(clone.Arguments[0], aliasSlots))
            return null;

        if (entries.Count == 0 || HasDuplicateMembers(entries))
            return null;

        foreach (var entry in entries)
            if (ReferencesAnySlot(entry.Arguments[0], aliasSlots))
                return null;

        var consumedSet = consumed.ToHashSet();
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
            if (aliasSlots.Contains(store.Slot) && !ReferenceOwnership.IsInsideAny(store, consumed))
                return null;

        var outsideUses = function.Descendants.OfType<LoadStackSlot>()
            .Where(load => aliasSlots.Contains(load.Slot) && !ReferenceOwnership.IsInsideAny(load, consumed))
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

    static InitializerEntry? TryMemberStore(IrNode statement, HashSet<int> aliasSlots) => statement switch
    {
        StoreProperty { HasInstance: true, Instance: LoadStackSlot receiver } property
            when aliasSlots.Contains(receiver.Slot)
                && property.IndexArguments.Count == 0
                && ObjectInitializerPass.IsInitializerSpellable(property)
            => new InitializerEntry(property.PropertyName, [property.Value], ConsumedMethod: property.Accessor),

        StoreField { Instance: LoadStackSlot receiver } field
            when aliasSlots.Contains(receiver.Slot) && CSharpNaming.IsEscapableIdentifier(field.Field.Name)
            => new InitializerEntry(field.Field.Name, [field.Value], ConsumedField: field.Field),

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

    static bool ReferencesAnySlot(IrNode node, HashSet<int> slots)
        => (node is LoadStackSlot load && slots.Contains(load.Slot))
            || node.Descendants.OfType<LoadStackSlot>().Any(descendant => slots.Contains(descendant.Slot));

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
