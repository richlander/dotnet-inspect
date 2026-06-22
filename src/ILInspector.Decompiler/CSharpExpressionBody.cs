namespace ILInspector.Decompiler;

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
