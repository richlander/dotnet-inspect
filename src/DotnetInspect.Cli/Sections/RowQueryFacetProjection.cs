using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

internal sealed record RowQueryFacetValuePresentation(
    string ValueKind,
    ImmutableArray<string> Values,
    string ExampleValue);

internal static class RowQueryFacetProjection
{
    internal static ImmutableArray<SectionQueryFacet> Create<TRow>(
        RowQuerySchema<TRow> schema,
        Func<
            RowQueryField<TRow>,
            RowQueryFacetValuePresentation> valuePresentation,
        IReadOnlyList<RowQueryNamedOrder<TRow>> namedOrders)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(valuePresentation);
        ArgumentNullException.ThrowIfNull(namedOrders);

        var facets = ImmutableArray.CreateBuilder<SectionQueryFacet>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (RowQueryField<TRow> field in schema.Fields)
        {
            bool filterable = field.Operators.Count > 0;
            if (!filterable && !field.SupportsOrdering)
                continue;

            RowQueryFacetValuePresentation? presentation =
                filterable
                    ? valuePresentation(field)
                        ?? throw new InvalidOperationException(
                            $"Query facet {field.Key} has no value presentation.")
                    : null;
            if (presentation is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    presentation.ValueKind);
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    presentation.ExampleValue);
            }

            Add(
                facets,
                keys,
                new(
                    field.Key,
                    [
                        .. filterable
                            ? new[] { "--where" }
                            : [],
                        .. field.SupportsOrdering
                            ? new[] { "--order-by", "--top" }
                            : [],
                    ],
                    [.. field.Operators.Select(Comparison)],
                    presentation?.ValueKind ?? "order",
                    presentation?.Values ?? [],
                    filterable
                        ? $"--where \"{field.Key}"
                            + $"{ExampleComparison(field)}"
                            + $"{presentation!.ExampleValue}\""
                        : $"--top 10 --order-by \"{field.Key} desc\""));
        }

        foreach (RowQueryNamedOrder<TRow> namedOrder in namedOrders)
        {
            ArgumentNullException.ThrowIfNull(namedOrder);
            if (!schema.NamedOrders.Any(
                    declared => ReferenceEquals(declared, namedOrder)))
            {
                throw new ArgumentException(
                    $"Named order {namedOrder.Key} is not declared by the row schema.",
                    nameof(namedOrders));
            }

            bool ranking =
                namedOrder.Purpose is RowQueryOrderPurpose.Ranking;
            Add(
                facets,
                keys,
                new(
                    namedOrder.Key,
                    [
                        "--order-by",
                        .. ranking
                            ? new[] { "--top" }
                            : [],
                    ],
                    [],
                    "order",
                    [],
                    ranking
                        ? $"--top 10 --order-by \"{namedOrder.Key} desc\""
                        : $"--order-by \"{namedOrder.Key} asc\""));
        }

        return facets.ToImmutable();
    }

    private static void Add(
        ImmutableArray<SectionQueryFacet>.Builder facets,
        ISet<string> keys,
        SectionQueryFacet facet)
    {
        if (!keys.Add(facet.Name))
        {
            throw new InvalidOperationException(
                $"Query facet {facet.Name} is duplicated.");
        }

        facets.Add(facet);
    }

    private static string ExampleComparison<TRow>(
        RowQueryField<TRow> field) =>
        field.Operators.Contains(RowQueryOperator.GreaterOrEqual)
            ? ">="
            : Comparison(field.Operators[0]);

    private static string Comparison(RowQueryOperator @operator) =>
        @operator switch
        {
            RowQueryOperator.Equals => "=",
            RowQueryOperator.NotEquals => "!=",
            RowQueryOperator.GreaterOrEqual => ">=",
            RowQueryOperator.LessOrEqual => "<=",
            _ => throw new ArgumentOutOfRangeException(
                nameof(@operator),
                @operator,
                "Unsupported row-query predicate operator."),
        };
}
