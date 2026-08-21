namespace CSharpText;

/// <summary>
/// Parses fully-qualified names with optional namespace, type, member, and overload notation.
/// Handles patterns like: Type, Type.Member, Type&lt;T&gt;.Member:N, Namespace.Type.Member, etc.
/// </summary>
public static class FqnParser
{
    private const int MaxNestedGenericDepth = 64;

    /// <summary>
    /// Result of parsing an FQN string.
    /// </summary>
    public record ParseResult(
        string? QualifiedPrefix,    // Namespace or package prefix (e.g., "System.Collections.Generic")
        string TypeName,             // Type name (e.g., "List`1", "JsonSerializer")
        string? MemberName,          // Member name if present (e.g., "IndexOf", "Deserialize")
        int? OverloadIndex);         // Overload index if present (e.g., 3 from "IndexOf:3")

    /// <summary>
    /// Parses a fully-qualified name into its components.
    /// </summary>
    /// <param name="input">The FQN string to parse (e.g., "List&lt;T&gt;.IndexOf:3", "System.Text.Json.JsonSerializer.Deserialize")</param>
    /// <returns>Parsed components, or null if the input doesn't match any known pattern.</returns>
    /// <remarks>
    /// This parser focuses on structural parsing only. It identifies Type.Member patterns
    /// and extracts overload indices, but does NOT attempt to split namespaces from type names
    /// for standalone type references (that's handled by type resolution logic elsewhere).
    /// </remarks>
    public static ParseResult? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // Extract overload index suffix (e.g., ":3" from "IndexOf:3") from the raw input,
        // before any normalization runs.
        var (work, overloadIndex) = TrySplitOverload(input.Trim());

        // Determine the member-split point using the user's own dots. Normalization can
        // introduce dots (e.g. primitive aliases: "string" → "System.String"), so it must
        // not influence whether the input has a Type.Member shape. Only top-level dots
        // (outside generic angle brackets) are candidate separators.
        var lastDot = LastTopLevelDot(work);
        if (lastDot <= 0)
        {
            // No member separator - the whole thing is a (possibly primitive/generic) type name.
            return new ParseResult(null, NormalizeTypeName(work), null, overloadIndex);
        }

        var rightSegment = work[(lastDot + 1)..];
        var leftSegment = work[..lastDot];

        // A member name is a simple identifier: no dots, no backticks/generics, starts with uppercase.
        bool rightLooksLikeMember = !rightSegment.Contains('`') &&
                                    !rightSegment.Contains('.') &&
                                    !rightSegment.Contains('<') &&
                                    !string.IsNullOrWhiteSpace(rightSegment) &&
                                    char.IsUpper(rightSegment[0]);

        if (!rightLooksLikeMember)
        {
            // NOT a Type.Member pattern - this is just a (possibly qualified) type name.
            // Don't try to split namespace from type; return the whole thing as TypeName
            // and let the type resolution logic elsewhere handle namespace splitting.
            return new ParseResult(null, NormalizeTypeName(work), null, overloadIndex);
        }

        // This is Type.Member or Namespace.Type.Member. Look for another top-level dot to the
        // left to separate an optional namespace/package prefix from the type name.
        string? qualifiedPrefix = null;
        string typePart = leftSegment;
        var secondLastDot = LastTopLevelDot(leftSegment);
        if (secondLastDot > 0)
        {
            qualifiedPrefix = leftSegment[..secondLastDot];
            typePart = leftSegment[(secondLastDot + 1)..];
        }

