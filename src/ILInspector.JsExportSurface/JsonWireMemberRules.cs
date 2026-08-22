using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

public static class JsonWireMemberRules
{
    public static bool IsSerialized(ApiMember member)
    {
        if (member.IsStatic
            || member.IsCompilerGenerated
            || member.HasJsonIgnore && !member.HasJsonIgnoreNever)
        {
            return false;
        }

        return member.Kind switch
        {
            "property" => IsSerializedProperty(member),
            "field" => member.HasJsonInclude
                && IsSourceGeneratorAccessible(member.Accessibility),
            _ => false,
        };
    }

    static bool IsSerializedProperty(ApiMember member)
    {
        if (member.HasGetter is false)
            return false;

        if (member.HasJsonInclude)
        {
            string? getterAccessibility = member.HasGetter is true
                ? member.GetterAccessibility
                : member.Accessibility;
            return IsSourceGeneratorAccessible(getterAccessibility);
        }

        return member.HasGetter is true
            ? member.GetterAccessibility is null
            : member.Accessibility is null;
    }

    static bool IsSourceGeneratorAccessible(string? accessibility) =>
        accessibility is null or "internal" or "protected internal";
}
