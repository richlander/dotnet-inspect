namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises csc's fixed-buffer element lowering from the generated
/// <c>FixedElementField</c> address plus byte offset back to the source
/// <c>buffer[index]</c> place.
/// </summary>
public sealed class FixedBufferElementAccessPass : IIrPass
{
    public string Name => "fixed-buffer-element-access";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var node in function.Descendants.ToList())
        {
            switch (node)
            {
                case LoadIndirect load
                    when load.Type is { } loadType
                        && TryCreate(load.Address, loadType, out var loadAddress):
                    context.Stepper.StepOver("raise fixed-buffer read address", load.Address);
                    load.Address.ReplaceWith(loadAddress);
                    break;

                case StoreIndirect store
                    when store.Type is { } storeType
                        && TryCreate(store.Address, storeType, out var storeAddress):
                    context.Stepper.StepOver("raise fixed-buffer write address", store.Address);
                    store.Address.ReplaceWith(storeAddress);
                    break;

                case StoreLocal store
                    when store.Type is { Kind: TypeRefKind.Pinned, ElementType: { Kind: TypeRefKind.ByRef, ElementType: { } element } }
                        && TryCreate(store.Value, element, out var pinSource):
                    context.Stepper.StepOver("raise fixed-buffer pinned source", store.Value);
                    store.Value.ReplaceWith(pinSource);
                    break;
            }
        }
    }

    static bool TryCreate(IrExpression address, TypeRef expectedElement, out FixedBufferElementAddress access)
    {
        access = null!;
        if (address is not Binary { Kind: BinaryKind.Add } add
            || !TrySplitFixedElementAddress(add, out var elementFieldAddress, out var offset)
            || elementFieldAddress.Instance is not LoadFieldAddress bufferFieldAddress
            || bufferFieldAddress.Instance is null
            || bufferFieldAddress.Field.FixedBuffer is not { } fixedBuffer
            || !ElementAccessTypeMatches(expectedElement, fixedBuffer.ElementType)
            || !elementFieldAddress.Field.Type.Equals(fixedBuffer.ElementType)
            || !elementFieldAddress.Field.DeclaringType.Equals(bufferFieldAddress.Field.Type)
            || !TryScaledPointerIndex(offset, fixedBuffer.ElementType, out var index))
        {
            return false;
        }

        access = new FixedBufferElementAddress(
            bufferFieldAddress.Field,
            fixedBuffer.ElementType,
            (IrExpression)bufferFieldAddress.Instance.Clone(),
            (IrExpression)index.Clone());
        access.InheritSourceOffset(add);
        return true;
    }

    static bool TrySplitFixedElementAddress(Binary add, out LoadFieldAddress fieldAddress, out IrExpression offset)
    {
        if (add.Left is LoadFieldAddress left && IsFixedElementField(left))
        {
            fieldAddress = left;
            offset = add.Right;
            return true;
        }

        if (add.Right is LoadFieldAddress right && IsFixedElementField(right))
        {
            fieldAddress = right;
            offset = add.Left;
            return true;
        }

        fieldAddress = null!;
        offset = add.Right;
        return false;
    }

    static bool IsFixedElementField(LoadFieldAddress address)
        => address.Field.Name == "FixedElementField";

    static bool ElementAccessTypeMatches(TypeRef observed, TypeRef element)
    {
        if (observed.Equals(element))
            return true;

        return element is { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Boolean" }
                && observed is { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Byte" }
            || element is { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Char" }
                && observed is { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "UInt16" };
    }

    static bool TryScaledPointerIndex(IrExpression offset, TypeRef elementType, out IrExpression index)
    {
        if (ByteSize(elementType) is not { } elementSize)
        {
            index = offset;
            return false;
        }

        if (TryConstantMultiple(offset, elementSize, out var multiple))
        {
            index = multiple >= int.MinValue && multiple <= int.MaxValue
                ? new Constant((int)multiple, TypeRef.CoreLib("System", "Int32"))
                : new Constant(multiple, TypeRef.CoreLib("System", "Int64"));
            return true;
        }

        if (offset is Binary { Kind: BinaryKind.Multiply } multiply)
        {
            if (IsConstant(multiply.Left, elementSize))
            {
                index = NativeIntegerOperand(multiply.Right);
                return true;
            }

            if (IsConstant(multiply.Right, elementSize))
            {
                index = NativeIntegerOperand(multiply.Left);
                return true;
            }
        }

        if (elementSize == 1)
        {
            index = NativeIntegerOperand(offset);
            return true;
        }

        index = offset;
        return false;
    }

    static IrExpression NativeIntegerOperand(IrExpression expression)
        => expression is Convert { Target: { Namespace: "System", Assembly: TypeRef.CoreLibrary, Name: "IntPtr" or "UIntPtr" }, Operand: { } operand }
            ? operand
            : expression;

    static bool IsConstant(IrExpression expression, int value)
        => expression is Constant { Value: int i } && i == value
            || expression is Constant { Value: long l } && l == value;

    static bool TryConstantMultiple(IrExpression expression, int divisor, out long multiple)
    {
        long value = expression switch
        {
            Constant { Value: int i } => i,
            Constant { Value: long l } => l,
            _ => 0,
        };
        if (expression is not Constant { Value: int or long } || divisor == 0 || value % divisor != 0)
        {
            multiple = 0;
            return false;
        }

        multiple = value / divisor;
        return true;
    }

    static int? ByteSize(TypeRef type)
        => type is { Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            ? type.Name switch
            {
                "Boolean" or "Byte" or "SByte" => 1,
                "Char" or "Int16" or "UInt16" => 2,
                "Int32" or "UInt32" or "Single" => 4,
                "Int64" or "UInt64" or "Double" => 8,
                _ => null,
            }
            : null;
}
