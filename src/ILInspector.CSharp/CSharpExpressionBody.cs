namespace ILInspector.CSharp;

/// <summary>
/// Converts a rendered single-statement body to the expression used by C#
/// expression-bodied members, when the statement form is known to be legal.
/// </summary>
public static class CSharpExpressionBody
{
    public static string? FromSingleStatement(string body)
    {
        var line = body.Trim();
        if (line.Length == 0
            || line.Contains('\n')
            || !line.EndsWith(';')
            || line.StartsWith("//", StringComparison.Ordinal)
            || line.StartsWith("/*", StringComparison.Ordinal))
            return null;

        line = line[..^1].TrimEnd();
        if (line.StartsWith("return ", StringComparison.Ordinal))
        {
            var expression = line["return ".Length..].TrimStart();
            return expression.Length == 0 ? null : expression;
        }
        if (line is "return")
            return null;
        if (line.StartsWith("throw ", StringComparison.Ordinal))
            return line;
        return IsStatementExpression(line) ? line : null;
    }

    /// <summary>
    /// The expression of a <em>multi-line</em> single-statement body — a raised
    /// switch-expression return (issue #3088), a wrapped fluent chain in
    /// <c>return</c> or void expression-statement position, or any other wrapped
    /// single expression (issue #3084) — as its lines, with the leading
    /// <c>return </c> (when present) and trailing <c>;</c> removed and every
    /// line's trailing whitespace trimmed. The first entry is the value/arrow line
    /// (the token that opens the expression, such as <c>&lt;value&gt; switch</c>,
    /// the chain's receiver, or a leading <c>throw</c>); the rest are the
    /// continuation lines at their body-relative (column-zero) indent, which the
    /// layout re-indents under the member. Returns <see langword="null"/> for a
    /// single-line body (that is <see cref="FromSingleStatement"/>'s job) or
    /// anything not shaped as a single <c>&lt;expr&gt;;</c> statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The helper is deliberately shape-agnostic about the statement keyword: it
    /// strips a leading <c>return </c> when present and otherwise keeps the whole
    /// first line as the arrow value, so a void expression statement folds to
    /// <c>=&gt; &lt;expr&gt;;</c> and a wrapped <c>throw &lt;expr&gt;;</c> (should
    /// one ever print multi-line) folds to <c>=&gt; throw &lt;expr&gt;;</c>.
    /// </para>
    /// <para>
    /// This never asserts, on its own, that the body is a single statement — a
    /// flat string cannot prove that soundly. Callers must gate it on the typed
    /// <c>BodyIsSingleExpressionBody</c> signal the printer proves structurally.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string>? MultilineExpressionBodyLines(string body)
    {
        var trimmed = body.Trim();
        if (!trimmed.EndsWith(';'))
            return null;
        int newline = trimmed.IndexOf('\n');
        if (newline < 0)
            return null;   // single line — FromSingleStatement owns it

        var firstLine = trimmed[..newline].TrimStart();
        var valueLine = firstLine.StartsWith("return ", StringComparison.Ordinal)
            ? firstLine["return ".Length..].Trim()
            : firstLine;
        if (valueLine.Length == 0)
            return null;

        var rest = trimmed[(newline + 1)..^1];   // drop the terminating ';'
        var lines = new List<string> { valueLine };
        foreach (var line in rest.Split('\n'))
            lines.Add(line.TrimEnd());
        return lines;
    }

    static bool IsStatementExpression(string expression)
    {
        if (expression.StartsWith("await ", StringComparison.Ordinal))
        {
            var awaited = expression["await ".Length..].TrimStart();
            return awaited.Length > 0
                && !awaited.StartsWith("using ", StringComparison.Ordinal)
                && !awaited.StartsWith("foreach ", StringComparison.Ordinal);
        }
        if (expression.StartsWith("new ", StringComparison.Ordinal))
            return true;
        if (expression.EndsWith("++", StringComparison.Ordinal)
            || expression.EndsWith("--", StringComparison.Ordinal)
            || expression.StartsWith("++", StringComparison.Ordinal)
            || expression.StartsWith("--", StringComparison.Ordinal))
            return true;

        if (TryFindAssignmentOperator(expression, out var operatorIndex))
        {
            var target = expression[..operatorIndex].Trim();
            return target.Length > 0
                && (!target.Contains(' ', StringComparison.Ordinal) || target.StartsWith('('));
        }

        var paren = expression.IndexOf('(');
        if (paren <= 0)
            return false;

        var receiver = expression[..paren].TrimEnd();
        return receiver.Length > 0
            && !receiver.Contains(' ', StringComparison.Ordinal)
            && receiver is not ("if" or "for" or "while" or "switch" or "using" or "lock");
    }

    static bool TryFindAssignmentOperator(string expression, out int index)
    {
        foreach (var op in new[]
        {
            " ??= ", " <<= ", " >>= ",
            " += ", " -= ", " *= ", " /= ", " %= ", " &= ", " |= ", " ^= ",
            " = "
        })
        {
            index = expression.IndexOf(op, StringComparison.Ordinal);
            if (index > 0)
                return true;
        }

        index = -1;
        return false;
    }
}
