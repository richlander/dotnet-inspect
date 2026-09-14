using System.Collections.Immutable;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Sections;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Options;

internal static class PerformanceTriageRowQuery
{
    private static readonly RowQueryNamedOrder<Analysis.OptimizationOpportunity>
        TriageOrder =
        new(
            RowQueryNamedOrderIdentity.Create(),
            "Triage",
            RowQueryOrderPurpose.Ranking,
            direction => Directional(
                Analysis.OptimizationOpportunityRanking.OpportunityComparer,
                direction));

    private static readonly RowQueryNamedOrder<Analysis.OptimizationOpportunity>
        AllocationFanoutOrder =
        new(
            RowQueryNamedOrderIdentity.Create(),
            "AllocationFanout",
            RowQueryOrderPurpose.Ranking,
            direction => Directional(
                Comparer<Analysis.OptimizationOpportunity>.Create(
                    CompareAllocationFanout),
                direction));

    private static readonly RowQueryFacetValuePresentation TextValue =
        new("text/glob", [], "*");

    private static readonly RowQueryFacetValuePresentation IntegerValue =
        new("integer", [], "10");

    private static readonly ImmutableArray<string> RankedValues =
        ["low", "medium", "high"];

    private static readonly RowQueryFacetValuePresentation RankedValue =
        new("rank", RankedValues, "high");

    private static readonly ImmutableArray<FieldBinding> FieldBindings =
        [.. CreateFields()];

    // AllocationFanout is selected only by focused shape lowering; the public
    // --order-by grammar does not bind it.
    private static readonly ImmutableArray<
        RowQueryNamedOrder<Analysis.OptimizationOpportunity>>
        DiscoverableNamedOrders = [TriageOrder];

    private static readonly RowQuerySchema<Analysis.OptimizationOpportunity>
        Schema =
        RowQuerySchema<Analysis.OptimizationOpportunity>.Create(
            RowQuerySchemaIdentity.Create(),
            [.. FieldBindings.Select(binding => binding.Field)],
            [TriageOrder, AllocationFanoutOrder],
            defaultTopRanking:
                new(
                    TriageOrder,
                    RowQueryOrderDirection.Descending));

    internal static RowQuerySchema<Analysis.OptimizationOpportunity>
        ExecutableSchema => Schema;

    internal static ImmutableArray<SectionQueryFacet> QueryFacets { get; } =
        RowQueryFacetProjection.Create(
            Schema,
            ValuePresentation,
            DiscoverableNamedOrders);

    internal static IReadOnlyList<string> FilterableFields { get; } =
        [.. Schema.Fields
            .Where(field => field.Operators.Count > 0)
            .Select(field => field.Key)];

    internal static IReadOnlyList<string> SortableFields { get; } =
        CreateSortableFields();

    internal static bool IsNumericField(string field) =>
        ValuePresentation(Field(field)).ValueKind == "integer";

    internal static bool IsRankedField(string field) =>
        ValuePresentation(Field(field)).ValueKind == "rank";

    internal static bool IsRankedValue(string value) =>
        Rank(value) >= 0;

