namespace DotnetInspector.Metadata;

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

    private static string? MapBinaryOrUnary(string suffix) => suffix switch
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
}
