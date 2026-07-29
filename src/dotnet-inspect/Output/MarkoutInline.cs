using ILInspector.CSharp;

namespace DotnetInspector.Output;

internal static class MarkoutInline
{
    /// <summary>
    /// Wraps display text in an inline code span, containing it on the way.
    /// </summary>
    /// <remarks>
    /// Emitting <c>&lt;code&gt;</c> markup makes this a presentation sink by
    /// construction, never an identity path, so containing here is always safe
    /// and is a no-op on clean text.
    ///
    /// The property this enforces is narrow, and an earlier version of this
    /// comment overstated it: text routed through here is contained, but
    /// nothing makes a caller route text through here. A row that renders a
    /// value plainly is untouched by this, and several did --
    /// <c>LibraryViewShapeDerivedContainmentTests</c> found them by filling the
    /// model by reflection rather than by trusting an enumeration. Treat that
    /// gate, not this function, as the statement about a view's coverage
    /// (issue #3319).
    /// </remarks>
    public static string Code(string value)
        => $"<code>{EscapeXmlText(CSharpIdentifier.ContainRenderedText(value))}</code>";

    private static string EscapeXmlText(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
