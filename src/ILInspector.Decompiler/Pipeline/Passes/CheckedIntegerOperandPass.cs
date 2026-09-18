namespace ILInspector.Decompiler.Pipeline;

/// <summary>Decides checked integer operand domains before emission. See docs/design/checked-integer-operands.md.</summary>
public sealed class CheckedIntegerOperandPass : IIrPass
{
    public string Name => "checked-integer-operand";

    public void Run(IrFunction function, PassContext context)
    {
        // Parents clone their operands; finish each nested binary first.
        foreach (var binary in function.Descendants.OfType<Binary>().Reverse().ToList())
        {
            if (OperandType(binary) is not { } target)
                continue;

            Bind(binary.Left, target, context);
            Bind(binary.Right, target, context);
        }
    }

    internal static TypeRef? OperandType(Binary binary)
    {
        if (!binary.IsChecked
            || binary.Kind is not (BinaryKind.Add or BinaryKind.Subtract or BinaryKind.Multiply)
            || !IsFullWidthInteger(binary.Left.ResultType)
            || !IsFullWidthInteger(binary.Right.ResultType)
            || TypeFamilies.Of(binary.Left.ResultType) != TypeFamilies.Of(binary.Right.ResultType))
        {
            return null;
        }

        var left = binary.Left.ResultType!;
        return binary.IsUnsigned
            ? TypeFamilies.UnsignedCounterpart(left) ?? left
            : TypeFamilies.SignedCounterpart(left) ?? left;
    }

    static bool IsFullWidthInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System",
            Name: "Int32" or "UInt32" or "Int64" or "UInt64" or "IntPtr" or "UIntPtr" };

    static void Bind(IrExpression operand, TypeRef target, PassContext context)
    {
        if (operand is Coerce existing && existing.Target.Equals(target))
            return;
        if (operand.ResultType?.Equals(target) == true && HasDeclaredOperandType(operand, target))
            return;

        context.Stepper.StepOver("bind checked integer operand", operand);
        var coercion = new Coerce(target, (IrExpression)operand.Clone());
        coercion.InheritSourceOffset(operand);
        operand.ReplaceWith(coercion);
    }

    static bool HasDeclaredOperandType(IrExpression operand, TypeRef target) => operand switch
    {
        LoadArgument or LoadLocal or LoadField or LoadProperty or Call or Convert or Constant => true,
        LoadElement load => load.Array.ResultType?.ElementType?.Equals(target) == true,
        LoadIndirect load => PointerArithmetic.PointeeType(load.Address)?.Equals(target) == true,
        _ => false,
    };
}
