using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.HarnessReportDiff;

public enum MetricGoal
{
    Higher,
    Lower,
    Hold,
    Context,
}

public enum MetricVerdict
{
    Improved,
    Neutral,
    Regressed,
    Incomparable,
}

public sealed record MetricValue(long Count, long? Total = null)
{
    public string Display => Total is > 0
        ? $"{Count.ToString("N0", CultureInfo.InvariantCulture)} ({100.0 * Count / Total.Value:0.00}%)"
        : Count.ToString("N0", CultureInfo.InvariantCulture);
}

public sealed record ComparableMetric(
    string Id,
    string Label,
    MetricGoal Goal,
    MetricValue Value,
    string PopulationKey);

public sealed record ResidueEvidence(long Count, bool MeasurementComplete = true);

public sealed record StructuredHarnessReport(
    int SchemaVersion,
    string Kind,
    string Description,
    string PopulationKey,
    IReadOnlyList<ComparableMetric> Metrics,
    ResidueEvidence? Residue);

public sealed record MetricComparison(
    string Id,
    string Label,
    MetricGoal Goal,
    MetricValue Before,
    MetricValue After,
    MetricVerdict Verdict,
    string Delta);

public sealed record FullyRaisedComparison(
    string Before,
    string After,
    string Basis = "zero decompiler residue (V1 signal)");

public sealed record HarnessComparison(
    StructuredHarnessReport Before,
    StructuredHarnessReport After,
    IReadOnlyList<MetricComparison> Metrics,
    FullyRaisedComparison FullyRaised,
    IReadOnlyList<string> Warnings)
{
    public bool HasRegressions => Metrics.Any(metric => metric.Verdict == MetricVerdict.Regressed);
}

public static class HarnessReportComparer
{
    public static HarnessComparison Compare(StructuredHarnessReport before, StructuredHarnessReport after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.SchemaVersion != after.SchemaVersion)
            throw new InvalidOperationException($"Report schema differs ({before.SchemaVersion} vs {after.SchemaVersion}).");
        if (!string.Equals(before.Kind, after.Kind, StringComparison.Ordinal))
            throw new InvalidOperationException($"Report kind differs ('{before.Kind}' vs '{after.Kind}').");

        var warnings = new List<string>();
        if (!string.Equals(before.PopulationKey, after.PopulationKey, StringComparison.Ordinal))
            warnings.Add("The aggregate population differs; population-sensitive rows are marked incomparable.");

        var beforeById = before.Metrics.ToDictionary(metric => metric.Id, StringComparer.Ordinal);
        var afterById = after.Metrics.ToDictionary(metric => metric.Id, StringComparer.Ordinal);
        var comparisons = new List<MetricComparison>();
        foreach (string id in beforeById.Keys.Intersect(afterById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var left = beforeById[id];
            var right = afterById[id];
            if (left.Goal != right.Goal)
                throw new InvalidOperationException($"Metric '{id}' changed goal ({left.Goal} vs {right.Goal}).");

            bool comparable = string.Equals(left.PopulationKey, right.PopulationKey, StringComparison.Ordinal);
            long delta = right.Value.Count - left.Value.Count;
            var verdict = comparable ? Verdict(left.Goal, delta) : MetricVerdict.Incomparable;
            comparisons.Add(new MetricComparison(
                id,
                right.Label,
                right.Goal,
                left.Value,
                right.Value,
                verdict,
                comparable ? Delta(left.Value, right.Value) : "n/a"));
        }

        foreach (string id in beforeById.Keys.Except(afterById.Keys, StringComparer.Ordinal))
            warnings.Add($"Metric '{id}' is missing from the after report.");
        foreach (string id in afterById.Keys.Except(beforeById.Keys, StringComparer.Ordinal))
            warnings.Add($"Metric '{id}' is missing from the before report.");

        return new HarnessComparison(
            before,
            after,
            comparisons,
            new FullyRaisedComparison(FullyRaisedText(before.Residue), FullyRaisedText(after.Residue)),
            warnings);
    }

