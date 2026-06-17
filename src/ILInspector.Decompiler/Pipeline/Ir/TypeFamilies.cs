namespace ILInspector.Decompiler.Pipeline;

/// <summary>The ECMA-335 evaluation-stack families.</summary>
public enum StackFamily { I4, I8, F, I, O }

/// <summary>
/// A type's C#-relevant shape, knowable only by resolving the definition (its
/// base type): the distinction <see cref="TypeFamilies.Of"/> cannot make for a
/// bare definition. <see cref="Unknown"/> covers the unresolved cases — a
/// cross-assembly type with no loaded definition, or anything not a definition.
/// </summary>
public enum TypeShape { Unknown, Reference, ValueType, Enum }

/// <summary>
/// The single home for type-family classification (review consolidation:
/// this knowledge previously lived in the importer's slot merging, the
/// structuring pass's float detection, and three printer spellings). When
/// <c>IsValueType</c> knowledge arrives via resolution, it lands here too.
/// </summary>
public static class TypeFamilies
{
    /// <summary>The stack family, where it is knowable without resolution; null when it is not (a bare definition may be struct, enum, or class).</summary>
    public static StackFamily? Of(TypeRef? type)
    {
        if (type is null)
            return null;
        if (type.Kind is TypeRefKind.SzArray or TypeRefKind.Array)
            return StackFamily.O;
        if (type.Kind != TypeRefKind.Definition || type.Assembly != TypeRef.CoreLibrary || type.Namespace != "System")
            return null;
        return type.Name switch
        {
            "Boolean" or "Char" or "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32" => StackFamily.I4,
            "Int64" or "UInt64" => StackFamily.I8,
            "Single" or "Double" => StackFamily.F,
            "IntPtr" or "UIntPtr" => StackFamily.I,
            "Object" or "String" => StackFamily.O,
            _ => null,
        };
    }

    /// <summary>
    /// The result type of a binary numeric op, per ECMA-335 III.1.5: the wider
    /// operand family wins (F over all, I8 over I4/I, I over I4). Returns the
    /// wider operand's EXACT type, not a canonical one, so signedness and width
    /// survive (<c>uint + uint</c> stays <c>uint</c>); a same-family pair or an
    /// unknown operand keeps the left type, the prior behavior. Only genuine
    /// cross-family pairs (<c>int + long</c>) change — the left-operand shortcut
    /// was wrong for exactly those.
    /// </summary>
    public static TypeRef? BinaryResult(TypeRef? left, TypeRef? right)
    {
        var leftFamily = Of(left);
        var rightFamily = Of(right);
        if (leftFamily is not { } lf || rightFamily is not { } rf || lf == rf)
            return left;
        return Wider(lf, rf) == rf ? right : left;
    }

    static StackFamily Wider(StackFamily a, StackFamily b) => (a, b) switch
    {
        (StackFamily.F, _) or (_, StackFamily.F) => StackFamily.F,
        (StackFamily.I8, _) or (_, StackFamily.I8) => StackFamily.I8,
        (StackFamily.I, _) or (_, StackFamily.I) => StackFamily.I,
        _ => a,
    };

    public static bool IsFloat(TypeRef? type) => Of(type) == StackFamily.F;

    /// <summary>True when the family is a known integer (I4/I8/I).</summary>
    public static bool IsInteger(TypeRef? type) => Of(type) is StackFamily.I4 or StackFamily.I8 or StackFamily.I;

    /// <summary>The canonical TypeRef for a family — what the evaluation stack physically carries at a join.</summary>
    public static TypeRef Canonical(StackFamily family) => family switch
    {
        StackFamily.I4 => TypeRef.CoreLib("System", "Int32"),
        StackFamily.I8 => TypeRef.CoreLib("System", "Int64"),
        StackFamily.F => TypeRef.CoreLib("System", "Double"),
        StackFamily.I => TypeRef.CoreLib("System", "IntPtr"),
        _ => TypeRef.CoreLib("System", "Object"),
    };

    /// <summary>
    /// True when a value of <paramref name="source"/> needs an explicit C# cast
    /// to flow into a <paramref name="target"/> position — the missing-cast case
    /// behind CS0266. Restricted to numeric primitives of the SAME stack family
    /// (int↔uint, ushort↔char, long↔ulong, nint↔nuint, int→byte, …): there the
    /// cast is a pure reinterpretation of bits the evaluation stack already
    /// carries, so it is faithful to the IL, never a value change. Cross-family
    /// narrowing (long→int) is excluded — that always carries an explicit IL
    /// <c>conv</c> already, so it never reaches here as a bare mismatch. Boolean
    /// is excluded: it shares the I4 family but `(int)b` is not even legal C#.
    /// </summary>
    public static bool NeedsNumericCast(TypeRef? source, TypeRef? target)
    {
        if (source is null || target is null || source.Equals(target))
            return false;
        if (IsBoolean(source) || IsBoolean(target) || !IsNumericPrimitive(source) || !IsNumericPrimitive(target))
            return false;
        var family = Of(source);
        if (family is null || family != Of(target))
            return false;   // same stack family only — guarantees a faithful reinterpretation
        return !IsImplicitlyConvertible(source.Name, target.Name);
    }

