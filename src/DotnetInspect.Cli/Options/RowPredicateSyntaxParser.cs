using ILInspector.CSharp;
using QuerySpace;
using QuerySpace.Rows;

namespace DotnetInspect.Cli.Options;

internal enum RowPredicateOperator
{
    Equals,
    NotEquals,
    StartsWith,
    GreaterOrEqual,
    LessOrEqual,
}

internal readonly record struct RowPredicateSyntax(
    string Field,
    RowPredicateOperator Operator,
    string Value);

internal static class RowPredicateSyntaxParser
{
    private readonly record struct OperatorSyntax(
        string Token,
        string Comparison,
        RowPredicateOperator PredicateOperator,
        PortableQueryOperator PortableOperator,
        RowQueryOperator? RowOperator);

    private static readonly OperatorSyntax[] Operators =
    [
        new(
            " starts-with ",
            "starts-with",
            RowPredicateOperator.StartsWith,
            PortableQueryOperator.StartsWith,
            null),
        new(
            ">=",
            ">=",
            RowPredicateOperator.GreaterOrEqual,
            PortableQueryOperator.AtLeast,
            RowQueryOperator.GreaterOrEqual),
        new(
            "<=",
            "<=",
            RowPredicateOperator.LessOrEqual,
            PortableQueryOperator.AtMost,
            RowQueryOperator.LessOrEqual),
        new(
            "!=",
            "!=",
            RowPredicateOperator.NotEquals,
            PortableQueryOperator.NotEqual,
            RowQueryOperator.NotEquals),
        new(
            "=",
            "=",
            RowPredicateOperator.Equals,
            PortableQueryOperator.Equal,
            RowQueryOperator.Equals),
    ];

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
                + "'Field starts-with value', 'RootReach>=10', "
                + "or 'Confidence>=medium'.";
            return false;
        }

        var (index, operatorSyntax) = found;
        string value =
            expression[(index + operatorSyntax.Token.Length)..].Trim();
        if (value.Length == 0)
        {
            error = $"Missing value in --where predicate '{Contain(expression)}'.";
            return false;
        }

        syntax = new RowPredicateSyntax(
            expression[..index].Trim(),
            operatorSyntax.PredicateOperator,
            value);
        error = "";
        return true;
    }

    internal static string NormalizeFieldName(string value)
        => value.Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);

    internal static PortableQueryOperator PortableOperator(
        RowPredicateOperator @operator)
    {
        foreach (OperatorSyntax syntax in Operators)
        {
            if (syntax.PredicateOperator == @operator)
                return syntax.PortableOperator;
        }

        throw new ArgumentOutOfRangeException(nameof(@operator));
    }

    internal static bool TryRowOperator(
        RowPredicateOperator @operator,
        out RowQueryOperator rowOperator)
    {
        foreach (OperatorSyntax syntax in Operators)
        {
            if (syntax.PredicateOperator == @operator
                && syntax.RowOperator is { } resolved)
            {
                rowOperator = resolved;
                return true;
            }
        }

        rowOperator = default;
        return false;
    }

    internal static string Comparison(
        PortableQueryOperator @operator)
    {
        foreach (OperatorSyntax syntax in Operators)
        {
            if (syntax.PortableOperator == @operator)
                return syntax.Comparison;
        }

        throw new ArgumentOutOfRangeException(nameof(@operator));
    }

    internal static string Comparison(
        RowQueryOperator @operator)
    {
        foreach (OperatorSyntax syntax in Operators)
        {
            if (syntax.RowOperator == @operator)
                return syntax.Comparison;
        }

        throw new ArgumentOutOfRangeException(nameof(@operator));
    }

    private static (
        int Index,
        OperatorSyntax Syntax)? FindOperator(string expression)
    {
        (int Index, OperatorSyntax Syntax)? best = null;
        foreach (OperatorSyntax candidate in Operators)
        {
            int index = expression.IndexOf(candidate.Token, StringComparison.Ordinal);
            if (index <= 0)
                continue;
            if (best is null
                || index < best.Value.Index
                || index == best.Value.Index
                && candidate.Token.Length
                    > best.Value.Syntax.Token.Length)
            {
                best = (index, candidate);
            }
        }
        return best;
    }

    private static string Contain(string text)
        => CSharpIdentifier.ContainRenderedText(text);
}
