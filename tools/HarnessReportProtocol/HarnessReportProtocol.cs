using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.HarnessReports;

public interface IDecompilerHarnessReport
{
    HarnessReportDescriptor Descriptor { get; }
    HarnessRunDisposition Disposition { get; }
    IReadOnlyList<HarnessBlocker> Blockers { get; }
    IReadOnlyList<HarnessArtifact> Artifacts { get; }
    HarnessComparisonProjection Comparison { get; }
}

public sealed record HarnessReportDescriptor
{
    public HarnessReportDescriptor(string id, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        Id = id;
        SchemaVersion = schemaVersion;
    }

    public string Id { get; }
    public int SchemaVersion { get; }
}

public enum HarnessRunDisposition
{
    Completed,
    Partial,
    Blocked,
    Failed,
}

public sealed record HarnessBlocker(string Code, string Detail);

public sealed record HarnessArtifact(string Kind, string Path);

public enum MetricGoal
{
    Higher,
    Lower,
    Hold,
    Context,
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

public sealed record HarnessComparisonProjection(
    string Description,
    string PopulationKey,
    IReadOnlyList<ComparableMetric> Metrics,
    ResidueEvidence? Residue = null);

public sealed record StoredHarnessReport(
    HarnessReportDescriptor Descriptor,
    HarnessRunDisposition Disposition,
    IReadOnlyList<HarnessBlocker> Blockers,
    IReadOnlyList<HarnessArtifact> Artifacts,
    HarnessComparisonProjection Comparison);

public sealed record StructuredHarnessReport(
    int SchemaVersion,
    string Kind,
    string Description,
    string PopulationKey,
    IReadOnlyList<ComparableMetric> Metrics,
    ResidueEvidence? Residue,
    HarnessRunDisposition Disposition = HarnessRunDisposition.Completed,
    IReadOnlyList<HarnessBlocker>? Blockers = null,
    IReadOnlyList<HarnessArtifact>? Artifacts = null);

public sealed record DecompilerHarnessReport<T> : IDecompilerHarnessReport
    where T : notnull
{
    public DecompilerHarnessReport(
        HarnessReportDescriptor descriptor,
        T payload,
        HarnessComparisonProjection comparison,
        HarnessRunDisposition disposition = HarnessRunDisposition.Completed,
        IReadOnlyList<HarnessBlocker>? blockers = null,
        IReadOnlyList<HarnessArtifact>? artifacts = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));
        Disposition = disposition;
        Blockers = blockers ?? [];
        Artifacts = artifacts ?? [];

        if (disposition == HarnessRunDisposition.Completed && Blockers.Count != 0)
            throw new ArgumentException("A completed harness report cannot carry blockers.", nameof(blockers));
    }

    public HarnessReportDescriptor Descriptor { get; }
    public T Payload { get; }
    public HarnessComparisonProjection Comparison { get; }
    public HarnessRunDisposition Disposition { get; }
    public IReadOnlyList<HarnessBlocker> Blockers { get; }
    public IReadOnlyList<HarnessArtifact> Artifacts { get; }

    public StoredHarnessReport ToStoredReport()
        => new(Descriptor, Disposition, Blockers, Artifacts, Comparison);
}

public static class HarnessReportStorage
{
    public static void Write(string path, IDecompilerHarnessReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(
            new StoredHarnessReport(
                report.Descriptor,
                report.Disposition,
                report.Blockers,
                report.Artifacts,
                report.Comparison),
            JsonOptions(writeIndented: true)));
    }

    public static StoredHarnessReport Read(JsonElement root)
        => JsonSerializer.Deserialize<StoredHarnessReport>(root.GetRawText(), JsonOptions(writeIndented: false))
            ?? throw new InvalidOperationException("The stored harness report was empty.");

    public static JsonSerializerOptions JsonOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = writeIndented,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public static class HarnessPopulationKey
{
    public static string Create(string scope, IEnumerable<string> identities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(identities);
        string value = string.Join("\n", identities.Order(StringComparer.Ordinal));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
        return $"{scope}:{hash}";
    }
}
