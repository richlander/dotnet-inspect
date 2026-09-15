using System.Text;

namespace CSharpText;

/// <summary>
/// The text grammar used by XML-documentation member identifiers.
/// </summary>
public static class XmlDocumentationNotation
{
    private static readonly IReadOnlyDictionary<string, int> EmptyParameterMap =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private static readonly HashSet<string> KnownNullableValueTypes = new(StringComparer.Ordinal)
    {
        "System.DateTime",
        "System.DateTimeOffset",
        "System.Decimal",
        "System.Guid",
        "System.TimeSpan",
        "System.IntPtr",
        "System.UIntPtr"
    };

    /// <summary>
    /// Builds an exact compiler XML-documentation identity from owner-issued
    /// definition-name segments and already-projected parameter types.
    /// </summary>
    public static XmlDocMemberIdentity CreateMemberIdentity(
        string kind,
        string typeNamespace,
        IReadOnlyList<string> typeNameSegments,
        string memberName,
        IReadOnlyList<string> parameterTypeIds,
        int methodGenericArity = 0,
        string? conversionReturnType = null,
        bool isVararg = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(typeNamespace);
        ArgumentNullException.ThrowIfNull(typeNameSegments);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ArgumentNullException.ThrowIfNull(parameterTypeIds);
        ArgumentOutOfRangeException.ThrowIfNegative(methodGenericArity);

        string typeName = FormatDefinitionName(typeNamespace, typeNameSegments);
        var builder = new StringBuilder(
            kind.Length + typeName.Length + memberName.Length + 8);
        builder.Append(kind);
        builder.Append(':');
        builder.Append(typeName);
        builder.Append('.');
        builder.Append(NormalizeMemberName(memberName));
        if (methodGenericArity > 0)
        {
            builder.Append("``");
            builder.Append(methodGenericArity);
        }
        if (parameterTypeIds.Count > 0 || isVararg)
        {
            builder.Append('(');
            builder.AppendJoin(',', parameterTypeIds);
            if (isVararg && parameterTypeIds.Count > 0)
                builder.Append(',');
            builder.Append(')');
        }
        if (!string.IsNullOrWhiteSpace(conversionReturnType))
        {
            builder.Append('~');
            builder.Append(conversionReturnType);
        }
        return new XmlDocMemberIdentity(builder.ToString());
    }

    /// <summary>
    /// Builds an exact compiler XML-documentation identity for one type.
    /// </summary>
    public static XmlDocMemberIdentity CreateTypeIdentity(
        string typeNamespace,
        IReadOnlyList<string> typeNameSegments)
        => new($"T:{FormatDefinitionName(typeNamespace, typeNameSegments)}");

    /// <summary>
    /// Formats a structured metadata definition name for compiler XML documentation.
    /// </summary>
    public static string FormatDefinitionName(
        string typeNamespace,
        IReadOnlyList<string> typeNameSegments)
    {
        ArgumentNullException.ThrowIfNull(typeNamespace);
        ArgumentNullException.ThrowIfNull(typeNameSegments);
        if (typeNameSegments.Count == 0)
            throw new ArgumentException("At least one type-name segment is required.", nameof(typeNameSegments));

        string typeName = string.Join(
            '.',
            typeNameSegments.Select(FormatNameSegment));
        return typeNamespace.Length == 0
            ? typeName
            : $"{typeNamespace}.{typeName}";
    }

    /// <summary>
    /// Escapes one metadata name segment without interpreting nested-type boundaries.
    /// </summary>
    public static string FormatNameSegment(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        return segment.Replace('.', '#');
    }

    /// <summary>Normalizes a CLR nested-type name for XML-documentation identity.</summary>
    public static string NormalizeTypeName(string typeName)
        => typeName.Replace('+', '.');

    /// <summary>Normalizes a member name for XML-documentation identity.</summary>
    public static string NormalizeMemberName(string memberName)
        => memberName
            .Replace('.', '#')
            .Replace('<', '{')
            .Replace('>', '}');

    /// <summary>Normalizes one XML-documentation parameter type.</summary>
    public static string NormalizeParameterType(string parameter)
        => NormalizeParameterType(parameter, EmptyParameterMap, EmptyParameterMap);

    /// <summary>
    /// Removes a parameter name and default before normalizing its XML-documentation type.
    /// </summary>
    public static string NormalizeSignatureParameter(
        string parameter,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap)
        => NormalizeParameterType(
            ExtractSignatureParameterType(parameter),
            typeParameterMap,
            methodParameterMap);

