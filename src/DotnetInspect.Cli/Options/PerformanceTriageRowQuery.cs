using DotnetInspect.Cli.Inspectors;
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

    private static readonly RowQuerySchema<Analysis.OptimizationOpportunity>
        Schema =
        RowQuerySchema<Analysis.OptimizationOpportunity>.Create(
            RowQuerySchemaIdentity.Create(),
            CreateFields(),
            [TriageOrder, AllocationFanoutOrder],
            defaultTopRanking:
                new(
                    TriageOrder,
                    RowQueryOrderDirection.Descending));

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
                    predicate.Operator switch
                    {
                        PerformanceTriageOptions.RowOperator.Equals =>
                            RowQueryOperator.Equals,
                        PerformanceTriageOptions.RowOperator.NotEquals =>
                            RowQueryOperator.NotEquals,
                        PerformanceTriageOptions.RowOperator.GreaterOrEqual =>
                            RowQueryOperator.GreaterOrEqual,
                        PerformanceTriageOptions.RowOperator.LessOrEqual =>
                            RowQueryOperator.LessOrEqual,
                        _ => throw new InvalidOperationException(
                            $"Unsupported Performance Triage operator {predicate.Operator}."),
                    },
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

    private static IReadOnlyList<
        RowQueryField<Analysis.OptimizationOpportunity>> CreateFields() =>
        [
            MemberField(),
            TextField("Candidate", row => row.CandidateId, ordered: true),
            TextField("Finding", row => row.SourceFinding, ordered: true),
            TextField(
                "Provenance",
                row => LibraryMetadataService.FormatProvenance(row.Provenance),
                ordered: true),
            NumericField("RootReach", row => row.RootReach, missingLast: false),
            TextField("Shape", row => row.Shape, ordered: true),
            TextField("Operation", row => row.Operation, ordered: true),
            TokenField(),
            TextField(
                "EvidenceMethod",
                row => FormatToken(row.EvidenceMethodToken),
                ordered: true),
            TextField("Evidence", row => row.Evidence, ordered: false),
            TextField("Fix", row => row.SafeFixDirection, ordered: false),
            RankedField(
                "Priority",
                row => (int)Analysis.OptimizationOpportunityRanking.Priority(row),
                missing: false),
            RankedField(
                "Confidence",
                row => Rank(row.Confidence),
                missing: false),
            TextField(
                "Loop",
                row => Analysis.OptimizationOpportunityRanking.IteratesInLoop(row)
                    ? "loop"
                    : "",
                ordered: true),
            TextField(
                "CallerLoop",
                row => LibraryMetadataService.FormatCallerLoop(row.CallerLoop),
                ordered: true),
            NumericField(
                "CallerLoopDepth",
                row => row.CallerLoop?.Depth,
                missingLast: true),
            TextField(
                "CallerLoopWitness",
                row => LibraryMetadataService.FormatCallerLoopWitness(row.CallerLoop),
                ordered: true),
            TextField(
                "Allocation",
                row => row.RuntimeAllocationType,
                ordered: true),
            TextField("Path", row => row.PathContext, ordered: true),
            TextField(
                "PathConfidence",
                row => row.PathConfidence,
                ordered: true),
            TextField(
                "PostDominance",
                row => row.PostDominance,
                ordered: true),
            IlField(),
            RankedField(
                "Weight",
                row => row.Weight is null ? null : Rank(row.Weight),
                missing: true),
            NumericField(
                "DirectSites",
                row => row.DirectAllocationSites,
                missingLast: false),
            NumericField(
                "OncePaths",
                row => row.OnceAllocationPaths,
                missingLast: false),
            NumericField(
                "ConditionalPaths",
                row => row.ConditionalAllocationPaths,
                missingLast: false),
            NumericField(
                "RepeatedPaths",
                row => row.RepeatedAllocationPaths,
                missingLast: false),
            NumericField(
                "UnknownPaths",
                row => row.UnknownAllocationPaths,
                missingLast: false),
            NumericField(
                "CachedSites",
                row => row.CachedAllocationSites,
                missingLast: false),
            NumericField(
                "OpaquePaths",
                row => row.OpaqueCallPaths,
                missingLast: false),
            TextField(
                "Saturated",
                row => row.AllocationCountSaturated ? "yes" : null,
                ordered: false),
        ];

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
        if (value.Equals("high", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (value.Equals("medium", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (value.Equals("low", StringComparison.OrdinalIgnoreCase))
            return 0;
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

    private sealed class TokenValueComparer : IComparer<TokenValue>
    {
        public int Compare(TokenValue left, TokenValue right) =>
            (left.Token ?? -1).CompareTo(right.Token ?? -1);
    }
}
