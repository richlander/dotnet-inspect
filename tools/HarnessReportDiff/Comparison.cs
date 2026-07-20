using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspector.HarnessReports;

namespace DotnetInspector.HarnessReportDiff;

public enum MetricVerdict
{
    Improved,
    Neutral,
    Regressed,
    Incomparable,
}

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
    MetricVerdict Verdict,
    string Basis = "zero decompiler residue (V1 signal)");

public sealed record HarnessComparison(
    StructuredHarnessReport Before,
    StructuredHarnessReport After,
    IReadOnlyList<MetricComparison> Metrics,
    FullyRaisedComparison? FullyRaised,
    IReadOnlyList<string> Warnings)
{
    public bool HasRegressions
        => Metrics.Any(metric => metric.Verdict == MetricVerdict.Regressed)
            || FullyRaised?.Verdict == MetricVerdict.Regressed;
}

public static class HarnessReportComparer
{
    public static HarnessComparison Compare(StructuredHarnessReport before, StructuredHarnessReport after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ValidateReport(before, "before");
        ValidateReport(after, "after");
        if (before.SchemaVersion != after.SchemaVersion)
            throw new InvalidOperationException($"Report schema differs ({before.SchemaVersion} vs {after.SchemaVersion}).");
        if (!string.Equals(before.Kind, after.Kind, StringComparison.Ordinal))
            throw new InvalidOperationException($"Report kind differs ('{before.Kind}' vs '{after.Kind}').");

        bool aggregatePopulationKnown = PopulationKnown(before.PopulationKey)
            && PopulationKnown(after.PopulationKey);
        bool aggregateComparable = aggregatePopulationKnown
            && string.Equals(before.PopulationKey, after.PopulationKey, StringComparison.Ordinal);
        bool reportsComplete = before.Disposition == HarnessRunDisposition.Completed
            && after.Disposition == HarnessRunDisposition.Completed;
        var warnings = new List<string>();
        if (!aggregatePopulationKnown)
            warnings.Add("The aggregate population identity is unavailable; population-sensitive rows are marked incomparable.");
        else if (!aggregateComparable)
            warnings.Add("The aggregate population differs; population-sensitive rows are marked incomparable.");
        if (before.Disposition != HarnessRunDisposition.Completed || after.Disposition != HarnessRunDisposition.Completed)
            warnings.Add($"Execution disposition is not completed ({before.Disposition} -> {after.Disposition}).");

        var beforeById = IndexMetrics(before.Metrics, "before");
        var afterById = IndexMetrics(after.Metrics, "after");
        string[] missingAfter = [.. beforeById.Keys.Except(afterById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] missingBefore = [.. afterById.Keys.Except(beforeById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (missingAfter.Length != 0 || missingBefore.Length != 0)
        {
            throw new InvalidOperationException(
                $"Metric set differs; missing after: {ListOrNone(missingAfter)}; missing before: {ListOrNone(missingBefore)}.");
        }

        var comparisons = new List<MetricComparison>();
        foreach (string id in beforeById.Keys.Intersect(afterById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var left = beforeById[id];
            var right = afterById[id];
            if (left.Goal != right.Goal)
                throw new InvalidOperationException($"Metric '{id}' changed goal ({left.Goal} vs {right.Goal}).");

            bool comparable = reportsComplete
                && PopulationKnown(left.PopulationKey)
                && PopulationKnown(right.PopulationKey)
                && string.Equals(left.PopulationKey, right.PopulationKey, StringComparison.Ordinal);
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

        return new HarnessComparison(
            before,
            after,
            comparisons,
            CompareFullyRaised(before.Residue, after.Residue, aggregateComparable && reportsComplete),
            warnings);
    }

    static void ValidateReport(StructuredHarnessReport report, string side)
    {
        if (report.SchemaVersion < 1)
            throw new InvalidOperationException($"The {side} report has an invalid schema version.");
        if (string.IsNullOrWhiteSpace(report.Kind))
            throw new InvalidOperationException($"The {side} report has no kind.");
        if (string.IsNullOrWhiteSpace(report.PopulationKey))
            throw new InvalidOperationException($"The {side} report has no aggregate population identity.");
        if (report.Metrics is null)
            throw new InvalidOperationException($"The {side} report has no metric collection.");

        for (int i = 0; i < report.Metrics.Count; i++)
        {
            var metric = report.Metrics[i];
            if (metric is null)
                throw new InvalidOperationException($"Metric {i} is null in the {side} report.");
            if (string.IsNullOrWhiteSpace(metric.Id))
                throw new InvalidOperationException($"Metric {i} has no ID in the {side} report.");
            if (metric.Value is null)
                throw new InvalidOperationException($"Metric '{metric.Id}' has no value in the {side} report.");
            if (string.IsNullOrWhiteSpace(metric.PopulationKey))
                throw new InvalidOperationException($"Metric '{metric.Id}' has no population identity in the {side} report.");
        }
    }

    static bool PopulationKnown(string populationKey)
        => !populationKey.StartsWith("unavailable:", StringComparison.Ordinal);

    static string ListOrNone(IReadOnlyList<string> values)
        => values.Count == 0 ? "none" : string.Join(", ", values.Select(value => $"'{value}'"));

    static Dictionary<string, ComparableMetric> IndexMetrics(
        IReadOnlyList<ComparableMetric> metrics,
        string side)
    {
        var indexed = new Dictionary<string, ComparableMetric>(StringComparer.Ordinal);
        foreach (var metric in metrics)
        {
            if (!indexed.TryAdd(metric.Id, metric))
                throw new InvalidOperationException($"Metric '{metric.Id}' is duplicated in the {side} report.");
        }
        return indexed;
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

    static FullyRaisedComparison? CompareFullyRaised(
        ResidueEvidence? before,
        ResidueEvidence? after,
        bool populationComparable)
    {
        if (before is null && after is null)
            return null;
        if (!populationComparable)
        {
            return new FullyRaisedComparison(
                "Incomparable",
                "Incomparable",
                MetricVerdict.Incomparable);
        }

        string beforeText = FullyRaisedText(before);
        string afterText = FullyRaisedText(after);
        var verdict = before is { MeasurementComplete: true }
            && after is { MeasurementComplete: true }
            ? Verdict(MetricGoal.Lower, after.Count - before.Count)
            : MetricVerdict.Incomparable;
        return new FullyRaisedComparison(beforeText, afterText, verdict);
    }

    static string FullyRaisedText(ResidueEvidence? residue)
        => residue is not { MeasurementComplete: true }
            ? "Not established — residue measurement unavailable"
            : residue.Count == 0
                ? "Yes — zero residue"
                : $"No — {residue.Count.ToString("N0", CultureInfo.InvariantCulture)} residual method(s)";

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
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.TryGetProperty("descriptor", out _))
                return ReadStored(root);
            if (root.TryGetProperty("kind", out _))
                return ReadStructured(root);
            if (root.TryGetProperty("schemaVersion", out _)
                && root.TryGetProperty("metrics", out _)
                && root.TryGetProperty("assemblies", out _))
            {
                return ReadCorpusSnapshot(root);
            }
            throw new InvalidOperationException("Input is not a stored harness report or a decompiler corpus snapshot.");
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException($"Harness report is missing a required property: {ex.Message}", ex);
        }
    }

    static StructuredHarnessReport ReadStored(JsonElement root)
    {
        var stored = HarnessReportStorage.Read(root);
        if (stored.Descriptor is null)
            throw new InvalidOperationException("The stored harness report has no descriptor.");
        if (stored.Comparison is null)
            throw new InvalidOperationException("The stored harness report has no comparison projection.");
        return new StructuredHarnessReport(
            stored.Descriptor.SchemaVersion,
            stored.Descriptor.Id,
            stored.Comparison.Description,
            stored.Comparison.PopulationKey,
            stored.Comparison.Metrics,
            stored.Comparison.Residue,
            stored.Disposition,
            stored.Blockers,
            stored.Artifacts);
    }

    static StructuredHarnessReport ReadStructured(JsonElement root)
        => JsonSerializer.Deserialize<StructuredHarnessReport>(
            root.GetRawText(),
            HarnessReportStorage.JsonOptions(writeIndented: false))
            ?? throw new InvalidOperationException("The structured harness report was empty.");

    static StructuredHarnessReport ReadCorpusSnapshot(JsonElement root)
    {
        int schemaVersion = RequiredInt(root, "schemaVersion");
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

        string methodPopulation = AggregatePopulation(root);
        string aggregatePopulation = methodPopulation.StartsWith("unavailable:", StringComparison.Ordinal)
            ? $"unavailable:{profile}|cap={methodCap?.ToString(CultureInfo.InvariantCulture) ?? "all"}|{methodPopulation}"
            : $"{profile}|cap={methodCap?.ToString(CultureInfo.InvariantCulture) ?? "all"}|{methodPopulation}";
        string validityPopulation = SamplePopulation(root, "validity", value => value != "not-sampled", aggregatePopulation);
        string fidelityPopulation = SamplePopulation(root, "fidelityCheck", value => value != "not-sampled", aggregatePopulation);

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
            schemaVersion,
            $"decompiler-corpus.{profile}.{oracle}",
            description,
            aggregatePopulation,
            rows,
            new ResidueEvidence(total - fullyRaised));
    }

    static ComparableMetric Metric(string id, string label, MetricGoal goal, long count, long total, string population)
        => new(id, label, goal, new MetricValue(count, total), population);

    static string AggregatePopulation(JsonElement root)
    {
        if (root.TryGetProperty("methods", out var methods) && methods.ValueKind == JsonValueKind.Array)
        {
            var identities = methods.EnumerateArray()
                .Select(method => $"{RequiredString(method, "assembly")}|{RequiredString(method, "displayMethod")}")
                .Order(StringComparer.Ordinal);
            return Hash(string.Join("\n", identities));
        }

        var values = root.GetProperty("assemblies").EnumerateArray()
            .Select(row => $"{RequiredString(row, "assembly")}:{RequiredLong(row, "totalMethods")}")
            .Order(StringComparer.Ordinal);
        return $"unavailable:{Hash(string.Join("\n", values))}";
    }

    static string SamplePopulation(
        JsonElement root,
        string property,
        Func<string, bool> include,
        string aggregatePopulation)
    {
        if (!root.TryGetProperty("methods", out var methods) || methods.ValueKind != JsonValueKind.Array)
            return $"unavailable:{aggregatePopulation}";
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
}