    internal static ResolvedRowQueryPlan<Analysis.OptimizationOpportunity>
        Resolve(
            PerformanceTriageOptions options,
            IReadOnlyList<PerformanceTriageOptions.RowPredicate> predicates,
            IReadOnlyList<PerformanceTriageOptions.OrderTerm> orderTerms)
    {
        var loweredPredicates =
            new List<RowQueryPredicateIntent>(
                predicates.Count + 2);
        if (options.LoopOnly)
        {
            loweredPredicates.Add(
                Predicate(
                    "Loop",
                    RowQueryOperator.Equals,
                    "loop"));
        }

        if (options.MinConfidence is { Length: > 0 } confidence)
        {
            loweredPredicates.Add(
                Predicate(
                    "Confidence",
                    RowQueryOperator.GreaterOrEqual,
                    confidence));
        }

        foreach (PerformanceTriageOptions.RowPredicate predicate in predicates)
        {
            loweredPredicates.Add(
                Predicate(
                    predicate.Field,
                    predicate.Operator,
                    predicate.Value));
        }

        RowQueryOrderIntent order = LowerOrder(orderTerms);
        RowQueryOrderIntent? baseline = null;
        RowSelectionIntent<RowQueryOrderIntent> selection =
            RowSelectionIntent<RowQueryOrderIntent>.Empty;
        if (options.Top is { } top)
        {
            if (!string.IsNullOrWhiteSpace(options.OrderBy))
            {
                selection = selection.Append(
                    RowSelectionIntentOperation<RowQueryOrderIntent>.Top(
                        top,
                        order));
            }
            else if (options.IncludesAllocationFanout)
            {
                selection = selection.Append(
                    RowSelectionIntentOperation<RowQueryOrderIntent>.Top(
                        top,
                        RowQueryOrderIntent.Named(
                            "AllocationFanout",
                            RowQueryOrderDirection.Descending)));
            }
            else
            {
                selection = selection.Append(
                    RowSelectionIntentOperation<RowQueryOrderIntent>.Top(top));
            }
        }
        else
        {
            baseline = !string.IsNullOrWhiteSpace(options.OrderBy)
                ? order
                : options.IncludesAllocationFanout
                    ? RowQueryOrderIntent.Named(
                        "AllocationFanout",
                        RowQueryOrderDirection.Descending)
                    : RowQueryOrderIntent.Named(
                        "Triage",
                        RowQueryOrderDirection.Descending);
        }

        RowQueryResolutionResult<Analysis.OptimizationOpportunity> result =
            RowQueryResolver.Resolve(
                Schema,
                RowQueryIntent.Create(
                    loweredPredicates,
                    baseline,
                    selection));
        return result.Plan
            ?? throw new InvalidOperationException(
                $"Canonical Performance Triage row-query resolution failed: "
                + $"{result.Failure!.OperationKind}/"
                + $"{result.Failure.Reason}.");
    }

    private static RowQueryPredicateIntent Predicate(
        string field,
        RowQueryOperator @operator,
        string value) =>
        new(
            field,
            @operator,
            new RowQueryValueToken(value));

    private static RowQueryOrderIntent LowerOrder(
        IReadOnlyList<PerformanceTriageOptions.OrderTerm> terms)
    {
        if (terms.Count == 1
            && terms[0].Field == "Triage")
        {
            return RowQueryOrderIntent.Named(
                "Triage",
                Direction(terms[0].Descending));
        }

        return RowQueryOrderIntent.Fields(
            [
                .. terms.Select(
                    term => new RowQueryOrderTermIntent(
                        term.Field,
                        Direction(term.Descending))),
            ]);
    }

    private static RowQueryOrderDirection Direction(bool descending) =>
        descending
            ? RowQueryOrderDirection.Descending
            : RowQueryOrderDirection.Ascending;

