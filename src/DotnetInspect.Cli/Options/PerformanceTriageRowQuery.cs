using System.Collections.Immutable;

using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using QuerySpace;
using QuerySpace.Rows;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Options;

internal static class PerformanceTriageRowQuery
{
    private static readonly RowQueryVocabulary<
        Analysis.OptimizationOpportunity> Vocabulary =
            Analysis.OptimizationOpportunityRowSpace.RowScope.Vocabulary;

    private static readonly ImmutableArray<
        RowQueryNamedOrder<Analysis.OptimizationOpportunity>>
        DiscoverableNamedOrders =
        [
            Vocabulary.NamedOrders.Single(
                order => order.Key
                    == Analysis.OptimizationOpportunityRowSpace
                        .TriageOrderKey),
        ];

    private static readonly ImmutableArray<string> RankedValues =
        [
            .. Analysis.OptimizationOpportunityRowSpace.RankedValues,
        ];

    private static readonly RowQueryKeyValuePresentation TextValue =
        new("text/glob", [], "*");

    private static readonly RowQueryKeyValuePresentation IntegerValue =
        new("integer", [], "10");

    private static readonly RowQueryKeyValuePresentation RankedValue =
        new("rank", RankedValues, "high");

    internal static RowQueryVocabulary<Analysis.OptimizationOpportunity>
        ExecutableVocabulary => Vocabulary;

    internal static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
        RowQueryKeyProjection.Create(
            Vocabulary,
            ValuePresentation,
            DiscoverableNamedOrders,
            key => key.Key
                != Analysis.OptimizationOpportunityRowSpace.KindKey);

    internal static IReadOnlyList<string> FilterableFields { get; } =
        [
            .. Vocabulary.Keys
                .Where(key =>
                    key.Key
                        != Analysis.OptimizationOpportunityRowSpace.KindKey
                    && key.Operators.Count > 0)
                .Select(key => key.Key),
        ];

    internal static IReadOnlyList<string> SortableFields { get; } =
        CreateSortableFields();

    internal static bool IsNumericKey(string field) =>
        ValuePresentation(Key(field)).ValueKind == "integer";

    internal static bool IsRankedKey(string field) =>
        ValuePresentation(Key(field)).ValueKind == "rank";

    internal static bool IsRankedValue(string value) =>
        RankedValues.Any(
            ranked => value.Equals(
                ranked,
                StringComparison.OrdinalIgnoreCase));

    internal static ResolvedRowQueryPlan<Analysis.OptimizationOpportunity>
        Resolve(
            PerformanceTriageOptions options,
            IReadOnlyList<PerformanceTriageOptions.RowPredicate> predicates,
            IReadOnlyList<PerformanceTriageOptions.OrderTerm> orderTerms)
    {
        PortableQueryIntent intent = Lower(
            options,
            predicates,
            orderTerms);
        RowQueryResolutionResult<Analysis.OptimizationOpportunity> result =
            Analysis.OptimizationOpportunityRowSpace.Resolve(
                Analysis.OptimizationOpportunityRowSpace.PerformanceTriage,
                intent);
        return result.Plan
            ?? throw new InvalidOperationException(
                $"Canonical Performance Triage row-query resolution failed: "
                + $"{result.Failure!.OperationKind}/"
                + $"{result.Failure.Reason}.");
    }

    internal static ImmutableArray<Analysis.OptimizationOpportunity> Select(
        OptimizationOpportunitiesResult.Available available,
        PerformanceTriageOptions options)
    {
        ImmutableArray<Analysis.OptimizationOpportunity> candidates =
            Analysis.OptimizationOpportunityRowSpace.PerformanceCandidates(
                available.Opportunities,
                available.AllocationFanoutOpportunities,
                available.GeneratedFrameworkTypes,
                options.IncludesAllocationFanout);
        return Apply(candidates, options);
    }

