namespace ILInspector.Decompiler.Pipeline;

/// <summary>The ECMA-335 evaluation-stack families.</summary>
public enum StackFamily { I4, I8, F, I, O }

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