    static MetricVerdict Verdict(MetricGoal goal, long delta) => goal switch
    {
        MetricGoal.Higher when delta > 0 => MetricVerdict.Improved,
        MetricGoal.Higher when delta < 0 => MetricVerdict.Regressed,
        MetricGoal.Lower when delta < 0 => MetricVerdict.Improved,
        MetricGoal.Lower when delta > 0 => MetricVerdict.Regressed,
        MetricGoal.Hold when delta != 0 => MetricVerdict.Regressed,
        MetricGoal.Context when delta != 0 => MetricVerdict.Incomparable,
        _ => MetricVerdict.Neutral,
    };

    static string FullyRaisedText(ResidueEvidence? residue)
    {
        if (residue is not { MeasurementComplete: true })
            return "Not established — residue measurement unavailable";
        return residue.Count == 0
            ? "Yes — zero residue"
            : $"No — {residue.Count.ToString("N0", CultureInfo.InvariantCulture)} residual method(s)";
    }

    static string Signed(long value)
        => value == 0 ? "0" : value.ToString("+0;-0", CultureInfo.InvariantCulture);

    static string Delta(MetricValue before, MetricValue after)
    {
        string count = Signed(after.Count - before.Count);
        if (before.Total is not > 0 || after.Total is not > 0)
            return count;
        double percentagePoints = 100.0 * after.Count / after.Total.Value
            - 100.0 * before.Count / before.Total.Value;
        string rate = percentagePoints == 0
            ? "0 pp"
            : percentagePoints.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " pp";
        return $"{count} / {rate}";
    }
}

