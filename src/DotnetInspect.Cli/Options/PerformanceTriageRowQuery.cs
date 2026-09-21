using System.Collections.Immutable;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Sections;
using QuerySpace.Rows;
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

    private static readonly RowQueryKeyValuePresentation TextValue =
        new("text/glob", [], "*");

    private static readonly RowQueryKeyValuePresentation IntegerValue =
        new("integer", [], "10");

    private static readonly ImmutableArray<string> RankedValues =
        ["low", "medium", "high"];

    private static readonly RowQueryKeyValuePresentation RankedValue =
        new("rank", RankedValues, "high");

    private static readonly ImmutableArray<KeyBinding> KeyBindings =
        [.. CreateKeys()];

    // AllocationFanout is selected only by focused shape lowering; the public
    // --order-by grammar does not bind it.
    private static readonly ImmutableArray<
        RowQueryNamedOrder<Analysis.OptimizationOpportunity>>
        DiscoverableNamedOrders = [TriageOrder];

    private static readonly RowQueryVocabulary<Analysis.OptimizationOpportunity>
        Vocabulary =
        RowQueryVocabulary<Analysis.OptimizationOpportunity>.Create(
            RowQueryVocabularyIdentity.Create(),
            [.. KeyBindings.Select(binding => binding.Key)],
            [TriageOrder, AllocationFanoutOrder],
            defaultTopRanking:
                new(
                    TriageOrder,
                    RowQueryOrderDirection.Descending));

    internal static RowQueryVocabulary<Analysis.OptimizationOpportunity>
        ExecutableVocabulary => Vocabulary;

    internal static ImmutableArray<SectionQueryKey> QueryKeys { get; } =
        RowQueryKeyProjection.Create(
            Vocabulary,
            ValuePresentation,
            DiscoverableNamedOrders);

    internal static IReadOnlyList<string> FilterableFields { get; } =
        [.. Vocabulary.Keys
            .Where(key => key.Operators.Count > 0)
            .Select(key => key.Key)];

    internal static IReadOnlyList<string> SortableFields { get; } =
        CreateSortableFields();

    internal static bool IsNumericKey(string field) =>
        ValuePresentation(Key(field)).ValueKind == "integer";

    internal static bool IsRankedKey(string field) =>
        ValuePresentation(Key(field)).ValueKind == "rank";

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
                Vocabulary,
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

        return RowQueryOrderIntent.Keys(
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

    private static IReadOnlyList<KeyBinding> CreateKeys() =>
        [
            Describe(MemberKey(), TextValue, sortablePosition: 8),
            Describe(
                TextKey("Candidate", row => row.CandidateId, ordered: true),
                TextValue,
                sortablePosition: 9),
            Describe(
                TextKey("Finding", row => row.SourceFinding, ordered: true),
                TextValue,
                sortablePosition: 10),
            Describe(
                TextKey(
                    "Provenance",
                    row => LibraryMetadataService.FormatProvenance(row.Provenance),
                    ordered: true),
                TextValue,
                sortablePosition: 11),
            Describe(
                NumericKey(
                    "RootReach",
                    row => row.RootReach,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 1),
            Describe(
                TextKey("Shape", row => row.Shape, ordered: true),
                TextValue,
                sortablePosition: 12),
            Describe(
                TextKey("Operation", row => row.Operation, ordered: true),
                TextValue,
                sortablePosition: 13),
            Describe(TokenKey(), TextValue, sortablePosition: 14),
            Describe(
                TextKey(
                    "EvidenceMethod",
                    row => FormatToken(row.EvidenceMethodToken),
                    ordered: true),
                TextValue,
                sortablePosition: 15),
            Describe(
                TextKey("Evidence", row => row.Evidence, ordered: false),
                TextValue),
            Describe(
                TextKey("Fix", row => row.SafeFixDirection, ordered: false),
                TextValue),
            Describe(
                RankedKey(
                    "Priority",
                    row => (int)Analysis.OptimizationOpportunityRanking.Priority(row),
                    missing: false),
                RankedValue,
                sortablePosition: 2),
            Describe(
                RankedKey(
                    "Confidence",
                    row => Rank(row.Confidence),
                    missing: false),
                RankedValue,
                sortablePosition: 3),
            Describe(
                TextKey(
                    "Loop",
                    row => Analysis.OptimizationOpportunityRanking.IteratesInLoop(row)
                        ? "loop"
                        : "",
                    ordered: true),
                TextValue,
                sortablePosition: 4),
            Describe(
                TextKey(
                    "CallerLoop",
                    row => LibraryMetadataService.FormatCallerLoop(row.CallerLoop),
                    ordered: true),
                TextValue,
                sortablePosition: 5),
            Describe(
                NumericKey(
                    "CallerLoopDepth",
                    row => row.CallerLoop?.Depth,
                    missingLast: true),
                IntegerValue,
                sortablePosition: 6),
            Describe(
                TextKey(
                    "CallerLoopWitness",
                    row => LibraryMetadataService.FormatCallerLoopWitness(row.CallerLoop),
                    ordered: true),
                TextValue,
                sortablePosition: 7),
            Describe(
                TextKey(
                    "Allocation",
                    row => row.RuntimeAllocationType,
                    ordered: true),
                TextValue,
                sortablePosition: 17),
            Describe(
                TextKey("Path", row => row.PathContext, ordered: true),
                TextValue,
                sortablePosition: 18),
            Describe(
                TextKey(
                    "PathConfidence",
                    row => row.PathConfidence,
                    ordered: true),
                TextValue,
                sortablePosition: 19),
            Describe(
                TextKey(
                    "PostDominance",
                    row => row.PostDominance,
                    ordered: true),
                TextValue,
                sortablePosition: 20),
            Describe(IlKey(), TextValue, sortablePosition: 16),
            Describe(
                RankedKey(
                    "Weight",
                    row => row.Weight is null ? null : Rank(row.Weight),
                    missing: true),
                RankedValue,
                sortablePosition: 21),
            Describe(
                NumericKey(
                    "DirectSites",
                    row => row.DirectAllocationSites,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 22),
            Describe(
                NumericKey(
                    "OncePaths",
                    row => row.OnceAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 23),
            Describe(
                NumericKey(
                    "ConditionalPaths",
                    row => row.ConditionalAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 24),
            Describe(
                NumericKey(
                    "RepeatedPaths",
                    row => row.RepeatedAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 25),
            Describe(
                NumericKey(
                    "UnknownPaths",
                    row => row.UnknownAllocationPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 26),
            Describe(
                NumericKey(
                    "CachedSites",
                    row => row.CachedAllocationSites,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 27),
            Describe(
                NumericKey(
                    "OpaquePaths",
                    row => row.OpaqueCallPaths,
                    missingLast: false),
                IntegerValue,
                sortablePosition: 28),
            Describe(
                TextKey(
                    "Saturated",
                    row => row.AllocationCountSaturated ? "yes" : null,
                    ordered: false),
                TextValue),
        ];

    private static KeyBinding Describe(
        RowQueryKey<Analysis.OptimizationOpportunity> key,
        RowQueryKeyValuePresentation valuePresentation,
        int? sortablePosition = null) =>
        new(key, valuePresentation, sortablePosition);

    private static RowQueryKey<Analysis.OptimizationOpportunity> TextKey(
        string key,
        Func<Analysis.OptimizationOpportunity, string?> accessor,
        bool ordered) =>
        RowQueryKey<Analysis.OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
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

    private static RowQueryKey<Analysis.OptimizationOpportunity> NumericKey(
        string key,
        Func<Analysis.OptimizationOpportunity, long?> accessor,
        bool missingLast) =>
        RowQueryKey<Analysis.OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
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

    private static RowQueryKey<Analysis.OptimizationOpportunity> RankedKey(
        string key,
        Func<Analysis.OptimizationOpportunity, int?> accessor,
        bool missing) =>
        RowQueryKey<Analysis.OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
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

    private static RowQueryKey<Analysis.OptimizationOpportunity> MemberKey() =>
        RowQueryKey<Analysis.OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
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

    private static RowQueryKey<Analysis.OptimizationOpportunity> IlKey() =>
        RowQueryKey<Analysis.OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
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

    private static RowQueryKey<Analysis.OptimizationOpportunity> TokenKey() =>
        RowQueryKey<Analysis.OptimizationOpportunity>.Create(
            RowQueryKeyIdentity.Create(),
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
        // from the vocabulary-owned ordering capability.
        KeyBinding[] sortable =
        [
            .. KeyBindings
                .Where(binding => binding.Key.SupportsOrdering)
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
            .. sortable.Select(binding => binding.Key.Key),
        ];
    }

    internal static RowQueryKey<Analysis.OptimizationOpportunity> Key(
        string key) =>
        Vocabulary.Keys.Single(
            candidate => string.Equals(
                candidate.Key,
                key,
                StringComparison.Ordinal));

    private static RowQueryKeyValuePresentation ValuePresentation(
        RowQueryKey<Analysis.OptimizationOpportunity> key) =>
        KeyBindings.Single(
            binding => ReferenceEquals(binding.Key, key))
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

    private sealed record KeyBinding(
        RowQueryKey<Analysis.OptimizationOpportunity> Key,
        RowQueryKeyValuePresentation ValuePresentation,
        int? SortablePosition);

    private sealed class TokenValueComparer : IComparer<TokenValue>
    {
        public int Compare(TokenValue left, TokenValue right) =>
            (left.Token ?? -1).CompareTo(right.Token ?? -1);
    }
}
