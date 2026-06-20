namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Removes identity conversions: a <em>plain</em> <c>conv</c> whose target
/// type already equals its operand's result type. The canonical source is the
/// array-length idiom — <c>ldlen</c> yields a native int that the importer
/// models as the <c>int</c>-typed <see cref="ArrayLength"/>, and the trailing
/// <c>conv.i4</c> then converts <c>int</c> to <c>int</c>. C# spells
/// <c>array.Length</c> with no cast, so the conversion is pure noise
/// (<c>((int)a.Length)</c> → <c>a.Length</c>). A genuine narrowing —
/// <c>conv.i4</c> of a <c>long</c> — has differing types and is left untouched.
///
/// Only plain (unchecked, signed) conversions qualify: a checked or unsigned
/// conversion is not a no-op even at equal types. <c>conv.ovf.i4.un</c> on an
/// <c>int</c> is <c>Int32 → Int32</c>, but it reinterprets the source as
/// unsigned and throws for negative bit patterns — the printer preserves that
/// as <c>checked((int)(uint)x)</c>, which this pass must not erase.
///
/// <para>One more case is <em>not</em> identity despite equal IR types: a
/// narrowing to a sub-<c>int</c> type (<c>byte</c>/<c>sbyte</c>/<c>short</c>/
/// <c>ushort</c>/<c>char</c>) over an arithmetic or bitwise expression. IL models
/// <c>byte | byte</c> as <c>byte</c> (ECMA-335 "wider operand wins"), so the
/// trailing <c>conv.u1</c> is <c>Byte → Byte</c> here — but C# applies binary
/// numeric promotion and types that same expression as <c>int</c>, so the cast
/// is a real narrowing the language requires (<c>b = b | x;</c> is CS0266 without
/// it). Keeping it is fidelity-neutral — the <c>conv</c> was in the original IL.</para>
/// </summary>
public sealed class IdentityConvertPass : IIrPass
{
    public string Name => "identity-convert";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var convert in function.Descendants.OfType<Convert>().ToList())
        {
            if (convert.Parent is null)
                continue;  // already detached by an outer rewrite this pass
            if (convert.IsChecked || convert.IsUnsigned)
                continue;  // overflow/unsigned semantics survive equal types
            var operandType = convert.Operand.ResultType;
            if (operandType is not null && operandType.Equals(convert.Target))
            {
                if (IsSubInt32Integer(convert.Target) && PromotesToInt32(convert.Operand))
                    continue;  // C# promotes the operand to int; the narrowing cast is real
                var operand = (IrExpression)convert.DetachChildren()[0];
                context.Stepper.StepOver($"drop identity conversion to {convert.Target.Name}", convert);
                convert.ReplaceWith(operand);
            }
        }
    }

    // C# binary numeric promotion widens sub-int operands to int, so an arithmetic,
    // bitwise, shift (Binary), or unary negate/complement expression over them is
    // int-typed in C# even when the IR models it narrower. Comparisons are a
    // separate node (bool-typed), so every Binary/Unary here is a promoting op.
    static bool PromotesToInt32(IrExpression operand) => operand is Binary or Unary;

    static bool IsSubInt32Integer(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "Byte" or "SByte" or "Int16" or "UInt16" or "Char";
}
