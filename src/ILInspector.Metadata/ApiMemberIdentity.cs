namespace ILInspector.Metadata;

/// <summary>
/// Metadata-owned API identity helpers for durable member selectors. These
/// helpers compose identity strings from queryable metadata facts, not from C#
/// declaration text.
/// </summary>
public static class ApiMemberIdentity
{
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
            : string.IsNullOrWhiteSpace(signature.MemberName)
                ? member.Name
                : signature.MemberName!;
        canonicalSignature = $"{kindCode}:{declaringType}.{memberName}{NormalizeCanonicalParameters(signature.ParameterTypesSummary)}";
        return true;
    }

    static string NormalizeCanonicalParameters(string parameterTypesSummary)
        => string.IsNullOrEmpty(parameterTypesSummary)
            ? "()"
            : parameterTypesSummary.Replace(", ", ",", StringComparison.Ordinal).Trim();
}
