namespace ILInspector.Decompiler.Pipeline;

/// <summary>The C# type produced by spelling an expression's decided IR.</summary>
internal static class CSharpExpressionType
{
    internal static TypeRef? Effective(IrExpression value)
    {
        if (value is Binary binary)
        {
            if (IsSubIntInteger(binary.ResultType))
                return TypeRef.CoreLib("System", "Int32");
            if (binary is { IsUnsigned: true, Kind: BinaryKind.Divide or BinaryKind.Remainder or BinaryKind.ShiftRight }
                && TypeFamilies.UnsignedCounterpart(binary.ResultType) is { } unsigned)
            {
                return unsigned;
            }
            if (RendersUnsigned(binary))
            {
                return TypeFamilies.IsUnsignedIntegerPrimitive(binary.ResultType)
                    ? binary.ResultType
                    : TypeFamilies.UnsignedCounterpart(binary.ResultType);
            }
        }
        return value.ResultType;
    }

    internal static bool IsWideInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "Int32" or "UInt32" or "Int64" or "UInt64" or "IntPtr" or "UIntPtr";

    static bool RendersUnsigned(Binary binary)
    {
        bool arithmetic = !binary.IsChecked
            && binary.Kind is BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply;
        bool bitwise = binary.Kind is BinaryKind.And or BinaryKind.Or or BinaryKind.Xor;
        if (!arithmetic && !bitwise)
            return false;
        var left = Effective(binary.Left);
        var right = Effective(binary.Right);
        return IsWideInteger(left) && IsWideInteger(right)
            && TypeFamilies.Of(left) == TypeFamilies.Of(right)
            && (TypeFamilies.IsUnsignedIntegerPrimitive(left)
                || TypeFamilies.IsUnsignedIntegerPrimitive(right));
    }

    static bool IsSubIntInteger(TypeRef? type)
        => type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            Namespace: "System",
        }
        && type.Name is "Byte" or "SByte" or "Int16" or "UInt16" or "Char";
}
