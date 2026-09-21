using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using ILInspector.CSharp;

namespace DotnetInspect.Cli.Options;

/// <summary>
/// Row predicates for the Performance Triage section.
/// </summary>
public sealed record PerformanceTriageOptions
{
    private static readonly ConditionalWeakTable<
        PerformanceTriageOptions,
        ResolvedPlanCache> ResolvedPlans = new();

    /// <summary>
    /// Contains a fragment of the user's own <c>--order-by</c>/<c>--where</c>
    /// text before it is quoted back in a diagnostic. An agent composes these
    /// option values from names it read out of a package, so the fragment is
    /// untrusted even though the sentence around it is not.
    /// </summary>
    private static string Contain(string? text) => CSharpIdentifier.ContainRenderedText(text ?? string.Empty);

    public sealed record RowPredicate(
        string Field,
        RowQueryOperator Operator,
        string Value);

    public sealed record OrderTerm(string Field, bool Descending);

    public static PerformanceTriageOptions Default { get; } = new();
    public static IReadOnlyList<string> FilterableFields =>
        PerformanceTriageRowQuery.FilterableFields;

    public static IReadOnlyList<string> SortableFields =>
        PerformanceTriageRowQuery.SortableFields;

    internal static IEnumerable<(string Name, string Kind)> DiscoveryItems()
    {
        yield return ("Triage desc", "default-order");
        foreach (string step in new[]
                 {
                     "Priority desc (high > medium > low)",
                     "Confidence desc (high > medium > low)",
                     "Weight desc (high > medium > low > none)",
                     "RootReach desc",
                 })
        {
            yield return (step, "order-step");
        }
        foreach (string field in FilterableFields)
            yield return (field, "filterable");
        foreach (string field in SortableFields)
            yield return (field, "sortable");
    }

    public static readonly string[] KnownShapes =
    [
        "allocation-hotspot",
        "allocation-fanout",
        "async-state-machine",
        "box-value-type",
        "cache-lookup-factory-delegate",
        "capturing-delegate",
        "enumerator-allocation",
        "generic-parameter-object-box",
        "instance-method-group-delegate",
        "linq-scan-in-loop",
        "materialize-in-loop",
        "scan-method-in-loop-call",
        "scan-method-in-recursive-traversal",
        "small-array",
        "span-to-array-copy",
        "stackalloc-candidate",
        "string-build-in-loop",
        "string-materialization",
        "sync-call-in-async",
        "temporary-byte-array-copy",
    ];

    public bool LoopOnly { get; init; }
    public string? MinConfidence { get; init; }
    public string[] Shapes { get; init; } = [];
    public int? Top { get; init; }
    public string[] Where { get; init; } = [];
    public string? OrderBy { get; init; }

    public bool HasCandidateFilters =>
        LoopOnly
        || !string.IsNullOrWhiteSpace(MinConfidence)
        || Shapes.Length > 0
        || Where.Length > 0;

    public bool HasRanking =>
        Top.HasValue
        || !string.IsNullOrWhiteSpace(OrderBy);

    public bool HasFilters => HasCandidateFilters || HasRanking;

    public bool IncludesAllocationFanout =>
        Shapes.Contains("allocation-fanout", StringComparer.OrdinalIgnoreCase);

    public bool TryGetPredicates(out RowPredicate[] predicates, out OptionError error)
    {
        var builder = new List<RowPredicate>();
        foreach (var expression in Where)
        {
            if (!TryParsePredicate(expression, out var predicate, out error))
            {
                predicates = [];
                return false;
            }
            builder.Add(predicate);
        }
        predicates = [.. builder];
        error = "";
        return true;
    }

