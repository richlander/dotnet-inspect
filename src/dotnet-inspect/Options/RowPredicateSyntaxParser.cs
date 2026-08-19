using ILInspector.CSharp;

namespace DotnetInspector.Options;

internal enum RowPredicateOperator
{
    Equals,
    NotEquals,
    GreaterOrEqual,
    LessOrEqual,
}

internal readonly record struct RowPredicateSyntax(
    string Field,
    RowPredicateOperator Operator,
    string Value);

internal static class RowPredicateSyntaxParser
{
    internal static bool TryParse(
        string expression,
        out RowPredicateSyntax syntax,
        out OptionError error)
    {
        syntax = default;
        expression = expression.Trim();
        if (expression.Length == 0)
        {
            error = "Empty --where predicate.";
            return false;
        }

        if (FindOperator(expression) is not { } found)
        {
            error =
                $"Invalid --where predicate '{Contain(expression)}'. "
                + "Use forms like 'Field=value', 'Field!=value', "
                + "'RootReach>=10', or 'Confidence>=medium'.";
            return false;
        }

        var (index, token, parsedOperator) = found;
        string value = expression[(index + token.Length)..].Trim();
        if (value.Length == 0)
        {
            error = $"Missing value in --where predicate '{Contain(expression)}'.";
            return false;
        }

        syntax = new RowPredicateSyntax(
            expression[..index].Trim(),
            parsedOperator,
            value);
        error = "";
        return true;
    }

    internal static string NormalizeFieldName(string value)
        => value.Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);

    private static (
        int Index,
        string Token,
        RowPredicateOperator Operator)? FindOperator(string expression)
    {
        (int Index, string Token, RowPredicateOperator Operator)? best = null;
        foreach (var candidate in new[]
        {
            (Token: ">=", Operator: RowPredicateOperator.GreaterOrEqual),
            (Token: "<=", Operator: RowPredicateOperator.LessOrEqual),
            (Token: "!=", Operator: RowPredicateOperator.NotEquals),
            (Token: "=", Operator: RowPredicateOperator.Equals),
        })
        {
            int index = expression.IndexOf(candidate.Token, StringComparison.Ordinal);
            if (index <= 0)
                continue;
            if (best is null
                || index < best.Value.Index
                || index == best.Value.Index
                && candidate.Token.Length > best.Value.Token.Length)
            {
                best = (index, candidate.Token, candidate.Operator);
            }
        }
        return best;
    }

    private static string Contain(string text)
        => CSharpIdentifier.ContainRenderedText(text);
}
