using ILInspector.CSharp;

namespace DotnetInspector.Output;

internal static class MarkoutInline
{
    /// <summary>
    /// Wraps display text in an inline code span. The value usually carries
    /// untrusted metadata names, so it is contained here rather than at each
    /// call site: emitting <c>&lt;code&gt;</c> markup makes this a presentation
    /// sink by construction, never an identity path, so a new caller cannot
    /// reopen issue #3319. Containment is a no-op on clean text.
    /// </summary>
    public static string Code(string value)
        => $"<code>{EscapeXmlText(CSharpIdentifier.ContainRenderedText(value))}</code>";

    private static string EscapeXmlText(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