    private static IReadOnlyList<FieldBinding> CreateFields() =>
        [
            Describe(MemberField(), TextValue, sortablePosition: 8),
            Describe(
                TextField("Candidate", row => row.CandidateId, ordered: true),
                TextValue,
                sortablePosition: 9),
            Describe(
                TextField("Finding", row => row.SourceFinding, ordered: true),
                TextValue,
                sortablePosition: 10),
            Describe(
                TextField(
                    "Provenance",
                    row => LibraryMetadataService.FormatProvenance(row.Provenance),
                    ordered: true),
                TextValue,
                sortablePosition: 11),
            Describe(
                NumericField(
                    "RootReach",
                    row => row.RootReach,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 1),
            Describe(
                TextField("Shape", row => row.Shape, ordered: true),
                TextValue,
                sortablePosition: 12),
            Describe(
                TextField("Operation", row => row.Operation, ordered: true),
                TextValue,
                sortablePosition: 13),
            Describe(TokenField(), TextValue, sortablePosition: 14),
            Describe(
                TextField(
                    "EvidenceMethod",
                    row => FormatToken(row.EvidenceMethodToken),
                    ordered: true),
                TextValue,
                sortablePosition: 15),
            Describe(
                TextField("Evidence", row => row.Evidence, ordered: false),
                TextValue),
            Describe(
                TextField("Fix", row => row.SafeFixDirection, ordered: false),
                TextValue),
            Describe(
                RankedField(
                    "Priority",
                    row => (int)Analysis.OptimizationOpportunityRanking.Priority(row),
                    missing: false),
                RankedValue,
                sortablePosition: 2),
            Describe(
                RankedField(
                    "Confidence",
                    row => Rank(row.Confidence),
                    missing: false),
                RankedValue,
                sortablePosition: 3),
            Describe(
                TextField(
                    "Loop",
                    row => Analysis.OptimizationOpportunityRanking.IteratesInLoop(row)
                        ? "loop"
                        : "",
                    ordered: true),
                TextValue,
                sortablePosition: 4),
            Describe(
                TextField(
                    "CallerLoop",
                    row => LibraryMetadataService.FormatCallerLoop(row.CallerLoop),
                    ordered: true),
                TextValue,
                sortablePosition: 5),
            Describe(
                NumericField(
                    "CallerLoopDepth",
                    row => row.CallerLoop?.Depth,
                    missingLast: true),
                IntegerValue,
                sortablePosition: 6),
            Describe(
                TextField(
                    "CallerLoopWitness",
                    row => LibraryMetadataService.FormatCallerLoopWitness(row.CallerLoop),
                    ordered: true),
                TextValue,
                sortablePosition: 7),
            Describe(
                TextField(
                    "Allocation",
                    row => row.RuntimeAllocationType,
                    ordered: true),
                TextValue,
                sortablePosition: 17),
            Describe(
                TextField("Path", row => row.PathContext, ordered: true),
                TextValue,
                sortablePosition: 18),
            Describe(
                TextField(
                    "PathConfidence",
                    row => row.PathConfidence,
                    ordered: true),
                TextValue,
                sortablePosition: 19),
            Describe(
                TextField(
                    "PostDominance",
                    row => row.PostDominance,
                    ordered: true),
                TextValue,
                sortablePosition: 20),
            Describe(IlField(), TextValue, sortablePosition: 16),
            Describe(
                RankedField(
                    "Weight",
                    row => row.Weight is null ? null : Rank(row.Weight),
                    missing: true),
                RankedValue,
                sortablePosition: 21),
            Describe(
                NumericField(
                    "DirectSites",
                    row => row.DirectAllocationSites,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 22),
            Describe(
                NumericField(
                    "OncePaths",
                    row => row.OnceAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 23),
            Describe(
                NumericField(
                    "ConditionalPaths",
                    row => row.ConditionalAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 24),
            Describe(
                NumericField(
                    "RepeatedPaths",
                    row => row.RepeatedAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 25),
            Describe(
                NumericField(
                    "UnknownPaths",
                    row => row.UnknownAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 26),
            Describe(
                NumericField(
                    "CachedSites",
                    row => row.CachedAllocationSites,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 27),
            Describe(
                NumericField(
                    "OpaquePaths",
                    row => row.OpaqueCallPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 28),
            Describe(
                TextField(
                    "Saturated",
                    row => row.AllocationCountSaturated ? "yes" : null,
                    ordered: false),
                TextValue),
        ];

    private static FieldBinding Describe(
        RowQueryField<Analysis.OptimizationOpportunity> field,
        RowQueryFacetValuePresentation valuePresentation,
        int? sortablePosition = null) =>
        new(field, valuePresentation, sortablePosition);

    private static RowQueryField<Analysis.OptimizationOpportunity> TextField(
        string key,
        Func<Analysis.OptimizationOpportunity, string?> accessor,
        bool ordered) =>
        RowQueryField<Analysis.OptimizationOpportunity>.Create(
            RowQueryFieldIdentity.Create(),
            key,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<string>.Present(accessor(row) ?? ""),
            BindText,
            ordered
                ? direction => RowQueryValueOrder.Create(
                    Comparer<string>.Create(
                        (left, right) =>
                            StringComparer.OrdinalIgnoreCase.Compare(
                                left,
                                right)),
                    direction,
                    missingLast: false)
                : null);

    private static RowQueryField<Analysis.OptimizationOpportunity> NumericField(
        string key,
        Func<Analysis.OptimizationOpportunity, long?> accessor,
        bool missingLast) =>
        RowQueryField<Analysis.OptimizationOpportunity>.Create(
            RowQueryFieldIdentity.Create(),
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
                RowQueryOperator.GreaterOrEqual,
                RowQueryOperator.LessOrEqual,
            ],
            row => accessor(row) is { } value
                ? RowQueryValue<long>.Present(value)
                : RowQueryValue<long>.Missing,
            BindLong,
            direction => RowQueryValueOrder.Create(
                Comparer<long>.Default,
                direction,
                missingLast || direction is RowQueryOrderDirection.Descending));

    private static RowQueryField<Analysis.OptimizationOpportunity> RankedField(
        string key,
        Func<Analysis.OptimizationOpportunity, int?> accessor,
        bool missing) =>
        RowQueryField<Analysis.OptimizationOpportunity>.Create(
            RowQueryFieldIdentity.Create(),
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
                RowQueryOperator.GreaterOrEqual,
                RowQueryOperator.LessOrEqual,
            ],
            row => accessor(row) is { } value
                ? RowQueryValue<int>.Present(value)
                : RowQueryValue<int>.Missing,
            BindRank,
            direction => RowQueryValueOrder.Create(
                Comparer<int>.Default,
                direction,
                missing && direction is RowQueryOrderDirection.Descending));

    private static RowQueryField<Analysis.OptimizationOpportunity> MemberField() =>
        RowQueryField<Analysis.OptimizationOpportunity>.Create(
            RowQueryFieldIdentity.Create(),
            "Member",
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<MemberValue>.Present(
                new(
                    LibraryMetadataService.FormatMethod(row.Method),
                    ShortMemberSignature(row.Method))),
            (operation, token) =>
            {
                bool Match(MemberValue value) =>
                    WildcardMatch(value.Full, token.Text)
                    || WildcardMatch(value.Short, token.Text);
                return operation switch
                {
                    RowQueryOperator.Equals => Match,
                    RowQueryOperator.NotEquals => value => !Match(value),
                    _ => null,
                };
            },
            direction => RowQueryValueOrder.Create(
                MemberValueComparer.Instance,
                direction,
                missingLast: false));

    private static RowQueryField<Analysis.OptimizationOpportunity> IlField() =>
        RowQueryField<Analysis.OptimizationOpportunity>.Create(
            RowQueryFieldIdentity.Create(),
            "IL",
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<IlValue>.Present(
                new(
                    row.ILOffset is { } offset
                        ? $"IL_{offset:X4}"
                        : "",
                    row.ILOffset ?? -1)),
            (operation, token) =>
            {
                bool Match(IlValue value) =>
                    WildcardMatch(value.Text, token.Text);
                return operation switch
                {
                    RowQueryOperator.Equals => Match,
                    RowQueryOperator.NotEquals => value => !Match(value),
                    _ => null,
                };
            },
            direction => RowQueryValueOrder.Create(
                new IlValueComparer(),
                direction,
                missingLast: false));

    private static RowQueryField<Analysis.OptimizationOpportunity> TokenField() =>
        RowQueryField<Analysis.OptimizationOpportunity>.Create(
            RowQueryFieldIdentity.Create(),
            "Token",
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<TokenValue>.Present(
                new(
                    FormatToken(row.OperandToken) ?? "",
                    row.OperandToken)),
            (operation, token) =>
            {
                bool Match(TokenValue value)
                {
                    if (!token.Text.Contains('*')
                        && !token.Text.Contains('?')
                        && TryParseMetadataToken(
                            token.Text,
                            out int expected))
                    {
                        return value.Token == expected;
                    }

                    return WildcardMatch(value.Text, token.Text);
                }

                return operation switch
                {
                    RowQueryOperator.Equals => Match,
                    RowQueryOperator.NotEquals => value => !Match(value),
                    _ => null,
                };
            },
            direction => RowQueryValueOrder.Create(
                new TokenValueComparer(),
                direction,
                missingLast: false));

    private static IReadOnlyList<string> CreateSortableFields()
    {
        // Preserve the established -D and diagnostic ordering independently
        // from the schema-owned ordering capability.
        FieldBinding[] sortable =
        [
            .. FieldBindings
                .Where(binding => binding.Field.SupportsOrdering)
                .OrderBy(binding => binding.SortablePosition),
        ];
        if (sortable.Any(binding => binding.SortablePosition is null)
            || sortable.Select(binding => binding.SortablePosition)
                .Distinct()
                .Count() != sortable.Length)
        {
            throw new InvalidOperationException(
                "Sortable Performance Triage fields require unique display positions.");
        }

        return
        [
            .. DiscoverableNamedOrders.Select(order => order.Key),
            .. sortable.Select(binding => binding.Field.Key),
        ];
    }

    internal static RowQueryField<Analysis.OptimizationOpportunity> Field(
        string key) =>
        Schema.Fields.Single(
            field => string.Equals(
                field.Key,
                key,
                StringComparison.Ordinal));

    private static RowQueryFacetValuePresentation ValuePresentation(
        RowQueryField<Analysis.OptimizationOpportunity> field) =>
        FieldBindings.Single(
            binding => ReferenceEquals(binding.Field, field))
        .ValuePresentation;

    private static Predicate<string>? BindText(
        RowQueryOperator operation,
        RowQueryValueToken token) =>
        operation switch
        {
            RowQueryOperator.Equals =>
                value => WildcardMatch(value, token.Text),
            RowQueryOperator.NotEquals =>
                value => !WildcardMatch(value, token.Text),
            _ => null,
        };

    private static Predicate<long>? BindLong(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!long.TryParse(
                token.Text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long expected))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            RowQueryOperator.GreaterOrEqual => value => value >= expected,
            RowQueryOperator.LessOrEqual => value => value <= expected,
            _ => null,
        };
    }

    private static Predicate<int>? BindRank(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        int expected = Rank(token.Text);
        if (expected < 0)
            return null;
        return operation switch
        {
            RowQueryOperator.Equals => value => value == expected,
            RowQueryOperator.NotEquals => value => value != expected,
            RowQueryOperator.GreaterOrEqual => value => value >= expected,
            RowQueryOperator.LessOrEqual => value => value <= expected,
            _ => null,
        };
    }

    private static int Rank(string value)
    {
        for (int index = 0; index < RankedValues.Length; index++)
        {
            if (value.Equals(
                    RankedValues[index],
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int CompareAllocationFanout(
        Analysis.OptimizationOpportunity left,
        Analysis.OptimizationOpportunity right)
    {
        int comparison = (right.OnceAllocationPaths ?? -1)
            .CompareTo(left.OnceAllocationPaths ?? -1);
        if (comparison != 0)
            return comparison;
        comparison = (right.RepeatedAllocationPaths ?? -1)
            .CompareTo(left.RepeatedAllocationPaths ?? -1);
        if (comparison != 0)
            return comparison;
        return (right.ConditionalAllocationPaths ?? -1)
            .CompareTo(left.ConditionalAllocationPaths ?? -1);
    }

    private static IComparer<T> Directional<T>(
        IComparer<T> descendingComparer,
        RowQueryOrderDirection direction) =>
        direction is RowQueryOrderDirection.Descending
            ? descendingComparer
            : Comparer<T>.Create(
                (left, right) => descendingComparer.Compare(right, left));

    private static string? FormatToken(int? token) =>
        token is { } value ? $"0x{value:X8}" : null;

    private static bool TryParseMetadataToken(
        string value,
        out int token)
    {
        token = default;
        value = value.Trim();
        return value.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                value.AsSpan(2),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out token);
    }

    private static string ShortMemberSignature(Analysis.MethodIdentity method) =>
        $"{method.Name}({string.Join(", ", method.ParameterTypes.Select(
            parameter => parameter.ToQualifiedDisplayString()))})";

    private static bool WildcardMatch(string actual, string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return string.Equals(
                actual,
                pattern,
                StringComparison.OrdinalIgnoreCase);
        }

        int textIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int matchIndex = 0;
        while (textIndex < actual.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?'
                    || char.ToUpperInvariant(pattern[patternIndex])
                        == char.ToUpperInvariant(actual[textIndex])))
            {
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length
                && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                matchIndex = textIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length
            && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private readonly record struct MemberValue(
        string Full,
        string Short);

    private sealed class MemberValueComparer : IComparer<MemberValue>
    {
        internal static MemberValueComparer Instance { get; } = new();

        public int Compare(MemberValue left, MemberValue right) =>
            StringComparer.OrdinalIgnoreCase.Compare(
                left.Full,
                right.Full);
    }

    private readonly record struct IlValue(string Text, int Offset);

    private sealed class IlValueComparer : IComparer<IlValue>
    {
        public int Compare(IlValue left, IlValue right) =>
            left.Offset.CompareTo(right.Offset);
    }

    private readonly record struct TokenValue(
        string Text,
        int? Token);

    private sealed record FieldBinding(
        RowQueryField<Analysis.OptimizationOpportunity> Field,
        RowQueryFacetValuePresentation ValuePresentation,
        int? SortablePosition);

    private sealed class TokenValueComparer : IComparer<TokenValue>
    {
        public int Compare(TokenValue left, TokenValue right) =>
            (left.Token ?? -1).CompareTo(right.Token ?? -1);
    }
}
