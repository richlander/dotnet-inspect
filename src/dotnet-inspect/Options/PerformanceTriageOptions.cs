namespace DotnetInspector.Options;

/// <summary>
/// Row predicates for the Performance Triage section.
/// </summary>
public sealed record PerformanceTriageOptions
{
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
        "RootReach",
        "Shape",
        "Evidence",
        "Fix",
        "Confidence",
        "Loop",
        "Allocation",
        "Path",
        "PathConfidence",
        "IL",
    ];

    public static readonly string[] SortableFields =
    [
        "Triage",
        "RootReach",
        "Confidence",
        "Loop",
        "Member",
        "Shape",
        "IL",
        "Allocation",
        "Path",
        "PathConfidence",
    ];

    public static readonly string[] KnownShapes =
    [
        "allocation-hotspot",
        "async-state-machine",
        "box-value-type",
        "capturing-delegate",
        "enumerator-allocation",
        "instance-method-group-delegate",
        "linq-scan-in-loop",
        "materialize-in-loop",
        "scan-method-in-loop-call",
        "small-array",
        "span-to-array-copy",
        "stackalloc-candidate",
        "string-build-in-loop",
        "temporary-byte-array-copy",
    ];

    public bool LoopOnly { get; init; }
    public string? MinConfidence { get; init; }
    public string[] Shapes { get; init; } = [];
    public int? Top { get; init; }
    public string[] Where { get; init; } = [];
    public string? OrderBy { get; init; }

    public bool HasFilters =>
        LoopOnly
        || !string.IsNullOrWhiteSpace(MinConfidence)
        || Shapes.Length > 0
        || Top.HasValue
        || Where.Length > 0
        || !string.IsNullOrWhiteSpace(OrderBy);

    public bool TryGetPredicates(out RowPredicate[] predicates, out string error)
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

    public bool TryGetOrderTerms(out OrderTerm[] orderTerms, out string error)
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
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is < 1 or > 2)
            {
                orderTerms = [];
                error = $"Error: Invalid --order-by term '{raw}'. Use forms like 'RootReach desc' or 'Confidence desc,RootReach desc'.";
                return false;
            }

            var field = NormalizeField(parts[0], SortableFields);
            if (field is null)
            {
                orderTerms = [];
                error = UnknownFieldError(parts[0], "sortable", SortableFields);
                return false;
            }

            bool descending = false;
            if (parts.Length == 2)
            {
                if (parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase)
                    || parts[1].Equals("descending", StringComparison.OrdinalIgnoreCase))
                {
                    descending = true;
                }
                else if (parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || parts[1].Equals("ascending", StringComparison.OrdinalIgnoreCase))
                {
                    descending = false;
                }
                else
                {
                    orderTerms = [];
                    error = $"Error: Invalid --order-by direction '{parts[1]}'. Valid directions: asc, desc.";
                    return false;
                }
            }

            terms.Add(new OrderTerm(field, descending));
        }

        orderTerms = [.. terms];
        if (orderTerms.Length > 1 && orderTerms.Any(term => term.Field == "Triage"))
        {
            error = "Error: Triage is a composite order and must be used alone, e.g. --order-by \"Triage desc\".";
            return false;
        }
        error = "";
        return true;
    }

    public static bool TryValidateShapes(PerformanceTriageOptions options, out string error)
    {
        var known = KnownShapes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = options.Shapes.Where(shape => !known.Contains(shape)).ToArray();
        if (invalid.Length == 0)
        {
            error = "";
            return true;
        }

        var quotedInvalid = string.Join(", ", invalid.Select(shape => $"'{shape}'"));
        error = $"Error: Unknown Performance Triage shape{(invalid.Length == 1 ? "" : "s")} {quotedInvalid}. Valid shapes: {string.Join(", ", KnownShapes)}.";
        return false;
    }

    public static bool TryValidate(PerformanceTriageOptions options, out string error)
    {
        if (!TryValidateShapes(options, out error))
            return false;
        if (!options.TryGetPredicates(out _, out error))
            return false;
        if (!options.TryGetOrderTerms(out _, out error))
            return false;
        return true;
    }

    static bool TryParsePredicate(string expression, out RowPredicate predicate, out string error)
    {
        predicate = default!;
        expression = expression.Trim();
        if (expression.Length == 0)
        {
            error = "Error: Empty --where predicate.";
            return false;
        }

        foreach (var (token, op) in new[]
        {
            (">=", RowOperator.GreaterOrEqual),
            ("<=", RowOperator.LessOrEqual),
            ("!=", RowOperator.NotEquals),
            ("=", RowOperator.Equals),
        })
        {
            int index = expression.IndexOf(token, StringComparison.Ordinal);
            if (index <= 0)
                continue;

            var field = NormalizeField(expression[..index].Trim(), FilterableFields);
            if (field is null)
            {
                error = UnknownFieldError(expression[..index].Trim(), "filterable", FilterableFields);
                return false;
            }

            var value = expression[(index + token.Length)..].Trim();
            if (value.Length == 0)
            {
                error = $"Error: Missing value in --where predicate '{expression}'.";
                return false;
            }

            if (op is RowOperator.GreaterOrEqual or RowOperator.LessOrEqual
                && field is not ("RootReach" or "Confidence"))
            {
                error = $"Error: Field '{field}' supports only = and != predicates.";
                return false;
            }

            predicate = new RowPredicate(field, op, value);
            error = "";
            return true;
        }

        error = $"Error: Invalid --where predicate '{expression}'. Use forms like 'Field=value', 'Field!=value', 'RootReach>=10', or 'Confidence>=medium'.";
        return false;
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
        => value.Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);

    static string UnknownFieldError(string field, string kind, IReadOnlyList<string> knownFields)
    {
        var suggestion = knownFields
            .OrderBy(candidate => EditDistance(NormalizeName(field).ToLowerInvariant(), NormalizeName(candidate).ToLowerInvariant()))
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return suggestion is null
            ? $"Error: Field '{field}' is not {kind} in section 'Performance Triage'."
            : $"Error: Field '{field}' is not {kind} in section 'Performance Triage'.{Environment.NewLine}{Environment.NewLine}Did you mean:{Environment.NewLine}  {suggestion}";
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
