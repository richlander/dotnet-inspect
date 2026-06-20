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
/// in expression position (a <c>return</c>/argument initializer). It covers named
/// members (<c>X = a</c>), indexer members (<c>[k] = v</c>), single-element
/// collection <c>Add</c>s, and multi-argument <c>Add</c>s (the dictionary
/// <c>{ k, v }</c> form). It also covers <em>nested</em> initializers
/// (<c>Inner = { X = a }</c> / <c>Items = { e0, e1 }</c>): stores/Adds rooted at a
/// member <em>read</em> off the threaded reference rather than the reference
/// itself, contiguous runs on the same member folded into one
/// <see cref="InitializerBlock"/>. Named-local initializers are left for a later
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
        IReadOnlyList<EntryPlan> Entries,
        LoadStackSlot Use);

    /// <summary>
    /// A planned initializer entry. A flat entry carries its leaf <see cref="Arguments"/>
    /// directly (<see cref="Block"/> null); a nested entry (<c>Member = { ... }</c>)
    /// carries a <see cref="BlockPlan"/> and no direct arguments. Block construction
    /// is deferred to <see cref="Apply"/> so its leaf arguments are detached from
    /// their lowered statements before being reparented into the new IR.
    /// </summary>
    sealed record EntryPlan(string? Member, IReadOnlyList<IrExpression> Arguments, BlockPlan? Block);

    /// <summary>A nested initializer body: an object/collection brace group with no creation.</summary>
    sealed record BlockPlan(bool IsCollection, IReadOnlyList<EntryPlan> Entries);

    static Plan? TryBuild(IrFunction function, StoreStackSlot seed, NewObject creation)
    {
        var statements = seed.Parent!.Children;
        var aliasSlots = new HashSet<int> { seed.Slot };
        var consumed = new List<IrNode> { seed };
        var entries = new List<EntryPlan>();
        bool? isCollection = null;

        // A run of nested ops on the same member folds into one InitializerBlock
        // entry; this holds the run currently being accumulated.
        string? pendingMember = null;
        bool pendingBlockIsCollection = false;
        List<EntryPlan>? pendingInner = null;

        void FlushPending()
        {
            if (pendingInner is null)
                return;
            entries.Add(new EntryPlan(pendingMember, [], new BlockPlan(pendingBlockIsCollection, pendingInner)));
            pendingMember = null;
            pendingInner = null;
        }

        for (int i = seed.ChildIndex + 1; i < statements.Count; i++)
        {
            var statement = statements[i];

            // A dup of the threaded reference: sNew = LoadStackSlot(sKnown). These
            // interleave nested ops, so they do not break a pending nested run.
            if (statement is StoreStackSlot { Value: LoadStackSlot source } copy && aliasSlots.Contains(source.Slot))
            {
                aliasSlots.Add(copy.Slot);
                consumed.Add(copy);
                continue;
            }

            // A nested initializer op: a store/Add rooted at a member read off the
            // threaded reference (Inner = { ... } / Items = { ... }). Top level is
            // object form (the member is assigned via `=`), never collection.
            if (TryNestedOp(statement, aliasSlots) is { } nested)
            {
                if (isCollection == true)
                    break;
                isCollection = false;

                bool sameRun = pendingInner is not null
                    && pendingMember == nested.OuterMember
                    && pendingBlockIsCollection == nested.IsCollection;
                if (!sameRun)
                {
                    FlushPending();
                    pendingMember = nested.OuterMember;
                    pendingBlockIsCollection = nested.IsCollection;
                    pendingInner = [];
                }
                pendingInner!.Add(nested.Inner);
                consumed.Add(statement);
                continue;
            }

            // An object-initializer member store on the threaded reference — a named
            // member (X = v) or an indexer member ([k] = v).
            if (TryMemberStore(statement, aliasSlots) is { } member)
            {
                if (isCollection == true)
                    break;  // C# initializers are member-only or element-only, never mixed
                FlushPending();
                isCollection = false;
                entries.Add(member);
                consumed.Add(statement);
                continue;
            }

            // A collection-initializer element: receiver.Add(value, ...) on the reference.
            if (TryCollectionAdd(statement, aliasSlots) is { } element)
            {
                if (isCollection == false)
                    break;
                FlushPending();
                isCollection = true;
                entries.Add(element);
                consumed.Add(statement);
                continue;
            }

            break;
        }

        FlushPending();

        if (entries.Count == 0)
            return null;  // a bare `new T()` with no initializer — nothing to raise
        if (HasDuplicateNamedMembers(isCollection ?? false, entries))
            return null;  // duplicate member initializers do not compile in C#

        // A self-referential entry (t.Next = t) cannot fold into a single expression.
        foreach (var leaf in LeafArguments(entries))
            if (ReferencesAnySlot(leaf, aliasSlots))
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

    sealed record NestedOp(string OuterMember, bool IsCollection, EntryPlan Inner);

    /// <summary>
    /// Matches a store/Add whose target reads a member off the threaded reference
    /// (the nested-initializer shape), returning the outer member name and the
    /// inner entry to accumulate. A plain property/field read distinguishes a
    /// nested op from a flat one (which targets the reference directly).
    /// </summary>
    static NestedOp? TryNestedOp(IrNode statement, HashSet<int> aliasSlots)
    {
        switch (statement)
        {
            // Nested object member store: outer.Member.X = v / outer.Member[k] = v.
            case StoreProperty { HasInstance: true } property
                when OuterMemberOffSlot(property.Instance, aliasSlots) is { } outer:
                var objectInner = property.IndexArguments.Count != 0
                    ? new EntryPlan(null, [.. property.IndexArguments, property.Value], null)
                    : new EntryPlan(property.PropertyName, [property.Value], null);
                return new NestedOp(outer, IsCollection: false, objectInner);

            case StoreField { HasInstance: true } field
                when OuterMemberOffSlot(field.Instance, aliasSlots) is { } outer:
                return new NestedOp(outer, IsCollection: false, new EntryPlan(field.Field.Name, [field.Value], null));

            // Nested collection element: outer.Member.Add(v, ...).
            case ExpressionStatement { Expression: Call { Callee.HasThis: true } call }
                when call.Callee.Name == "Add" && call.Arguments.Count >= 2
                    && OuterMemberOffSlot(call.Arguments[0], aliasSlots) is { } outer:
                return new NestedOp(outer, IsCollection: true, new EntryPlan(null, [.. call.Arguments.Skip(1)], null));

            default:
                return null;
        }
    }

    /// <summary>The member name when <paramref name="instance"/> reads a plain property/field off a threaded slot; otherwise null.</summary>
    static string? OuterMemberOffSlot(IrExpression? instance, HashSet<int> aliasSlots) => instance switch
    {
        LoadProperty { HasInstance: true, Instance: LoadStackSlot slot } property
            when aliasSlots.Contains(slot.Slot) && property.IndexArguments.Count == 0
            => property.PropertyName,
        LoadField { Instance: LoadStackSlot slot } field
            when aliasSlots.Contains(slot.Slot)
            => field.Field.Name,
        _ => null,
    };

    static EntryPlan? TryMemberStore(IrNode statement, HashSet<int> aliasSlots) => statement switch
    {
        // An indexer member `[k0, k1] = v`: the keys precede the value.
        StoreProperty { HasInstance: true, Instance: LoadStackSlot slot } property
            when aliasSlots.Contains(slot.Slot) && property.IndexArguments.Count != 0
            => new EntryPlan(null, [.. property.IndexArguments, property.Value], null),
        StoreProperty { HasInstance: true, Instance: LoadStackSlot slot } property
            when aliasSlots.Contains(slot.Slot)
            => new EntryPlan(property.PropertyName, [property.Value], null),
        StoreField { HasInstance: true, Instance: LoadStackSlot slot } field
            when aliasSlots.Contains(slot.Slot)
            => new EntryPlan(field.Field.Name, [field.Value], null),
        _ => null,
    };

    static EntryPlan? TryCollectionAdd(IrNode statement, HashSet<int> aliasSlots)
    {
        if (statement is not ExpressionStatement { Expression: Call { Callee.HasThis: true } call })
            return null;
        if (call.Callee.Name != "Add" || call.Arguments.Count < 2)
            return null;  // receiver + at least one value; multi-value Add is the dictionary form
        if (call.Arguments[0] is not LoadStackSlot receiver || !aliasSlots.Contains(receiver.Slot))
            return null;
        return new EntryPlan(null, [.. call.Arguments.Skip(1)], null);
    }

    static bool HasDuplicateNamedMembers(bool isCollection, IEnumerable<EntryPlan> entries)
    {
        if (isCollection)
            return entries.Any(e => e.Block is { } block && HasDuplicateNamedMembers(block.IsCollection, block.Entries));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Member is { } member && !seen.Add(member))
                return true;
            if (entry.Block is { } block && HasDuplicateNamedMembers(block.IsCollection, block.Entries))
                return true;
        }
        return false;
    }

    /// <summary>Every leaf argument expression across the entry tree, flattening nested blocks.</summary>
    static IEnumerable<IrExpression> LeafArguments(IEnumerable<EntryPlan> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Block is { } block)
            {
                foreach (var leaf in LeafArguments(block.Entries))
                    yield return leaf;
            }
            else
            {
                foreach (var argument in entry.Arguments)
                    yield return argument;
            }
        }
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
        // Drop the lowered run from the block, then lift the creation and leaf
        // arguments out of those now-detached statements before reparenting them
        // into the new initializer tree.
        foreach (var statement in plan.Consumed)
            statement.Detach();

        plan.Creation.Detach();
        foreach (var leaf in LeafArguments(plan.Entries))
            leaf.Detach();

        var entries = plan.Entries.Select(BuildEntry).ToList();
        var initializer = new ObjectInitializerExpression(plan.Creation, plan.IsCollection, entries);
        initializer.InheritSourceOffset(plan.Creation);
        plan.Use.ReplaceWith(initializer);
    }

    static InitializerEntry BuildEntry(EntryPlan entry)
        => entry.Block is { } block
            ? new InitializerEntry(entry.Member, [BuildBlock(block)])
            : new InitializerEntry(entry.Member, entry.Arguments);

    static InitializerBlock BuildBlock(BlockPlan block)
        => new(block.IsCollection, block.Entries.Select(BuildEntry).ToList());
}