    /// <summary>
    /// Normalizes one XML-documentation parameter type with its generic-parameter positions.
    /// </summary>
    public static string NormalizeParameterType(
        string parameter,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap)
    {
        var type = StripLeadingAttributes(NormalizeDynamicToObject(parameter).Trim());
        var isByRef = false;
        foreach (var prefix in (string[])["ref ", "out ", "in ", "params ", "this "])
        {
            if (type.StartsWith(prefix, StringComparison.Ordinal))
            {
                isByRef = prefix is "ref " or "out " or "in ";
                type = type[prefix.Length..].TrimStart();
                break;
            }
        }

        if (type.EndsWith('@'))
        {
            isByRef = true;
            type = type.TrimEnd('@');
        }

        var nullableValueType = false;
        if (type.EndsWith("?", StringComparison.Ordinal))
        {
            var unwrapped = type[..^1];
            if (PrimitiveTypeNames.TryToClrFullName(unwrapped, out var primitive)
                && primitive is not ("System.String" or "System.Object" or "System.Void"))
            {
                type = $"System.Nullable<{primitive}>";
                nullableValueType = true;
            }
            else if (KnownNullableValueTypes.Contains(unwrapped))
            {
                type = $"System.Nullable<{unwrapped}>";
                nullableValueType = true;
            }
            else
            {
                type = unwrapped;
            }
        }

        string normalized;
        if (!nullableValueType
            && TryNormalizeGenericParameterReference(
                type,
                typeParameterMap,
                methodParameterMap,
                out var genericParameter))
        {
            normalized = genericParameter;
        }
        else if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            normalized = $"{NormalizeParameterType(type[..^2], typeParameterMap, methodParameterMap)}[]";
        }
        else if (type.EndsWith("*", StringComparison.Ordinal))
        {
            normalized = $"{NormalizeParameterType(type[..^1], typeParameterMap, methodParameterMap)}*";
        }
        else if (TryGetArraySuffix(type, out var arrayElementType, out var arraySuffix))
        {
            normalized = $"{NormalizeParameterType(arrayElementType, typeParameterMap, methodParameterMap)}{arraySuffix}";
        }
        else
        {
            var genericStart = IndexOfAny(type, '<', '{');
            if (genericStart >= 0
                && TryGetGenericParts(
                    type,
                    genericStart,
                    out var genericType,
                    out var genericArgs,
                    out var remainder))
            {
                var normalizedType = PrimitiveTypeNames.ToClrFullName(genericType);
                var normalizedArgs = SplitParameters(genericArgs)
                    .Select(argument => NormalizeParameterType(argument, typeParameterMap, methodParameterMap));
                normalized = $"{normalizedType}{{{string.Join(",", normalizedArgs)}}}";
                if (remainder.StartsWith('.') || remainder.StartsWith('+'))
                {
                    normalized += "." + NormalizeParameterType(
                        remainder[1..],
                        typeParameterMap,
                        methodParameterMap);
                }
            }
            else
            {
                normalized = PrimitiveTypeNames.ToClrFullName(type);
            }
        }

