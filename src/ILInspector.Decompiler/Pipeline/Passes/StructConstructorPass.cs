namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises an in-place struct constructor call to a whole-value assignment:
/// <c>ldloca s; call S::.ctor(args)</c> is the C# compiler's lowering of
/// <c>s = new S(args)</c> when <c>s</c> is a local, by-ref argument, or field of
/// struct type (the JIT initializes the struct in place rather than copying a
/// temporary). The IL spelling prints as the illegal <c>s..ctor(args);</c>
/// (CS0201, "is not a valid statement"), so this pass rewrites it back into the
/// <see cref="NewObject"/> assignment the printer already renders as
/// <c>s = new S(args);</c>.
///
/// The constructor fully owns the receiver address — it is the call's receiver
/// and nothing else in the tree — so replacing the in-place init with a
/// whole-value assignment is observationally identical. The this/base
/// constructor-chain case (receiver is <c>this</c>) is left to
/// <see cref="ConstructorChainPass"/> and the printer's base(...)/this(...)
/// spelling; this pass only matches the address of a nameable storage location.
/// </summary>
public sealed class StructConstructorPass : IIrPass
{
    public string Name => "struct-constructor";

    public void Run(IrFunction function, PassContext context)
    {
        var byRefSlots = ByRefSlotTargets(function);
        foreach (var statement in function.Descendants.OfType<ExpressionStatement>().ToList())
        {
            if (statement.Parent is null)
                continue;  // detached by an earlier rewrite in this walk
            if (statement.Expression is not Call { Callee: { Name: ".ctor", HasThis: true } } call)
                continue;
            if (call.Arguments.Count == 0)
                continue;

            // The receiver's storage type must be the same value type as the constructor's
            // declaring type. Without this, `ldloca a; call B::.ctor()` would raise to the
            // invalid whole-value assignment `A a = new B();`. Check before the (destructive)
            // ResolveTarget so a decline leaves the IR untouched.
            var receiverType = ReceiverStorageType(call.Arguments[0], byRefSlots);
            if (receiverType is null || !receiverType.Equals(call.Callee.DeclaringType))
                continue;

            var receiver = ResolveTarget(call.Arguments[0], byRefSlots);
            if (receiver is null)
                continue;

            var children = call.DetachChildren().Cast<IrExpression>().ToList();
            var value = new NewObject(call.Callee, children.Skip(1));
            context.Stepper.StepOver($"raise in-place {call.Callee.DeclaringType.Name}..ctor to assignment", statement);
            statement.ReplaceWith(Assign(receiver, value));
        }
    }

    // Only the address of a nameable storage location is a safe target: the
    // address is a leaf load (local/argument) or a field load whose own
    // receiver travels with it, so the rewrite stays a local tree edit.
    static bool IsRaisableTarget(IrExpression receiver)
        => receiver is LoadLocalAddress or LoadArgumentAddress or LoadFieldAddress;

    // The value type of the receiver's storage, read non-destructively (unlike
    // ResolveTarget, which detaches the by-ref slot's address/store). Used to verify
    // the storage type matches the constructor's declaring type before raising.
    static TypeRef? ReceiverStorageType(IrExpression receiver, Dictionary<int, SlotTarget> byRefSlots)
    {
        var address = receiver;
        if (!IsRaisableTarget(address)
            && receiver is LoadStackSlot slot
            && byRefSlots.TryGetValue(slot.Slot, out var target)
            && ReferenceEquals(target.Load, receiver))
        {
            address = target.Address;
        }

        return address switch
        {
            LoadLocalAddress local => local.Type,
            LoadArgumentAddress argument => argument.Type,
            LoadFieldAddress field => field.Field.Type,
            _ => null,
        };
    }

    static IrExpression? ResolveTarget(IrExpression receiver, Dictionary<int, SlotTarget> byRefSlots)
    {
        if (IsRaisableTarget(receiver))
            return receiver;

        if (receiver is LoadStackSlot slot
            && byRefSlots.TryGetValue(slot.Slot, out var target)
            && ReferenceEquals(target.Load, receiver))
        {
            var address = target.Address;
            address.Detach();
            target.Store.Detach();
            return address;
        }

        return null;
    }

    static IrNode Assign(IrExpression receiver, NewObject value) => receiver switch
    {
        LoadLocalAddress local => new StoreLocal(local.Index, local.Type, value),
        LoadArgumentAddress argument => new StoreArgument(argument.Index, argument.Name, argument.Type, value),
        LoadFieldAddress field => new StoreField(field.Field, DetachInstance(field), value),
        _ => throw new InvalidOperationException($"Unexpected struct-ctor target {receiver.Describe()}."),
    };

    static Dictionary<int, SlotTarget> ByRefSlotTargets(IrFunction function)
    {
        var slots = new Dictionary<int, SlotUsage>();

        SlotUsage Entry(int slot)
        {
            if (!slots.TryGetValue(slot, out var entry))
                slots[slot] = entry = new SlotUsage();
            return entry;
        }

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreStackSlot store when IsRaisableTarget(store.Value):
                    Entry(store.Slot).Stores.Add(store);
                    break;
                case StoreStackSlot store:
                    Entry(store.Slot).OtherStores++;
                    break;
                case LoadStackSlot load:
                    Entry(load.Slot).Loads.Add(load);
                    break;
            }
        }

        var result = new Dictionary<int, SlotTarget>();
        foreach (var (slot, usage) in slots)
        {
            if (usage.Stores is [var store] && usage.Loads is [var load] && usage.OtherStores == 0)
                result[slot] = new SlotTarget(store, load, store.Value);
        }
        return result;
    }

    // The field address has already left the call tree; lift its own receiver
    // out so the new StoreField can adopt it without a double parent.
    static IrExpression? DetachInstance(LoadFieldAddress field)
    {
        var instance = field.Instance;
        instance?.Detach();
        return instance;
    }

    sealed class SlotUsage
    {
        public List<StoreStackSlot> Stores { get; } = [];
        public List<LoadStackSlot> Loads { get; } = [];
        public int OtherStores { get; set; }
    }

    sealed record SlotTarget(StoreStackSlot Store, LoadStackSlot Load, IrExpression Address);
}
