using System.Text;

namespace CSharpText;

/// <summary>
/// Lays out one decompiled member or accessor from a declaration head plus
/// already-rendered body content: it owns the block-vs-expression-bodied
/// decision and the brace/indent envelope, so the decompiler supplies body
/// content and CSharp owns the member layout.
/// </summary>
/// <remarks>
/// This is intentionally <em>not</em> unified with
/// <c>CSharpTypePrinter</c>: that composer is block-only and drops blank
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
    /// <param name="bodyIsSingleExpressionBody">
    /// When <see langword="true"/> the caller has proven, from the typed
    /// <c>DecompilerResult.BodyIsSingleExpressionBody</c> signal, that
    /// <paramref name="body"/> is exactly one multi-line single-statement
    /// expression — a <c>return &lt;expr&gt;;</c> or a void <c>&lt;expr&gt;;</c>
    /// statement (a raised switch expression, a wrapped fluent chain, or any
    /// other wrapped single expression). Such a body renders expression-bodied —
    /// <c>head =&gt; &lt;value&gt;</c> with the continuation lines re-indented under
    /// the member (issues #3088 and #3084). Only ever set for a multi-line body;
    /// single-line bodies stay on the
    /// <see cref="CSharpExpressionBody.FromSingleStatement"/> path.
    /// </param>
    /// <param name="disableSignatureWrapping">
    /// When <see langword="true"/>, suppresses the layout's one-line wrapping
    /// decisions. The public parameter name is retained for source
    /// compatibility with existing callers using the named argument.
    /// </param>
    public static void Append(StringBuilder sb, string head, string? body, int indent, bool wrapExpressionBodyArrow = false, bool bodyIsSingleExpressionBody = false, bool disableSignatureWrapping = false)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(head);

        bool disableOneLinerWrapping = disableSignatureWrapping;
        string pad = new(' ', indent);
        if (body is null)
        {
            sb.Append(LayOutHead(pad, head, ";", ";", disableOneLinerWrapping)).Append('\n');
            return;
        }
        if (bodyIsSingleExpressionBody
            && CSharpExpressionBody.MultilineExpressionBodyLines(body) is { } expressionLines)
        {
            AppendMultilineExpressionBody(sb, head, expressionLines, indent, wrapExpressionBodyArrow, disableOneLinerWrapping);
            return;
        }
        if (CSharpExpressionBody.FromSingleStatement(body) is { } expression)
        {
            if (wrapExpressionBodyArrow)
            {
                sb.Append(LayOutHead(pad, head, "", " =>", disableOneLinerWrapping)).Append('\n');
                sb.Append($"{pad}    => {expression};").Append('\n');
            }
            else
            {
                sb.Append(LayOutHead(pad, head, $" => {expression};", " =>", disableOneLinerWrapping)).Append('\n');
            }
            return;
        }
        sb.Append(LayOutHead(pad, head, "", " {", disableOneLinerWrapping)).Append('\n');
        sb.Append($"{pad}{{").Append('\n');
        AppendIndentedBody(sb, body, indent + 4);
        sb.Append($"{pad}}}").Append('\n');
    }

    /// <summary>
    /// Applies this layout's generic-constraint wrapping to a declaration head
    /// without adding a body, terminator, or indentation. A head without
    /// constraints remains unchanged, including an over-width signature. A
    /// line comment disables splitting so commented text cannot become live;
    /// <c>LayOutDeclarationHead_WhereInsideLineComment_DoesNotBecomeLiveConstraint</c>
    /// gates that token-preservation boundary.
    /// </summary>
    public static string LayOutDeclarationHead(string head, bool disableSignatureWrapping = false)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (disableSignatureWrapping || ContainsLineComment(head))
            return head;

        var parts = SplitConstraintClauses(head);
        if (parts.Count == 1)
            return head;

        var sb = new StringBuilder(parts[0]);
        for (int i = 1; i < parts.Count; i++)
            sb.Append('\n').Append("    ").Append(parts[i]);
        return sb.ToString();
    }

    /// <summary>
    /// Renders a multi-line single-<c>return</c> expression body (a raised switch
    /// expression, a wrapped fluent chain, or any other wrapped single
    /// expression). The value/arrow line sits after the arrow — on
    /// <paramref name="head"/>'s line, or, when
    /// <paramref name="wrapExpressionBodyArrow"/> is set, one level deeper on its
    /// own line — and the continuation lines re-indent so that whatever opens the
    /// expression (a switch <c>{</c>, a chained <c>.Method(…)</c>) aligns with the
    /// token after the arrow: the member indent for the same-line arrow, or the
    /// arrow's indent for the wrapped arrow. The final line gains the statement
    /// terminator (<c>;</c>). Blank lines are preserved.
    /// </summary>
    static void AppendMultilineExpressionBody(
        StringBuilder sb, string head, IReadOnlyList<string> expressionLines, int indent, bool wrapExpressionBodyArrow, bool disableOneLinerWrapping)
    {
        string pad = new(' ', indent);
        string valueLine = expressionLines[0];
        if (wrapExpressionBodyArrow)
        {
            sb.Append(LayOutHead(pad, head, "", " =>", disableOneLinerWrapping)).Append('\n');
            sb.Append($"{pad}    => {valueLine}").Append('\n');
        }
        else
        {
            sb.Append(LayOutHead(pad, head, $" => {valueLine}", " =>", disableOneLinerWrapping)).Append('\n');
        }

        string continuationPad = new(' ', wrapExpressionBodyArrow ? indent + 4 : indent);
        for (int i = 1; i < expressionLines.Count; i++)
        {
            string line = expressionLines[i];
            bool last = i == expressionLines.Count - 1;
            if (line.Length == 0)
            {
                sb.Append('\n');
                continue;
            }
            sb.Append(continuationPad).Append(line);
            if (last)
                sb.Append(';');
            sb.Append('\n');
        }
    }

    /// <summary>
    /// The dotnet/runtime max line width. A member signature whose single physical
    /// line would exceed this wraps its parameter list one parameter per line (the
    /// revealed corpus practice). Shares the rationale — and value — of the
    /// decompiler's fluent-chain wrap width; a pure formatting tiebreaker that never
    /// changes which tokens are emitted.
    /// </summary>
    internal const int SignatureWrapWidth = 120;

    /// <summary>
    /// Renders the declaration <paramref name="head"/> at <paramref name="pad"/>.
    /// Top-level generic-constraint clauses each occupy an indented continuation
    /// line, matching the dominant dotnet/runtime source form. Independently, an
    /// over-width parameter list wraps one parameter per continuation line when it
    /// can be located unambiguously. <paramref name="renderTail"/> is the member
    /// terminator, expression body, or empty block tail; with constraints it follows
    /// the final clause. Falls back to the inline single line when one-liner wrapping
    /// is disabled or a line comment makes line breaks semantically significant.
    /// Block-comment contents are ignored while locating clauses. Whitespace only:
    /// every transformed form is token-identical.
    /// </summary>
    static string LayOutHead(string pad, string head, string renderTail, string decisionSuffix, bool disableOneLinerWrapping)
    {
        if (disableOneLinerWrapping || ContainsLineComment(head))
            return pad + head + renderTail;

        if (SplitConstraintClauses(head) is { Count: > 1 } parts)
        {
            string declaration = parts[0];
            string laidOutDeclaration =
                pad.Length + declaration.Length > SignatureWrapWidth
                    && WrapParameterList(pad, declaration, renderTail: "") is { } wrapped
                    ? wrapped
                    : pad + declaration;

            var sb = new StringBuilder(laidOutDeclaration);
            string continuation = pad + "    ";
            for (int i = 1; i < parts.Count; i++)
                sb.Append('\n').Append(continuation).Append(parts[i]);
            sb.Append(renderTail);
            return sb.ToString();
        }

        if (pad.Length + head.Length + decisionSuffix.Length > SignatureWrapWidth
            && WrapParameterList(pad, head, renderTail) is { } widthWrapped)
            return widthWrapped;
        return pad + head + renderTail;
    }

    static List<string> SplitConstraintClauses(string head)
    {
        if (ContainsUnsupportedLiteral(head))
            return [head];

        var indexes = new List<int>();
        int angle = 0, paren = 0, bracket = 0, brace = 0;
        for (int i = 0; i + 7 <= head.Length; i++)
        {
            if (TrySkipConventionalLiteralOrBlockComment(head, ref i))
                continue;

            char c = head[i];
            switch (c)
            {
                case '<':
                    if (paren == 0 && bracket == 0 && brace == 0)
                        angle++;
                    break;
                case '>':
                    if (paren == 0 && bracket == 0 && brace == 0 && angle > 0)
                        angle--;
                    break;
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
                case '{': brace++; break;
                case '}': if (brace > 0) brace--; break;
                case ' ':
                    if (angle == 0 && paren == 0 && bracket == 0 && brace == 0
                        && string.CompareOrdinal(head, i, " where ", 0, 7) == 0)
                    {
                        indexes.Add(i);
                        i += 6;
                    }
                    break;
            }
        }

        if (indexes.Count == 0)
            return [head];

        int firstConstraint = indexes[0];
        if (!TryLocateParameterList(head, firstConstraint, out int parameterOpen, out _)
            || parameterOpen == 0
            || head[parameterOpen - 1] != '>'
            || indexes.Any(index => !IsConstraintClauseStart(head, index + 1)))
        {
            return [head];
        }

        var parts = new List<string>(indexes.Count + 1)
        {
            head[..firstConstraint].TrimEnd()
        };
        for (int i = 0; i < indexes.Count; i++)
        {
            int start = indexes[i] + 1;
            int end = i + 1 < indexes.Count ? indexes[i + 1] : head.Length;
            parts.Add(head[start..end].Trim());
        }
        return parts;
    }

    static bool IsConstraintClauseStart(string head, int start)
    {
        int i = start + "where".Length;
        while (i < head.Length && char.IsWhiteSpace(head[i]))
            i++;

        if (i < head.Length && head[i] == '@')
            i++;

        int identifierStart = i;
        while (i < head.Length && (char.IsLetterOrDigit(head[i]) || head[i] == '_'))
            i++;
        if (i == identifierStart)
            return false;

        while (i < head.Length && char.IsWhiteSpace(head[i]))
            i++;
        return i < head.Length && head[i] == ':';
    }

    static bool ContainsLineComment(string head)
    {
        for (int i = 0; i + 1 < head.Length; i++)
        {
            if (TrySkipConventionalLiteralOrBlockComment(head, ref i))
                continue;

            char c = head[i];
            if (c != '/')
                continue;
            if (head[i + 1] == '/')
                return true;
        }
        return false;
    }

    static int SkipBlockComment(string head, int start)
    {
        int end = head.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return end < 0 ? head.Length - 1 : end + 1;
    }

    static string? WrapParameterList(string pad, string head, string renderTail)
    {
        if (ContainsUnsupportedLiteral(head))
            return null;
        if (!TryLocateParameterList(head, out int open, out int close))
            return null;
        var parameters = SplitTopLevelCommas(head, open + 1, close);
        if (parameters.Count == 0)
            return null;

        string continuation = pad + "    ";
        var sb = new StringBuilder();
        sb.Append(pad).Append(head, 0, open + 1);
        for (int i = 0; i < parameters.Count; i++)
        {
            sb.Append('\n').Append(continuation).Append(parameters[i].Trim());
            if (i < parameters.Count - 1)
                sb.Append(',');
        }
        sb.Append(head, close, head.Length - close);
        sb.Append(renderTail);
        return sb.ToString();
    }

    /// <summary>
    /// Locates the method/constructor parameter-list parentheses in
    /// <paramref name="head"/> — the first top-level <c>(</c> immediately preceded by
    /// the member name (an identifier char or a generic-arg <c>&gt;</c>), so a
    /// parenthesized/tuple return type (which precedes the name) is skipped and a
    /// <c>new()</c> in a trailing <c>where</c> constraint is excluded by bounding the
    /// scan before the constraint clause. Returns false for shapes it cannot match
    /// with confidence (e.g. operator tokens before the paren), so the caller leaves
    /// the signature inline rather than risk mangling it.
    /// </summary>
    static bool TryLocateParameterList(string head, out int open, out int close)
        => TryLocateParameterList(
            head,
            FindTopLevelWhere(head) is { } where and >= 0 ? where : head.Length,
            out open,
            out close);

    static bool TryLocateParameterList(string head, int limit, out int open, out int close)
    {
        open = -1;
        close = -1;

        int angle = 0, bracket = 0, brace = 0;
        for (int i = 0; i < limit; i++)
        {
            if (TrySkipConventionalLiteralOrBlockComment(head, ref i))
                continue;

            char c = head[i];
            switch (c)
            {
                case '<': angle++; break;
                case '>': if (angle > 0) angle--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
                case '{': brace++; break;
                case '}': if (brace > 0) brace--; break;
                case '(':
                    if (angle == 0 && bracket == 0 && brace == 0)
                    {
                        char prev = i > 0 ? head[i - 1] : '\0';
                        if (char.IsLetterOrDigit(prev) || prev == '_' || prev == '>')
                        {
                            int match = MatchParen(head, i);
                            if (match < 0)
                                return false;
                            open = i;
                            close = match;
                            return true;
                        }
                        // A top-level '(' not preceded by the member name (a tuple or
                        // parenthesized return type): skip its whole group.
                        int end = MatchParen(head, i);
                        if (end < 0)
                            return false;
                        i = end;
                    }
                    break;
            }
        }
        return false;
    }

    /// <summary>
    /// Splits the parameter text <c>head[start..end]</c> on top-level commas —
    /// commas at bracket depth zero, ignoring those inside <c>&lt;&gt;</c>,
    /// <c>()</c>, <c>[]</c>, <c>{}</c> or string/char literals — so a generic type
    /// argument, tuple, attribute argument, or default value with its own commas
    /// stays on one line.
    /// </summary>
    static List<string> SplitTopLevelCommas(string head, int start, int end)
    {
        var result = new List<string>();
        int angle = 0, paren = 0, bracket = 0, brace = 0;
        int segmentStart = start;
        for (int i = start; i < end; i++)
        {
            if (TrySkipConventionalLiteralOrBlockComment(head, ref i))
                continue;

            char c = head[i];
            switch (c)
            {
                case '<': angle++; break;
                case '>': if (angle > 0) angle--; break;
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
                case '{': brace++; break;
                case '}': if (brace > 0) brace--; break;
                case ',':
                    if (angle == 0 && paren == 0 && bracket == 0 && brace == 0)
                    {
                        result.Add(head[segmentStart..i]);
                        segmentStart = i + 1;
                    }
                    break;
            }
        }
        string tail = head[segmentStart..end];
        if (tail.Trim().Length > 0 || result.Count > 0)
            result.Add(tail);
        return result;
    }

    /// <summary>Index of the <c>)</c> matching the <c>(</c> at <paramref name="open"/>, or -1.</summary>
    static int MatchParen(string head, int open)
    {
        int depth = 0;
        for (int i = open; i < head.Length; i++)
        {
            if (TrySkipConventionalLiteralOrBlockComment(head, ref i))
                continue;

            char c = head[i];
            if (c == '(')
                depth++;
            else if (c == ')' && --depth == 0)
                return i;
        }
        return -1;
    }

    /// <summary>Index of a top-level <c>" where "</c> generic-constraint clause, or -1.</summary>
    static int FindTopLevelWhere(string head)
    {
        int angle = 0, paren = 0, bracket = 0, brace = 0;
        for (int i = 0; i + 7 <= head.Length; i++)
        {
            if (TrySkipConventionalLiteralOrBlockComment(head, ref i))
                continue;

            char c = head[i];
            switch (c)
            {
                case '<': angle++; break;
                case '>': if (angle > 0) angle--; break;
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
                case '{': brace++; break;
                case '}': if (brace > 0) brace--; break;
                case ' ':
                    if (angle == 0 && paren == 0 && bracket == 0 && brace == 0
                        && string.CompareOrdinal(head, i, " where ", 0, 7) == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    /// <summary>
    /// True when <paramref name="head"/> contains a string-literal form this layout
    /// does not parse well enough to split around safely: a verbatim (<c>@"…"</c>,
    /// whose escape is a doubled <c>""</c>), an interpolated (<c>$"…"</c>, whose
    /// <c>{…}</c> holes contain arbitrary nested quotes and commas), or a raw
    /// (<c>"""…"""</c>) string. <see cref="SkipLiteral"/> models only the
    /// conventional <c>\</c>-escaped literal, so a comma or quote inside one of
    /// these forms could be misread as a top-level separator. When any is present
    /// the caller declines to wrap and keeps the signature on one line — the safe
    /// fallback — rather than risk splitting inside a literal. Conventional
    /// literals and block comments are skipped correctly during the scan so
    /// their contents never trigger a false positive or hide a later unsupported
    /// literal opener.
    /// </summary>
    static bool ContainsUnsupportedLiteral(string head)
    {
        for (int i = 0; i < head.Length; i++)
        {
            if (TrySkipBlockComment(head, ref i))
                continue;

            char c = head[i];
            if (c == '\'')
            {
                i = SkipLiteral(head, i);
                continue;
            }
            if (c == '"')
            {
                if (IsUnsupportedLiteralStart(head, i))
                    return true;
                i = SkipLiteral(head, i);
            }
        }
        return false;
    }

    static bool TrySkipConventionalLiteralOrBlockComment(string head, ref int i)
    {
        if (TrySkipBlockComment(head, ref i))
            return true;

        if (head[i] != '"' && head[i] != '\'')
            return false;

        i = SkipLiteral(head, i);
        return true;
    }

    static bool TrySkipBlockComment(string head, ref int i)
    {
        if (head[i] != '/' || i + 1 >= head.Length || head[i + 1] != '*')
            return false;

        i = SkipBlockComment(head, i);
        return true;
    }

    static bool IsUnsupportedLiteralStart(string head, int i)
    {
        char prev = i > 0 ? head[i - 1] : '\0';
        if (prev is '@' or '$')
            return true; // verbatim / interpolated (incl. $@" and @$")
        if (i + 2 < head.Length && head[i + 1] == '"' && head[i + 2] == '"')
            return true; // raw string literal ("""…""")
        return false;
    }

    /// <summary>
    /// Given <paramref name="start"/> at a conventional string or char literal's
    /// opening quote, returns the index of its closing quote (respecting <c>\</c>
    /// escapes), or the last index when unterminated. Verbatim, interpolated, and
    /// raw literals are not modeled here; <see cref="ContainsUnsupportedLiteral"/>
    /// makes the caller decline to wrap before this runs on one.
    /// </summary>
    static int SkipLiteral(string head, int start)
    {
        char quote = head[start];
        for (int i = start + 1; i < head.Length; i++)
        {
            if (head[i] == '\\')
            {
                i++;
                continue;
            }
            if (head[i] == quote)
                return i;
        }
        return head.Length - 1;
    }

    /// <summary>
    /// Appends <paramref name="body"/> content at <paramref name="indent"/>
    /// spaces, trimming trailing whitespace and preserving blank lines (an empty
    /// line stays an empty line). This blank-line-preserving policy is the
    /// deliberate difference from <c>CSharpTypePrinter</c>, which drops
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
                sb.Append('\n');
            else
                sb.Append($"{pad}{trimmed}").Append('\n');
        }
    }
}
