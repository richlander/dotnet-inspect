namespace CSharpText;

/// <summary>
/// Formats operator method names (op_*) as C# operator declarations.
/// </summary>
public static class OperatorNames
{
    public static bool IsConversionOperatorMethodName(string name)
        => name is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";

    public static bool IsOperatorMethod(
        string name,
        bool isSpecialName,
        int genericArity)
        => isSpecialName
            && genericArity == 0
            && IsOperatorMethodName(name);

    public static bool IsOperatorMethodName(string name) =>
        IsConversionOperatorMethodName(name)
        || name is
            "op_Addition"
            or "op_Subtraction"
            or "op_Multiply"
            or "op_Division"
            or "op_Modulus"
            or "op_UnaryPlus"
            or "op_UnaryNegation"
            or "op_Increment"
            or "op_Decrement"
            or "op_BitwiseAnd"
            or "op_BitwiseOr"
            or "op_ExclusiveOr"
            or "op_OnesComplement"
            or "op_LeftShift"
            or "op_RightShift"
            or "op_UnsignedRightShift"
            or "op_Equality"
            or "op_Inequality"
            or "op_LessThan"
            or "op_GreaterThan"
            or "op_LessThanOrEqual"
            or "op_GreaterThanOrEqual"
            or "op_True"
            or "op_False"
            or "op_LogicalNot"
            or "op_AdditionAssignment"
            or "op_SubtractionAssignment"
            or "op_MultiplicationAssignment"
            or "op_DivisionAssignment"
            or "op_ModulusAssignment"
            or "op_BitwiseAndAssignment"
            or "op_BitwiseOrAssignment"
            or "op_ExclusiveOrAssignment"
            or "op_LeftShiftAssignment"
            or "op_RightShiftAssignment"
            or "op_UnsignedRightShiftAssignment"
            or "op_IncrementAssignment"
            or "op_DecrementAssignment"
            or "op_Exponent"
            or "op_IntegerDivision"
            or "op_Concatenate"
            or "op_Like"
        || name.StartsWith("op_Checked", StringComparison.Ordinal)
            && (MapBinaryOrUnary(name["op_Checked".Length..]) is not null
                || MapCheckedAssignment(name["op_Checked".Length..]) is not null);

    public static bool IsAssignmentOperatorMethodName(string name)
    {
        if (name.StartsWith("op_Checked", StringComparison.Ordinal))
            return MapCheckedAssignment(name["op_Checked".Length..]) is not null;
        return name.StartsWith("op_", StringComparison.Ordinal)
            && MapAssignment(name["op_".Length..]) is not null;
    }

    public static bool IsCSharpInstanceAssignmentOperator(
        string methodName,
        bool isStatic,
        bool isPublic,
        string returnType,
        int parameterCount,
        bool hasRefOrOutParameter = false)
    {
        if (isStatic || !isPublic || returnType != "void" || hasRefOrOutParameter)
            return false;

        string? suffix = methodName.StartsWith("op_Checked", StringComparison.Ordinal)
            ? MapCheckedAssignment(methodName["op_Checked".Length..]) is null
                ? null
                : methodName["op_Checked".Length..]
            : methodName.StartsWith("op_", StringComparison.Ordinal)
                && MapAssignment(methodName["op_".Length..]) is not null
                    ? methodName["op_".Length..]
                    : null;
        if (suffix is null)
            return false;

        int expectedParameterCount = suffix is "IncrementAssignment" or "DecrementAssignment"
            ? 0
            : 1;
        return parameterCount == expectedParameterCount;
    }

    public static bool IsCSharpOperatorDeclaration(
        string methodName,
        bool isStatic,
        bool isPublic,
        string returnType,
        int parameterCount,
        bool hasRefOrOutParameter = false)
    {
        if (IsAssignmentOperatorMethodName(methodName))
        {
            return IsCSharpInstanceAssignmentOperator(
                methodName,
                isStatic,
                isPublic,
                returnType,
                parameterCount,
                hasRefOrOutParameter);
        }

        if (!isStatic
            || !isPublic
            || returnType is "void" or "System.Void"
            || hasRefOrOutParameter)
        {
            return false;
        }

        return CSharpOperatorParameterCount(methodName) == parameterCount;
    }

