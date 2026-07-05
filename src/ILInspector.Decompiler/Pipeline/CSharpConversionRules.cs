namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// C# conversion predicates shared by target-typed rendering sites. This is the
/// narrow start of #2379's conversion model: rules graduate here when multiple
/// printer/pass seams need the same C# conversion fact.
/// </summary>
public static class CSharpConversionRules
{
    /// <summary>
    /// True when a value of <paramref name="source"/> needs an explicit C# cast
    /// to flow into <paramref name="target"/>. Restricted to numeric primitives
    /// in the same stack family where the cast is a value-preserving
    /// reinterpretation of bits already on the evaluation stack.
    /// </summary>
    public static bool NeedsNumericCast(TypeRef? source, TypeRef? target)
    {
        if (source is null || target is null || source.Equals(target))
            return false;
        if (TypeFamilies.IsBoolean(source)
            || TypeFamilies.IsBoolean(target)
            || !TypeFamilies.IsNumericPrimitive(source)
            || !TypeFamilies.IsNumericPrimitive(target))
            return false;
        var family = TypeFamilies.Of(source);
        if (family is null || family != TypeFamilies.Of(target))
            return false;
        return !ImplicitSameFamilyNumericConversion(source.Name, target.Name);
    }

    /// <summary>
    /// Same storage width for integer reinterpret casts, including the
    /// platform-sized <c>nint</c>/<c>nuint</c> pair whose runtime width is
    /// unknown statically but always equal.
    /// </summary>
    public static bool SameNumericSlotWidth(TypeRef? source, TypeRef? target)
        => TypeFamilies.SameWidth(source, target)
            || TypeFamilies.Of(source) == StackFamily.I && TypeFamilies.Of(target) == StackFamily.I;

    /// <summary>True when the integer constant can flow to <paramref name="target"/> through C#'s implicit constant-expression conversion.</summary>
    public static bool ConstantFits(long value, TypeRef target) => target switch
    {
        { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" } => target.Name switch
        {
            "SByte" => value is >= sbyte.MinValue and <= sbyte.MaxValue,
            "Byte" => value is >= byte.MinValue and <= byte.MaxValue,
            "Int16" => value is >= short.MinValue and <= short.MaxValue,
            "UInt16" or "Char" => value is >= ushort.MinValue and <= ushort.MaxValue,
            "Int32" => value is >= int.MinValue and <= int.MaxValue,
            "UInt32" => value is >= uint.MinValue and <= uint.MaxValue,
            "Int64" or "IntPtr" => true,
            "UInt64" or "UIntPtr" => value >= 0,
            "Single" or "Double" => true,
            _ => false,
        },
        _ => false,
    };

    /// <summary>
    /// C# implicit integer/native/char widening: the source value range is wholly
    /// contained in the target range, so no cast or checked wrapper is needed.
    /// </summary>
    public static bool IsImplicitIntegerWidening(TypeRef source, TypeRef target)
        => TypeFamilies.IsIntegerLike(source) && TypeFamilies.IsIntegerLike(target) && (source.Name, target.Name) switch
        {
            ("SByte", "Int16" or "Int32" or "Int64" or "IntPtr") => true,
            ("Byte", "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64" or "IntPtr" or "UIntPtr") => true,
            ("Int16", "Int32" or "Int64" or "IntPtr") => true,
            ("UInt16", "Int32" or "UInt32" or "Int64" or "UInt64" or "IntPtr" or "UIntPtr") => true,
            ("Char", "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64" or "IntPtr" or "UIntPtr") => true,
            ("Int32", "Int64" or "IntPtr") => true,
            ("UInt32", "Int64" or "UInt64" or "UIntPtr") => true,
            ("IntPtr", "Int64") => true,
            ("UIntPtr", "UInt64") => true,
            _ => false,
        };

    /// <summary>
    /// True when the C# checked conversion can throw for some value. Synthesized
    /// reinterpret casts in a lexical checked context need <c>unchecked(...)</c>
    /// exactly for these pairs.
    /// </summary>
    public static bool CheckedConversionCanThrow(TypeRef? source, TypeRef? target)
    {
        if (source is null || target is null)
            return true;
        if (source.Equals(target))
            return false;
        if (Width(source) is not { } sourceWidth || Width(target) is not { } targetWidth)
            return true;
        bool sourceUnsigned = IsUnsignedInteger(source);
        bool targetUnsigned = IsUnsignedInteger(target);
        if (sourceUnsigned == targetUnsigned)
            return targetWidth < sourceWidth;
        if (sourceUnsigned)
            return targetWidth <= sourceWidth;
        return true;
    }

    static bool ImplicitSameFamilyNumericConversion(string source, string target) => (source, target) switch
    {
        ("SByte", "Int16" or "Int32") => true,
        ("Byte", "Int16" or "UInt16" or "Int32" or "UInt32") => true,
        ("Int16", "Int32") => true,
        ("UInt16", "Int32" or "UInt32") => true,
        ("Char", "UInt16" or "Int32" or "UInt32") => true,
        ("Single", "Double") => true,
        _ => false,
    };

    static int? Width(TypeRef? type) => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
        ? type.Name switch
        {
            "SByte" or "Byte" => 1,
            "Int16" or "UInt16" or "Char" => 2,
            "Int32" or "UInt32" => 4,
            "Int64" or "UInt64" => 8,
            _ => null,
        }
        : null;

    static bool IsUnsignedInteger(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Byte" or "UInt16" or "Char" or "UInt32" or "UInt64" };
}