public static class HarnessReportReader
{
    public static StructuredHarnessReport Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return root.TryGetProperty("kind", out _)
            ? ReadStructured(root)
            : ReadCorpusSnapshot(root);
    }

    static StructuredHarnessReport ReadStructured(JsonElement root)
        => JsonSerializer.Deserialize<StructuredHarnessReport>(root.GetRawText(), JsonOptions())
            ?? throw new InvalidOperationException("The structured harness report was empty.");

    static StructuredHarnessReport ReadCorpusSnapshot(JsonElement root)
    {
        _ = RequiredInt(root, "schemaVersion");
        string description = RequiredString(root, "description");
        string profile = root.TryGetProperty("profile", out var profileNode) ? profileNode.GetString() ?? "real-world" : "real-world";
        string oracle = root.TryGetProperty("fidelityOracle", out var oracleNode) ? oracleNode.GetString() ?? "compile-back" : "compile-back";
        int? methodCap = root.TryGetProperty("methodCap", out var capNode) && capNode.ValueKind != JsonValueKind.Null ? capNode.GetInt32() : null;
        var metrics = root.GetProperty("metrics");
        long total = RequiredLong(metrics, "totalMethods");
        long fullyRaised = RequiredLong(metrics, "fullyRaisedMethods");
        long semanticChecked = RequiredLong(metrics, "semanticCheckedMethods");
        var fidelity = metrics.GetProperty("fidelity");
        long fidelityChecked = RequiredLong(fidelity, "checkedMethods");

        string aggregatePopulation = $"{profile}|cap={methodCap?.ToString(CultureInfo.InvariantCulture) ?? "all"}|{AssemblyPopulation(root)}";
        string validityPopulation = SamplePopulation(root, "validity", value => value != "not-sampled");
        string fidelityPopulation = SamplePopulation(root, "fidelityCheck", value => value != "not-sampled");

        var rows = new List<ComparableMetric>
        {
            Metric("fully-raised", "Fully raised", MetricGoal.Higher, fullyRaised, total, aggregatePopulation),
            Metric("lowering-residue", "Detected lowering residue", MetricGoal.Lower, total - fullyRaised, total, aggregatePopulation),
            Metric("conditional-residual", "Conditional-branch residual", MetricGoal.Lower, RequiredLong(metrics, "conditionalBranchMethods"), total, aggregatePopulation),
            Metric("forward-merge-stops", "Forward-merge stops", MetricGoal.Lower, RequiredLong(metrics, "forwardMergeStoppedContainers"), total, aggregatePopulation),
            Metric("full-malformed", "Full malformed", MetricGoal.Lower, RequiredLong(metrics, "fullMalformedMethods"), semanticChecked, validityPopulation),
            Metric("semantic-defects", "Semantic defects", MetricGoal.Lower, RequiredLong(metrics, "semanticDefectMethods"), semanticChecked, validityPopulation),
            Metric("fidelity-exact", "Fidelity exact", MetricGoal.Higher, RequiredLong(fidelity, "exactMethods"), fidelityChecked, fidelityPopulation),
            Metric("fidelity-opcode-diff", "Fidelity opcode diffs", MetricGoal.Lower, RequiredLong(fidelity, "opcodeDiffMethods"), fidelityChecked, fidelityPopulation),
            Metric("fidelity-operand-diff", "Fidelity operand diffs", MetricGoal.Lower, OptionalLong(fidelity, "operandDiffMethods"), fidelityChecked, fidelityPopulation),
            Metric("fidelity-recompile-fail", "Fidelity RecompileFail", MetricGoal.Lower, RequiredLong(fidelity, "recompileFailMethods"), fidelityChecked, fidelityPopulation),
            Metric("fidelity-context-fail", "Fidelity ContextFail", MetricGoal.Lower, RequiredLong(fidelity, "contextFailMethods"), fidelityChecked, fidelityPopulation),
            Metric("pass-bugs", "Pass bugs", MetricGoal.Lower, RequiredLong(metrics, "passBugs"), total, aggregatePopulation),
        };

        if (fidelity.TryGetProperty("returnToSenderParity", out var parity) && parity.ValueKind == JsonValueKind.Object)
        {
            rows.Add(Metric("rts-parity-worse", "RTS parity worse", MetricGoal.Lower, RequiredLong(parity, "worseMethods"), fidelityChecked, fidelityPopulation));
        }

        return new StructuredHarnessReport(
            1,
            $"decompiler-corpus.{profile}.{oracle}",
            description,
            aggregatePopulation,
            rows,
            new ResidueEvidence(total - fullyRaised));
    }

    static ComparableMetric Metric(string id, string label, MetricGoal goal, long count, long total, string population)
        => new(id, label, goal, new MetricValue(count, total), population);

    static string AssemblyPopulation(JsonElement root)
    {
        var values = root.GetProperty("assemblies").EnumerateArray()
            .Select(row => $"{RequiredString(row, "assembly")}:{RequiredLong(row, "totalMethods")}")
            .Order(StringComparer.Ordinal);
        return Hash(string.Join("\n", values));
    }

    static string SamplePopulation(JsonElement root, string property, Func<string, bool> include)
    {
        if (!root.TryGetProperty("methods", out var methods) || methods.ValueKind != JsonValueKind.Array)
            return "sample-unavailable";
        var values = methods.EnumerateArray()
            .Where(method => method.TryGetProperty(property, out var value) && include(value.GetString() ?? ""))
            .Select(method => method.TryGetProperty("displayMethod", out var display) ? display.GetString() ?? "" : "")
            .Order(StringComparer.Ordinal);
        return Hash(string.Join("\n", values));
    }

    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    static int RequiredInt(JsonElement value, string property) => value.GetProperty(property).GetInt32();
    static long RequiredLong(JsonElement value, string property) => value.GetProperty(property).GetInt64();
    static long OptionalLong(JsonElement value, string property) => value.TryGetProperty(property, out var node) ? node.GetInt64() : 0;
    static string RequiredString(JsonElement value, string property) => value.GetProperty(property).GetString() ?? throw new InvalidOperationException($"'{property}' was null.");
    static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