        return isByRef ? $"{normalized}@" : normalized;
    }

    /// <summary>
    /// Collapses the display keyword <c>dynamic</c> back to <c>object</c> for identity
    /// and matching. <c>dynamic</c> and <c>object</c> are the same metadata type, so the
    /// display-only distinction must never reach canonical signatures, correspondence
    /// keys, or XML-documentation identity, which encodes dynamic positions as
    /// <c>System.Object</c>.
    /// <para>
    /// This runs in string space by necessity: a serialized display signature may be the
    /// only available input. It is boundary-aware, so it never rewrites a dotted name
    /// segment or a longer identifier such as <c>System.Dynamic.X</c>, <c>Ns.dynamic</c>,
    /// or <c>MyDynamicType</c>. The boundary behavior is gated by
    /// <c>XmlDocumentationNotationTests.NormalizeDynamicToObject_KeywordOnly_SparesRealTypeNames</c>.
    /// </para>
    /// <para>
    /// Known limitation: an identifier literally spelled <c>dynamic</c> in a position where
    /// the keyword is legal — a type in the global namespace or a generic parameter, both
    /// authored as <c>@dynamic</c> — renders as a bare token that this string pass collapses
    /// to <c>object</c>. Preserving fingerprint stability for the ubiquitous keyword case is
    /// the deliberate existing tradeoff.
    /// </para>
    /// </summary>
    public static string NormalizeDynamicToObject(string value)
    {
        const string token = "dynamic";
        if (value.IndexOf(token, StringComparison.Ordinal) < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            if (index + token.Length <= value.Length
                && string.CompareOrdinal(value, index, token, 0, token.Length) == 0
                && (index == 0 || !IsTypeNameChar(value[index - 1]))
                && (index + token.Length >= value.Length || !IsTypeNameChar(value[index + token.Length])))
            {
                builder.Append("object");
                index += token.Length;
            }
            else
            {
                builder.Append(value[index]);
                index++;
            }
        }

        return builder.ToString();

        static bool IsTypeNameChar(char c) =>
            char.IsLetterOrDigit(c) || c is '_' or '.' or '`' or '+' or '/';
    }

    /// <summary>Removes a parameter name, default, and leading attributes from signature text.</summary>
    public static string ExtractSignatureParameterType(string parameter)
    {
        parameter = StripLeadingAttributes(parameter.TrimStart());
        var equalsIndex = parameter.IndexOf('=');
        if (equalsIndex >= 0)
            parameter = parameter[..equalsIndex].Trim();

        var depth = 0;
        var lastSpace = -1;
        for (var i = 0; i < parameter.Length; i++)
        {
            var character = parameter[i];
            if (character == '<')
                depth++;
            else if (character == '>')
                depth--;
            else if (character == ' ' && depth == 0)
                lastSpace = i;
        }

        return lastSpace > 0 ? parameter[..lastSpace] : parameter;
    }

    private static string StripLeadingAttributes(string parameter)
    {
        while (parameter.StartsWith('['))
        {
            var depth = 0;
            var end = -1;
            for (var i = 0; i < parameter.Length; i++)
            {
                if (parameter[i] == '[')
                    depth++;
                else if (parameter[i] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (end < 0)
                return parameter;

            parameter = parameter[(end + 1)..].TrimStart();
        }

        return parameter;
    }

    private static IEnumerable<string> SplitParameters(string parameters)
    {
        var depth = 0;
        var lastSplit = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var character = parameters[i];
            if (character is '<' or '{' or '(')
                depth++;
            else if (character is '>' or '}' or ')')
                depth--;
            else if (character == ',' && depth == 0)
            {
                yield return parameters[lastSplit..i].Trim();
                lastSplit = i + 1;
            }
        }

        yield return parameters[lastSplit..].Trim();
    }

    private static int IndexOfAny(string value, char first, char second)
    {
        var firstIndex = value.IndexOf(first);
        var secondIndex = value.IndexOf(second);
        return (firstIndex, secondIndex) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => secondIndex,
            (_, < 0) => firstIndex,
            _ => Math.Min(firstIndex, secondIndex)
        };
    }

    private static bool TryGetGenericParts(
        string type,
        int genericStart,
        out string genericType,
        out string genericArguments,
        out string remainder)
    {
        genericType = type[..genericStart];
        genericArguments = "";
        remainder = "";

        var open = type[genericStart];
        var close = open == '<' ? '>' : '}';
        var depth = 0;
        for (var i = genericStart; i < type.Length; i++)
        {
            var character = type[i];
            if (character == open)
                depth++;
            else if (character == close)
            {
                depth--;
                if (depth == 0)
                {
                    genericArguments = type[(genericStart + 1)..i];
                    remainder = type[(i + 1)..];
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryNormalizeGenericParameterReference(
        string type,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap,
        out string normalized)
    {
        normalized = "";
        if (type.StartsWith("``", StringComparison.Ordinal)
            && int.TryParse(type[2..], out var methodIndex))
        {
            normalized = $"M{methodIndex}";
            return true;
        }

        if (type.StartsWith('`') && int.TryParse(type[1..], out var typeIndex))
        {
            normalized = $"T{typeIndex}";
            return true;
        }

        if (methodParameterMap.TryGetValue(type, out methodIndex))
        {
            normalized = $"M{methodIndex}";
            return true;
        }

        if (typeParameterMap.TryGetValue(type, out typeIndex))
        {
            normalized = $"T{typeIndex}";
            return true;
        }

        return false;
    }

    private static bool TryGetArraySuffix(string type, out string elementType, out string suffix)
    {
        elementType = "";
        suffix = "";
        if (!type.EndsWith("]", StringComparison.Ordinal))
            return false;

        var open = type.LastIndexOf('[');
        if (open <= 0)
            return false;

        var rankSpec = type[(open + 1)..^1];
        if (rankSpec.Length == 0)
            return false;

        elementType = type[..open];
        var rank = rankSpec.Count(character => character == ',') + 1;
        suffix = $"[{new string(',', rank - 1)}]";
        return true;
    }
}

/// <summary>
/// An exact compiler XML-documentation ID for one type or member.
/// </summary>
public sealed record XmlDocMemberIdentity
{
    public XmlDocMemberIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
