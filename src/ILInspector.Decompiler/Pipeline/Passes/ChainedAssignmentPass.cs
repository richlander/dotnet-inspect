namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recomposes a C# chained (embedded) assignment — <c>A = B = C = value;</c> —
/// from the compiler's dup-of-value lowering. The rvalue is evaluated once and
/// <c>dup</c>'d to each successive sink (right to left), which the importer sees
/// as a run of independent stores of one slot-carried value. This pass keys on
/// that shared dup slot — <em>real</em> evidence — so genuinely separate
/// statements <c>A = v; B = v;</c> (no dup, no slot) are never collapsed.
///
/// <para>Slice scope: receiver-free sinks (static properties and static fields),
/// which is the Dapper <c>Settings.SetDefaults</c> shape (issue #2994) and needs
/// no receiver or index re-evaluation. Instance and local targets — which drag
/// in a receiver dup or a compiler temp and local-declaration tracking — are out
/// of scope and never match the strict pattern below, so they fall through
/// untouched.</para>
///
/// <para>Runs after <see cref="PropertySugarPass"/> (so setters are already
/// <see cref="StoreProperty"/> nodes, uniform with field stores) and before
/// <see cref="ObjectInitializerPass"/> (whose <see cref="NewObject"/>-seeded
/// chains never overlap a constant/value-seeded assignment chain).</para>
///
/// <para>After recomposition, any remaining dup'd <em>constant</em> slot that was
/// not part of a chain is re-materialized — cloned into each of its loads and the
/// store dropped — so a per-sink typed literal is recovered (issue #2982) instead
/// of being spilled through an int32-typed slot (CS0029). This preserves the
/// validity fix #2995 landed in the importer, now that the importer keeps dup'd
/// constants on the slot path so a chain retains its dup evidence.</para>
/// </summary>
public sealed class ChainedAssignmentPass : IIrPass
{
    public string Name => "chained-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var seed in function.Descendants.OfType<StoreStackSlot>().ToList())
        {
            // A seed is the origin of a dup chain: a dup slot whose value is the
            // rvalue itself (not a load — that would be a copy of another slot).
            if (seed.Parent is not Block)
                continue;
            if (seed.Slot < StoreStackSlot.DupSlotBase)
                continue;
            if (seed.Value is LoadStackSlot)
                continue;

            if (TryBuild(function, seed) is not { } plan)
                continue;

            context.Stepper.StepOver($"raise chained assignment ({plan.Targets.Count} targets)", seed);
            Apply(plan);
        }

