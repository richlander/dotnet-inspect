namespace ILInspector.Decompiler.Pipeline;

/// <summary>Decides pointer-variable updates before rendering. See docs/design/pointer-variable-compound-updates.md.</summary>
public sealed class PointerCompoundAssignmentPass : IIrPass
{
    public string Name => "pointer-compound-assignment";

    public void Run(IrFunction function, PassContext context)
    {
        var nodes = function.DescendantsOutsideNestedFunctions.ToArray();
        var slotTypes = nodes.OfType<StoreStackSlot>().Any()
            ? CoercionSinks.TestifiedSlotTypes(function.Body, function.Signature.ReturnType, function.TypeShapes)
            : [];
        var slotStores = nodes.OfType<StoreStackSlot>().ToLookup(store => store.Slot);

        foreach (var store in nodes)
        {
            var (value, type) = store switch
            {
                StoreLocal s => (s.Value, s.Type),
                StoreArgument s => (s.Value, s.Type),
                StoreStackSlot s when slotTypes.TryGetValue(s.Slot, out var slotType)
                    && slotStores[s.Slot].All(other => CoercionDomain.IsAtTarget(other.Value, slotType))
                    => (s.Value, slotType),
                StoreField s => (s.Value, s.Field.Type),
                StoreProperty s => (s.Value, s.Accessor.ParameterTypes.LastOrDefault()),
                StoreIndirect s => (s.Value, s.Address.ResultType is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer } address
                    ? address.ElementType : s.Type),
                _ => (null, null),
            };
            if (type is not { Kind: TypeRefKind.Pointer, ElementType: { } element }
                || value is not Binary { Kind: BinaryKind.Add or BinaryKind.Subtract } binary
                || binary.IsUnsigned != binary.IsChecked
                || !ReadsTarget(store, binary.Left, type)
                || binary.Left.DescendantsAndSelfOutsideNestedFunctions.Any(node => node is LoadField { IsVolatile: true } or LoadIndirect { IsVolatile: true })
                || !TryIndex(binary.Right, element, binary.IsChecked, out var index))
            {
                continue;
            }

            var kind = (binary.Kind, index is Constant { Value: 1 }) switch
            {
                (BinaryKind.Add, true) => PointerUpdateKind.Increment,
                (BinaryKind.Subtract, true) => PointerUpdateKind.Decrement,
                (BinaryKind.Add, false) => PointerUpdateKind.Add,
                _ => PointerUpdateKind.Subtract,
            };
            context.Stepper.StepOver("decide pointer-variable compound update", store);
            var target = binary.Left;
            index.InheritSourceOffset(binary.Right);
            target.Detach();
            if (index.Parent is not null)
                index.Detach();
            var update = new PointerCompoundAssignment(type, kind, binary.IsChecked, target, index,
                store is StoreProperty property ? property.Accessor : null);
            update.InheritSourceOffset(store);
            store.ReplaceWith(update);
        }
    }

    static bool ReadsTarget(IrNode store, IrExpression read, TypeRef pointerType) => (store, read) switch
    {
        (StoreLocal s, LoadLocal l) => s.Index == l.Index,
        (StoreArgument s, LoadArgument l) => PlaceIdentity.SameArgument(s.Index, s.Parameter, l.Index, l.Parameter),
        (StoreStackSlot s, LoadStackSlot l) => s.Slot == l.Slot,
        (StoreField { IsVolatile: false } s, LoadField { IsVolatile: false } l)
            => Equals(s.Field, l.Field) && PlaceIdentity.SamePlace(s.Instance, l.Instance),
        (StoreProperty s, LoadProperty l)
            => s.PropertyName == l.PropertyName
                && Equals(s.Accessor.DeclaringType, l.Accessor.DeclaringType)
                && Equals(s.Accessor.ParameterTypes.LastOrDefault(), l.ResultType)
                && s.Accessor.ParameterTypes.Take(s.Accessor.ParameterTypes.Length - 1).SequenceEqual(l.Accessor.ParameterTypes)
                && s.IsVirtual == l.IsVirtual
                && PlaceIdentity.SameLValue(s.Instance, l.Instance)
                && PlaceIdentity.SameOperands(s.IndexArguments, l.IndexArguments),
        (StoreIndirect { IsVolatile: false } s, LoadIndirect { IsVolatile: false } l)
            => Equals(s.Type, l.Type)
                && (Equals(s.Type, pointerType)
                    || MemberIdentity.IsCoreLibraryType(s.Type, "System", "IntPtr")
                    || MemberIdentity.IsCoreLibraryType(s.Type, "System", "UIntPtr"))
                && PlaceIdentity.SameLValue(s.Address, l.Address),
        _ => false,
    };

    static bool TryIndex(IrExpression offset, TypeRef element, bool isChecked, out IrExpression index)
    {
        if (!PointerArithmetic.TryScaledPointerIndex(offset, element, out index)
            || !MemberIdentity.IsCoreLibraryType(index.ResultType, "System", "Int32"))
        {
            return false;
        }
        if (offset is Constant { Value: int })
            return true;
        if (PointerArithmetic.ByteSize(element) == 1)
            return isChecked ? IsNativeIndex(offset, index) : ReferenceEquals(offset, index);
        return offset is Binary { Kind: BinaryKind.Multiply, IsUnsigned: false } scale
            && scale.IsChecked == isChecked
            && IsNativeIndex(scale.Left, index)
            && PointerArithmetic.ByteSize(element) is { } size
            && PointerArithmetic.IsConstant(scale.Right, size);
    }

    static bool IsNativeIndex(IrExpression expression, IrExpression index)
        => expression is Convert { IsChecked: false, IsUnsigned: false } conversion
            && MemberIdentity.IsCoreLibraryType(conversion.Target, "System", "IntPtr")
            && ReferenceEquals(conversion.Operand, index);
}
