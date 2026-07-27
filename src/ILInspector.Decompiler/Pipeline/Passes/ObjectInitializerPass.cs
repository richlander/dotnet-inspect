namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's object/collection-initializer lowering back into
/// <c>new T { X = a, ... }</c> / <c>new C { e0, e1, ... }</c>. The lowering
/// threads a freshly-constructed reference through a dup chain — modeled here as
/// stack slots — or a fresh local, applying a contiguous run of member stores
/// (object form) or single-argument <c>Add</c> calls (collection form) to the
/// threaded reference, then consuming it exactly once downstream. This pass folds
/// that run into an initializer at the single use site when the escape is
/// contiguous, or at the original seed when a later single escape would otherwise
/// reorder intervening side effects, and removes the now-dead chain.
///
/// <para>Slice scope: the stack-slot dup form, which is what the compiler emits
/// in expression position (a <c>return</c>/argument initializer), plus the
/// single-use named-local form (a local declaration initialized with
/// <c>new T { ... }</c>). It covers named members (<c>X = a</c>), indexer
/// members (<c>[k] = v</c>), single-element collection <c>Add</c>s, and
/// multi-argument <c>Add</c>s (the dictionary <c>{ k, v }</c> form). It also
/// covers <em>nested</em> initializers
/// (<c>Inner = { X = a }</c> / <c>Items = { e0, e1 }</c>): stores/Adds rooted at a
/// member <em>read</em> off the threaded reference rather than the reference
/// itself, contiguous runs on the same member folded into one
/// <see cref="InitializerBlock"/>. Named locals with extra outside uses stay
/// lowered because there is no single expression site to replace safely.</para>
///
/// <para>Runs after <see cref="PropertySugarPass"/> so property setters are
/// already <see cref="StoreProperty"/> nodes, uniform with field stores.</para>
/// </summary>
public sealed class ObjectInitializerPass : IIrPass
{
    public string Name => "object-initializer";

    public void Run(IrFunction function, PassContext context)
    {
        // Process seeds inner-first. The compiler emits the outer `new T(...)`
        // before any nested `new U(...)` that feeds one of its members, so reverse
        // document order folds sub-initializers before their enclosing initializer
        // — letting a nested member's value (`Inner = new U { ... }`) be a raised
        // initializer by the time the outer run inspects that member store.
        foreach (var seed in function.Descendants.OfType<StoreStackSlot>().Reverse().ToList())
        {
            if (seed.Parent is not Block || seed.Value is not NewObject creation)
                continue;

            if (TryBuild(function, seed, creation) is not { } plan)
                continue;

            context.Stepper.StepOver(
                $"raise {creation.Constructor.DeclaringType.Name} {(plan.IsCollection ? "collection" : "object")} initializer", seed);
            Apply(plan);
        }

        foreach (var seed in function.Descendants.OfType<StoreLocal>().Reverse().ToList())
        {
            if (seed.Parent is not Block || seed.Value is not NewObject creation)
                continue;
            if (GeneratedCodeIdentity.IsDisplayClassName(creation.Constructor.DeclaringType))
                continue;

            if (TryBuild(function, seed, creation) is not { } plan)
                continue;

            context.Stepper.StepOver(
                $"raise named-local {creation.Constructor.DeclaringType.Name} {(plan.IsCollection ? "collection" : "object")} initializer", seed);
            Apply(plan);
        }
    }

    abstract record InitializerTarget;
    sealed record StackSlotTarget(LoadStackSlot Use) : InitializerTarget;
    sealed record StackSlotSeedTarget(StoreStackSlot Seed, LoadStackSlot Use) : InitializerTarget;
    sealed record LocalSeedTarget(StoreLocal Seed) : InitializerTarget;

    sealed record Plan(
        IReadOnlyList<IrNode> Consumed,
        NewObject Creation,
        bool IsCollection,
        IReadOnlyList<EntryPlan> Entries,
        InitializerTarget Target);

