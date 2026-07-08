namespace ILInspector.Metadata;

/// <summary>
/// Formats operator method names (op_*) as C# operator declarations.
/// </summary>
public static class OperatorNames
{
    /// <summary>
    /// Converts an IL operator method name to its C# display form.
    /// Non-operator names are returned unchanged.
    /// </summary>
    public static string FormatDisplayName(string name)
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
