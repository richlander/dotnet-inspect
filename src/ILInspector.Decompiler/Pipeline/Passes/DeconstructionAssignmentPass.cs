using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the narrow tuple deconstruction lowering:
/// <code>
/// S = tuple;
/// T1 a = S.Item1;
/// T2 b = S.Item2;
/// </code>
/// into <c>(T1 a, T2 b) = tuple;</c> when the targets are fresh locals declared
/// here, <c>(a, b) = tuple;</c> when they assign into pre-existing locals, or a
/// mix such as <c>(T1 a, b) = tuple;</c> — each target is classified independently
/// as a declaration or an assignment. Scoped to direct <c>System.ValueTuple</c>
/// fields, arities 2-7. Targets may be locals, by-value parameters
/// (<c>StoreArgument</c> → <c>(p, …) = tuple;</c>), or fields — a static field
/// (<c>StoreField</c> with no instance) or an instance field reached through
/// <c>this</c> (<c>StoreField</c> whose instance is <c>this</c>, only in an
/// instance method). Other field receivers, ref/out parameters, and the
/// <c>StoreLocal</c>-seed form (where an instance-field receiver disrupts the
/// importer's slot promotion) are later slices. Nested/rest tuples and
/// user-defined <c>Deconstruct</c> calls with non-local targets are later slices.
/// </summary>
public sealed class DeconstructionAssignmentPass : IIrPass
{
    public string Name => "deconstruction-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (TryRaiseValueTuple(function, block, i, context)
                    || TryRaiseDeconstructMethod(function, children[i], context))
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The receiver-spill + <c>ItemN</c> store form: a <c>ValueTuple</c> value
    /// stored to a temp — a stack slot, or a real local when an instance-field
    /// target's receiver kept the importer from promoting the temp to a slot —
    /// then read field-by-field into the targets.
    /// </summary>
    static bool TryRaiseValueTuple(IrFunction function, Block block, int i, PassContext context)
    {
        var children = block.Children;
        if (SeedOf(children[i]) is not { } seed
            || !MemberIdentity.IsSupportedValueTupleType(seed.TupleType, out var arity)
            || i + arity >= children.Count)
        {
            return false;
        }

        var stores = new List<IrNode>(arity);
        for (int j = 0; j < arity; j++)
        {
            var store = children[i + 1 + j];
            if (StoredValue(store) is not LoadField { Field.Name: var fieldName, Instance: { } instance }
                || !seed.IsSeedLoad(instance)
                || fieldName != $"Item{j + 1}")
            {
                stores.Clear();
                break;
            }
            stores.Add(store);
        }

        // The temp must be consumed entirely by this run — every read of it is one
        // of the element stores (plus, for a real-local temp, the seed store
        // itself). A read anywhere else means it is a live value, not a spill, so
        // folding it into a deconstruction would drop a use.
        if (stores.Count != arity
            || !seed.ConsumedOnlyBy(function, stores))
        {
            return false;
        }

        if (ClassifyTargets(function, stores) is not { } targets
            || targets.Any(seed.IsTarget))
        {
            return false;
        }

        var source = (IrExpression)seed.Node.DetachChildren()[^1];
        var deconstruction = new DeconstructionAssignment(targets, source);
        context.Stepper.StepOver("raise ValueTuple field stores to deconstruction", seed.Node);
        seed.Node.ReplaceWith(deconstruction);
        foreach (var store in stores)
            store.Detach();
        return true;
    }

    /// <summary>
    /// The tuple temp the element stores read from: either a <c>StoreStackSlot</c>
    /// (a compiler stack spill) or a <c>StoreLocal</c> (a real local the importer
    /// could not promote, the shape an instance-field target leaves behind).
    /// Returns null when the statement is neither, or its value is not a tuple.
    /// </summary>
    static SeedTemp? SeedOf(IrNode statement) => statement switch
    {
        StoreStackSlot slot when slot.Value.ResultType is { } type => new SlotSeed(slot, type),
        StoreLocal local when local.Value.ResultType is { } type => new LocalSeed(local, type),
        _ => null,
    };

    abstract class SeedTemp
    {
        public abstract IrNode Node { get; }
        public abstract TypeRef TupleType { get; }

        /// <summary>True when <paramref name="instance"/> is a read of this temp.</summary>
        public abstract bool IsSeedLoad(IrExpression instance);

        /// <summary>True when every read of this temp is one of the element stores (or the seed itself).</summary>
        public abstract bool ConsumedOnlyBy(IrFunction function, IReadOnlyList<IrNode> stores);

        /// <summary>True when a target would overwrite this temp — a real-local temp used as its own target.</summary>
        public virtual bool IsTarget(DeconstructionTarget target) => false;
    }

    sealed class SlotSeed : SeedTemp
    {
        readonly StoreStackSlot _seed;
        public SlotSeed(StoreStackSlot seed, TypeRef tupleType) { _seed = seed; TupleType = tupleType; }
        public override IrNode Node => _seed;
        public override TypeRef TupleType { get; }
        public override bool IsSeedLoad(IrExpression instance) => instance is LoadStackSlot load && load.Slot == _seed.Slot;
        public override bool ConsumedOnlyBy(IrFunction function, IReadOnlyList<IrNode> stores)
        {
            foreach (var load in function.Descendants.OfType<LoadStackSlot>().Where(load => load.Slot == _seed.Slot))
                if (!stores.Any(store => IsInside(load, store)))
                    return false;
            return true;
        }
    }

    sealed class LocalSeed : SeedTemp
    {
        readonly StoreLocal _seed;
        public LocalSeed(StoreLocal seed, TypeRef tupleType) { _seed = seed; TupleType = tupleType; }
        public override IrNode Node => _seed;
        public override TypeRef TupleType { get; }
        public override bool IsSeedLoad(IrExpression instance) => instance is LoadLocal load && load.Index == _seed.Index;
        public override bool IsTarget(DeconstructionTarget target) => target is LocalDeconstructionTarget local && local.Index == _seed.Index;
        public override bool ConsumedOnlyBy(IrFunction function, IReadOnlyList<IrNode> stores)
        {
            foreach (var node in function.Descendants)
            {
                bool reads = node switch
                {
                    LoadLocal load => load.Index == _seed.Index,
                    LoadLocalAddress address => address.Index == _seed.Index,
                    StoreLocal store => store.Index == _seed.Index,
                    _ => false,
                };
                if (reads && !ReferenceEquals(node, _seed) && !stores.Any(store => IsInside(node, store)))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The <c>Deconstruct</c>-method form: a single void <c>r.Deconstruct(out a,
    /// out b, ...)</c> call statement, the lowering of <c>(a, b) = r;</c> when
    /// <c>r</c>'s type supplies a <c>Deconstruct</c> method. Scoped to a
    /// side-effect-free local/parameter receiver (the only shape in the corpus —
    /// foreach <c>Current</c> and locals) and all-local targets; the out-temp +
    /// copy form, non-local targets, and other receivers are later slices.
    /// </summary>
    static bool TryRaiseDeconstructMethod(IrFunction function, IrNode statement, PassContext context)
    {
        var call = statement as Call ?? (statement as ExpressionStatement)?.Expression as Call;
        if (call is null
            || call.Callee is not { Name: "Deconstruct", HasThis: true, ReturnType: { Namespace: "System", Name: "Void" } }
            || call.Arguments.Count is < 3 or > 8)
        {
            return false;
        }

        var receiver = call.Arguments[0];
        if (ReceiverValue(receiver) is not { } source)
            return false;

        var outArgs = call.Arguments.Skip(1).ToList();
        if (outArgs.Any(arg => arg is not LoadLocalAddress))
            return false;

        // A target that aliases the receiver local would re-read the value it is
        // overwriting; `(a, b) = a` keeps the de-sugared call instead.
        if (receiver is LoadLocalAddress receiverLocal
            && outArgs.Cast<LoadLocalAddress>().Any(arg => arg.Index == receiverLocal.Index))
        {
            return false;
        }

        var targets = outArgs.Cast<LoadLocalAddress>()
            .Select(arg => (DeconstructionTarget)new LocalDeconstructionTarget(arg.Index, arg.Type, IsFirstReference(function, arg, arg.Index)))
            .ToImmutableArray();
        if (!DistinctTargets(targets))
            return false;

        var deconstruction = new DeconstructionAssignment(targets, source);
        context.Stepper.StepOver("raise Deconstruct-method call to deconstruction", statement);
        statement.ReplaceWith(deconstruction);
        return true;
    }

    /// <summary>The value form of a side-effect-free <c>Deconstruct</c> receiver, or null when the receiver is unsupported.</summary>
    static IrExpression? ReceiverValue(IrExpression receiver) => receiver switch
    {
        LoadLocalAddress address => new LoadLocal(address.Index, address.Type),
        LoadArgumentAddress address => new LoadArgument(address.Index, address.Name, address.Type),
        LoadLocal local => new LoadLocal(local.Index, local.Type),
        LoadArgument argument => new LoadArgument(argument.Index, argument.Name, argument.Type),
        _ => null,
    };

    /// <summary>The value an assignment store writes, regardless of its target kind.</summary>
    static IrExpression? StoredValue(IrNode store) => store switch
    {
        StoreLocal local => local.Value,
        StoreArgument argument => argument.Value,
        StoreField field => field.Value,
        _ => null,
    };

    /// <summary>
    /// Resolves the deconstruction targets from the per-element stores, classifying
    /// each independently. A local is a fresh declaration when its first reference
    /// is this store, otherwise an assignment; a run may mix the two
    /// (<c>(int x, y) = …</c>). Parameter and field targets are always assignments.
    /// Field targets are limited to static fields and <c>this</c>-instance fields
    /// (instance only in an instance method); any other store shape declines the
    /// whole run. Distinct target places are required since a deconstruction
    /// <em>declaration</em> cannot repeat a name and a repeated place would also be
    /// a degenerate write. Returns null to decline.
    /// </summary>
    static ImmutableArray<DeconstructionTarget>? ClassifyTargets(IrFunction function, IReadOnlyList<IrNode> stores)
    {
        var targets = new DeconstructionTarget[stores.Count];
        for (int j = 0; j < stores.Count; j++)
        {
            if (BuildTarget(function, stores[j]) is not { } target)
                return null;
            targets[j] = target;
        }

        var resolved = ImmutableArray.Create(targets);
        return DistinctTargets(resolved) ? resolved : null;
    }

    static DeconstructionTarget? BuildTarget(IrFunction function, IrNode store) => store switch
    {
        StoreLocal local => new LocalDeconstructionTarget(local.Index, local.Type, IsFirstReference(function, local, local.Index)),
        StoreArgument argument => new ArgumentDeconstructionTarget(argument.Index, argument.Name, argument.Type),
        StoreField { HasInstance: false } field => new FieldDeconstructionTarget(field.Field, isThisInstance: false),
        StoreField { HasInstance: true, Instance: LoadArgument { Index: 0 } } field when function.Signature.HasThis
            => new FieldDeconstructionTarget(field.Field, isThisInstance: true),
        _ => null,
    };

    /// <summary>True when every target names a distinct place: a unique local index, parameter index, or field identity.</summary>
    static bool DistinctTargets(ImmutableArray<DeconstructionTarget> targets)
    {
        var seen = new HashSet<(int, int, string?)>();
        foreach (var target in targets)
        {
            var key = target switch
            {
                LocalDeconstructionTarget local => (0, local.Index, (string?)null),
                ArgumentDeconstructionTarget argument => (1, argument.Index, (string?)null),
                FieldDeconstructionTarget field => (field.IsThisInstance ? 2 : 3, 0, $"{field.Field.DeclaringType.ToDisplayString()}.{field.Field.Name}"),
                _ => (4, 0, (string?)null),
            };
            if (!seen.Add(key))
                return false;
        }
        return true;
    }

    static bool IsFirstReference(IrFunction function, IrNode target, int index)
    {
        foreach (var node in function.Descendants)
        {
            if (ReferenceEquals(node, target))
                return true;
            if (node is LoadLocal load && load.Index == index
                || node is StoreLocal store && store.Index == index
                || node is LoadLocalAddress address && address.Index == index)
            {
                return false;
            }
        }
        return false;
    }

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }
}
