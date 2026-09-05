namespace CSharpText;

/// <summary>
/// Formats operator method names (op_*) as C# operator declarations.
/// </summary>
public static class OperatorNames
{
    private static readonly HashSet<string> KnownMetadataNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "op_Implicit",
            "op_Explicit",
            "op_CheckedImplicit",
            "op_CheckedExplicit",
            "op_Addition",
            "op_Subtraction",
            "op_Multiply",
            "op_Division",
            "op_Modulus",
            "op_UnaryPlus",
            "op_UnaryNegation",
            "op_Increment",
            "op_Decrement",
            "op_BitwiseAnd",
            "op_BitwiseOr",
            "op_ExclusiveOr",
            "op_OnesComplement",
            "op_LeftShift",
            "op_RightShift",
            "op_UnsignedRightShift",
            "op_Equality",
            "op_Inequality",
            "op_LessThan",
            "op_GreaterThan",
            "op_LessThanOrEqual",
            "op_GreaterThanOrEqual",
            "op_True",
            "op_False",
            "op_LogicalNot",
        };

    private static readonly HashSet<string> CheckedOperatorSuffixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Addition",
            "Subtraction",
            "Multiply",
            "Division",
            "Increment",
            "Decrement",
            "UnaryNegation",
        };

    public static bool IsMetadataOperatorName(string name)
    {
        if (KnownMetadataNames.Contains(name))
            return true;

        const string checkedPrefix = "op_Checked";
        return name.StartsWith(
                checkedPrefix,
                StringComparison.OrdinalIgnoreCase)
            && CheckedOperatorSuffixes.Contains(
                name[checkedPrefix.Length..]);
    }

    /// <summary>
    /// Converts an IL operator method name to its C# display form.
    /// Non-operator names are returned unchanged.
    /// </summary>
    /// <remarks>
    /// The name is untrusted metadata and every caller of this method renders the
    /// result — into a Markdown table cell, a tree node, or a heading — so the
    /// result is contained here rather than at each call site. Containment is a
    /// no-op for any name a compiler can emit, which is what keeps output
    /// byte-identical (issue #3319).
    /// </remarks>
    public static string FormatDisplayName(string name)
        => CSharpIdentifierCore.ContainComposedName(
            FormatDisplayNameUntreated(name));

    /// <summary>
    /// Converts an IL operator method name to its untreated C# display form.
    /// Presentation boundaries that carry <c>InertString</c> use this overload
    /// so the typed value retains the original text concerns.
    /// </summary>
    public static string FormatDisplayNameUntreated(string name)
    {
        if (!name.StartsWith("op_", StringComparison.Ordinal))
            return name;

        // Checked variants: op_CheckedAddition → checked operator +
        if (name.StartsWith("op_Checked", StringComparison.Ordinal))
        {
            var inner = name["op_Checked".Length..];
            var symbol = MapBinaryOrUnary(inner);
            if (symbol is not null)
                return $"checked operator {symbol}";
        }

        return name switch
        {
            // Conversion operators
            "op_Implicit" => "implicit operator",
            "op_Explicit" => "explicit operator",
            "op_CheckedExplicit" => "checked explicit operator",

            // Arithmetic
            "op_Addition" => "operator +",
            "op_Subtraction" => "operator -",
            "op_Multiply" => "operator *",
            "op_Division" => "operator /",
            "op_Modulus" => "operator %",
            "op_UnaryPlus" => "operator +",
            "op_UnaryNegation" => "operator -",
            "op_Increment" => "operator ++",
            "op_Decrement" => "operator --",

            // Bitwise
            "op_BitwiseAnd" => "operator &",
            "op_BitwiseOr" => "operator |",
            "op_ExclusiveOr" => "operator ^",
            "op_OnesComplement" => "operator ~",
            "op_LeftShift" => "operator <<",
            "op_RightShift" => "operator >>",
            "op_UnsignedRightShift" => "operator >>>",

            // Comparison
            "op_Equality" => "operator ==",
            "op_Inequality" => "operator !=",
            "op_LessThan" => "operator <",
            "op_GreaterThan" => "operator >",
            "op_LessThanOrEqual" => "operator <=",
            "op_GreaterThanOrEqual" => "operator >=",

            // Logical
            "op_True" => "operator true",
            "op_False" => "operator false",
            "op_LogicalNot" => "operator !",

            _ => name
        };
    }

    /// <summary>
    /// Maps a binary/unary operator suffix (without any "op_"/"Checked" prefix, e.g. "Addition")
    /// to its C# symbol ("+"), or null if the suffix is not a binary/unary operator. Shared with the
    /// signature renderer so checked-operator symbols are defined in exactly one place.
    /// </summary>
    public static string? MapBinaryOrUnary(string suffix) => suffix switch
    {
        "Addition" => "+",
        "Subtraction" => "-",
        "Multiply" => "*",
        "Division" => "/",
        "Increment" => "++",
        "Decrement" => "--",
        "UnaryNegation" => "-",
        _ => null
    };

    /// <summary>
    /// The unchecked operator method name paired with a checked operator method
    /// name — <c>op_CheckedAddition → op_Addition</c>, <c>op_CheckedExplicit →
    /// op_Explicit</c>, <c>op_CheckedImplicit → op_Implicit</c> — or null when
    /// <paramref name="methodName"/> is not a checked operator with a defined
    /// unchecked sibling. C# requires a checked operator's unchecked form to be
    /// declared, so consumers pair the two when composing operator surfaces.
    /// </summary>
    public static string? UncheckedOperator(string methodName)
    {
        if (methodName is "op_CheckedExplicit")
            return "op_Explicit";
        if (methodName is "op_CheckedImplicit")
            return "op_Implicit";
        if (!methodName.StartsWith("op_Checked", StringComparison.Ordinal))
            return null;

        string inner = methodName["op_Checked".Length..];
        return MapBinaryOrUnary(inner) is null ? null : $"op_{inner}";
    }
}