        return new ParseResult(qualifiedPrefix, NormalizeTypeName(typePart), rightSegment, overloadIndex);
    }

    /// <summary>
    /// Returns the last dot outside a generic argument list, or -1 when none exists.
    /// </summary>
    public static int LastTopLevelDot(string value)
    {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var c = value[i];
            if (c == '>')
                depth++;
            else if (c == '<')
                depth--;
            else if (c == '.' && depth == 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Splits a trailing <c>:N</c> overload selector from a value, without normalizing the head.
    /// This is the single structural primitive for overload notation used across the parsers.
    /// </summary>
    /// <param name="value">A member or FQN string, possibly ending in <c>:N</c>.</param>
    /// <returns>The value without the suffix, and the 1-based overload index if present.</returns>
    internal static (string Head, int? Index) TrySplitOverload(string value)
    {
        var colonIdx = value.LastIndexOf(':');
        if (colonIdx > 0 && colonIdx < value.Length - 1
            && int.TryParse(value[(colonIdx + 1)..], out var idx) && idx > 0)
            return (value[..colonIdx], idx);
        return (value, null);
    }

    /// <summary>
    /// Normalizes a type selector by converting C#-style generic arguments to
    /// CLR backtick notation.
    /// </summary>
    public static string NormalizeTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        var trimmed = typeName.Trim();
        if (typeName.Equals(trimmed, StringComparison.Ordinal)
            && PrimitiveTypeNames.TryToClrFullName(typeName, out var primitiveFullName))
            return primitiveFullName;

        var angleIdx = typeName.IndexOf('<');
        if (angleIdx <= 0)
            return typeName;

        var normalized = new System.Text.StringBuilder(typeName.Length);
        var segmentStart = 0;
        while (angleIdx > 0)
        {
            var closeIdx = FindMatchingAngleBracket(typeName, angleIdx);
            if (closeIdx < 0)
                return typeName;
            if (!TryCountTypeParameters(
                    typeName.AsSpan((angleIdx + 1)..closeIdx),
                    out var arity))
            {
                return typeName;
            }

            normalized.Append(typeName, segmentStart, angleIdx - segmentStart);
            normalized.Append('`');
            normalized.Append(arity);
            segmentStart = closeIdx + 1;
            angleIdx = typeName.IndexOf('<', segmentStart);
        }

        normalized.Append(typeName, segmentStart, typeName.Length - segmentStart);
        return normalized.ToString();
    }

    private static int FindMatchingAngleBracket(string value, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < value.Length; i++)
        {
            if (value[i] == '<')
                depth++;
            else if (value[i] == '>' && --depth == 0)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Normalizes a member selector to metadata member notation.
    /// </summary>
    public static string NormalizeMemberName(string memberName)
    {
        memberName = memberName.Trim();
        var angleIdx = FindTerminalGenericArgumentListStart(memberName);
        if (angleIdx > 0 && GetMemberGenericArity(memberName).HasValue)
        {
            memberName = memberName[..angleIdx];
        }
        else
        {
            var backtickIdx = memberName.LastIndexOf('`');
            if (backtickIdx > 0 && GetMemberGenericArity(memberName).HasValue)
                memberName = memberName[..backtickIdx];
        }

        return NormalizeOperatorOrSpecialMemberName(memberName);
    }

    /// <summary>
    /// Returns the arity of a well-formed generic member selector.
    /// </summary>
    public static int? GetMemberGenericArity(string memberName)
    {
        memberName = memberName.Trim();
        var angleIdx = FindTerminalGenericArgumentListStart(memberName);
        if (angleIdx > 0)
        {
            if (memberName.AsSpan(..angleIdx).TrimEnd().EndsWith(
                    ">",
                    StringComparison.Ordinal))
            {
                return null;
            }

            var closeIdx = FindMatchingAngleBracket(memberName, angleIdx);
            if (closeIdx != memberName.Length - 1
                || !TryCountTypeParameters(
                    memberName.AsSpan((angleIdx + 1)..closeIdx),
                    out var arity))
            {
                return null;
            }

            return arity;
        }

        var backtickIdx = memberName.LastIndexOf('`');
        if (backtickIdx > 0
            && backtickIdx < memberName.Length - 1
            && memberName[backtickIdx + 1] != '0'
            && memberName.AsSpan((backtickIdx + 1)..).IndexOfAnyExceptInRange('0', '9') < 0
            && int.TryParse(
                memberName.AsSpan((backtickIdx + 1)..),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var metadataArity))
        {
            return metadataArity;
        }

        return null;
    }

    private static int FindTerminalGenericArgumentListStart(
        ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value[^1] != '>')
            return -1;

        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            if (value[i] == '>')
            {
                depth++;
            }
            else if (value[i] == '<')
            {
                depth--;
                if (depth == 0)
                    return i;
                if (depth < 0)
                    return -1;
            }
        }

        return -1;
    }

    private static string NormalizeOperatorOrSpecialMemberName(string memberName)
    {
        if (memberName.Equals("ctor", StringComparison.OrdinalIgnoreCase)
            || memberName.Equals("constructor", StringComparison.OrdinalIgnoreCase))
            return ".ctor";

        if ((memberName.StartsWith("this[", StringComparison.OrdinalIgnoreCase)
                && memberName.EndsWith("]", StringComparison.Ordinal))
            || memberName.Equals("this", StringComparison.OrdinalIgnoreCase)
            || memberName.Equals("[]", StringComparison.OrdinalIgnoreCase))
            return "this[]";

        var requestedMemberName = memberName;
        var hasOperatorPrefix = false;
        if (memberName.StartsWith("operator", StringComparison.OrdinalIgnoreCase))
        {
            hasOperatorPrefix = true;
            memberName = memberName["operator".Length..].Trim();
        }
        else if (memberName.StartsWith("op_", StringComparison.OrdinalIgnoreCase))
            return memberName;

        var compact = memberName.Replace(" ", "", StringComparison.Ordinal);
        return compact.ToLowerInvariant() switch
        {
            "implicit" => "op_Implicit",
            "explicit" => "op_Explicit",
            "checkedimplicit" => "op_CheckedImplicit",
            "checkedexplicit" => "op_CheckedExplicit",
            "+" => "op_Addition",
            "checked+" => "op_CheckedAddition",
            "-" => "op_Subtraction",
            "checked-" => "op_CheckedSubtraction",
            "*" => "op_Multiply",
            "checked*" => "op_CheckedMultiply",
            "/" => "op_Division",
            "%" => "op_Modulus",
            "++" => "op_Increment",
            "checked++" => "op_CheckedIncrement",
            "--" => "op_Decrement",
            "checked--" => "op_CheckedDecrement",
            "==" => "op_Equality",
            "!=" => "op_Inequality",
            "<" => "op_LessThan",
            ">" => "op_GreaterThan",
            "<=" => "op_LessThanOrEqual",
            ">=" => "op_GreaterThanOrEqual",
            "&" => "op_BitwiseAnd",
            "|" => "op_BitwiseOr",
            "^" => "op_ExclusiveOr",
            "~" => "op_OnesComplement",
            "!" => "op_LogicalNot",
            "<<" => "op_LeftShift",
            ">>" => "op_RightShift",
            ">>>" => "op_UnsignedRightShift",
            "true" => "op_True",
            "false" => "op_False",
            _ => hasOperatorPrefix ? requestedMemberName : memberName
        };
    }

    internal static int CountTypeParameters(ReadOnlySpan<char> typeParams)
        => TryCountTypeParameters(typeParams, out var count) ? count : 0;

    private static bool TryCountTypeParameters(
        ReadOnlySpan<char> typeParams,
        out int count,
        bool nestingValidated = false)
    {
        count = 0;
        if (typeParams.IsEmpty || typeParams.IsWhiteSpace())
        {
            if (nestingValidated)
                return false;

            count = 1;
            return true;
        }
        if (!nestingValidated
            && !HasSupportedGenericNesting(typeParams))
        {
            return false;
        }

        var unboundArity = 1;
        var isUnboundGeneric = false;
        foreach (var c in typeParams)
        {
            if (c == ',')
            {
                unboundArity++;
                isUnboundGeneric = true;
            }
            else if (!char.IsWhiteSpace(c))
            {
                isUnboundGeneric = false;
                break;
            }
        }
        if (isUnboundGeneric)
        {
            if (nestingValidated)
                return false;

            count = unboundArity;
            return true;
        }

        count = 1;
        var segmentStart = 0;
        var currentPartHasCore = false;
        var coreSeparatedByWhitespace = false;
        var coreCompleted = false;
        var hasPostfix = false;
        var nullableApplied = false;
        var pointerApplied = false;
        var byRefApplied = false;
        var arrayRankDepth = 0;
        for (var i = 0; i < typeParams.Length; i++)
        {
            if (arrayRankDepth > 0)
            {
                if (typeParams[i] == ']')
                {
                    arrayRankDepth--;
                    coreCompleted = true;
                    hasPostfix = true;
                    nullableApplied = false;
                    pointerApplied = false;
                }
                else if (typeParams[i] != ','
                    && !char.IsWhiteSpace(typeParams[i]))
                {
                    count = 0;
                    return false;
                }

                continue;
            }

            switch (typeParams[i])
            {
                case '<':
                    if (!currentPartHasCore
                        || coreCompleted
                        || typeParams[segmentStart..i].IsWhiteSpace())
                    {
                        count = 0;
                        return false;
                    }

                    var close = FindMatchingAngleBracket(typeParams, i);
                    if (close < 0
                        || !TryCountTypeParameters(
                            typeParams[(i + 1)..close],
                            out _,
                            nestingValidated: true))
                    {
                        count = 0;
                        return false;
                    }

                    i = close;
                    coreCompleted = true;
                    hasPostfix = false;
                    break;
                case '>':
                    count = 0;
                    return false;
                case ',':
                    if (!currentPartHasCore
                        || typeParams[segmentStart..i].IsWhiteSpace())
                    {
                        count = 0;
                        return false;
                    }

                    count++;
                    segmentStart = i + 1;
                    currentPartHasCore = false;
                    coreSeparatedByWhitespace = false;
                    coreCompleted = false;
                    hasPostfix = false;
                    nullableApplied = false;
                    pointerApplied = false;
                    byRefApplied = false;
                    break;
                case '[':
                    if (!currentPartHasCore
                        || byRefApplied
                        || typeParams[segmentStart..i].IsWhiteSpace())
                    {
                        count = 0;
                        return false;
                    }

                    arrayRankDepth++;
                    break;
                case ']':
                    count = 0;
                    return false;
                case '?':
                    if (!currentPartHasCore
                        || nullableApplied
                        || pointerApplied
                        || byRefApplied)
                    {
                        count = 0;
                        return false;
                    }

                    coreCompleted = true;
                    hasPostfix = true;
                    nullableApplied = true;
                    break;
                case '*':
                    if (!currentPartHasCore
                        || nullableApplied
                        || byRefApplied)
                    {
                        count = 0;
                        return false;
                    }

                    coreCompleted = true;
                    hasPostfix = true;
                    pointerApplied = true;
                    break;
                case '&':
                    if (!currentPartHasCore || byRefApplied)
                    {
                        count = 0;
                        return false;
                    }

                    coreCompleted = true;
                    hasPostfix = true;
                    byRefApplied = true;
                    break;
                case '.':
                case '+':
                    if (!currentPartHasCore
                        || hasPostfix
                        || byRefApplied)
                    {
                        count = 0;
                        return false;
                    }

                    currentPartHasCore = false;
                    coreSeparatedByWhitespace = false;
                    coreCompleted = false;
                    nullableApplied = false;
                    pointerApplied = false;
                    break;
                case ':':
                    if (!currentPartHasCore
                        || hasPostfix
                        || byRefApplied
                        || i + 1 >= typeParams.Length
                        || typeParams[i + 1] != ':')
                    {
                        count = 0;
                        return false;
                    }

                    i++;
                    currentPartHasCore = false;
                    coreSeparatedByWhitespace = false;
                    coreCompleted = false;
                    nullableApplied = false;
                    pointerApplied = false;
                    break;
                case '(':
                case ')':
                    count = 0;
                    return false;
                default:
                    if (char.IsWhiteSpace(typeParams[i]))
                    {
                        if (currentPartHasCore && !coreCompleted)
                            coreSeparatedByWhitespace = true;
                        break;
                    }
                    if (coreCompleted || coreSeparatedByWhitespace)
                    {
                        count = 0;
                        return false;
                    }
                    currentPartHasCore = true;
                    break;
            }
        }

        if (arrayRankDepth != 0
            || !currentPartHasCore
            || typeParams[segmentStart..].IsWhiteSpace())
        {
            count = 0;
            return false;
        }

        return true;
    }

    private static bool HasSupportedGenericNesting(
        ReadOnlySpan<char> value)
    {
        var depth = 0;
        foreach (var c in value)
        {
            if (c == '<')
            {
                depth++;
                if (depth > MaxNestedGenericDepth)
                    return false;
            }
            else if (c == '>')
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth == 0;
    }

    private static int FindMatchingAngleBracket(
        ReadOnlySpan<char> value,
        int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < value.Length; i++)
        {
            if (value[i] == '<')
                depth++;
            else if (value[i] == '>' && --depth == 0)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Parses a member filter value, extracting the overload index if present.
    /// </summary>
    /// <param name="value">Member filter (e.g., "IndexOf:3", "Deserialize")</param>
    /// <returns>Normalized member name and optional overload index.</returns>
    public static (string Name, int? Index) ParseMemberFilter(string value)
    {
        var (head, index) = TrySplitOverload(value);
        return (NormalizeMemberName(head), index);
    }
}
