namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// C# conversion predicates shared by target-typed rendering sites. This is the
/// narrow start of #2379's conversion model: rules graduate here when multiple
/// printer/pass seams need the same C# conversion fact.
/// </summary>
public static class CSharpConversionRules
{
    /// <summary>
    /// Same storage width for integer reinterpret casts, including the
    /// platform-sized <c>nint</c>/<c>nuint</c> pair whose runtime width is
    /// unknown statically but always equal.
    /// </summary>
    public static bool SameNumericSlotWidth(TypeRef? source, TypeRef? target)
        => TypeFamilies.SameWidth(source, target)
            || TypeFamilies.Of(source) == StackFamily.I && TypeFamilies.Of(target) == StackFamily.I;
}