    /// <summary>
    /// A planned initializer entry. A flat entry carries its leaf <see cref="Arguments"/>
    /// directly (<see cref="Block"/> null); a nested entry (<c>Member = { ... }</c>)
    /// carries a <see cref="BlockPlan"/> and no direct arguments. Block construction
    /// is deferred to <see cref="Apply"/> so its leaf arguments are detached from
    /// their lowered statements before being reparented into the new IR.
    /// </summary>
    sealed record EntryPlan(
        string? Member,
        IReadOnlyList<IrExpression> Arguments,
        BlockPlan? Block,
        MethodRef? ConsumedMethod = null,
        FieldRef? ConsumedField = null);

    /// <summary>A nested initializer body: an object/collection brace group with no creation.</summary>
    sealed record BlockPlan(bool IsCollection, IReadOnlyList<EntryPlan> Entries);

    static Plan? TryBuild(IrFunction function, StoreStackSlot seed, NewObject creation)
    {
        var statements = seed.Parent!.Children;
        var aliasSlots = new HashSet<int> { seed.Slot };
        var consumed = new List<IrNode> { seed };
        var entries = new List<EntryPlan>();
        // Statements interleaved before the first initializer entry that are
        // independent of the threaded reference (a sibling constructor argument
        // spilled to its own slot because it was live on the stack beneath this dup
        // chain). They are left in place and the escape is treated as if they were
        // not present. See the skip branch below.
        var skipped = new List<IrNode>();
        bool? isCollection = null;

        // A run of nested ops on the same member folds into one InitializerBlock
        // entry; this holds the run currently being accumulated.
        EntryPlan? pendingOuter = null;
        bool pendingBlockIsCollection = false;
        List<EntryPlan>? pendingInner = null;

        void FlushPending()
        {
            if (pendingInner is null)
                return;
            entries.Add(pendingOuter! with { Block = new BlockPlan(pendingBlockIsCollection, pendingInner) });
            pendingOuter = null;
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
            if (TryNestedOp(function, statement, aliasSlots) is { } nested)
            {
                if (isCollection == true)
                    break;
                isCollection = false;

                bool sameRun = pendingInner is not null
                    && pendingOuter == nested.Outer
                    && pendingBlockIsCollection == nested.IsCollection;
                if (!sameRun)
                {
                    FlushPending();
                    pendingOuter = nested.Outer;
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
            if (TryCollectionAdd(function, statement, aliasSlots) is { } element)
            {
                if (isCollection == false)
                    break;
                FlushPending();
                isCollection = true;
                entries.Add(element);
                consumed.Add(statement);
                continue;
            }

            // A statement interleaved before the first initializer entry that neither
            // reads nor writes the threaded reference — e.g. `t = new(); a = Other();
            // t.X = ...` where `a` is a preceding constructor argument the stackifier
            // spilled because it was live beneath the dup chain. It cannot observe the
            // partially-built object, so leave it in place and skip it; the escape
            // logic below treats it as a permitted gap and folds via the use site
            // (never the seed), which keeps the member stores after this statement in
            // their original order. Only tolerated before the first entry so no member
            // store is ever reordered across it.
            //
            // Skipping is only sound when the statement's own computation already
            // executed BEFORE the `newobj` (its IL offset precedes the creation's).
            // Use-site folding moves the `newobj` after the skipped statement, so a
            // statement that originally ran after the `newobj` (e.g. `t = new();
            // SideEffect(); t.X = ...`, where Roslyn erased the named local into this
            // dup form) would have its construction reordered across it — observable
            // if the constructor or the statement has side effects. The offset guard
            // admits only genuine preceding-argument spills, which the compiler always
            // emits before the `newobj`.
            if (entries.Count == 0
                && pendingInner is null
                && !TouchesAnySlot(statement, aliasSlots)
                && ExecutesBefore(statement, creation))
            {
                skipped.Add(statement);
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

        var consumedSet = consumed.ToHashSet();

        // The threaded reference must not be clobbered between the run and its
        // escape: any store to an alias slot outside the consumed dup chain means
        // the slot was reused for an unrelated value, so a later load is not this
        // receiver and folding into it would drop the re-store. Dup slots are
        // unique per dup, so real dup-chain lowerings never hit this; carry-slot
        // reuse (and hand-written IL) can. The named-local form has the analogous
        // single-store guard below.
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
            if (aliasSlots.Contains(store.Slot)
                && !consumedSet.Contains(store)
                && !HasAncestorIn(store, consumedSet))
                return null;

        // The threaded reference must escape the run exactly once: that single
        // downstream load is where the initializer expression belongs.
        var outsideUses = function.Descendants.OfType<LoadStackSlot>()
            .Where(load => aliasSlots.Contains(load.Slot) && !HasAncestorIn(load, consumedSet))
            .ToList();
        if (outsideUses.Count != 1)
            return null;

        // When the escape immediately follows the consumed run, the initializer can be
        // moved to that use site (return/call argument position). When unrelated
        // statements sit between the run and the escape, materialize the initializer at
        // the original seed instead: that keeps member-value side effects before the
        // gap and still collapses the compiler temp chain into one initialized value.
        // Skipped pre-entry statements (see the loop) are permitted gaps: they must be
        // folded via the use site so the member stores stay after them, never via the
        // seed (which would hoist the member stores before them).
        var skippedSet = skipped.ToHashSet();
        IrNode escapeStatement = outsideUses[0];
        while (escapeStatement.Parent is { } parent && !ReferenceEquals(parent, seed.Parent))
            escapeStatement = parent;
        if (!ReferenceEquals(escapeStatement.Parent, seed.Parent))
            return null;   // the reference escapes in another block — not a contiguous run
        bool contiguousEscape = true;
        for (int i = seed.ChildIndex + 1; i < escapeStatement.ChildIndex; i++)
        {
            var between = statements[i];
            if (!consumedSet.Contains(between) && !skippedSet.Contains(between))
            {
                contiguousEscape = false;
                break;
            }
        }

        // A skip forces use-site folding. If the escape is not contiguous (a real gap
        // after the members that would need seed materialization), seed folding would
        // hoist the members before the skipped statement — unsound — so decline.
        if (!contiguousEscape && skipped.Count != 0)
            return null;

        return new Plan(
            consumed,
            creation,
            isCollection ?? false,
            entries,
            contiguousEscape
                ? new StackSlotTarget(outsideUses[0])
                : new StackSlotSeedTarget(seed, outsideUses[0]));
    }

    static Plan? TryBuild(IrFunction function, StoreLocal seed, NewObject creation)
    {
        var statements = seed.Parent!.Children;
        int index = seed.Index;
        var consumed = new List<IrNode>();
        var entries = new List<EntryPlan>();
        bool? isCollection = null;

        EntryPlan? pendingOuter = null;
        bool pendingBlockIsCollection = false;
        List<EntryPlan>? pendingInner = null;

        void FlushPending()
        {
            if (pendingInner is null)
                return;
            entries.Add(pendingOuter! with { Block = new BlockPlan(pendingBlockIsCollection, pendingInner) });
            pendingOuter = null;
            pendingInner = null;
        }

        for (int i = seed.ChildIndex + 1; i < statements.Count; i++)
        {
            var statement = statements[i];

            if (TryNestedOp(function, statement, index) is { } nested)
            {
                if (isCollection == true)
                    break;
                isCollection = false;

                bool sameRun = pendingInner is not null
                    && pendingOuter == nested.Outer
                    && pendingBlockIsCollection == nested.IsCollection;
                if (!sameRun)
                {
                    FlushPending();
                    pendingOuter = nested.Outer;
                    pendingBlockIsCollection = nested.IsCollection;
                    pendingInner = [];
                }
                pendingInner!.Add(nested.Inner);
                consumed.Add(statement);
                continue;
            }

            if (TryMemberStore(statement, index) is { } member)
            {
                if (isCollection == true)
                    break;
                FlushPending();
                isCollection = false;
                entries.Add(member);
                consumed.Add(statement);
                continue;
            }

            if (TryCollectionAdd(function, statement, index) is { } element)
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
            return null;
        if (HasDuplicateNamedMembers(isCollection ?? false, entries))
            return null;
        if (function.Descendants.OfType<StoreLocal>().Any(store => store.Index == index && !ReferenceEquals(store, seed)))
            return null;

        foreach (var leaf in LeafArguments(entries))
            if (ReferencesLocal(leaf, index))
                return null;

        var consumedSet = consumed.ToHashSet();
        var outsideUses = function.Descendants.OfType<LoadLocal>()
            .Where(load => load.Index == index && !HasAncestorIn(load, consumedSet))
            .ToList();
        if (outsideUses.Count != 1)
            return null;

        return new Plan(consumed, creation, isCollection ?? false, entries, new LocalSeedTarget(seed));
    }

    sealed record NestedOp(EntryPlan Outer, bool IsCollection, EntryPlan Inner);

    sealed record OuterMember(string Name, MethodRef? Method, FieldRef? Field);

    /// <summary>
    /// Matches a store/Add whose target reads a member off the threaded reference
    /// (the nested-initializer shape), returning the outer member name and the
    /// inner entry to accumulate. A plain property/field read distinguishes a
    /// nested op from a flat one (which targets the reference directly).
    /// </summary>
    static NestedOp? TryNestedOp(IrFunction function, IrNode statement, HashSet<int> aliasSlots)
    {
        switch (statement)
        {
            // Nested object member store: outer.Member.X = v / outer.Member[k] = v.
            case StoreProperty { HasInstance: true } property
                when IsInitializerSpellable(property) && OuterMemberOffSlot(property.Instance, aliasSlots) is { } outer:
                var objectInner = property.IndexArguments.Count != 0
                    ? new EntryPlan(null, [.. property.IndexArguments, property.Value], null, ConsumedMethod: property.Accessor)
                    : new EntryPlan(property.PropertyName, [property.Value], null, ConsumedMethod: property.Accessor);
                return new NestedOp(new EntryPlan(outer.Name, [], null, outer.Method, outer.Field), IsCollection: false, objectInner);

            case StoreField { HasInstance: true } field
                when OuterMemberOffSlot(field.Instance, aliasSlots) is { } outer:
                return new NestedOp(
                    new EntryPlan(outer.Name, [], null, outer.Method, outer.Field),
                    IsCollection: false,
                    new EntryPlan(field.Field.Name, [field.Value], null, ConsumedField: field.Field));

            // Nested collection element: outer.Member.Add(v, ...).
            case ExpressionStatement { Expression: Call { Callee.HasThis: true } call }
                when IsCollectionAdd(function, call)
                    && OuterMemberOffSlot(call.Arguments[0], aliasSlots) is { } outer:
                return new NestedOp(
                    new EntryPlan(outer.Name, [], null, outer.Method, outer.Field),
                    IsCollection: true,
                    new EntryPlan(null, [.. call.Arguments.Skip(1)], null, ConsumedMethod: call.Callee));

            default:
                return null;
        }
    }

    static NestedOp? TryNestedOp(IrFunction function, IrNode statement, int localIndex)
    {
        switch (statement)
        {
            case StoreProperty { HasInstance: true } property
                when IsInitializerSpellable(property) && OuterMemberOffLocal(property.Instance, localIndex) is { } outer:
                var objectInner = property.IndexArguments.Count != 0
                    ? new EntryPlan(null, [.. property.IndexArguments, property.Value], null, ConsumedMethod: property.Accessor)
                    : new EntryPlan(property.PropertyName, [property.Value], null, ConsumedMethod: property.Accessor);
                return new NestedOp(new EntryPlan(outer.Name, [], null, outer.Method, outer.Field), IsCollection: false, objectInner);

            case StoreField { HasInstance: true } field
                when OuterMemberOffLocal(field.Instance, localIndex) is { } outer:
                return new NestedOp(
                    new EntryPlan(outer.Name, [], null, outer.Method, outer.Field),
                    IsCollection: false,
                    new EntryPlan(field.Field.Name, [field.Value], null, ConsumedField: field.Field));

            case ExpressionStatement { Expression: Call { Callee.HasThis: true } call }
                when IsCollectionAdd(function, call)
                    && OuterMemberOffLocal(call.Arguments[0], localIndex) is { } outer:
                return new NestedOp(
                    new EntryPlan(outer.Name, [], null, outer.Method, outer.Field),
                    IsCollection: true,
                    new EntryPlan(null, [.. call.Arguments.Skip(1)], null, ConsumedMethod: call.Callee));

            default:
                return null;
        }
    }

    /// <summary>The member name when <paramref name="instance"/> reads a plain property/field off a threaded slot; otherwise null.</summary>
    static OuterMember? OuterMemberOffSlot(IrExpression? instance, HashSet<int> aliasSlots) => instance switch
    {
        LoadProperty { HasInstance: true, Instance: LoadStackSlot slot } property
            when aliasSlots.Contains(slot.Slot) && property.IndexArguments.Count == 0
                && property.Accessor.TypeArguments.IsDefaultOrEmpty && CSharpNaming.IsEscapableIdentifier(property.PropertyName)
            => new OuterMember(property.PropertyName, property.Accessor, null),
        LoadField { Instance: LoadStackSlot slot } field
            when aliasSlots.Contains(slot.Slot)
            => new OuterMember(field.Field.Name, null, field.Field),
        _ => null,
    };

    static OuterMember? OuterMemberOffLocal(IrExpression? instance, int localIndex) => instance switch
    {
        LoadProperty { HasInstance: true, Instance: LoadLocal local } property
            when local.Index == localIndex && property.IndexArguments.Count == 0
                && property.Accessor.TypeArguments.IsDefaultOrEmpty && CSharpNaming.IsEscapableIdentifier(property.PropertyName)
            => new OuterMember(property.PropertyName, property.Accessor, null),
        LoadField { Instance: LoadLocal local } field
            when local.Index == localIndex
            => new OuterMember(field.Field.Name, null, field.Field),
        _ => null,
    };

    /// <summary>
    /// Whether a property setter store has a C# object-initializer spelling. A
    /// generic accessor (such as <c>set_Value&lt;T&gt;(T)</c>) has no <c>Value = …</c>
    /// initializer form; a real setter returns <c>void</c>; its parameter list is
    /// exactly the index arguments followed by the single by-value assigned value;
    /// and a named member must have an escapable C# identifier (a backing-field-style
    /// or otherwise unspellable name has no <c>Name = …</c> form). Without this guard
    /// <see cref="ObjectInitializerPass"/> would emit invalid initializers such as
    /// <c>new Owner { Value = v }</c> from a generic accessor or
    /// <c>new Owner { bad-name = v }</c> from an unspellable accessor (#1416).
    /// </summary>
    internal static bool IsInitializerSpellable(StoreProperty property)
    {
        var accessor = property.Accessor;
        if (!accessor.TypeArguments.IsDefaultOrEmpty
            || accessor.ReturnType is not { Namespace: "System", Name: "Void" }
            || accessor.ParameterTypes.Length != property.IndexArguments.Count + 1
            || accessor.ParameterTypes.Any(parameter => parameter.Kind == TypeRefKind.ByRef))
        {
            return false;
        }

        // A named member entry prints `Name = value`; an indexer prints `[k] = value`,
        // so only the named form needs a spellable member identifier.
        return property.IndexArguments.Count != 0 || CSharpNaming.IsEscapableIdentifier(property.PropertyName);
    }

    static EntryPlan? TryMemberStore(IrNode statement, HashSet<int> aliasSlots) => statement switch
    {
        // An indexer member `[k0, k1] = v`: the keys precede the value.
        StoreProperty { HasInstance: true, Instance: LoadStackSlot slot } property
            when aliasSlots.Contains(slot.Slot) && property.IndexArguments.Count != 0 && IsInitializerSpellable(property)
            => new EntryPlan(null, [.. property.IndexArguments, property.Value], null, ConsumedMethod: property.Accessor),
        StoreProperty { HasInstance: true, Instance: LoadStackSlot slot } property
            when aliasSlots.Contains(slot.Slot) && IsInitializerSpellable(property)
            => new EntryPlan(property.PropertyName, [property.Value], null, ConsumedMethod: property.Accessor),
        StoreField { HasInstance: true, Instance: LoadStackSlot slot } field
            when aliasSlots.Contains(slot.Slot)
            => new EntryPlan(field.Field.Name, [field.Value], null, ConsumedField: field.Field),
        _ => null,
    };

    static EntryPlan? TryMemberStore(IrNode statement, int localIndex) => statement switch
    {
        StoreProperty { HasInstance: true, Instance: LoadLocal local } property
            when local.Index == localIndex && property.IndexArguments.Count != 0 && IsInitializerSpellable(property)
            => new EntryPlan(null, [.. property.IndexArguments, property.Value], null, ConsumedMethod: property.Accessor),
        StoreProperty { HasInstance: true, Instance: LoadLocal local } property
            when local.Index == localIndex && IsInitializerSpellable(property)
            => new EntryPlan(property.PropertyName, [property.Value], null, ConsumedMethod: property.Accessor),
        StoreField { HasInstance: true, Instance: LoadLocal local } field
            when local.Index == localIndex
            => new EntryPlan(field.Field.Name, [field.Value], null, ConsumedField: field.Field),
        _ => null,
    };

    static EntryPlan? TryCollectionAdd(IrFunction function, IrNode statement, HashSet<int> aliasSlots)
    {
        if (statement is not ExpressionStatement { Expression: Call { Callee.HasThis: true } call })
            return null;
        if (!IsCollectionAdd(function, call))
            return null;
        if (call.Arguments[0] is not LoadStackSlot receiver || !aliasSlots.Contains(receiver.Slot))
            return null;
        return new EntryPlan(null, [.. call.Arguments.Skip(1)], null, ConsumedMethod: call.Callee);
    }

    static EntryPlan? TryCollectionAdd(IrFunction function, IrNode statement, int localIndex)
    {
        if (statement is not ExpressionStatement { Expression: Call { Callee.HasThis: true } call })
            return null;
        if (!IsCollectionAdd(function, call))
            return null;
        if (call.Arguments[0] is not LoadLocal receiver || receiver.Index != localIndex)
            return null;
        return new EntryPlan(null, [.. call.Arguments.Skip(1)], null, ConsumedMethod: call.Callee);
    }

    static bool IsCollectionAdd(IrFunction function, Call call)
        => call.Callee.Name == "Add"
            && call.Arguments.Count >= 2
            && IsCollectionInitializerType(function, call.Arguments[0].ResultType ?? call.Callee.DeclaringType);

    static bool IsCollectionInitializerType(IrFunction function, TypeRef? type)
        => type is not null
            && (function.CollectionInitializerTypes.Contains(type)
                || (type.Kind == TypeRefKind.GenericInstance
                    && type.ElementType is { } definition
                    && function.CollectionInitializerTypes.Contains(definition)));

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

    /// <summary>
    /// Whether a statement subtree reads or writes any of the threaded-reference
    /// slots. Used to prove an interleaved statement is independent of the object
    /// under construction before skipping it: independence requires it neither
    /// loads the reference (observing the partial object) nor stores an alias slot
    /// (clobbering the reference).
    /// </summary>
    static bool TouchesAnySlot(IrNode node, HashSet<int> slots)
    {
        if (node is LoadStackSlot load && slots.Contains(load.Slot))
            return true;
        if (node is StoreStackSlot store && slots.Contains(store.Slot))
            return true;
        foreach (var descendant in node.Descendants)
        {
            if (descendant is LoadStackSlot d && slots.Contains(d.Slot))
                return true;
            if (descendant is StoreStackSlot s && slots.Contains(s.Slot))
                return true;
        }
        return false;
    }

    // True when the statement's own computation completed BEFORE `creation`'s
    // `newobj` executed in the original IL, so keeping it ahead of the folded
    // `newobj` preserves observable order. See EffectOffset for how the offset is
    // derived. Missing offsets decline the skip (conservative).
    static bool ExecutesBefore(IrNode statement, NewObject creation)
    {
        int creationOffset = creation.SourceOffset;
        if (creationOffset < 0)
            return false;
        int effect = EffectOffset(statement);
        return effect >= 0 && effect < creationOffset;
    }

    // The latest IL offset at which the statement's side effect is observed. Later
    // passes can rebuild a value's root node (e.g. property-access recognition)
    // and drop its offset, so scan the whole value subtree and take the max
    // retained offset rather than trusting the root alone. The store/expression
    // wrapper's own offset is a stackifier artifact that can fall after the
    // `newobj`, so it is excluded — only the value's computation counts. Because a
    // statement's operands execute in the same window as the statement, this max
    // is a sound bound: a statement that ran before the `newobj` has every
    // retained offset below the creation offset, and one that ran after has every
    // retained offset above it (or none, which conservatively declines the skip).
    static int EffectOffset(IrNode statement)
    {
        var value = statement switch
        {
            StoreStackSlot store => (IrNode)store.Value,
            StoreLocal store => store.Value,
            ExpressionStatement expr => expr.Expression,
            _ => statement,
        };
        return MaxOffsetInSubtree(value);
    }

    static int MaxOffsetInSubtree(IrNode node)
    {
        int max = node.SourceOffset;
        foreach (var descendant in node.Descendants)
            if (descendant.SourceOffset > max)
                max = descendant.SourceOffset;
        return max;
    }

    static bool ReferencesLocal(IrNode node, int index)
        => (node is LoadLocal load && load.Index == index)
            || node.Descendants.OfType<LoadLocal>().Any(descendant => descendant.Index == index);

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
        {
            if (plan.Target is StackSlotSeedTarget target && ReferenceEquals(statement, target.Seed))
                continue;
            statement.Detach();
        }

        foreach (var leaf in LeafArguments(plan.Entries))
            leaf.Detach();

        var entries = plan.Entries.Select(BuildEntry).ToList();
        switch (plan.Target)
        {
            case StackSlotTarget stackSlot:
            {
                plan.Creation.Detach();
                var initializer = new ObjectInitializerExpression(plan.Creation, plan.IsCollection, entries);
                initializer.InheritSourceOffset(plan.Creation);
                stackSlot.Use.ReplaceWith(initializer);
                break;
            }

            case StackSlotSeedTarget stackSlotSeed:
            {
                var creation = (NewObject)plan.Creation.Clone();
                var initializer = new ObjectInitializerExpression(creation, plan.IsCollection, entries);
                initializer.InheritSourceOffset(plan.Creation);
                plan.Creation.ReplaceWith(initializer);
                if (stackSlotSeed.Use.Slot != stackSlotSeed.Seed.Slot)
                    stackSlotSeed.Use.ReplaceWith(new LoadStackSlot(stackSlotSeed.Seed.Slot, stackSlotSeed.Use.Type));
                break;
            }

            case LocalSeedTarget:
            {
                var creation = (NewObject)plan.Creation.Clone();
                var initializer = new ObjectInitializerExpression(creation, plan.IsCollection, entries);
                initializer.InheritSourceOffset(plan.Creation);
                plan.Creation.ReplaceWith(initializer);
                break;
            }
        }
    }

    static InitializerEntry BuildEntry(EntryPlan entry)
        => entry.Block is { } block
            ? new InitializerEntry(entry.Member, [BuildBlock(block)], entry.ConsumedMethod, entry.ConsumedField)
            : new InitializerEntry(entry.Member, entry.Arguments, entry.ConsumedMethod, entry.ConsumedField);

    static InitializerBlock BuildBlock(BlockPlan block)
        => new(block.IsCollection, block.Entries.Select(BuildEntry).ToList());
}