    public bool TryGetOrderTerms(out OrderTerm[] orderTerms, out OptionError error)
    {
        if (string.IsNullOrWhiteSpace(OrderBy))
        {
            orderTerms = [new OrderTerm("Triage", Descending: true)];
            error = "";
            return true;
        }

        var terms = new List<OrderTerm>();
        foreach (var raw in OrderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (fieldText, directionText) = SplitOrderTerm(raw);

            var field = NormalizeField(fieldText, SortableFields);
            if (field is null)
            {
                orderTerms = [];
                error = UnknownFieldError(fieldText, "sortable", SortableFields);
                return false;
            }

            bool descending = false;
            if (directionText is { Length: > 0 })
            {
                if (directionText.Equals("desc", StringComparison.OrdinalIgnoreCase)
                    || directionText.Equals("descending", StringComparison.OrdinalIgnoreCase))
                {
                    descending = true;
                }
                else if (directionText.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || directionText.Equals("ascending", StringComparison.OrdinalIgnoreCase))
                {
                    descending = false;
                }
                else
                {
                    orderTerms = [];
                    error = $"Invalid --order-by direction '{Contain(directionText)}'. Valid directions: asc, desc.";
                    return false;
                }
            }

            terms.Add(new OrderTerm(field, descending));
        }

        orderTerms = [.. terms];
        if (orderTerms.Length == 0)
        {
            error = "--order-by requires at least one field.";
            return false;
        }
        if (orderTerms.Length > 1 && orderTerms.Any(term => term.Field == "Triage"))
        {
            error = "Triage is a composite order and must be used alone, e.g. --order-by \"Triage desc\".";
            return false;
        }
        error = "";
        return true;
    }

    static (string Field, string? Direction) SplitOrderTerm(string raw)
    {
        raw = raw.Trim();
        int lastSpace = raw.LastIndexOf(' ');
        if (lastSpace < 0)
            return (raw, null);

        var maybeDirection = raw[(lastSpace + 1)..].Trim();
        if (maybeDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            || maybeDirection.Equals("ascending", StringComparison.OrdinalIgnoreCase)
            || maybeDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
            || maybeDirection.Equals("descending", StringComparison.OrdinalIgnoreCase))
        {
            return (raw[..lastSpace].Trim(), maybeDirection);
        }

        return (raw, null);
    }

    public static bool TryValidateShapes(PerformanceTriageOptions options, out OptionError error)
    {
        var known = KnownShapes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = options.Shapes.Where(shape => !known.Contains(shape)).ToArray();
        if (invalid.Length == 0)
        {
            error = "";
            return true;
        }

        var quotedInvalid = string.Join(", ", invalid.Select(shape => $"'{shape}'"));
        error = $"Unknown Performance Triage shape{(invalid.Length == 1 ? "" : "s")} {Contain(quotedInvalid)}. Valid shapes: {string.Join(", ", KnownShapes)}.";
        return false;
    }

    public static bool TryValidate(PerformanceTriageOptions options, out OptionError error)
    {
        if (!TryValidateShapes(options, out error))
            return false;
        if (!options.TryGetPredicates(out RowPredicate[] predicates, out error))
            return false;
        if (!options.TryGetOrderTerms(out OrderTerm[] orderTerms, out error))
            return false;
        if (ResolvedPlans.TryGetValue(
                options,
                out ResolvedPlanCache? cached)
            && cached.Matches(options))
        {
            return true;
        }

        ResolvedPlans.AddOrUpdate(
            options,
            new ResolvedPlanCache(
                options,
                PerformanceTriageRowQuery.Resolve(
                    options,
                    predicates,
                    orderTerms)));
        return true;
    }

    internal ResolvedRowQueryPlan<ILInspector.Analysis.OptimizationOpportunity>
        GetResolvedPlan()
    {
        if (ResolvedPlans.TryGetValue(
                this,
                out ResolvedPlanCache? cached)
            && cached.Matches(this))
        {
            return cached.Plan;
        }

        if (!TryValidate(this, out OptionError error))
        {
            throw new InvalidOperationException(
                $"Performance Triage options were not validated: {error}");
        }

        return ResolvedPlans.GetValue(
            this,
            _ => throw new InvalidOperationException(
                "Performance Triage validation produced no resolved plan."))
            .Plan;
    }

    private sealed class ResolvedPlanCache
    {
        private readonly bool _loopOnly;
        private readonly string? _minConfidence;
        private readonly ImmutableArray<string> _shapes;
        private readonly int? _top;
        private readonly ImmutableArray<string> _where;
        private readonly string? _orderBy;

        internal ResolvedPlanCache(
            PerformanceTriageOptions options,
            ResolvedRowQueryPlan<
                ILInspector.Analysis.OptimizationOpportunity> plan)
        {
            _loopOnly = options.LoopOnly;
            _minConfidence = options.MinConfidence;
            _shapes = [.. options.Shapes];
            _top = options.Top;
            _where = [.. options.Where];
            _orderBy = options.OrderBy;
            Plan = plan;
        }

        internal ResolvedRowQueryPlan<
            ILInspector.Analysis.OptimizationOpportunity> Plan { get; }

