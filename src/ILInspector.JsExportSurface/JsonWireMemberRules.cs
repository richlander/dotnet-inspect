using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

public static class JsonWireMemberRules
{
    public static bool IsSerialized(ApiMember member)
    {
        if (member.IsStatic
            || member.IsCompilerGenerated
            || member.HasJsonIgnore)
        {
            return false;
        }

        return member.Kind switch
        {
            "property" => IsSerializedProperty(member),
            "field" => member.HasJsonInclude,
            _ => false,
        };
    }

    static bool IsSerializedProperty(ApiMember member)
    {
        if (member.HasGetter is false)
            return false;

        if (member.HasJsonInclude)
            return true;

        return member.HasGetter is true
            ? member.GetterAccessibility is null
            : member.Accessibility is null;
    }
}
