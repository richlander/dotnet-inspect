using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.AnalysisHarness;

public sealed record AllocationAnnotationRow(
    string Id,
    int Offset,
    string Conditionality,
    string? Detail);

public sealed record AllocationAnnotationParityResult(
    string Category,
    IReadOnlyList<AllocationAnnotationRow> Missing,
    IReadOnlyList<AllocationAnnotationRow> Unexpected)
{
    public bool Passed => Missing.Count == 0 && Unexpected.Count == 0;
}

public static class AllocationAnnotationParity
{
    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AllocationAnnotationParityResult CompareJson(string expectedJson, string actualJson, string category = "Allocation")
        => Compare(Parse(expectedJson, category), Parse(actualJson, category), category);

    public static AllocationAnnotationParityResult Compare(
        IReadOnlyList<AllocationAnnotationRow> expected,
        IReadOnlyList<AllocationAnnotationRow> actual,
        string category = "Allocation")
    {
        var expectedCounts = CountRows(expected);
        var actualCounts = CountRows(actual);

        var missing = new List<AllocationAnnotationRow>();
        var unexpected = new List<AllocationAnnotationRow>();

        foreach (var (row, count) in expectedCounts)
        {
            int actualCount = actualCounts.GetValueOrDefault(row);
            for (int i = actualCount; i < count; i++)
                missing.Add(row);
        }

        foreach (var (row, count) in actualCounts)
        {
            int expectedCount = expectedCounts.GetValueOrDefault(row);
            for (int i = expectedCount; i < count; i++)
                unexpected.Add(row);
        }

        return new AllocationAnnotationParityResult(category, Sort(missing), Sort(unexpected));
    }

    public static IReadOnlyList<AllocationAnnotationRow> Parse(string annotationJson, string categoryFilter = "Allocation")
    {
        var rows = new List<AllocationAnnotationRow>();
        using var doc = JsonDocument.Parse(annotationJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Annotation JSON must be an array.");

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("category", out var category)
                || category.ValueKind != JsonValueKind.String
                || !category.ValueEquals(categoryFilter))
            {
                continue;
            }

            string id = RequiredString(element, "id");
            string conditionality = RequiredString(element, "conditionality");
            int offset = element.TryGetProperty("offset", out var offsetElement) && offsetElement.ValueKind == JsonValueKind.Number
                ? offsetElement.GetInt32()
                : -1;
            string? detail = element.TryGetProperty("detail", out var detailElement)
                && detailElement.ValueKind == JsonValueKind.String
                ? detailElement.GetString()
                : null;

            rows.Add(new AllocationAnnotationRow(id, offset, conditionality, detail));
        }

        return Sort(rows);
    }

    public static string Format(AllocationAnnotationParityResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{result.Category.ToUpperInvariant()} ANNOTATION PARITY: {(result.Passed ? "PASS" : "FAIL")} ({result.Missing.Count} missing, {result.Unexpected.Count} unexpected)");
        if (result.Missing.Count > 0)
        {
            sb.AppendLine("  missing:");
            foreach (var row in result.Missing)
                sb.AppendLine($"    {Describe(row)}");
        }

        if (result.Unexpected.Count > 0)
        {
            sb.AppendLine("  unexpected:");
            foreach (var row in result.Unexpected)
                sb.AppendLine($"    {Describe(row)}");
        }

        return sb.ToString();
    }

    public static string ToJson(AllocationAnnotationParityResult result)
        => JsonSerializer.Serialize(result, s_json);

    static Dictionary<AllocationAnnotationRow, int> CountRows(IReadOnlyList<AllocationAnnotationRow> rows)
    {
        var counts = new Dictionary<AllocationAnnotationRow, int>();
        foreach (var row in rows)
            counts[row] = counts.GetValueOrDefault(row) + 1;
        return counts;
    }

    static IReadOnlyList<AllocationAnnotationRow> Sort(IEnumerable<AllocationAnnotationRow> rows)
        => [.. rows
            .OrderBy(row => row.Offset)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ThenBy(row => row.Conditionality, StringComparer.Ordinal)
            .ThenBy(row => row.Detail, StringComparer.Ordinal)];

    static string Describe(AllocationAnnotationRow row)
        => $"{row.Id} {Offset(row.Offset)} {row.Conditionality} {row.Detail ?? ""}".TrimEnd();

    static string Offset(int offset) => offset < 0 ? "" : $"IL_{offset:X4}";

    static string RequiredString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : throw new InvalidOperationException($"Annotation row missing string property '{property}'.");
}
