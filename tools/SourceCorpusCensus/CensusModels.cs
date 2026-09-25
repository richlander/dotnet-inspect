using System.Text.Json.Serialization;

namespace DotnetInspector.SourceCorpusCensus;

sealed record MetricSummary(
    int Count,
    long Sum,
    int P50,
    int P75,
    int P90,
    int P95,
    int P99,
    int Max);

sealed record ThresholdCount(int Threshold, int Count);

sealed record SegmentCandidate(
    int MaximumLines,
    int MaximumJsonUtf8,
    int MaximumUtf16,
    int TotalSegments,
    int DocumentsContinued,
    MetricSummary SegmentsPerDocument,
    int OversizeLines,
    int DocumentsWithOversizeLines);

sealed record DocumentFailure(string Url, string Kind, string Error);

sealed record DocumentExample(
    string Url,
    int RawBytes,
    int LineCount,
    int MaximumLineUtf16,
    int MaximumLineJsonUtf8);

sealed record DocumentCorpusReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int CorpusRows,
    int UniqueDocuments,
    int VerifiedDocuments,
    int ExactChecksumDocuments,
    int LineEndingNormalizedDocuments,
    int TotalLines,
    DocumentFailure[] Failures,
    MetricSummary RawDocumentBytes,
    MetricSummary DecodedDocumentUtf16,
    MetricSummary LinesPerDocument,
    MetricSummary MaximumLineUtf16PerDocument,
    MetricSummary LineContentUtf16,
    MetricSummary LineContentUtf8,
    MetricSummary LineRowJsonUtf8,
    ThresholdCount[] LineUtf16Thresholds,
    ThresholdCount[] LineJsonUtf8Thresholds,
    SegmentCandidate[] SegmentCandidates,
    DocumentExample[] LargestDocuments,
    DocumentExample[] LongestLineDocuments);

sealed record AssemblyCensus(
    string Assembly,
    string Outcome,
    string? Detail,
    bool HasPdb,
    bool HasSourceLink,
    int TotalTypes,
    int RejectedTypeNames,
    int MappedTypes,
    int CorrelatedTypes,
    int InferredTypes);

sealed record TypeMappingExample(
    string Assembly,
    string Type,
    int DocumentCount,
    string[] Documents);

sealed record TypeMappingCorpusReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int AssembliesSelected,
    int AssembliesMeasured,
    int AssembliesWithPdb,
    int AssembliesWithSourceLink,
    int AssembliesFailed,
    int TypeDefinitions,
    int RejectedTypeNames,
    int MappedTypes,
    int CorrelatedMappedTypes,
    int InferredMappedTypes,
    int CorrelatedMultiDocumentTypes,
    MetricSummary CorrelatedDocumentsPerType,
    MetricSummary InferredDocumentsPerType,
    int SourceNamedCorrelatedTypes,
    int SourceNamedCorrelatedMultiDocumentTypes,
    MetricSummary SourceNamedCorrelatedDocumentsPerType,
    int GeneratedCorrelatedTypes,
    int GeneratedCorrelatedMultiDocumentTypes,
    MetricSummary GeneratedCorrelatedDocumentsPerType,
    TypeMappingExample[] TopSourceNamedMappings,
    AssemblyCensus[] Assemblies);

static class Statistics
{
    public static MetricSummary Metric(IEnumerable<int> values)
    {
        int[] sorted = values.Order().ToArray();
        return sorted.Length == 0
            ? new(0, 0, 0, 0, 0, 0, 0, 0)
            : new(
                sorted.Length,
                sorted.Sum(static value => (long)value),
                Percentile(sorted, 0.50),
                Percentile(sorted, 0.75),
                Percentile(sorted, 0.90),
                Percentile(sorted, 0.95),
                Percentile(sorted, 0.99),
                sorted[^1]);
    }

    public static int Percentile(int[] sorted, double percentile)
    {
        int index = Math.Max(
            0,
            (int)Math.Ceiling(percentile * sorted.Length) - 1);
        return sorted[index];
    }
}

[JsonSerializable(typeof(CorpusRow))]
[JsonSerializable(typeof(SweepManifest))]
[JsonSerializable(typeof(DocumentCorpusReport))]
[JsonSerializable(typeof(TypeMappingCorpusReport))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
sealed partial class CensusJsonContext : JsonSerializerContext;
