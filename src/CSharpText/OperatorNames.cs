namespace CSharpText;

/// <summary>
/// Formats operator method names (op_*) as C# operator declarations.
/// </summary>
public static class OperatorNames
{
    /// <summary>
    /// True for an operator method name that overloads on return type — the
    /// conversion family. Identity producers append a return-type suffix for
    /// these so two conversions sharing a source parameter stay distinct.
    /// Wider than <see cref="IsCSharpConversionOperatorMethodName"/> because
    /// <c>op_CheckedImplicit</c> is a recognized conversion name that C# has no
    /// declaration syntax for.
    /// </summary>
    public static bool IsConversionOperatorMethodName(string name)
        => IsCSharpConversionOperatorMethodName(name) || name is "op_CheckedImplicit";

    /// <summary>
    /// True for the conversion operator names C# can spell as a declaration:
    /// <c>implicit operator</c>, <c>explicit operator</c>, and
    /// <c>explicit operator checked</c>.
    /// </summary>
    public static bool IsCSharpConversionOperatorMethodName(string name)
        => name is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";

    /// <summary>
    /// Metadata (CLI) operator classification: a <c>SpecialName</c>, nongeneric
    /// method whose name is in the CLI operator vocabulary. This is the
    /// vocabulary that API <c>Kind</c> and stable operator selectors use — it is
    /// deliberately wider than what C# can declare, because a CLI operator
    /// authored by another language is still an operator in metadata.
    /// </summary>
    public static bool IsMetadataOperatorMethod(
        string name,
        bool isSpecialName,
        int genericArity)
        => isSpecialName
            && genericArity == 0
            && IsMetadataOperatorMethodName(name);

    /// <summary>
    /// The complete CLI operator method-name vocabulary: ECMA-335 I.10.3
    /// (Table I.4 unary, Table I.5 binary, and the I.10.3.3 conversions), plus
    /// the names later languages added under the same convention — C#'s checked
    /// operators and C# 14 instance compound-assignment operators, and the
    /// Visual Basic arithmetic names. Membership is by exact name; a
    /// <c>op_</c> prefix alone is not an operator.
    /// </summary>
    public static bool IsMetadataOperatorMethodName(string name) =>
        IsConversionOperatorMethodName(name)
        // ECMA-335 I.10.3.1, Table I.4 — unary operators.
        || name is
            "op_Decrement"
            or "op_Increment"
            or "op_UnaryNegation"
            or "op_UnaryPlus"
            or "op_LogicalNot"
            or "op_True"
            or "op_False"
            or "op_AddressOf"
            or "op_OnesComplement"
            or "op_PointerDereference"
        // ECMA-335 I.10.3.2, Table I.5 — binary operators.
        || name is
            "op_Addition"
            or "op_Subtraction"
            or "op_Multiply"
            or "op_Division"
            or "op_Modulus"
            or "op_ExclusiveOr"
            or "op_BitwiseAnd"
            or "op_BitwiseOr"
            or "op_LogicalAnd"
            or "op_LogicalOr"
            or "op_Assign"
            or "op_LeftShift"
            or "op_RightShift"
            or "op_SignedRightShift"
            or "op_UnsignedRightShift"
            or "op_Equality"
            or "op_GreaterThan"
            or "op_LessThan"
            or "op_Inequality"
            or "op_GreaterThanOrEqual"
            or "op_LessThanOrEqual"
            or "op_UnsignedRightShiftAssignment"
            or "op_MemberSelection"
            or "op_RightShiftAssignment"
            or "op_MultiplicationAssignment"
            or "op_PointerToMemberSelection"
            or "op_SubtractionAssignment"
            or "op_ExclusiveOrAssignment"
            or "op_LeftShiftAssignment"
            or "op_ModulusAssignment"
            or "op_AdditionAssignment"
            or "op_BitwiseAndAssignment"
            or "op_BitwiseOrAssignment"
            or "op_Comma"
            or "op_DivisionAssignment"
        // C# 14 instance compound assignment beyond the ECMA table.
        || name is "op_IncrementAssignment" or "op_DecrementAssignment"
        // Visual Basic arithmetic names sharing the convention.
        || name is "op_Exponent" or "op_IntegerDivision" or "op_Concatenate" or "op_Like"
        // C# checked operators (op_CheckedAddition, op_CheckedAdditionAssignment, ...).
        || name.StartsWith("op_Checked", StringComparison.Ordinal)
            && (MapBinaryOrUnary(name["op_Checked".Length..]) is not null
                || MapCheckedAssignment(name["op_Checked".Length..]) is not null);

