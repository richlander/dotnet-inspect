using System.Globalization;

using DotnetInspect.Cli.Sections;
using ILInspector.CSharp;

namespace DotnetInspect.Cli.Options;

public sealed record LibraryCallUseQueryOptions
{
    public static LibraryCallUseQueryOptions Default { get; } = new();

    public static SectionQueryKey QueryKey { get; } = new(
        "Cluster",
        ["--where"],
        ["="],
        "positive pair-wide direct-use cluster ordinal (exactly one)",
        [],
        "--where \"Cluster=3\"");

    public int? Cluster { get; init; }

    public static bool TryParse(
        IReadOnlyList<string> expressions,
        out LibraryCallUseQueryOptions options,
        out OptionError error)
    {
        int? cluster = null;
        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(
                    expression,
                    out RowPredicateSyntax syntax,
                    out error))
            {
                options = Default;
                return false;
            }

            if (!RowPredicateSyntaxParser.NormalizeFieldName(syntax.Field)
                    .Equals(
                        "Cluster",
                        StringComparison.OrdinalIgnoreCase))
            {
                string field =
                    CSharpIdentifier.ContainRenderedText(syntax.Field);
                options = Default;
                error = new OptionError(
                    $"Field '{field}' is not queryable by graph libraries.",
                    ["Use --where \"Cluster=<positive ordinal>\"."]);
                return false;
            }

            if (syntax.Operator != RowPredicateOperator.Equals)
            {
                options = Default;
                error =
                    "Field 'Cluster' in graph libraries supports only = predicates.";
                return false;
            }

            if (cluster is not null)
            {
                options = Default;
                error =
                    "graph libraries accepts exactly one --where Cluster=... predicate.";
                return false;
            }

            if (!int.TryParse(
                    syntax.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int ordinal)
                || ordinal <= 0)
            {
                options = Default;
                error =
                    "Field 'Cluster' requires a positive integer ordinal.";
                return false;
            }

            cluster = ordinal;
        }

        options = new LibraryCallUseQueryOptions
        {
            Cluster = cluster,
        };
        error = "";
        return true;
    }
}
