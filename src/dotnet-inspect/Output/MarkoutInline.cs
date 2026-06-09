namespace DotnetInspector.Output;

internal static class MarkoutInline
{
    public static string Code(string value) => $"<code>{EscapeXmlText(value)}</code>";

    private static string EscapeXmlText(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
