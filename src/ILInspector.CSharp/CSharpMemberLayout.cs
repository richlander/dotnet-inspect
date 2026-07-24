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
    /// <param name="bodyIsSingleReturnExpression">
    /// When <see langword="true"/> the caller has proven, from the typed
    /// <c>DecompilerResult.BodyIsSingleReturnExpression</c> signal, that
    /// <paramref name="body"/> is exactly one <c>return &lt;expr&gt;;</c>
    /// statement whose expression spans several lines (a raised switch
    /// expression). Such a body renders expression-bodied — <c>head =&gt; &lt;value&gt;
    /// switch { … };</c> — with the switch block re-indented under the member
    /// (issue #3088). Only ever set for a multi-line body; single-line bodies
    /// stay on the <see cref="CSharpExpressionBody.FromSingleStatement"/> path.
    /// </param>
    public static void Append(StringBuilder sb, string head, string? body, int indent, bool wrapExpressionBodyArrow = false, bool bodyIsSingleReturnExpression = false)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(head);

        string pad = new(' ', indent);
        if (body is null)
        {
            sb.AppendLine($"{pad}{head};");
            return;
        }
        if (bodyIsSingleReturnExpression
            && CSharpExpressionBody.MultilineReturnExpressionLines(body) is { } expressionLines)
        {
            AppendMultilineExpressionBody(sb, head, expressionLines, indent, wrapExpressionBodyArrow);
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
    /// Renders a multi-line single-<c>return</c> expression body (a raised switch
    /// expression). The value/arrow line sits after the arrow — on
    /// <paramref name="head"/>'s line, or, when
    /// <paramref name="wrapExpressionBodyArrow"/> is set, one level deeper on its
    /// own line — and the block lines re-indent so the switch <c>{</c> aligns with
    /// the token that opens the expression: the member indent for the same-line
    /// arrow, or the arrow's indent for the wrapped arrow. The final line gains the
    /// statement terminator (<c>};</c>). Blank lines are preserved.
    /// </summary>
    static void AppendMultilineExpressionBody(
        StringBuilder sb, string head, IReadOnlyList<string> expressionLines, int indent, bool wrapExpressionBodyArrow)
    {
        string pad = new(' ', indent);
        string valueLine = expressionLines[0];
        if (wrapExpressionBodyArrow)
        {
            sb.AppendLine($"{pad}{head}");
            sb.AppendLine($"{pad}    => {valueLine}");
        }
        else
        {
            sb.AppendLine($"{pad}{head} => {valueLine}");
        }

        string continuationPad = new(' ', wrapExpressionBodyArrow ? indent + 4 : indent);
        for (int i = 1; i < expressionLines.Count; i++)
        {
            string line = expressionLines[i];
            bool last = i == expressionLines.Count - 1;
            if (line.Length == 0)
            {
                sb.AppendLine();
                continue;
            }
            sb.Append(continuationPad).Append(line);
            if (last)
                sb.Append(';');
            sb.AppendLine();
        }
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
