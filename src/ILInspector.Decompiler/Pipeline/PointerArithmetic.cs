namespace ILInspector.Decompiler.Pipeline;

internal static class PointerArithmetic
{
    internal static TypeRef? PointeeType(IrExpression address) => address.ResultType switch
    {
        { Kind: TypeRefKind.Pointer or TypeRefKind.ByRef, ElementType: { } element } => element,
        _ => address switch
        {
            Binary { Kind: BinaryKind.Add or BinaryKind.Subtract } binary => PointeeType(binary.Left) ?? PointeeType(binary.Right),
            Convert conversion => PointeeType(conversion.Operand),
            _ => null,
        },
    };

    internal static bool TrySplitPointerAdd(Binary add, out IrExpression pointer, out IrExpression offset)
    {
        if (add.Left.ResultType is { Kind: TypeRefKind.Pointer } && add.Right.ResultType is not { Kind: TypeRefKind.Pointer })
        {
            pointer = add.Left;
            offset = add.Right;
            return true;
        }
        if (add.Right.ResultType is { Kind: TypeRefKind.Pointer } && add.Left.ResultType is not { Kind: TypeRefKind.Pointer })
        {
            pointer = add.Right;
            offset = add.Left;
            return true;
        }

        pointer = add.Left;
        offset = add.Right;
        return false;
    }

    internal static bool TryScaledPointerIndex(IrExpression offset, TypeRef elementType, out IrExpression index)
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

    internal static bool IsConstant(IrExpression expression, int value)
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

    internal static int? ByteSize(TypeRef type)
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
