using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

internal sealed record RowQueryKeyValuePresentation(
    string ValueKind,
    ImmutableArray<string> Values,
    string ExampleValue);

internal static class RowQueryKeyProjection
{
    internal static ImmutableArray<SectionQueryKey> Create<TRow>(
        RowQueryVocabulary<TRow> vocabulary,
        Func<
            RowQueryKey<TRow>,
            RowQueryKeyValuePresentation> valuePresentation,
        IReadOnlyList<RowQueryNamedOrder<TRow>> namedOrders)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(valuePresentation);
        ArgumentNullException.ThrowIfNull(namedOrders);

        var projectedKeys = ImmutableArray.CreateBuilder<SectionQueryKey>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (RowQueryKey<TRow> key in vocabulary.Keys)
        {
            bool filterable = key.Operators.Count > 0;
            if (!filterable && !key.SupportsOrdering)
                continue;

            RowQueryKeyValuePresentation? presentation =
                filterable
                    ? valuePresentation(key)
                        ?? throw new InvalidOperationException(
                            $"Query facet {key.Key} has no value presentation.")
                    : null;
            if (presentation is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    presentation.ValueKind);
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    presentation.ExampleValue);
            }

            Add(
                projectedKeys,
                keys,
                new(
                    key.Key,
                    [
                        .. filterable
                            ? new[] { "--where" }
                            : [],
                        .. key.SupportsOrdering
                            ? new[] { "--order-by", "--top" }
                            : [],
                    ],
                    [
                        .. key.Operators.Select(
                            RowPredicateSyntaxParser.Comparison),
                    ],
                    presentation?.ValueKind ?? "order",
                    presentation?.Values ?? [],
                    filterable
                        ? $"--where \"{key.Key}"
                            + $"{ExampleComparison(key)}"
                            + $"{presentation!.ExampleValue}\""
                        : $"--top 10 --order-by \"{key.Key} desc\""));
        }

        foreach (RowQueryNamedOrder<TRow> namedOrder in namedOrders)
        {
            ArgumentNullException.ThrowIfNull(namedOrder);
            if (!vocabulary.NamedOrders.Any(
                    declared => ReferenceEquals(declared, namedOrder)))
            {
                throw new ArgumentException(
                    $"Named order {namedOrder.Key} is not declared by the row vocabulary.",
                    nameof(namedOrders));
            }

            bool ranking =
                namedOrder.Purpose is RowQueryOrderPurpose.Ranking;
            Add(
                projectedKeys,
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

        return projectedKeys.ToImmutable();
    }

    private static void Add(
        ImmutableArray<SectionQueryKey>.Builder projectedKeys,
        ISet<string> keys,
        SectionQueryKey key)
    {
        if (!keys.Add(key.Name))
        {
            throw new InvalidOperationException(
                $"Query facet {key.Name} is duplicated.");
        }

        projectedKeys.Add(key);
    }

    private static string ExampleComparison<TRow>(
        RowQueryKey<TRow> key) =>
        key.Operators.Contains(RowQueryOperator.GreaterOrEqual)
            ? ">="
            : RowPredicateSyntaxParser.Comparison(
                key.Operators[0]);
}