    /// <summary>
    /// True when C# has declaration syntax for <paramref name="name"/>. This is
    /// the source-representability vocabulary, a strict subset of
    /// <see cref="IsMetadataOperatorMethodName"/>: the CLI names C# cannot
    /// declare (<c>op_AddressOf</c>, <c>op_LogicalAnd</c>, <c>op_Assign</c>,
    /// <c>op_Comma</c>, <c>op_CheckedImplicit</c>, …) are excluded. Name
    /// membership alone does not make a method representable — see
    /// <see cref="IsCSharpOperatorDeclaration"/> for the shape proof.
    /// </summary>
    public static bool IsCSharpOperatorMethodName(string name)
    {
        if (IsCSharpConversionOperatorMethodName(name))
            return true;
        if (name.StartsWith("op_Checked", StringComparison.Ordinal))
        {
            string checkedSuffix = name["op_Checked".Length..];
            return MapBinaryOrUnary(checkedSuffix) is not null
                || MapCheckedAssignment(checkedSuffix) is not null;
        }
        if (!name.StartsWith("op_", StringComparison.Ordinal))
            return false;

        string suffix = name["op_".Length..];
        return MapAssignment(suffix) is not null || CSharpUnaryOrBinaryArity(suffix) is not null;
    }

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

    /// <summary>
    /// True when a method's full shape is representable as a C# operator
    /// declaration: a name C# can spell, the required accessibility and
    /// static-ness, a legal parameter shape, a legal return shape, and
    /// declaring-type participation. Every caller supplies the shape from its
    /// own typed model; this method owns only the rule.
    /// </summary>
    /// <param name="declaringTypeParticipates">
    /// Whether the declaring type appears where C# requires it — see
    /// <see cref="DeclaringTypeParticipates"/>. Defaults to <see langword="true"/>
    /// so a caller that cannot recover the structural fact keeps the shape-only
    /// answer rather than silently rejecting a real operator.
    /// </param>
    public static bool IsCSharpOperatorDeclaration(
        string methodName,
        bool isStatic,
        bool isPublic,
        string returnType,
        int parameterCount,
        bool hasRefOrOutParameter = false,
        bool declaringTypeParticipates = true,
        bool allowsNonBooleanResult = false)
    {
        if (!IsCSharpOperatorMethodName(methodName))
            return false;

        if (IsAssignmentOperatorMethodName(methodName))
        {
            // The receiver of an instance compound-assignment operator is the
            // declaring type by construction, so participation is not a separate
            // obligation there.
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
            || hasRefOrOutParameter
            || !declaringTypeParticipates
            || RequiresBooleanReturn(methodName)
                && !allowsNonBooleanResult
                && returnType is not ("bool" or "System.Boolean"))
        {
            return false;
        }

        return CSharpOperatorParameterCount(methodName) == parameterCount;
    }

    static bool RequiresBooleanReturn(string methodName)
        => methodName is
            "op_True"
            or "op_False"
            or "op_Equality"
            or "op_Inequality"
            or "op_LessThan"
            or "op_GreaterThan"
            or "op_LessThanOrEqual"
            or "op_GreaterThanOrEqual";

    /// <summary>
    /// The C# declaring-type participation rule: a unary or binary operator
    /// needs the declaring type among its parameters (CS0562/CS0563), a
    /// conversion operator needs it as either the source or the target
    /// (CS0556), and an instance compound-assignment operator has it as the
    /// receiver. A caller decides type identity in its own model — including
    /// <c>T?</c> and the declaring type's own instantiation — and reports only
    /// the two positional answers here.
    /// </summary>
    public static bool DeclaringTypeParticipates(
        string methodName,
        bool anyParameterIsDeclaringType,
        bool returnTypeIsDeclaringType)
        => IsAssignmentOperatorMethodName(methodName)
            || (IsConversionOperatorMethodName(methodName)
                ? anyParameterIsDeclaringType || returnTypeIsDeclaringType
                : anyParameterIsDeclaringType);

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
        if (IsCSharpConversionOperatorMethodName(methodName))
            return 1;

        string suffix = methodName.StartsWith("op_Checked", StringComparison.Ordinal)
            ? methodName["op_Checked".Length..]
            : methodName.StartsWith("op_", StringComparison.Ordinal)
                ? methodName["op_".Length..]
                : "";

        return CSharpUnaryOrBinaryArity(suffix);
    }

    static int? CSharpUnaryOrBinaryArity(string suffix)
        => suffix switch
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