        internal bool Matches(PerformanceTriageOptions options) =>
            _loopOnly == options.LoopOnly
            && string.Equals(
                _minConfidence,
                options.MinConfidence,
                StringComparison.Ordinal)
            && _shapes.SequenceEqual(
                options.Shapes,
                StringComparer.Ordinal)
            && _top == options.Top
            && _where.SequenceEqual(
                options.Where,
                StringComparer.Ordinal)
            && string.Equals(
                _orderBy,
                options.OrderBy,
                StringComparison.Ordinal);
    }

    static bool TryParsePredicate(string expression, out RowPredicate predicate, out OptionError error)
    {
        predicate = default!;
        if (RowPredicateSyntaxParser.TryParse(
                expression,
                out var syntax,
                out error))
        {
            var fieldText = syntax.Field;
            var field = NormalizeField(fieldText, FilterableFields);
            if (field is null)
            {
                error = UnknownFieldError(fieldText, "filterable", FilterableFields);
                return false;
            }

            var queryField = PerformanceTriageRowQuery.Key(field);
            var value = syntax.Value;
            if (!TryBindPredicateOperator(
                    queryField,
                    syntax.Operator,
                    out RowQueryOperator @operator))
            {
                error =
                    $"Field '{Contain(field)}' supports only "
                    + FormatComparisons(queryField.Operators)
                    + " predicates.";
                return false;
            }
            if (IsNumericField(field)
                && !long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                error = $"Field '{Contain(field)}' expects an integer value in --where predicate '{Contain(expression)}'.";
                return false;
            }
            if (IsRankedField(field) && !IsKnownConfidence(value))
            {
                error = $"Field '{Contain(field)}' expects one of low, medium, high in --where predicate '{Contain(expression)}'.";
                return false;
            }

            predicate = new RowPredicate(field, @operator, value);
            error = "";
            return true;
        }

        return false;
    }

    static bool IsKnownConfidence(string value)
        => PerformanceTriageRowQuery.IsRankedValue(value);

    private static bool IsRankedField(string field)
        => PerformanceTriageRowQuery.IsRankedKey(field);

    internal static bool IsNumericField(string field)
        => PerformanceTriageRowQuery.IsNumericKey(field);

    internal static bool TryBindPredicateOperator<TRow>(
        RowQueryKey<TRow> field,
        RowPredicateOperator syntax,
        out RowQueryOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!RowPredicateSyntaxParser.TryRowOperator(
                syntax,
                out @operator))
            return false;
        return field.Operators.Contains(@operator);
    }

    private static string FormatComparisons(
        IReadOnlyList<RowQueryOperator> operators)
    {
        string[] comparisons =
        [
            .. operators.Select(
                RowPredicateSyntaxParser.Comparison),
        ];
        return comparisons.Length switch
        {
            0 => throw new InvalidOperationException(
                "A filterable row-query field declares no predicate operators."),
            1 => comparisons[0],
            2 => $"{comparisons[0]} and {comparisons[1]}",
            _ => $"{string.Join(", ", comparisons[..^1])}, "
                + $"and {comparisons[^1]}",
        };
    }

    static string? NormalizeField(string field, IReadOnlyList<string> knownFields)
    {
        var normalized = NormalizeName(field);
        foreach (var known in knownFields)
            if (NormalizeName(known).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return known;
        return null;
    }

    static string NormalizeName(string value)
        => RowPredicateSyntaxParser.NormalizeFieldName(value);

    static OptionError UnknownFieldError(string field, string kind, IReadOnlyList<string> knownFields)
    {
        var suggestion = knownFields
            .OrderBy(candidate => EditDistance(NormalizeName(field).ToLowerInvariant(), NormalizeName(candidate).ToLowerInvariant()))
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return suggestion is null
            ? new OptionError($"Field '{Contain(field)}' is not {kind} in section 'Performance Triage'.")
            // The suggestion travels as a detail rather than as a newline
            // inside the message: the writer indents each detail line itself,
            // so this structure cannot be confused with one injected through
            // the untrusted field name (issue #3319).
            : new OptionError(
                $"Field '{Contain(field)}' is not {kind} in section 'Performance Triage'.",
                ["Did you mean:", $"  {Contain(suggestion)}"]);
    }

    static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (int j = 0; j <= right.Length; j++)
            previous[j] = j;
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