    internal static ImmutableArray<Analysis.OptimizationOpportunity> Apply(
        IEnumerable<Analysis.OptimizationOpportunity> opportunities,
        PerformanceTriageOptions? options)
    {
        options ??= PerformanceTriageOptions.Default;
        RowSelectionResult<Analysis.OptimizationOpportunity> result =
            Analysis.OptimizationOpportunityRowSpace.Apply(
                opportunities,
                options.Shapes,
                options.GetResolvedPlan());
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                "Performance Triage produced an unexpected row-window failure.");
        }

        return [.. result.Values];
    }

    internal static RowQueryKey<Analysis.OptimizationOpportunity> Key(
        string key) =>
        Analysis.OptimizationOpportunityRowSpace.Key(key);

    private static PortableQueryIntent Lower(
        PerformanceTriageOptions options,
        IReadOnlyList<PerformanceTriageOptions.RowPredicate> predicates,
        IReadOnlyList<PerformanceTriageOptions.OrderTerm> orderTerms)
    {
        var terms = new List<PortableQueryTerm>(predicates.Count + 2);
        if (options.LoopOnly)
        {
            terms.Add(
                Term(
                    "Loop",
                    RowQueryOperator.Equals,
                    "loop"));
        }

        if (options.MinConfidence is { Length: > 0 } confidence)
        {
            terms.Add(
                Term(
                    "Confidence",
                    RowQueryOperator.GreaterOrEqual,
                    confidence));
        }

        foreach (PerformanceTriageOptions.RowPredicate predicate
            in predicates)
        {
            terms.Add(
                Term(
                    predicate.Field,
                    predicate.Operator,
                    predicate.Value));
        }

        var stages = new List<PortableQueryStage>();
        var order = new List<PortableQueryOrderOperation>();
        if (options.Top is { } top)
        {
            stages.Add(PortableQueryStage.Top(top));
            if (!string.IsNullOrWhiteSpace(options.OrderBy))
            {
                order.Add(
                    LowerOrder(
                        PortableQueryOrderRole.ForStage(0),
                        orderTerms));
            }
            else if (options.IncludesAllocationFanout)
            {
                order.Add(
                    PortableQueryOrderOperation.Named(
                        PortableQueryOrderRole.ForStage(0),
                        Analysis.OptimizationOpportunityRowSpace
                            .AllocationFanoutOrderKey,
                        PortableQueryDirection.Descending));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(options.OrderBy))
            {
                order.Add(
                    LowerOrder(
                        PortableQueryOrderRole.Baseline,
                        orderTerms));
            }
            else if (options.IncludesAllocationFanout)
            {
                order.Add(
                    PortableQueryOrderOperation.Named(
                        PortableQueryOrderRole.Baseline,
                        Analysis.OptimizationOpportunityRowSpace
                            .AllocationFanoutOrderKey,
                        PortableQueryDirection.Descending));
            }
        }

        return PortableQueryIntent.Create(
            terms,
            [],
            stages,
            order);
    }

    private static PortableQueryTerm Term(
        string field,
        RowQueryOperator operation,
        string value) =>
        new(
            field,
            PortableOperator(operation),
            value);

    private static PortableQueryOrderOperation LowerOrder(
        PortableQueryOrderRole role,
        IReadOnlyList<PerformanceTriageOptions.OrderTerm> terms)
    {
        if (terms.Count == 1
            && terms[0].Field
                == Analysis.OptimizationOpportunityRowSpace.TriageOrderKey)
        {
            return PortableQueryOrderOperation.Named(
                role,
                Analysis.OptimizationOpportunityRowSpace.TriageOrderKey,
                Direction(terms[0].Descending));
        }

        return PortableQueryOrderOperation.Fields(
            role,
            [
                .. terms.Select(
                    term => new PortableQueryOrderTerm(
                        term.Field,
                        Direction(term.Descending))),
            ]);
    }

    private static PortableQueryOperator PortableOperator(
        RowQueryOperator operation) =>
        operation switch
        {
            RowQueryOperator.Equals => PortableQueryOperator.Equal,
            RowQueryOperator.NotEquals => PortableQueryOperator.NotEqual,
            RowQueryOperator.GreaterOrEqual => PortableQueryOperator.AtLeast,
            RowQueryOperator.LessOrEqual => PortableQueryOperator.AtMost,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static PortableQueryDirection Direction(bool descending) =>
        descending
            ? PortableQueryDirection.Descending
            : PortableQueryDirection.Ascending;

    private static IReadOnlyList<string> CreateSortableFields()
    {
        string[] fields =
        [
            Analysis.OptimizationOpportunityRowSpace.TriageOrderKey,
            "RootReach",
            "Priority",
            "Confidence",
            "Loop",
            "CallerLoop",
            "CallerLoopDepth",
            "CallerLoopWitness",
            "Member",
            "Candidate",
            "Finding",
            "Provenance",
            "Shape",
            "Operation",
            "Token",
            "EvidenceMethod",
            "IL",
            "Allocation",
            "Path",
            "PathConfidence",
            "PostDominance",
            "Weight",
            "DirectSites",
            "OncePaths",
            "ConditionalPaths",
            "RepeatedPaths",
            "UnknownPaths",
            "CachedSites",
            "OpaquePaths",
        ];

        foreach (string field in fields.Skip(1))
        {
            if (!Key(field).SupportsOrdering)
            {
                throw new InvalidOperationException(
                    $"Sortable Performance Triage field '{field}' "
                    + "does not declare ordering.");
            }
        }

        return fields;
    }

    private static RowQueryKeyValuePresentation ValuePresentation(
        RowQueryKey<Analysis.OptimizationOpportunity> key) =>
        key.Key switch
        {
            "RootReach"
                or "CallerLoopDepth"
                or "DirectSites"
                or "OncePaths"
                or "ConditionalPaths"
                or "RepeatedPaths"
                or "UnknownPaths"
                or "CachedSites"
                or "OpaquePaths" => IntegerValue,
            "Priority"
                or "Confidence"
                or "Weight" => RankedValue,
            _ => TextValue,
        };
}
