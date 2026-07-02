namespace ILInspector.Metadata;

/// <summary>
/// Metadata-owned API identity helpers for durable member selectors. These
/// helpers compose identity strings from queryable metadata facts, not from C#
/// declaration text.
/// </summary>
public static class ApiMemberIdentity
{
    public sealed record XmlDocMemberIdentity(string LookupKey, IReadOnlyList<string> NormalizedParameters);

    public static bool TryGetCanonicalSignature(ApiType type, ApiMember member, out string canonicalSignature)
    {
        var declaringType = string.IsNullOrWhiteSpace(member.DeclaringType)
            ? MetadataTypeNameFormatter.FormatFullName(type)
            : member.DeclaringType!;

        var kindCode = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.Kind is "property" or "field" or "event")
        {
            canonicalSignature = $"{kindCode}:{declaringType}.{member.Name}";
            return true;
        }

        if (member.SignatureModel is not { } signature)
        {
            canonicalSignature = "";
            return false;
        }

        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : LegacyCanonicalMemberName(member.Signature, member.Name)
              ?? (string.IsNullOrWhiteSpace(signature.MemberName)
                  ? member.Name
                  : signature.MemberName!);
        memberName = NormalizeCanonicalCommas(memberName);
        canonicalSignature = $"{kindCode}:{declaringType}.{memberName}{NormalizeCanonicalParameters(signature.ParameterTypesSummary)}";
        return true;
    }

    // Preserve the v1 Member Index digest contract for members that already have
    // compatibility signature text. The legacy parser had edge-case behavior
    // around method names inside generic parameter names, and published stable
    // selectors hash that exact canonical string.
    static string? LegacyCanonicalMemberName(string? signature, string memberName)
    {
        if (string.IsNullOrEmpty(signature))
            return null;

        var parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return null;

        var nameIndex = signature.LastIndexOf(memberName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return null;

        var end = nameIndex + memberName.Length;
        if (end < parenStart && signature[end] == '<')
        {
            var depth = 0;
            for (var i = end; i < parenStart; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                        return signature[nameIndex..(i + 1)];
                }
            }
        }

        return memberName;
    }

    static string NormalizeCanonicalParameters(string parameterTypesSummary)
        => string.IsNullOrEmpty(parameterTypesSummary)
            ? "()"
            : NormalizeCanonicalCommas(parameterTypesSummary);

    static string NormalizeCanonicalCommas(string value)
        => value.Replace(", ", ",", StringComparison.Ordinal).Trim();

    public static bool TryGetXmlDocMemberIdentity(ApiType type, ApiMember member, out XmlDocMemberIdentity identity)
    {
        var typeXmlName = ToXmlDocName(type.FullName);
        var memberName = member.Name == ".ctor" ? "#ctor" : member.Name;
        var prefix = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        var lookupKey = $"{prefix}:{typeXmlName}.{memberName}";
        if (member.SignatureModel is not { } signature)
        {
            identity = new XmlDocMemberIdentity("", []);
            return false;
        }

        var typeParameterMap = type.TypeParameters
            .Select((p, i) => (p.Name, Index: i))
            .ToDictionary(p => p.Name, p => p.Index, StringComparer.Ordinal);
        var methodParameterMap = GetMethodGenericParameterMap(signature.MemberName);
        var parameters = signature.Parameters
            .Select(parameter => NormalizeXmlDocParameterType(parameter.TypeWithModifier, typeParameterMap, methodParameterMap))
            .ToList();
        identity = new XmlDocMemberIdentity(lookupKey, parameters);
        return true;
    }

    public static string NormalizeXmlDocParameterType(string parameter)
        => NormalizeXmlDocParameterType(parameter, EmptyParameterMap, EmptyParameterMap);

    internal static string NormalizeXmlDocSignatureParameter(
        string parameter,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap)
        => NormalizeXmlDocParameterType(ExtractSignatureParameterType(parameter), typeParameterMap, methodParameterMap);

    static readonly IReadOnlyDictionary<string, int> EmptyParameterMap =
        new Dictionary<string, int>(StringComparer.Ordinal);

    internal static string NormalizeXmlDocParameterType(
        string parameter,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap)
    {
        var type = parameter.Trim();
        foreach (var prefix in (string[])["ref ", "out ", "in ", "params ", "this "])
        {
            if (type.StartsWith(prefix, StringComparison.Ordinal))
            {
                type = type[prefix.Length..].TrimStart();
                break;
            }
        }

        type = type.TrimEnd('@');
        type = type.Replace("?", "", StringComparison.Ordinal);

        if (TryNormalizeGenericParameterReference(type, typeParameterMap, methodParameterMap, out var genericParameter))
            return genericParameter;

        if (type.EndsWith("[]", StringComparison.Ordinal))
            return $"{NormalizeXmlDocParameterType(type[..^2], typeParameterMap, methodParameterMap)}[]";

        var genericStart = IndexOfAny(type, '<', '{');
        if (genericStart >= 0 && TryGetGenericParts(type, genericStart, out var genericType, out var genericArgs))
        {
            var normalizedType = PrimitiveTypeNames.ToClrFullName(genericType);
            var normalizedArgs = SplitParameters(genericArgs)
                .Select(p => NormalizeXmlDocParameterType(p, typeParameterMap, methodParameterMap));
            return $"{normalizedType}{{{string.Join(",", normalizedArgs)}}}";
        }

        return PrimitiveTypeNames.ToClrFullName(type);
    }

    static string ExtractSignatureParameterType(string parameter)
    {
        var eqIndex = parameter.IndexOf('=');
        if (eqIndex >= 0)
            parameter = parameter[..eqIndex].Trim();

        var depth = 0;
        var lastSpace = -1;
        for (var i = 0; i < parameter.Length; i++)
        {
            var c = parameter[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ' ' && depth == 0)
                lastSpace = i;
        }

        return lastSpace > 0 ? parameter[..lastSpace] : parameter;
    }

    static Dictionary<string, int> GetMethodGenericParameterMap(string? memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        var genericStart = memberName.IndexOf('<');
        if (genericStart < 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        if (!TryGetGenericParts(memberName, genericStart, out _, out var parameters))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        return SplitParameters(parameters)
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .Where(p => p.Name.Length > 0)
            .ToDictionary(p => p.Name, p => p.Index, StringComparer.Ordinal);
    }

    static IEnumerable<string> SplitParameters(string parameters)
    {
        var depth = 0;
        var lastSplit = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = parameters[i];
            if (c is '<' or '{' or '(')
                depth++;
            else if (c is '>' or '}' or ')')
                depth--;
            else if (c == ',' && depth == 0)
            {
                yield return parameters[lastSplit..i].Trim();
                lastSplit = i + 1;
            }
        }

        yield return parameters[lastSplit..].Trim();
    }

    static int IndexOfAny(string value, char first, char second)
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

    static bool TryGetGenericParts(string type, int genericStart, out string genericType, out string genericArgs)
    {
        genericType = type[..genericStart];
        genericArgs = "";

        var open = type[genericStart];
        var close = open == '<' ? '>' : '}';
        var depth = 0;
        for (var i = genericStart; i < type.Length; i++)
        {
            var c = type[i];
            if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    genericArgs = type[(genericStart + 1)..i];
                    return true;
                }
            }
        }

        return false;
    }

    static bool TryNormalizeGenericParameterReference(
        string type,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap,
        out string normalized)
    {
        normalized = "";
        if (type.StartsWith("``", StringComparison.Ordinal) && int.TryParse(type[2..], out var methodIndex))
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

    static string ToXmlDocName(string typeName)
        => typeName.Replace('+', '.');
}