        RematerializeConstantDupSlots(function);
    }

    sealed record Plan(
        StoreStackSlot Seed,
        IReadOnlyList<IrNode> Consumed,
        IReadOnlyList<ChainedAssignmentTarget> Targets,
        IrExpression Value);

    static Plan? TryBuild(IrFunction function, StoreStackSlot seed)
    {
        var statements = seed.Parent!.Children;
        var aliasSlots = new HashSet<int> { seed.Slot };
        var consumed = new List<IrNode> { seed };

        // Sinks collected in document order, which is innermost-first: the
        // rightmost source target (`C` in `A = B = C = v`) is written first.
        var sinks = new List<ChainedAssignmentTarget>();

        for (int i = seed.ChildIndex + 1; i < statements.Count; i++)
        {
            var statement = statements[i];

            // A dup of the threaded value: sNew = LoadStackSlot(sKnown).
            if (statement is StoreStackSlot { Value: LoadStackSlot source } copy
                && copy.Slot >= StoreStackSlot.DupSlotBase
                && aliasSlots.Contains(source.Slot))
            {
                aliasSlots.Add(copy.Slot);
                consumed.Add(copy);
                continue;
            }

            // A static property/field sink whose value is (bare or an implicit
            // widening convert of) a load of the threaded value.
            if (TryStaticTarget(statement, aliasSlots) is { } target)
            {
                sinks.Add(target);
                consumed.Add(statement);
                continue;
            }

            break;
        }

        // A chain needs at least two targets; one store is an ordinary assignment.
        if (sinks.Count < 2)
            return null;

        var consumedSet = consumed.ToHashSet();

        // Clobber guard: the threaded value must not be re-stored to an alias slot
        // outside the consumed run (a reused slot is a different value).
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
            if (aliasSlots.Contains(store.Slot) && !consumedSet.Contains(store))
                return null;

        // Full-consumption guard: the chain is a statement, so the threaded value
        // must not escape. Any alias-slot load outside the consumed run (e.g.
        // WriteLine(P = 5)) leaves the chain to the constant-rematerialization
        // fallback instead of an unsound expression-position fold.
        foreach (var load in function.Descendants.OfType<LoadStackSlot>())
            if (aliasSlots.Contains(load.Slot) && !HasAncestorIn(load, consumedSet))
                return null;

        // Source order is the reverse of the innermost-first document order.
        var targets = ((IEnumerable<ChainedAssignmentTarget>)sinks).Reverse().ToList();

        // Type staircase: each outer target must accept the next-inner target's
        // type (the value of `B = C = v` has C's type), else the chain would not
        // typecheck on recompile.
        for (int i = 0; i < targets.Count - 1; i++)
            if (!IsAssignable(targets[i].TargetType, targets[i + 1].TargetType))
                return null;

        // The rvalue must reach the innermost target. A constant adapts to its
        // sink type (retyped downstream), so it is exempt; anything else must be
        // an identity or an implicit widening.
        var value = seed.Value;
        if (value is not Constant
            && value.ResultType is { } valueType
            && !IsAssignable(targets[^1].TargetType, valueType))
            return null;

        return new Plan(seed, consumed, targets, value);
    }

    static ChainedAssignmentTarget? TryStaticTarget(IrNode statement, HashSet<int> aliasSlots)
        => statement switch
        {
            StoreProperty { HasInstance: false, IndexArguments.Count: 0 } property
                when LoadsAliasSlot(property.Value, aliasSlots)
                => ChainedAssignmentTarget.StaticProperty(property.Accessor, property.IsVirtual),

            StoreField { HasInstance: false } field
                when LoadsAliasSlot(field.Value, aliasSlots)
                => ChainedAssignmentTarget.StaticField(field.Field),

            _ => null,
        };

    // A sink value reads the threaded slot either bare or through a single
    // implicit numeric widening (the compiler's conv on a wider target); the
    // widening becomes implicit in the chained assignment, so it is dropped.
    static bool LoadsAliasSlot(IrExpression value, HashSet<int> aliasSlots)
        => value switch
        {
            LoadStackSlot load => aliasSlots.Contains(load.Slot),
            Convert { Operand: LoadStackSlot { Type: { } operandType } load, Target: { } target }
                => aliasSlots.Contains(load.Slot)
                    && CSharpConversionRules.IsImplicitNumericAssignment(operandType, target),
            _ => false,
        };

    static bool IsAssignable(TypeRef target, TypeRef source)
        => target.Equals(source) || CSharpConversionRules.IsImplicitNumericAssignment(source, target);

    static void Apply(Plan plan)
    {
        var value = plan.Value;
        value.Detach();

        var chain = new ChainedAssignment(plan.Targets, value);
        chain.InheritSourceOffset(plan.Seed);
        plan.Seed.ReplaceWith(chain);

        foreach (var statement in plan.Consumed)
            if (!ReferenceEquals(statement, plan.Seed))
                statement.Detach();
    }

    // A dup'd constant left over after recomposition is re-materialized: pure
    // constants are safe to duplicate, so clone into each load and drop the
    // store. Iterated to a fixpoint because a copy slot (S1 = S0) turns into a
    // constant store once its source is re-materialized. Restricted to dup slots
    // (>= DupSlotBase) so join/edge slots are left to slot materialization.
    static void RematerializeConstantDupSlots(IrFunction function)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var store in function.Descendants.OfType<StoreStackSlot>().ToList())
            {
                if (store.Parent is null || store.Slot < StoreStackSlot.DupSlotBase)
                    continue;
                if (store.Value is not Constant constant)
                    continue;

                foreach (var load in function.Descendants.OfType<LoadStackSlot>().Where(l => l.Slot == store.Slot).ToList())
                    load.ReplaceWith((Constant)constant.Clone());
                store.Detach();
                changed = true;
            }
        }
    }

    static bool HasAncestorIn(IrNode node, HashSet<IrNode> set)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (set.Contains(parent))
                return true;
        return false;
    }
}
