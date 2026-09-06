using System.Collections.Immutable;
using DotnetInspector.Sections;
using ILInspector.CSharp;

namespace DotnetInspector.Options;

/// <summary>
/// Row predicates for the Performance Triage section.
/// </summary>
public sealed record PerformanceTriageOptions
{
    /// <summary>
    /// Contains a fragment of the user's own <c>--order-by</c>/<c>--where</c>
    /// text before it is quoted back in a diagnostic. An agent composes these
    /// option values from names it read out of a package, so the fragment is
    /// untrusted even though the sentence around it is not.
    /// </summary>
    private static string Contain(string? text) => CSharpIdentifier.ContainRenderedText(text ?? string.Empty);

    public enum RowOperator
    {
        Equals,
        NotEquals,
        GreaterOrEqual,
        LessOrEqual,
    }

    public sealed record RowPredicate(string Field, RowOperator Operator, string Value);

    public sealed record OrderTerm(string Field, bool Descending);

    public static PerformanceTriageOptions Default { get; } = new();
    public static readonly string[] FilterableFields =
    [
        "Member",
        "Candidate",
        "Finding",
        "Provenance",
        "RootReach",
        "Shape",
        "Operation",
        "Token",
        "EvidenceMethod",
        "Evidence",
        "Fix",
        "Priority",
        "Confidence",
        "Loop",
        "CallerLoop",
        "CallerLoopDepth",
        "CallerLoopWitness",
        "Allocation",
        "Path",
        "PathConfidence",
        "PostDominance",
        "IL",
        "Weight",
        "DirectSites",
        "OncePaths",
        "ConditionalPaths",
        "RepeatedPaths",
        "UnknownPaths",
        "CachedSites",
        "OpaquePaths",
        "Saturated",
    ];

    public static readonly string[] SortableFields =
    [
        "Triage",
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
        "sync-call-in-async",
        "temporary-byte-array-copy",
    ];

    public static ImmutableArray<SectionQueryFacet> QueryFacets { get; } =
        [.. FilterableFields.Concat(SortableFields).Distinct(StringComparer.Ordinal)
            .Select(CreateQueryFacet)];

    private static SectionQueryFacet CreateQueryFacet(string field)
    {
        bool filterable = FilterableFields.Contains(field);
        bool sortable = SortableFields.Contains(field);
        bool ranked = IsRankedField(field);
        string value = IsNumericField(field) ? "10" : ranked ? "high" : "*";
        return new(
            field,
            [.. filterable ? new[] { "--where" } : [],
                .. sortable ? new[] { "--order-by", "--top" } : []],
            filterable
                ? SupportsOrderedComparison(field) ? ["=", "!=", ">=", "<="] : ["=", "!="]
                : [],
            filterable
                ? IsNumericField(field) ? "integer" : ranked ? "rank" : "text/glob"
                : "order",
            ranked ? ["low", "medium", "high"] : [],
            filterable
                ? $"--where \"{field}{(SupportsOrderedComparison(field) ? ">=" : "=")}{value}\""
                : $"--top 10 --order-by \"{field} desc\"");
    }

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
        if (!options.TryGetPredicates(out _, out error))
            return false;
        if (!options.TryGetOrderTerms(out _, out error))
            return false;
        return true;
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

            var op = syntax.Operator switch
            {
                RowPredicateOperator.Equals => RowOperator.Equals,
                RowPredicateOperator.NotEquals => RowOperator.NotEquals,
                RowPredicateOperator.GreaterOrEqual => RowOperator.GreaterOrEqual,
                RowPredicateOperator.LessOrEqual => RowOperator.LessOrEqual,
                _ => throw new InvalidOperationException(
                    $"Unknown row predicate operator '{syntax.Operator}'."),
            };
            var value = syntax.Value;
            if (op is RowOperator.GreaterOrEqual or RowOperator.LessOrEqual
                && !SupportsOrderedComparison(field))
            {
                error = $"Field '{Contain(field)}' supports only = and != predicates.";
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

            predicate = new RowPredicate(field, op, value);
            error = "";
            return true;
        }

        return false;
    }

    static bool IsKnownConfidence(string value)
        => value.Equals("low", StringComparison.OrdinalIgnoreCase)
           || value.Equals("medium", StringComparison.OrdinalIgnoreCase)
           || value.Equals("high", StringComparison.OrdinalIgnoreCase);

    private static bool IsRankedField(string field)
        => field is "Priority" or "Confidence" or "Weight";

    private static bool SupportsOrderedComparison(string field)
        => IsNumericField(field) || IsRankedField(field);

    internal static bool IsNumericField(string field)
        => field is "RootReach"
            or "CallerLoopDepth"
            or "DirectSites"
            or "OncePaths"
            or "ConditionalPaths"
            or "RepeatedPaths"
            or "UnknownPaths"
            or "CachedSites"
            or "OpaquePaths";

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
