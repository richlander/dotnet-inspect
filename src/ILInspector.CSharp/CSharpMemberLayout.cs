using System.Text;

namespace ILInspector.CSharp;

/// <summary>
/// Lays out one decompiled member or accessor from a declaration head plus
/// already-rendered body content: it owns the block-vs-expression-bodied
/// decision and the brace/indent envelope, so the decompiler supplies body
/// content and CSharp owns the member layout.
/// </summary>
/// <remarks>
/// This is intentionally <em>not</em> unified with
/// <see cref="CSharpTypePrinter"/>: that composer is block-only and drops blank
/// lines, whereas decompiled bodies are expression-capable and preserve blank
/// lines. The two encode different policies on purpose.
/// </remarks>
public static class CSharpMemberLayout
{
    /// <summary>
    /// Appends <paramref name="head"/> at <paramref name="indent"/> spaces laid
    /// out as: <c>head;</c> when <paramref name="body"/> is <see langword="null"/>;
    /// <c>head =&gt; expr;</c> when the body is a single legal expression
    /// statement (per <see cref="CSharpExpressionBody.FromSingleStatement"/>);
    /// or, when <paramref name="wrapExpressionBodyArrow"/> is
    /// <see langword="true"/>, <c>head</c> on one line and
    /// <c>=&gt; expr;</c> on the next indented one level (four spaces) deeper.
    /// Otherwise a brace block with the body content one level (four spaces)
    /// deeper. Blank lines in the body are preserved.
    /// </summary>
    public static void Append(StringBuilder sb, string head, string? body, int indent, bool wrapExpressionBodyArrow = false)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(head);

        string pad = new(' ', indent);
        if (body is null)
        {
            sb.AppendLine($"{pad}{head};");
            return;
        }
        if (CSharpExpressionBody.FromSingleStatement(body) is { } expression)
        {
            if (wrapExpressionBodyArrow)
            {
                sb.AppendLine($"{pad}{head}");
                sb.AppendLine($"{pad}    => {expression};");
            }
            else
            {
                sb.AppendLine($"{pad}{head} => {expression};");
            }
            return;
        }
        sb.AppendLine($"{pad}{head}");
        sb.AppendLine($"{pad}{{");
        AppendIndentedBody(sb, body, indent + 4);
        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// Appends <paramref name="body"/> content at <paramref name="indent"/>
    /// spaces, trimming trailing whitespace and preserving blank lines (an empty
    /// line stays an empty line). This blank-line-preserving policy is the
    /// deliberate difference from <see cref="CSharpTypePrinter"/>, which drops
    /// empty lines.
    /// </summary>
    public static void AppendIndentedBody(StringBuilder sb, string body, int indent)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(body);

        string pad = new(' ', indent);
        foreach (var line in body.Split('\n'))
        {
            string trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
                sb.AppendLine();
            else
                sb.AppendLine($"{pad}{trimmed}");
        }
    }
}