    static bool IsBoolean(TypeRef type)
        => type is { Name: "Boolean", Assembly: TypeRef.CoreLibrary, Namespace: "System" };

    public static bool IsNumericPrimitive(TypeRef type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            && type.Name is "SByte" or "Byte" or "Int16" or "UInt16" or "Int32" or "UInt32"
                or "Int64" or "UInt64" or "IntPtr" or "UIntPtr" or "Char" or "Single" or "Double";

    /// <summary>Byte width of a fixed-width integer primitive; null for the platform-sized (nint/nuint) and float types.</summary>
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

    /// <summary>True when two fixed-width integer primitives occupy the same byte width (ushort/char, int/uint) — a same-width cast subsumes any inner conversion to the sibling.</summary>
    public static bool SameWidth(TypeRef? a, TypeRef? b) => Width(a) is { } wa && wa == Width(b);

    /// <summary>
    /// True when an integer constant is in <paramref name="target"/>'s range, so
    /// C# converts it implicitly (a constant-expression conversion) and no cast
    /// is needed. A value outside the range — a negative into unsigned, a bitmask
    /// wider than the target — does not convert bare and needs an unchecked cast.
    /// </summary>
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

    /// <summary>C#'s implicit numeric conversions within a stack family — the widenings that need no cast.</summary>
    static bool IsImplicitlyConvertible(string source, string target) => (source, target) switch
    {
        ("SByte", "Int16" or "Int32") => true,
        ("Byte", "Int16" or "UInt16" or "Int32" or "UInt32") => true,
        ("Int16", "Int32") => true,
        ("UInt16", "Int32" or "UInt32") => true,
        ("Char", "UInt16" or "Int32" or "UInt32") => true,
        ("Single", "Double") => true,
        _ => false,
    };

    /// <summary>The unsigned-counterpart TypeRef of a signed integer type; null when already unsigned, float, or unknown.</summary>
    public static TypeRef? UnsignedCounterpart(TypeRef? type) => UnsignedCastKeyword(type) switch
    {
        "uint" => TypeRef.CoreLib("System", "UInt32"),
        "ulong" => TypeRef.CoreLib("System", "UInt64"),
        "nuint" => TypeRef.CoreLib("System", "UIntPtr"),
        _ => null,
    };

    /// <summary>
    /// The C# cast keyword that reinterprets a signed-integer type as its
    /// unsigned counterpart; null when the type is already unsigned, float
    /// (.un means unordered there), or unknown.
    /// </summary>
    public static string? UnsignedCastKeyword(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System" }
            ? type.Name switch
            {
                "SByte" or "Int16" or "Int32" => "uint",
                "Int64" => "ulong",
                "IntPtr" => "nuint",
                _ => null,
            }
            : null;
}

/// <summary>
/// The single home for condition negation — the type-aware IL duals (the
/// old pipeline's NaN lesson): integer comparisons invert their kind; float
/// comparisons invert kind AND flip the unordered flag (NOT(a &lt; b) is
/// a &gt;= b unordered, never plain a &gt;= b); unknown operand types wrap
/// in LogicalNot honestly; double negation unwraps.
/// </summary>
public static class Conditions
{
    public static ComparisonKind Inverse(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equal => ComparisonKind.NotEqual,
        ComparisonKind.NotEqual => ComparisonKind.Equal,
        ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
        ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
        _ => ComparisonKind.LessThan,
    };

    /// <summary>Negates a DETACHED condition, producing a detached result ready for adoption.</summary>
    public static IrExpression Negate(IrExpression condition) => condition switch
    {
        LogicalNot not => (IrExpression)not.DetachChildren()[0],
        Comparison comparison => InvertComparison(comparison),
        _ => new LogicalNot(condition),
    };

    static IrExpression InvertComparison(Comparison comparison)
    {
        bool? isFloat = OperandFloatness(comparison);
        if (isFloat is null && comparison.Kind is not (ComparisonKind.Equal or ComparisonKind.NotEqual))
            return new LogicalNot(comparison);  // ordering duals need known operand types

        var operands = comparison.DetachChildren();
        bool isUnsigned = isFloat == true ? !comparison.IsUnsigned : comparison.IsUnsigned;
        return new Comparison(Inverse(comparison.Kind), isUnsigned, (IrExpression)operands[0], (IrExpression)operands[1]);
    }

    /// <summary>True = float operands, false = known integer, null = unknown.</summary>
    static bool? OperandFloatness(Comparison comparison)
    {
        bool? Of(TypeRef? type) => TypeFamilies.Of(type) switch
        {
            StackFamily.F => true,
            StackFamily.I4 or StackFamily.I8 or StackFamily.I => false,
            _ => null,
        };
        return Of(comparison.Left.ResultType) ?? Of(comparison.Right.ResultType);
    }
}
