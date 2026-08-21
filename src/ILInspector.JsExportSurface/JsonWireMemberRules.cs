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
            "property" => member.Accessibility is null
                || member.HasJsonInclude,
            "field" => member.HasJsonInclude,
            _ => false,
        };
    }
}