    public static string? MetadataNameFromSourceToken(
        string token,
        int parameterCount,
        bool isChecked)
    {
        string? suffix = token switch
        {
            "+" => parameterCount == 1 ? "UnaryPlus" : "Addition",
            "-" => parameterCount == 1 ? "UnaryNegation" : "Subtraction",
            "!" => "LogicalNot",
            "~" => "OnesComplement",
            "++" => parameterCount == 0 ? "IncrementAssignment" : "Increment",
            "--" => parameterCount == 0 ? "DecrementAssignment" : "Decrement",
            "true" => "True",
            "false" => "False",
            "*" => "Multiply",
            "/" => "Division",
            "%" => "Modulus",
            "&" => "BitwiseAnd",
            "|" => "BitwiseOr",
            "^" => "ExclusiveOr",
            "<<" => "LeftShift",
            ">>" => "RightShift",
            ">>>" => "UnsignedRightShift",
            "==" => "Equality",
            "!=" => "Inequality",
            "<" => "LessThan",
            ">" => "GreaterThan",
            "<=" => "LessThanOrEqual",
            ">=" => "GreaterThanOrEqual",
            "+=" => "AdditionAssignment",
            "-=" => "SubtractionAssignment",
            "*=" => "MultiplicationAssignment",
            "/=" => "DivisionAssignment",
            "%=" => "ModulusAssignment",
            "&=" => "BitwiseAndAssignment",
            "|=" => "BitwiseOrAssignment",
            "^=" => "ExclusiveOrAssignment",
            "<<=" => "LeftShiftAssignment",
            ">>=" => "RightShiftAssignment",
            ">>>=" => "UnsignedRightShiftAssignment",
            _ => null,
        };
        if (suffix is null
            || isChecked
                && MapBinaryOrUnary(suffix) is null
                && MapCheckedAssignment(suffix) is null)
        {
            return null;
        }

        return isChecked ? $"op_Checked{suffix}" : $"op_{suffix}";
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
        => CSharpIdentifierCore.ContainComposedName(FormatDisplayNameCore(name));

    static string FormatDisplayNameCore(string name)
    {
        if (!name.StartsWith("op_", StringComparison.Ordinal))
            return name;

        // Checked variants: op_CheckedAddition → checked operator +
        if (name.StartsWith("op_Checked", StringComparison.Ordinal))
        {
            var inner = name["op_Checked".Length..];
            var symbol = MapBinaryOrUnary(inner) ?? MapCheckedAssignment(inner);
            if (symbol is not null)
                return $"checked operator {symbol}";
        }
        if (MapAssignment(name["op_".Length..]) is { } assignmentSymbol)
            return $"operator {assignmentSymbol}";

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

    public static string? MapAssignment(string suffix) => suffix switch
    {
        "AdditionAssignment" => "+=",
        "SubtractionAssignment" => "-=",
        "MultiplicationAssignment" => "*=",
        "DivisionAssignment" => "/=",
        "ModulusAssignment" => "%=",
        "BitwiseAndAssignment" => "&=",
        "BitwiseOrAssignment" => "|=",
        "ExclusiveOrAssignment" => "^=",
        "LeftShiftAssignment" => "<<=",
        "RightShiftAssignment" => ">>=",
        "UnsignedRightShiftAssignment" => ">>>=",
        "IncrementAssignment" => "++",
        "DecrementAssignment" => "--",
        _ => null,
    };

    public static string? MapCheckedAssignment(string suffix) => suffix switch
    {
        "AdditionAssignment" => "+=",
        "SubtractionAssignment" => "-=",
        "MultiplicationAssignment" => "*=",
        "DivisionAssignment" => "/=",
        "IncrementAssignment" => "++",
        "DecrementAssignment" => "--",
        _ => null,
    };

    /// <summary>
    /// The unchecked operator method name paired with a checked operator method
    /// name — <c>op_CheckedAddition → op_Addition</c> or
    /// <c>op_CheckedExplicit → op_Explicit</c> — or null when
    /// <paramref name="methodName"/> is not a checked operator with a defined
    /// unchecked sibling. C# requires a checked operator's unchecked form to be
    /// declared, so consumers pair the two when composing operator surfaces.
    /// </summary>
    public static string? UncheckedOperator(string methodName)
    {
        if (methodName is "op_CheckedExplicit")
            return "op_Explicit";
        if (!methodName.StartsWith("op_Checked", StringComparison.Ordinal))
            return null;

        string inner = methodName["op_Checked".Length..];
        return MapBinaryOrUnary(inner) is null && MapCheckedAssignment(inner) is null
            ? null
            : $"op_{inner}";
    }

    /// <summary>
    /// The declaration C# requires alongside <paramref name="methodName"/>:
    /// equality/inequality's opposite or a checked operator's unchecked form.
    /// </summary>
    public static string? RequiredOperatorSibling(string methodName)
        => methodName switch
        {
            "op_Equality" => "op_Inequality",
            "op_Inequality" => "op_Equality",
            "op_LessThan" => "op_GreaterThan",
            "op_GreaterThan" => "op_LessThan",
            "op_LessThanOrEqual" => "op_GreaterThanOrEqual",
            "op_GreaterThanOrEqual" => "op_LessThanOrEqual",
            "op_True" => "op_False",
            "op_False" => "op_True",
            _ => UncheckedOperator(methodName),
        };

    /// <summary>
    /// The checked operator method name paired with an unchecked operator method
    /// name, or null when C# defines no checked sibling for that operator.
    /// </summary>
    public static string? CheckedOperator(string methodName)
    {
        if (methodName is "op_Explicit")
            return "op_CheckedExplicit";
        if (!methodName.StartsWith("op_", StringComparison.Ordinal)
            || methodName.StartsWith("op_Checked", StringComparison.Ordinal))
        {
            return null;
        }

        string inner = methodName["op_".Length..];
        return MapBinaryOrUnary(inner) is null && MapCheckedAssignment(inner) is null
            ? null
            : $"op_Checked{inner}";
    }

    static int? CSharpOperatorParameterCount(string methodName)
    {
        if (IsConversionOperatorMethodName(methodName))
            return 1;

        string suffix = methodName.StartsWith("op_Checked", StringComparison.Ordinal)
            ? methodName["op_Checked".Length..]
            : methodName.StartsWith("op_", StringComparison.Ordinal)
                ? methodName["op_".Length..]
                : "";

        return suffix switch
        {
            "UnaryPlus" or "UnaryNegation"
                or "Increment" or "Decrement"
                or "OnesComplement" or "True" or "False" or "LogicalNot" => 1,
            "Addition" or "Subtraction" or "Multiply" or "Division" or "Modulus"
                or "BitwiseAnd" or "BitwiseOr" or "ExclusiveOr"
                or "LeftShift" or "RightShift" or "UnsignedRightShift"
                or "Equality" or "Inequality"
                or "LessThan" or "GreaterThan"
                or "LessThanOrEqual" or "GreaterThanOrEqual" => 2,
            _ => null,
        };
    }
}
