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
    // When set, this is the second, post-structuring run (registered after
    // StructuringPass). Its sole extra capability over the default run is folding
    // reused-temp member-value spills (TryReusedTempSpill), which cannot fire at
    // the default early position: Roslyn spills a branchy member value
    // (`cond ? f(x) : null`) into a shared temp precisely because its branches
    // cannot sit on the stack beneath the dup-chain receiver, so at the default
    // position (before diamond collapsing and structuring) the value is still a
    // ConditionalBranch diamond spread across basic blocks, never the adjacent
    // `t = V; ref.M = t` this pass matches. Only after structuring has collapsed
    // the diamond into a single Conditional in one straight-line block does the
    // spill/store pair become adjacent. To stay narrow, the late run commits a
    // fold only when the plan actually rests on such a spill (PlanRequiresSpill):
    // any chain the early run could already have folded — one whose members are
    // all plain stores — was deliberately left lowered there (e.g. an initializer
    // argument sequenced before a short-circuit, or a trusted-platform factory
    // lookalike), and re-folding it here would reorder or misclassify it. The
    // spill anchor leaves those exactly as the early run decided.
    readonly bool _spillReusedTemps;

    public ObjectInitializerPass(bool spillReusedTemps = false) => _spillReusedTemps = spillReusedTemps;

    public string Name => _spillReusedTemps ? "object-initializer-late-spill" : "object-initializer";

    public void Run(IrFunction function, PassContext context)
    {
        // The late (spill) run folds inner-first, so an enclosing chain whose
        // member value is a nested initializer this run already produced can still
        // fold even though the enclosing chain has no spill of its own: the fold
        // that composes a spill-recovered initializer is itself part of the
        // #3336 residue. Track those produced nodes by identity so an outer plan
        // that reaches one (through the version-copy chain, landed use-site as the
        // member value) counts as resting on the spill.
        var spillDerived = _spillReusedTemps ? new HashSet<IrNode>(ReferenceEqualityComparer.Instance) : null;

        // Process seeds inner-first. The compiler emits the outer `new T(...)`
        // before any nested `new U(...)` that feeds one of its members, so reverse
        // document order folds sub-initializers before their enclosing initializer
        // — letting a nested member's value (`Inner = new U { ... }`) be a raised
        // initializer by the time the outer run inspects that member store.
        foreach (var seed in function.Descendants.OfType<StoreStackSlot>().Reverse().ToList())
        {
            if (seed.Parent is not Block || seed.Value is not NewObject creation)
                continue;

            if (TryBuild(function, seed, creation, _spillReusedTemps) is not { } plan)
                continue;

            // Late (spill) run: only commit folds that actually rest on a
            // reused-temp spill — the capability the early run lacked — directly or
            // by composing a nested initializer this run already recovered from a
            // spill. A plan of all plain stores that reaches no such spill was
            // already available to (and declined by) the early run; re-folding it
            // here would reorder or misclassify it.
            if (_spillReusedTemps
                && !PlanRequiresSpill(plan)
                && !PlanReferencesSpillDerived(plan, spillDerived!))
                continue;

            context.Stepper.StepOver(
                $"raise {creation.Constructor.DeclaringType.Name} {(plan.IsCollection ? "collection" : "object")} initializer", seed);
            var produced = Apply(plan, function);
            spillDerived?.Add(produced);
        }

        foreach (var seed in function.Descendants.OfType<StoreLocal>().Reverse().ToList())
        {
            if (seed.Parent is not Block || seed.Value is not NewObject creation)
                continue;
            if (GeneratedCodeIdentity.IsDisplayClassName(creation.Constructor.DeclaringType))
                continue;

            if (TryBuild(function, seed, creation) is not { } plan)
                continue;

            // The named-local TryBuild has no spill branch, so a named-local plan
            // only reaches the late run by composing a nested spill-recovered
            // initializer (PlanReferencesSpillDerived); a plain named-local chain
            // stays exactly as the early run left it.
            if (_spillReusedTemps
                && !PlanRequiresSpill(plan)
                && !PlanReferencesSpillDerived(plan, spillDerived!))
                continue;

            context.Stepper.StepOver(
                $"raise named-local {creation.Constructor.DeclaringType.Name} {(plan.IsCollection ? "collection" : "object")} initializer", seed);
            var produced = Apply(plan, function);
            spillDerived?.Add(produced);
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
        InitializerTarget Target,
        IReadOnlyList<IrNode>? Skipped = null);

    /// <summary>
    /// A planned initializer entry. A flat entry carries its leaf <see cref="Arguments"/>
    /// directly (<see cref="Block"/> null); a nested entry (<c>Member = { ... }</c>)
    /// carries a <see cref="BlockPlan"/> and no direct arguments. Block construction
    /// is deferred to <see cref="Apply"/> so its leaf arguments are detached from
    /// their lowered statements before being reparented into the new IR.
    /// <para>
    /// <see cref="FromReusedSpill"/> marks an entry recovered by
    /// <see cref="TryReusedTempSpill"/>. The late (post-structuring) run folds a
    /// plan only when it — directly or through a nested block — contains such an
    /// entry (see <see cref="PlanRequiresSpill"/>), because that spill is the one
    /// capability the early run lacked.
    /// </para>
    /// </summary>
    sealed record EntryPlan(
        string? Member,
        IReadOnlyList<IrExpression> Arguments,
        BlockPlan? Block,
        MethodRef? ConsumedMethod = null,
        FieldRef? ConsumedField = null,
        bool FromReusedSpill = false);

    /// <summary>A nested initializer body: an object/collection brace group with no creation.</summary>
    sealed record BlockPlan(bool IsCollection, IReadOnlyList<EntryPlan> Entries);

    static Plan? TryBuild(IrFunction function, StoreStackSlot seed, NewObject creation, bool spillReusedTemps)
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
        // The subset of skipped statements proven reorder-safe (side-effect-free and
        // non-throwing) by IsReorderSafeSpill. Only these are inlined back into the
        // folded call by InlineSingleUseSpills — a pure value moves to any position
        // without changing behavior. The offset-guarded (ExecutesBefore) skips, which
        // may be impure, are left in place for a later inlining pass.
        var inlinableSkipped = new List<IrNode>();
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

            // A member-value spill feeding the immediately-following member store,
            // in either of two shapes:
            //   * `default(T) V = default; ref.M = V` — the compiler spills a
            //     struct `default(T)` (an `initobj` into its own local) and reads it
            //     straight back (TryDefaultValueSpill).
            //   * `t = V; ref.M = t` — a (possibly reused) stack-slot temp holds a
            //     computed member value, e.g. `t = cond ? Write(x) : null; ref.M = t`
            //     that Roslyn spills because the value's branches cannot sit on the
            //     stack beneath the dup-chain receiver (TryReusedTempSpill).
            // Either way, fold both statements into `M = <value>` inlined at the
            // member's position. Sound because the member value stays in member order
            // at the fold site — unlike a pre-entry skip, it is never reordered across
            // the `newobj` or other members: the spill statement and its consuming
            // member store are adjacent in the same straight-line block, so the spilled
            // value already evaluated immediately before the member store and keeps
            // that spot in the initializer. TryReusedTempSpill additionally proves the
            // temp is a linear spill slot (each definition consumed by exactly its next
            // statement), so dropping this store/load pair strands no other reader.
            if (i + 1 < statements.Count
                && !TouchesAnySlot(statement, aliasSlots)
                && (TryDefaultValueSpill(function, statement, statements[i + 1], aliasSlots)
                    ?? (spillReusedTemps
                        ? TryReusedTempSpill(function, statement, statements[i + 1], aliasSlots)
                        : null)) is { } spilled)
            {
                if (isCollection == true)
                    break;
                FlushPending();
                isCollection = false;
                entries.Add(spilled);
                consumed.Add(statement);
                consumed.Add(statements[i + 1]);
                i++;   // the member store is consumed together with its spill
                continue;
            }

            // A side-effect-free, non-throwing spill interleaved in the construction
            // region: a pure receiver or argument the compiler spilled to its own slot
            // for the *enclosing call* whose argument this initializer becomes — e.g.
            // `S_257 = _rest;` before the members, or `V_0 = default;` in the gap
            // between the last member and the call. Use-site folding moves the
            // construction (the `newobj`, its constructor call, and every member value)
            // to the call-argument position, past this statement, so the statement must
            // commute with all of that.
            //
            // Soundness has two crossings, gated separately (see IsReorderSafeSpill):
            //   * The `newobj` constructor. The fold moves the construction — the
            //     `newobj`, its constructor, and every member value — down to the call
            //     argument position, past this spill. A `this`-field receiver read
            //     hoisted this way lands BEFORE the `newobj`, so the constructor (and
            //     the type initializer `newobj` triggers) must not mutate what the read
            //     observes. A parameterless ctor alone does NOT prove that: `newobj`
            //     also runs the type's `.cctor`, and a trivial-looking ctor can escape
            //     through a base ctor, a setter, or a static read. We therefore admit a
            //     `this`-field read only when the constructor is proven EFFECT-FREE —
            //     body exactly `ldarg.0; call object::.ctor; ret`, declaring type with
            //     no static ctor (see MethodRef.ConstructorEffectFree /
            //     ConstructorConfinementFacts). That is Roslyn-faithful (it assumes a
            //     non-null `this`), not arbitrary-IL-sound, and preserves the real
            //     `new TableProperties { ... }` / `new CallArgTarget { ... }` shape.
            //     Other reorder-safe spills read only args/locals/constants, which a
            //     parameterless ctor and its `.cctor` cannot reach, so they need no
            //     constructor proof.
            //   * The member values. A position-independent value (constant / `default`
            //     / `sizeof` / `ldtoken`) reads no mutable state and commutes anywhere.
            //     A mutable read (a field, argument, or local load — e.g. the receiver
            //     `_rest`) is admitted ONLY before the first entry: inlining it feeds
            //     the folded call's receiver position (evaluated before the argument),
            //     so a member value that ran before it must not be reordered after it.
            //     Requiring it to precede every entry keeps the read ahead of all
            //     member values in both the original and the folded order. (The
            //     impure-receiver case is handled by the offset skip below.)
            var beforeFirstEntry = entries.Count == 0;
            if (pendingInner is null
                && creation.Arguments.Count == 0
                && !TouchesAnySlot(statement, aliasSlots)
                && IsReorderSafeSpill(statement, aliasSlots, function, function.Signature.HasThis, beforeFirstEntry, creation.Constructor.ConstructorEffectFree))
            {
                skipped.Add(statement);
                inlinableSkipped.Add(statement);
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
                : new StackSlotSeedTarget(seed, outsideUses[0]),
            inlinableSkipped);
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

    /// <summary>
    /// Matches a <c>default(T)</c> member-value spill and its consuming member store:
    /// <c>InitObject L</c> (a struct-typed <c>initobj</c> that zero-inits its own
    /// local <c>L</c>) immediately followed by <c>ref.M = LoadLocal L</c> on a
    /// threaded slot, where <c>L</c> is single-assignment, addressed only by this
    /// <c>initobj</c>, and read exactly once — as that member value. Returns the
    /// member entry with <see cref="DefaultValue"/> inlined in place of the temp so
    /// the spill statement can be dropped. The compiler emits this shape for a
    /// struct-typed <c>default</c> assigned to an initializer member; recovering it
    /// lets the whole chain fold instead of leaving the trailing member lowered.
    /// </summary>
    static EntryPlan? TryDefaultValueSpill(IrFunction function, IrNode spill, IrNode memberStatement, HashSet<int> aliasSlots)
    {
        // The spill: `initobj L` writing the zero value through a local's address.
        if (spill is not InitObject { Address: LoadLocalAddress { Index: var localIndex } } init)
            return null;

        // The consuming member store: `ref.M = LoadLocal L` on a threaded slot, with
        // no index arguments (the value is the whole right-hand side).
        var (member, method, fieldRef) = memberStatement switch
        {
            StoreProperty { HasInstance: true, Instance: LoadStackSlot slot, Value: LoadLocal use } property
                when aliasSlots.Contains(slot.Slot) && use.Index == localIndex
                    && property.IndexArguments.Count == 0 && IsInitializerSpellable(property)
                => (property.PropertyName, (MethodRef?)property.Accessor, (FieldRef?)null),
            StoreField { HasInstance: true, Instance: LoadStackSlot slot, Value: LoadLocal use } field
                when aliasSlots.Contains(slot.Slot) && use.Index == localIndex
                => (field.Field.Name, null, field.Field),
            _ => ((string?)null, null, null),
        };
        if (member is null)
            return null;

        // The temp must belong solely to this spill/use pair: referenced or bound
        // nowhere else, never re-stored, and addressed only by this `initobj`. That
        // makes dropping its definition and inlining `default(T)` semantics-preserving.
        if (!ReferenceOwnership.LocalReferencedOrBoundOnlyWithin(function, localIndex, [spill, memberStatement]))
            return null;
        if (function.Descendants.OfType<StoreLocal>().Any(store => store.Index == localIndex))
            return null;
        if (function.Descendants.OfType<LoadLocalAddress>()
                .Any(address => address.Index == localIndex && !ReferenceEquals(address, init.Address)))
            return null;

        return new EntryPlan(member, [new DefaultValue(init.Type)], null, ConsumedMethod: method, ConsumedField: fieldRef);
    }

    /// <summary>
    /// Matches a stack-slot member-value spill and its consuming member store:
    /// <c>t = V</c> (a <see cref="StoreStackSlot"/> writing a computed value into a
    /// non-threaded temp slot <c>t</c>) immediately followed by
    /// <c>ref.M = LoadStackSlot t</c> on a threaded slot, where the load is the whole
    /// right-hand side. Returns the member entry with <paramref name="V"/> inlined in
    /// place of the temp so both statements fold into <c>M = V</c>.
    /// <para>
    /// The temp may be reused across several members (Roslyn spills each branchy
    /// member value — e.g. <c>cond ? Write(x) : null</c> — into one shared slot
    /// because the branches cannot straddle the dup-chain receiver on the stack), so
    /// the single-use ownership guard of <see cref="TryDefaultValueSpill"/> does not
    /// apply. Instead <see cref="IsLinearSpillSlot"/> proves the slot is used
    /// strictly linearly — every definition is consumed by exactly the immediately
    /// following statement — so removing this (store, load) pair strands no other
    /// reader and reorders nothing: spill and store are adjacent in one straight-line
    /// block, so <c>V</c> already evaluated in this exact position, after the
    /// <c>newobj</c> and in member order.
    /// </para>
    /// </summary>
    static EntryPlan? TryReusedTempSpill(IrFunction function, IrNode spill, IrNode memberStatement, HashSet<int> aliasSlots)
    {
        // The spill: `t = V`, where `t` is a temp slot distinct from the threaded
        // reference chain (a store into an alias slot is a version copy, not a spill).
        if (spill is not StoreStackSlot { Slot: var tempSlot, Value: var spilledValue }
            || aliasSlots.Contains(tempSlot))
            return null;

        // The consuming member store: `ref.M = LoadStackSlot t` on a threaded slot,
        // with no index arguments (the temp is the whole right-hand side).
        var (member, method, fieldRef) = memberStatement switch
        {
            StoreProperty { HasInstance: true, Instance: LoadStackSlot slot, Value: LoadStackSlot use } property
                when aliasSlots.Contains(slot.Slot) && use.Slot == tempSlot
                    && property.IndexArguments.Count == 0 && IsInitializerSpellable(property)
                => (property.PropertyName, (MethodRef?)property.Accessor, (FieldRef?)null),
            StoreField { HasInstance: true, Instance: LoadStackSlot slot, Value: LoadStackSlot use } field
                when aliasSlots.Contains(slot.Slot) && use.Slot == tempSlot
                => (field.Field.Name, null, field.Field),
            _ => ((string?)null, null, null),
        };
        if (member is null)
            return null;

        // The spilled value must not read the temp itself — that would observe the
        // slot's prior definition, which this fold drops.
        if (SubtreeLoadsStackSlot(spilledValue, tempSlot))
            return null;

        // Fold only when the temp is a linear spill slot: each definition reaches
        // exactly its adjacent consuming use, so dropping this pair is sound.
        if (!IsLinearSpillSlot(function, tempSlot))
            return null;

        return new EntryPlan(member, [spilledValue], null, ConsumedMethod: method, ConsumedField: fieldRef, FromReusedSpill: true);
    }

    /// <summary>
    /// Whether a plan rests on a reused-temp spill — an entry recovered by
    /// <see cref="TryReusedTempSpill"/>, directly or inside a nested block. The
    /// late (post-structuring) run folds only such plans, leaving every
    /// all-plain-store chain exactly as the early run decided.
    /// </summary>
    static bool PlanRequiresSpill(Plan plan) => EntriesRequireSpill(plan.Entries);

    static bool EntriesRequireSpill(IEnumerable<EntryPlan> entries)
        => entries.Any(entry => entry.FromReusedSpill
            || (entry.Block is { } block && EntriesRequireSpill(block.Entries)));

    /// <summary>
    /// Whether a plan composes a nested initializer this run already recovered from
    /// a spill: any leaf member value (recursively) is or contains one of the
    /// tracked <paramref name="spillDerived"/> nodes. Inner-first folding lands such
    /// a nested initializer use-site as the enclosing member's value, so the
    /// enclosing fold is part of the same #3336 residue even without a spill of its
    /// own. All of the pass's ordering guards still apply; this only relaxes the
    /// spill-narrowing, never a soundness check.
    /// </summary>
    static bool PlanReferencesSpillDerived(Plan plan, HashSet<IrNode> spillDerived)
        => spillDerived.Count != 0 && EntriesReferenceSpillDerived(plan.Entries, spillDerived);

    static bool EntriesReferenceSpillDerived(IEnumerable<EntryPlan> entries, HashSet<IrNode> spillDerived)
        => entries.Any(entry =>
            entry.Arguments.Any(argument => argument.Descendants.Prepend(argument).Any(spillDerived.Contains))
            || (entry.Block is { } block && EntriesReferenceSpillDerived(block.Entries, spillDerived)));

    /// <summary>
    /// Whether stack slot <paramref name="slot"/> is used strictly linearly: every
    /// <see cref="StoreStackSlot"/> of it is a block statement whose value does not
    /// re-read the slot and whose immediately following sibling statement consumes it
    /// with exactly one <see cref="LoadStackSlot"/> and no re-store, and the total
    /// load count equals the store count. Together these make each definition reach
    /// exactly its adjacent use (adjacency in a straight-line block means no branch
    /// intervenes), so folding any (store, load) pair strands no other reader.
    /// </summary>
    static bool IsLinearSpillSlot(IrFunction function, int slot)
    {
        int stores = 0;
        int loads = 0;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreStackSlot store when store.Slot == slot:
                {
                    stores++;
                    if (store.Parent is not { } parent)
                        return false;
                    int next = store.ChildIndex + 1;
                    if (next >= parent.Children.Count)
                        return false;  // a definition with no following consumer
                    if (SubtreeLoadsStackSlot(store.Value, slot))
                        return false;  // the definition reads the slot it defines
                    var consumer = parent.Children[next];
                    if (CountStackSlotLoads(consumer, slot) != 1 || SubtreeStoresStackSlot(consumer, slot))
                        return false;  // the next statement is not a lone consuming use
                    break;
                }

                case LoadStackSlot load when load.Slot == slot:
                    loads++;
                    break;
            }
        }

        // Each store contributes one consuming load in a distinct next-sibling
        // statement (two stores cannot share a consumer, and adjacent stores are
        // rejected by the re-store check above). Equal totals therefore mean every
        // load is one of those consuming loads — no stray reader of any definition.
        return stores > 0 && stores == loads;
    }

    static bool SubtreeLoadsStackSlot(IrNode root, int slot)
        => root.Descendants.Prepend(root).Any(node => node is LoadStackSlot load && load.Slot == slot);

    static bool SubtreeStoresStackSlot(IrNode root, int slot)
        => root.Descendants.Prepend(root).Any(node => node is StoreStackSlot store && store.Slot == slot);

    static int CountStackSlotLoads(IrNode root, int slot)
        => root.Descendants.Prepend(root).Count(node => node is LoadStackSlot load && load.Slot == slot);

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

    /// <summary>
    /// Whether an interleaved statement is a reorder-safe spill that use-site folding
    /// may move the construction (the <c>newobj</c>, its constructor, and every member
    /// value) past. Two tiers, per <paramref name="beforeFirstEntry"/>: a
    /// position-independent value (see <see cref="IsPositionIndependentValue"/>) is
    /// admitted anywhere, while a mutable-memory read (a field, argument, or local load)
    /// is admitted only before the first initializer entry, so it is never reordered
    /// across a member value that could write the memory it reads. A struct
    /// <c>default</c> init (<c>initobj</c> zeroing a local) writes only that fresh local
    /// and reads nothing mutable, and is admitted when the local's address is taken
    /// exactly once (its own <c>initobj</c>), so no other statement aliases it.
    /// A <c>this</c>-field read additionally requires
    /// <paramref name="constructorEffectFree"/>, because folding hoists it before the
    /// <c>newobj</c> whose constructor and type initializer could otherwise mutate the
    /// field (see <see cref="IsReorderSafeValue"/>). The other reorder-safe values read
    /// only args/locals/constants, which a parameterless constructor and its
    /// <c>.cctor</c> cannot reach, so they do not depend on the flag.
    /// </summary>
    static bool IsReorderSafeSpill(IrNode statement, HashSet<int> aliasSlots, IrFunction function, bool hasThis, bool beforeFirstEntry, bool constructorEffectFree) => statement switch
    {
        StoreStackSlot store => !aliasSlots.Contains(store.Slot) && IsReorderSafeValue(store.Value, hasThis, beforeFirstEntry, constructorEffectFree),
        StoreLocal store => IsReorderSafeValue(store.Value, hasThis, beforeFirstEntry, constructorEffectFree),
        // `default(T)` for a struct: `initobj` zeroing a local. Pure and non-throwing,
        // and it reads no mutable state — but only when nothing else can observe or
        // alias that local across the move. Require the local's address to be taken
        // exactly once (this `initobj` itself): otherwise a member value could pass
        // `ref V_0` to a call whose mutation this write would be reordered across.
        InitObject { Address: LoadLocalAddress { Index: var index } } => AddressTakenOnce(function, index),
        _ => false,
    };

    // Whether the local at <paramref name="index"/> has exactly one address-of use in
    // the function (the caller's own `initobj`), so no other statement — a constructor
    // argument or a member value — holds a `ref` to it that the fold could reorder
    // across. Scoped to this function's slot pool (nested functions have their own).
    static bool AddressTakenOnce(IrFunction function, int index)
        => GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function)
            .OfType<LoadLocalAddress>()
            .Count(address => address.Index == index) == 1;

    /// <summary>
    /// Whether an expression is side-effect-free, non-throwing, and — when
    /// <paramref name="beforeFirstEntry"/> is false — free of any mutable-memory read
    /// whose value a member store could have changed. So moving the object
    /// construction across a statement that computes it is unobservable.
    ///
    /// A <see cref="IsPositionIndependentValue"/> reads no mutable state and is
    /// admitted regardless of position. A non-volatile instance field read off the
    /// non-null <c>this</c> receiver is non-throwing but *is* a mutable-memory read;
    /// it is admitted only before the first entry, because inlining it feeds the
    /// folded call's receiver position (evaluated before the initializer argument) and
    /// a member value that ran before it could otherwise be reordered after it. The
    /// <c>this</c>-field carve-out further requires <paramref name="hasThis"/> (in a
    /// static method argument 0 is an ordinary, possibly null parameter, so a field
    /// read off it can throw <see cref="System.NullReferenceException"/>) AND
    /// <paramref name="constructorEffectFree"/>: folding hoists the field read before
    /// the <c>newobj</c>, so the constructor and the type initializer it triggers must
    /// not mutate the field. Only a proven effect-free ctor (a trivial direct-<c>Object</c>
    /// parameterless ctor with no static ctor — see
    /// <see cref="MethodRef.ConstructorEffectFree"/>) guarantees that; this is
    /// Roslyn-faithful, not arbitrary-IL-sound.
    /// Everything else (calls, static or nested field reads, indexers, throwing
    /// arithmetic) is rejected.
    /// </summary>
    static bool IsReorderSafeValue(IrExpression value, bool hasThis, bool beforeFirstEntry, bool constructorEffectFree) => value switch
    {
        _ when IsPositionIndependentValue(value) => true,
        LoadField { IsVolatile: false, Instance: LoadArgument { Index: 0 } } => hasThis && beforeFirstEntry && constructorEffectFree,
        // An argument load reads a caller-supplied slot. It cannot throw, but the slot
        // is mutable memory: a member value could pass `ref arg` to a call (`ldarga`)
        // and mutate it, so moving the read across the members is observable. Admit it
        // only before the first entry, keeping the read ahead of every member value in
        // both the original and the folded order — the same rule as a field/local read.
        LoadArgument => beforeFirstEntry,
        LoadLocal => beforeFirstEntry,
        _ => false,
    };

    /// <summary>
    /// Whether an expression reads no mutable memory and cannot throw, so it commutes
    /// with the object construction and every member value in either direction — a
    /// constant, a struct <c>default</c>, or a <c>sizeof</c>/<c>ldtoken</c>. An
    /// argument load is deliberately NOT here: its slot is mutable via <c>ldarga</c>,
    /// so it belongs to the before-first-entry tier in <see cref="IsReorderSafeValue"/>.
    /// </summary>
    static bool IsPositionIndependentValue(IrExpression value) => value switch
    {
        Constant or DefaultValue or SizeOf or LoadToken => true,
        _ => false,
    };

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

    static ObjectInitializerExpression Apply(Plan plan, IrFunction function)
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
            if (leaf.Parent is not null)
                leaf.Detach();   // a synthesized leaf (e.g. an inlined default) is already free

        var entries = plan.Entries.Select(BuildEntry).ToList();
        switch (plan.Target)
        {
            case StackSlotTarget stackSlot:
            {
                plan.Creation.Detach();
                var initializer = new ObjectInitializerExpression(plan.Creation, plan.IsCollection, entries);
                initializer.InheritSourceOffset(plan.Creation);
                stackSlot.Use.ReplaceWith(initializer);
                // Use-site folding collapsed the dup-chain region into a single
                // expression at the call-argument position. The pure receiver and
                // argument spills the stackifier materialized for that call (left in
                // place as skipped, reorder-safe statements) now feed the call with no
                // intervening dup chain, so inlining each single-use spill back into its
                // load restores the canonical stack-only spelling — e.g.
                // `S_257 = _rest; ... S_257.Create(new T { ... }, ...)` becomes
                // `_rest.Create(new T { ... }, ...)` — which recompiles byte-for-byte to
                // the original IL. Reorder-safety was already proven when the statement
                // was skipped, and the single-use guard keeps any spill read elsewhere.
                if (plan.Skipped is { } skipped)
                    InlineSingleUseSpills(skipped, function, initializer);
                return initializer;
            }

            case StackSlotSeedTarget stackSlotSeed:
            {
                var creation = (NewObject)plan.Creation.Clone();
                var initializer = new ObjectInitializerExpression(creation, plan.IsCollection, entries);
                initializer.InheritSourceOffset(plan.Creation);
                plan.Creation.ReplaceWith(initializer);
                if (stackSlotSeed.Use.Slot != stackSlotSeed.Seed.Slot)
                    stackSlotSeed.Use.ReplaceWith(new LoadStackSlot(stackSlotSeed.Seed.Slot, stackSlotSeed.Use.Type));
                return initializer;
            }

            case LocalSeedTarget:
            {
                var creation = (NewObject)plan.Creation.Clone();
                var initializer = new ObjectInitializerExpression(creation, plan.IsCollection, entries);
                initializer.InheritSourceOffset(plan.Creation);
                plan.Creation.ReplaceWith(initializer);
                return initializer;
            }

            default:
                throw new InvalidOperationException($"Unhandled initializer target {plan.Target.GetType().Name}.");
        }
    }

    /// <summary>
    /// Inlines each single-use reorder-safe spill back into its sole load, restoring
    /// the stack-only spelling the stackifier split into a named spill. Only the
    /// direct-value forms (<see cref="StoreStackSlot"/> / <see cref="StoreLocal"/>)
    /// are inlined; a default-struct init (<c>InitObject</c> writing a local in place)
    /// already round-trips and is left alone. The single-use guard (exactly one
    /// remaining load) prevents dropping a value read elsewhere, and the value's
    /// reorder-safety was established when the statement was skipped.
    ///
    /// A mutable-memory read (a field / local / argument load) carries an extra
    /// guard: it may be inlined only when its sole load evaluates BEFORE the folded
    /// <paramref name="initializer"/> in the enclosing call (the receiver, or an
    /// argument to its left). Leaving such a spill as a statement keeps the read in
    /// its original pre-call position, so declining the inline is always sound; but
    /// inlining a read into an argument to the RIGHT of the initializer would move it
    /// past the member values, which C# evaluates first — if a member value writes
    /// that memory, the read would observe the post-write value. Position-independent
    /// values (constants, <c>default</c>, <c>sizeof</c>, <c>ldtoken</c>) read no
    /// mutable state and are inlined regardless of position (e.g. the real witness's
    /// trailing <c>default</c> argument).
    /// </summary>
    static void InlineSingleUseSpills(IReadOnlyList<IrNode> skipped, IrFunction function, IrNode initializer)
    {
        // Count and rewrite only within this function's own scope. IrNode.Descendants
        // crosses into nested lambda/local-function bodies, whose stack slots and local
        // indices live in a separate pool that can collide with the outer spill's slot
        // — over-counting a load would wrongly block the inline, and (worse) inlining
        // into a nested scope's load would reparent an outer expression across the
        // closure boundary. DescendantsOutsideNestedFunctions stops at those bounds.
        IEnumerable<IrNode> Scoped() => GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function);

        foreach (var statement in skipped)
        {
            switch (statement)
            {
                case StoreStackSlot store:
                {
                    var loads = Scoped().OfType<LoadStackSlot>()
                        .Where(load => load.Slot == store.Slot)
                        .ToList();
                    var writers = Scoped().OfType<StoreStackSlot>()
                        .Count(other => other.Slot == store.Slot);
                    if (loads.Count != 1 || writers != 1)
                        continue;
                    if (!IsPositionIndependentValue(store.Value)
                        && !LoadEvaluatesBeforeInitializer(loads[0], initializer))
                        continue;
                    var value = store.Value;
                    value.Detach();
                    loads[0].ReplaceWith(value);
                    store.Detach();
                    break;
                }

                case StoreLocal store:
                {
                    var loads = Scoped().OfType<LoadLocal>()
                        .Where(load => load.Index == store.Index)
                        .ToList();
                    var writers = Scoped().OfType<StoreLocal>()
                        .Count(other => other.Index == store.Index);
                    var addressUses = Scoped().OfType<LoadLocalAddress>()
                        .Count(address => address.Index == store.Index);
                    if (loads.Count != 1 || writers != 1 || addressUses != 0)
                        continue;
                    if (!IsPositionIndependentValue(store.Value)
                        && !LoadEvaluatesBeforeInitializer(loads[0], initializer))
                        continue;
                    var value = store.Value;
                    value.Detach();
                    loads[0].ReplaceWith(value);
                    store.Detach();
                    break;
                }

                // `T V = default;` for a struct: an `initobj` zeroing a local in place.
                // Keeping it as a separate statement emits the `initobj` at the top of
                // the method, but the original evaluates `default` at the argument
                // position (mid-stream). Inline it as `default(T)` at the sole load so
                // the opcode order matches. Require the temp to be read exactly once
                // with no other writer or address escape beyond this init.
                case InitObject { Address: LoadLocalAddress { Index: var index } } init:
                {
                    var loads = Scoped().OfType<LoadLocal>()
                        .Where(load => load.Index == index)
                        .ToList();
                    var addressUses = Scoped().OfType<LoadLocalAddress>()
                        .Count(address => address.Index == index);
                    var stores = Scoped().OfType<StoreLocal>()
                        .Count(store => store.Index == index);
                    if (loads.Count != 1 || stores != 0 || addressUses != 1)
                        continue;
                    loads[0].ReplaceWith(new DefaultValue(init.Type));
                    init.Detach();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="load"/> is evaluated before <paramref name="initializer"/>
    /// in the enclosing call. Both feed the same call whose argument the initializer
    /// became, so their lowest common ancestor is that <see cref="Call"/> (or a
    /// <see cref="NewObject"/> when the initializer is a constructor argument), whose
    /// arguments — receiver first — evaluate strictly left-to-right; the branch with
    /// the smaller <see cref="IrNode.ChildIndex"/> evaluates first. Any other common
    /// ancestor (a conditional, short-circuit, or assignment does not evaluate its
    /// children left-to-right) is treated conservatively as "not before", declining
    /// the inline rather than risk reordering a read past a member value.
    /// </summary>
    static bool LoadEvaluatesBeforeInitializer(IrNode load, IrNode initializer)
    {
        var ancestor = LowestCommonAncestor(load, initializer);
        if (ancestor is not (Call or NewObject))
            return false;
        var loadBranch = BranchUnder(ancestor, load);
        var initBranch = BranchUnder(ancestor, initializer);
        return loadBranch is not null && initBranch is not null
            && loadBranch.ChildIndex < initBranch.ChildIndex;
    }

    static IrNode? LowestCommonAncestor(IrNode a, IrNode b)
    {
        var seen = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        for (IrNode? n = a; n is not null; n = n.Parent)
            seen.Add(n);
        for (IrNode? n = b; n is not null; n = n.Parent)
            if (seen.Contains(n))
                return n;
        return null;
    }

    // The direct child of `ancestor` on the path to `node` (node itself when it is a
    // direct child), or null when node is not a descendant of ancestor.
    static IrNode? BranchUnder(IrNode ancestor, IrNode node)
    {
        for (IrNode? n = node; n is not null; n = n.Parent)
            if (ReferenceEquals(n.Parent, ancestor))
                return n;
        return null;
    }

    static InitializerEntry BuildEntry(EntryPlan entry)
        => entry.Block is { } block
            ? new InitializerEntry(entry.Member, [BuildBlock(block)], entry.ConsumedMethod, entry.ConsumedField)
            : new InitializerEntry(entry.Member, entry.Arguments, entry.ConsumedMethod, entry.ConsumedField);

    static InitializerBlock BuildBlock(BlockPlan block)
        => new(block.IsCollection, block.Entries.Select(BuildEntry).ToList());
}
