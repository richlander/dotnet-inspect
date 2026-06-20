namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's object/collection-initializer lowering back into
/// <c>new T { X = a, ... }</c> / <c>new C { e0, e1, ... }</c>. The lowering
/// threads a freshly-constructed reference through a dup chain — modeled here as
/// stack slots — applying a contiguous run of member stores (object form) or
/// single-argument <c>Add</c> calls (collection form) to the threaded reference,
/// then consuming it exactly once downstream. This pass folds that run into an
/// initializer at the single use site and removes the now-dead chain.
///
/// <para>Slice scope: the stack-slot dup form, which is what the compiler emits
/// in expression position (a <c>return</c>/argument initializer). Named-local
/// initializers, indexer-element initializers, nested initializers, and
/// multi-argument <c>Add</c> (dictionary) initializers are left for a later
/// slice — those shapes simply fail to match and stay lowered.</para>
///
/// <para>Runs after <see cref="PropertySugarPass"/> so property setters are
/// already <see cref="StoreProperty"/> nodes, uniform with field stores.</para>
/// </summary>
public sealed class ObjectInitializerPass : IIrPass
{
    public string Name => "object-initializer";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var seed in function.Descendants.OfType<StoreStackSlot>().ToList())
        {
            if (seed.Parent is not Block || seed.Value is not NewObject creation)
                continue;

            if (TryBuild(function, seed, creation) is not { } plan)
                continue;

            context.Stepper.StepOver(
                $"raise {creation.Constructor.DeclaringType.Name} {(plan.IsCollection ? "collection" : "object")} initializer", seed);
            Apply(plan);
        }
    }

    sealed record Plan(
        IReadOnlyList<IrNode> Consumed,
        NewObject Creation,
        bool IsCollection,
        IReadOnlyList<(string? Member, IrExpression Value)> Entries,
        LoadStackSlot Use);

    static Plan? TryBuild(IrFunction function, StoreStackSlot seed, NewObject creation)
    {
        var statements = seed.Parent!.Children;
        var aliasSlots = new HashSet<int> { seed.Slot };
        var consumed = new List<IrNode> { seed };
        var entries = new List<(string? Member, IrExpression Value)>();
        bool? isCollection = null;

        for (int i = seed.ChildIndex + 1; i < statements.Count; i++)
        {
            var statement = statements[i];

            // A dup of the threaded reference: sNew = LoadStackSlot(sKnown).
            if (statement is StoreStackSlot { Value: LoadStackSlot source } copy && aliasSlots.Contains(source.Slot))
            {
                aliasSlots.Add(copy.Slot);
                consumed.Add(copy);
                continue;
            }

            // An object-initializer member store on the threaded reference.
            if (TryMemberStore(statement, aliasSlots) is { } member)
            {
                if (isCollection == true)
                    break;  // C# initializers are member-only or element-only, never mixed
                isCollection = false;
                entries.Add(member);
                consumed.Add(statement);
                continue;
            }

            // A collection-initializer element: receiver.Add(value) on the reference.
            if (TryCollectionAdd(statement, aliasSlots) is { } element)
            {
                if (isCollection == false)
                    break;
                isCollection = true;
                entries.Add(element);
                consumed.Add(statement);
                continue;
            }

            break;
        }

        if (entries.Count == 0)
            return null;  // a bare `new T()` with no initializer — nothing to raise

        // A self-referential entry (t.Next = t) cannot fold into a single expression.
        foreach (var (_, value) in entries)
            if (ReferencesAnySlot(value, aliasSlots))
                return null;

        // The threaded reference must escape the run exactly once: that single
        // downstream load is where the initializer expression belongs.
        var consumedSet = consumed.ToHashSet();
        var outsideUses = function.Descendants.OfType<LoadStackSlot>()
            .Where(load => aliasSlots.Contains(load.Slot) && !HasAncestorIn(load, consumedSet))
            .ToList();
        if (outsideUses.Count != 1)
            return null;

        return new Plan(consumed, creation, isCollection ?? false, entries, outsideUses[0]);
    }

    static (string? Member, IrExpression Value)? TryMemberStore(IrNode statement, HashSet<int> aliasSlots) => statement switch
    {
        StoreProperty { HasInstance: true, Instance: LoadStackSlot slot } property
            when aliasSlots.Contains(slot.Slot) && property.IndexArguments.Count == 0
            => (property.PropertyName, property.Value),
        StoreField { HasInstance: true, Instance: LoadStackSlot slot } field
            when aliasSlots.Contains(slot.Slot)
            => (field.Field.Name, field.Value),
        _ => null,
    };

    static (string? Member, IrExpression Value)? TryCollectionAdd(IrNode statement, HashSet<int> aliasSlots)
    {
        if (statement is not ExpressionStatement { Expression: Call { Callee.HasThis: true } call })
            return null;
        if (call.Callee.Name != "Add" || call.Arguments.Count != 2)
            return null;  // single-element Add only (receiver + one value); dictionary Add(k, v) is a later slice
        if (call.Arguments[0] is not LoadStackSlot receiver || !aliasSlots.Contains(receiver.Slot))
            return null;
        return (null, call.Arguments[1]);
    }

    static bool ReferencesAnySlot(IrNode node, HashSet<int> slots)
        => (node is LoadStackSlot load && slots.Contains(load.Slot))
            || node.Descendants.OfType<LoadStackSlot>().Any(descendant => slots.Contains(descendant.Slot));

    static bool HasAncestorIn(IrNode node, HashSet<IrNode> set)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (set.Contains(parent))
                return true;
        return false;
    }

    static void Apply(Plan plan)
    {
        // Drop the lowered run from the block, then lift the creation and entry
        // values out of those now-detached statements into the initializer.
        foreach (var statement in plan.Consumed)
            statement.Detach();

        plan.Creation.Detach();
        var entries = new List<(string?, IrExpression)>(plan.Entries.Count);
        foreach (var (member, value) in plan.Entries)
        {
            value.Detach();
            entries.Add((member, value));
        }

        var initializer = new ObjectInitializerExpression(plan.Creation, plan.IsCollection, entries);
        initializer.InheritSourceOffset(plan.Creation);
        plan.Use.ReplaceWith(initializer);
    }
}
