namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's null-coalescing assignment lowering:
/// <code>
/// if (V is null) { V = fallback; }            // local
/// if (obj.field is null) { obj.field = fallback; }  // field
/// </code>
/// into <c>V ??= fallback</c> / <c>obj.field ??= fallback</c>. The field form is
/// scoped to a re-evaluable receiver (a local/argument/this, or none for a static
/// field), so collapsing the two member loads into one <c>??=</c> reorders
/// nothing. The property form (<c>obj.Prop ??= fallback</c>) pairs the getter and
/// setter as one property and folds csc's value-spill (the setter takes the value
/// through a temp so the assignment can also be the expression's result; in
/// statement position that result is dead). The indexer form (<c>d[k] ??= fallback</c>)
/// is the property form plus index arguments, accepted when the receiver and every
/// index argument are pairwise re-evaluable (<see cref="PlaceIdentity.SameOperands"/>).
/// </summary>
public sealed class NullCoalescingAssignmentPass : IIrPass
{
    public string Name => "null-coalescing-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var statement in function.Descendants.OfType<IfStatement>().ToList())
        {
            if (TryMatchLocal(function, statement) is { } local)
            {
                var value = (IrExpression)local.Store.DetachChildren()[0];
                var replacement = new NullCoalescingAssignment(local.Local.Index, local.Local.Type, value);
                context.Stepper.StepOver("raise local null check assignment to ??=", statement);
                statement.ReplaceWith(replacement);
                continue;
            }

            if (TryMatchField(function, statement) is { } field)
            {
                var children = field.Store.DetachChildren();
                var instance = field.Store.HasInstance ? (IrExpression)children[0] : null;
                var value = (IrExpression)children[^1];
                var replacement = new NullCoalescingFieldAssignment(field.Store.Field, instance, value);
                context.Stepper.StepOver("raise field null check assignment to ??=", statement);
                statement.ReplaceWith(replacement);
                continue;
            }

            if (TryMatchProperty(function, statement) is { } property)
            {
                var instance = property.Store.Instance;
                var indexArguments = property.Store.IndexArguments.ToList();
                var value = property.Value;
                instance?.Detach();
                foreach (var argument in indexArguments)
                    argument.Detach();
                value.Detach();
                var replacement = new NullCoalescingPropertyAssignment(
                    property.Store.Accessor, instance, indexArguments, value, property.Store.IsVirtual);
                context.Stepper.StepOver("raise property null check assignment to ??=", statement);
                statement.ReplaceWith(replacement);
            }
        }
    }

    sealed record LocalMatch(LoadLocal Local, StoreLocal Store);

    static LocalMatch? TryMatchLocal(IrFunction function, IfStatement statement)
    {
        if (statement.HasElse
            || statement.Then.Children is not [StoreLocal store]
            || NullTested(statement.Condition) is not LoadLocal local
            || store.Index != local.Index)
        {
            return null;
        }

        if (IsNonNullableValue(function, local.Type))
            return null;

        return new LocalMatch(local, store);
    }

    sealed record FieldMatch(StoreField Store);

    static FieldMatch? TryMatchField(IrFunction function, IfStatement statement)
    {
        if (statement.HasElse
            || statement.Then.Children is not [StoreField store]
            || NullTested(statement.Condition) is not LoadField loaded
            || !SameField(loaded.Field, store.Field)
            || !SameReceiver(loaded.Instance, store.Instance))
        {
            return null;
        }

        if (IsNonNullableValue(function, store.Field.Type))
            return null;

        return new FieldMatch(store);
    }

    static bool IsNonNullableValue(IrFunction function, TypeRef type)
        => TypeFamilies.IsKnownNonNullableValueType(type, function.TypeShapes);

    static bool SameField(FieldRef a, FieldRef b)
        => a.Name == b.Name && Equals(a.DeclaringType, b.DeclaringType);

    sealed record PropertyMatch(StoreProperty Store, IrExpression Value);

    // obj.Prop ??= fallback lowers to a get/set diamond:
    //   if (obj.get_Prop() is null) obj.set_Prop(fallback);
    // The setter is a call, so csc routes the value through a temp — the slot the
    // setter reads is also stored to a (dead, in statement position) local so the
    // assignment can double as the ??= expression's result. We pair the getter and
    // setter as one property, require a re-evaluable receiver, and recover the
    // original value from behind that spill.
    static PropertyMatch? TryMatchProperty(IrFunction function, IfStatement statement)
    {
        if (statement.HasElse
            || NullTested(statement.Condition) is not LoadProperty getter
            || statement.Then.Children is not [.., StoreProperty store]
            || !CompatiblePropertyAccessors(getter, store)
            || !SameReceiver(getter.Instance, store.Instance)
            || !PlaceIdentity.SameOperands(getter.IndexArguments, store.IndexArguments))
        {
            return null;
        }

        if (IsNonNullableValue(function, store.Accessor.ParameterTypes[^1]))
            return null;

        var value = RecoverSpilledValue(function, statement.Then, store);
        return value is null ? null : new PropertyMatch(store, value);
    }

    static bool CompatiblePropertyAccessors(LoadProperty getter, StoreProperty store)
    {
        var getAccessor = getter.Accessor;
        var setAccessor = store.Accessor;
        if (store.PropertyName != getter.PropertyName
            || !Equals(setAccessor.DeclaringType, getAccessor.DeclaringType)
            || setAccessor.HasThis != getAccessor.HasThis
            || store.HasInstance != getter.HasInstance
            || store.IsVirtual != getter.IsVirtual
            || !IsVoid(setAccessor.ReturnType)
            || setAccessor.ParameterTypes.Length != getAccessor.ParameterTypes.Length + 1)
        {
            return false;
        }

        for (int i = 0; i < getAccessor.ParameterTypes.Length; i++)
            if (!Equals(setAccessor.ParameterTypes[i], getAccessor.ParameterTypes[i]))
                return false;

        return Equals(setAccessor.ParameterTypes[^1], getAccessor.ReturnType);
    }

    static bool IsVoid(TypeRef type) => type is { Namespace: "System", Name: "Void" };

    // The clean form is `Then = [store]` with the value inline (a static property,
    // or any setter csc fed directly). The spilled form is
    //   [StoreStackSlot S = fallback, (StoreLocal dead = S)*, store(value: load S)]
    // where the setter and the dead ??= result both read slot S. We return the real
    // value (fallback) only when slot S and the dead local are confined to this
    // block — i.e. nothing else in the function reads them, so dropping the spill is
    // sound.
    static IrExpression? RecoverSpilledValue(IrFunction function, Block then, StoreProperty store)
    {
        var children = then.Children;
        if (store.Value is not LoadStackSlot slot)
            return children.Count == 1 ? store.Value : null;

        if (children.Count < 2 || children[0] is not StoreStackSlot spill || spill.Slot != slot.Slot)
            return null;

        // Every statement between the spill and the store is the dead capture of the
        // ??= result: a local store of the same slot, never read elsewhere.
        for (int i = 1; i < children.Count - 1; i++)
        {
            if (children[i] is not StoreLocal { Value: LoadStackSlot read } dead
                || read.Slot != slot.Slot
                || function.Descendants.OfType<LoadLocal>().Any(l => l.Index == dead.Index))
            {
                return null;
            }
        }

        // The slot is a single-assignment spill local to this idiom: one store (the
        // spill) and reads only from the statements matched above.
        int expectedLoads = children.Count - 1;
        if (function.Descendants.OfType<StoreStackSlot>().Count(s => s.Slot == slot.Slot) != 1
            || function.Descendants.OfType<LoadStackSlot>().Count(l => l.Slot == slot.Slot) != expectedLoads)
        {
            return null;
        }

        return spill.Value;
    }

    // The receiver is loaded once for the null test and once for the store; the
    // fold is only sound when re-evaluating it is free of side effects and yields
    // the same value — i.e. a static field (no receiver) or a plain
    // local/argument/this variable read (PlaceIdentity.SameVariable).
    static bool SameReceiver(IrExpression? a, IrExpression? b)
        => (a is null && b is null) || PlaceIdentity.SameVariable(a, b);

    static IrExpression? NullTested(IrExpression condition) => condition switch
    {
        LogicalNot { Operand: var operand } => operand,
        Comparison { Kind: ComparisonKind.Equal, Left: var left, Right: Constant { Value: null } } => left,
        Comparison { Kind: ComparisonKind.Equal, Left: Constant { Value: null }, Right: var right } => right,
        _ => null,
    };
}
