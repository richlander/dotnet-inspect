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
    /// The expression of a <em>multi-line</em> single-<c>return</c> body — the
    /// raised switch-expression return (issue #3088) — as its lines, with the
    /// leading <c>return </c> and trailing <c>;</c> removed and every line's
    /// trailing whitespace trimmed. The first entry is the value/arrow line
    /// (<c>&lt;value&gt; switch</c>); the rest are the block lines (<c>{</c>,
    /// one arm per line, <c>}</c>) at their body-relative (column-zero) indent,
    /// which the layout re-indents under the member. Returns <see langword="null"/>
    /// for a single-line body (that is <see cref="FromSingleStatement"/>'s job) or
    /// anything not shaped as <c>return &lt;expr&gt;;</c>.
    /// </summary>
    /// <remarks>
    /// This never asserts, on its own, that the body is a single statement — a
    /// flat string cannot prove that soundly. Callers must gate it on the typed
    /// <c>BodyIsSingleReturnExpression</c> signal the printer proves structurally.
    /// </remarks>
    public static IReadOnlyList<string>? MultilineReturnExpressionLines(string body)
    {
        var trimmed = body.Trim();
        if (!trimmed.EndsWith(';'))
            return null;
        int newline = trimmed.IndexOf('\n');
        if (newline < 0)
            return null;   // single line — FromSingleStatement owns it

        var firstLine = trimmed[..newline].TrimStart();
        if (!firstLine.StartsWith("return ", StringComparison.Ordinal))
            return null;
        var valueLine = firstLine["return ".Length..].Trim();
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
