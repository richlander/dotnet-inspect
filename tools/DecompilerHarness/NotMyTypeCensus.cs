using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;

namespace ILInspector.DecompilerHarness;

// "Not My Type" (NMT) type-shape equivalence census: does the product type-shape oracle
// (MetadataSource.ClassifyType) agree with the legacy base-name/interface classifier that
// the harness sites (ReturnToSender.IsSupported*, FidelityCheck.ShapeOf, AnnotationCheck)
// each re-derive?
//   Axis A (agreement): over same-assembly TypeDefinitions the oracle and the legacy
//     classifier read the same local base type, so they must agree. Divergence is a real
//     discrepancy to fix BEFORE a producer is retired (the Return Address landing pattern).
//   Axis B (recovery): over cross-assembly TypeReferences the single-assembly legacy walk
//     can only answer Unknown (it cannot open the other PE — the documented AnnotationCheck
//     value-type gap). "Recovered" counts the refs the corpus-aware oracle resolves to a
//     definite kind, quantifying the gap the oracle closes.
// A thin observer: it embeds no shape knowledge beyond the legacy base-name rule it exists
// to retire, and compares product-produced classifications.

internal enum NmtCensusFormat { Markdown, Tsv, Jsonl }

internal sealed record NmtCensusExample(string Type, string Oracle, string Legacy);

internal sealed record NmtCensusAssembly(
    string Name,
    bool Opened,
    int Definitions,
    int Agree,
    int Diverge,
    int CrossRefs,
    int Recovered,
    IReadOnlyList<NmtCensusExample> Examples);

internal sealed record NmtCensusReport(IReadOnlyList<NmtCensusAssembly> Assemblies);

// Committed-baseline snapshot for the drift gate (mirrors ReturnAddressCensus): the same-assembly
// agree rate is a floor that must stay at/above baseline; refresh upward when it legitimately improves.
internal sealed record NmtCensusSnapshot(
    int SchemaVersion,
    string Description,
    DateTimeOffset GeneratedUtc,
    NmtCensusTolerances? Tolerances,
    int Definitions,
    int Agree,
    int Diverge,
    int CrossRefs,
    int Recovered,
    int AgreeBasisPoints,
    IReadOnlyList<NmtCensusAssemblySnapshot> Assemblies);

internal sealed record NmtCensusAssemblySnapshot(string Assembly, int Definitions, int Agree, int Diverge, int AgreeBasisPoints);

internal sealed record NmtCensusTolerances(int AgreeRateDropBasisPoints)
{
    // Same-assembly the oracle and legacy classifier read the same base, so the floor is a hard
    // 100%: any drop (>0 bps) is a real regression, not corpus-composition drift.
    public static readonly NmtCensusTolerances Default = new(AgreeRateDropBasisPoints: 0);
}

internal static class NotMyTypeCensus
{
    internal static readonly HarnessReportDescriptor Descriptor = new("census.not-my-type", 1);

    public static int Run(
        IReadOnlyList<string> assemblies,
        int maxExamples,
        NmtCensusFormat format,
        string? emitSnapshot = null,
        string? diffBaseline = null)
    {
        var report = new DecompilerHarnessReport<NmtCensusReport>(Descriptor, Measure(assemblies, maxExamples));
        Console.Write(Format(report, maxExamples, format));

        if (emitSnapshot is null && diffBaseline is null)
            return 0;

        var snapshot = BuildSnapshot(report.Payload);

        if (emitSnapshot is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitSnapshot)) ?? ".");
            File.WriteAllText(emitSnapshot, JsonSerializer.Serialize(snapshot, JsonOptions()));
            Console.Error.WriteLine($"Wrote not-my-type baseline: {emitSnapshot}");
        }

        if (diffBaseline is null)
            return 0;

        var baseline = JsonSerializer.Deserialize<NmtCensusSnapshot>(File.ReadAllText(diffBaseline), JsonOptions())
            ?? throw new InvalidOperationException($"Could not read not-my-type baseline '{diffBaseline}'.");
        var regressions = Compare(baseline, snapshot);
        if (regressions.Count == 0)
        {
            Console.Error.WriteLine($"Not-my-type census matched baseline: {diffBaseline}");
            return 0;
        }

        Console.Error.WriteLine("Not-my-type census regressions:");
        foreach (var regression in regressions)
            Console.Error.WriteLine($"- {regression}");
        return 1;
    }

    static NmtCensusReport Measure(IReadOnlyList<string> assemblies, int maxExamples)
    {
        // One shared corpus context so cross-assembly references (CoreLib, corpus siblings)
        // resolve for the oracle's cross-assembly path.
        using var context = CorpusMetadata.Create(assemblies);
        var rows = new List<NmtCensusAssembly>(assemblies.Count);
        foreach (var path in assemblies)
            rows.Add(MeasureOne(path, context, maxExamples));
        return new NmtCensusReport(rows);
    }

    static NmtCensusAssembly MeasureOne(string path, MetadataContext context, int maxExamples)
    {
        var name = Path.GetFileName(path);
        try
        {
            using var pe = new PEReader(File.OpenRead(path));
            if (!pe.HasMetadata)
                return new NmtCensusAssembly(name, Opened: false, 0, 0, 0, 0, 0, []);
            var reader = pe.GetMetadataReader();
            using var source = MetadataSource.Open(path, context: context);

            int definitions = 0, agree = 0, diverge = 0;
            var examples = new List<NmtCensusExample>();

            // Axis A: same-assembly definitions — oracle must agree with the legacy classifier.
            foreach (var handle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                TypeShapeKind oracle, legacy;
                try
                {
                    oracle = source.ClassifyType((EntityHandle)handle);
                    legacy = LegacyClassify(reader, typeDef);
                }
                catch
                {
                    continue;
                }

                definitions++;
                if (oracle == legacy)
                {
                    agree++;
                }
                else
                {
                    diverge++;
                    if (examples.Count < maxExamples)
                        examples.Add(new NmtCensusExample(reader.GetFullTypeName(typeDef), oracle.ToString(), legacy.ToString()));
                }
            }

            // Axis B: cross-assembly references — the single-assembly legacy walk answers Unknown;
            // count the refs the corpus-aware oracle recovers to a definite kind.
            int crossRefs = 0, recovered = 0;
            foreach (var refHandle in reader.TypeReferences)
            {
                if (reader.GetTypeReference(refHandle).ResolutionScope.Kind is not (HandleKind.AssemblyReference or HandleKind.TypeReference))
                    continue;
                crossRefs++;
                try
                {
                    if (source.ClassifyType((EntityHandle)refHandle) != TypeShapeKind.Unknown)
                        recovered++;
                }
                catch
                {
                    // A malformed ref never crashes the sweep; it simply counts as unrecovered.
                }
            }

            return new NmtCensusAssembly(name, Opened: true, definitions, agree, diverge, crossRefs, recovered, examples);
        }
        catch (Exception)
        {
            return new NmtCensusAssembly(name, Opened: false, 0, 0, 0, 0, 0, []);
        }
    }

    /// <summary>
    /// The legacy base-name/interface classifier that <c>ReturnToSender.IsSupported*</c>,
    /// <c>FidelityCheck.ShapeOf</c>, and <c>AnnotationCheck</c> each re-derive — the producer
    /// this census exists to retire. Same-assembly only: a cross-assembly base (or any TypeSpec
    /// base) reads as <see cref="TypeShapeKind.Class"/> here, exactly the imprecision the oracle removes.
    /// </summary>
    static TypeShapeKind LegacyClassify(MetadataReader reader, TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return TypeShapeKind.Interface;
        if (typeDef.BaseType.IsNil)
            return TypeShapeKind.Class;
        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };
        return baseName switch
        {
            "System.Enum" => TypeShapeKind.Enum,
            "System.ValueType" => TypeShapeKind.Struct,
            "System.MulticastDelegate" or "System.Delegate" => TypeShapeKind.Delegate,
            _ => TypeShapeKind.Class,
        };
    }

    static string Format(DecompilerHarnessReport<NmtCensusReport> report, int maxExamples, NmtCensusFormat format)
    {
        var output = new StringWriter();
        if (format == NmtCensusFormat.Markdown)
        {
            MarkoutSerializer.Serialize(
                BuildMarkdownView(report, maxExamples),
                output,
                new MarkdownFormatter(),
                NmtCensusViewContext.Default,
                new MarkoutWriterOptions());
        }
        else
        {
            MarkoutSerializer.Serialize(
                BuildTableView(report, maxExamples),
                output,
                new TableFormatter(showHeader: true),
                NmtCensusViewContext.Default,
                format == NmtCensusFormat.Tsv
                    ? new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv }
                    : new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl });
        }

        string rendered = output.ToString();
        return format == NmtCensusFormat.Jsonl
            ? string.Join(Environment.NewLine, rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
            : rendered;
    }

    static int TotalDefinitions(NmtCensusReport report) => report.Assemblies.Sum(a => a.Definitions);
    static int TotalAgree(NmtCensusReport report) => report.Assemblies.Sum(a => a.Agree);
    static int TotalDiverge(NmtCensusReport report) => report.Assemblies.Sum(a => a.Diverge);
    static int TotalCrossRefs(NmtCensusReport report) => report.Assemblies.Sum(a => a.CrossRefs);
    static int TotalRecovered(NmtCensusReport report) => report.Assemblies.Sum(a => a.Recovered);

    static string Pct(int n, int total) => total == 0 ? "n/a" : $"{100.0 * n / total:0.00}%";

    static NmtCensusSnapshot BuildSnapshot(NmtCensusReport report)
    {
        int definitions = TotalDefinitions(report), agree = TotalAgree(report), diverge = TotalDiverge(report);
        return new NmtCensusSnapshot(
            SchemaVersion: 1,
            Description: "#2548 Not My Type type-shape equivalence census: same-assembly agreement between "
                + "the product oracle (MetadataSource.ClassifyType) and the legacy base-name classifier the "
                + "harness sites re-derive, plus cross-assembly reference recovery. Same-assembly agreement is a "
                + "hard 100% floor; recovery is informational.",
            GeneratedUtc: DateTimeOffset.UtcNow,
            Tolerances: NmtCensusTolerances.Default,
            Definitions: definitions,
            Agree: agree,
            Diverge: diverge,
            CrossRefs: TotalCrossRefs(report),
            Recovered: TotalRecovered(report),
            AgreeBasisPoints: RateBasisPoints(agree, definitions),
            Assemblies: report.Assemblies
                .Where(a => a.Opened)
                .OrderBy(a => a.Name, StringComparer.Ordinal)
                .Select(a => new NmtCensusAssemblySnapshot(a.Name, a.Definitions, a.Agree, a.Diverge, RateBasisPoints(a.Agree, a.Definitions)))
                .ToList());
    }

    static IReadOnlyList<string> Compare(NmtCensusSnapshot baseline, NmtCensusSnapshot current)
    {
        var regressions = new List<string>();
        int tolerance = baseline.Tolerances?.AgreeRateDropBasisPoints ?? NmtCensusTolerances.Default.AgreeRateDropBasisPoints;

        int drop = baseline.AgreeBasisPoints - current.AgreeBasisPoints;
        if (drop > tolerance)
            regressions.Add(
                $"same-assembly agree rate dropped {FormatBps(drop)} ({FormatBps(baseline.AgreeBasisPoints)} -> "
                + $"{FormatBps(current.AgreeBasisPoints)}), tolerance {FormatBps(tolerance)}");

        return regressions;
    }

    static int RateBasisPoints(int part, int whole)
        => whole <= 0 ? 0 : (int)Math.Round(10_000.0 * part / whole, MidpointRounding.AwayFromZero);

    static string FormatBps(int basisPoints) => $"{basisPoints / 100.0:F2}%";

    static JsonSerializerOptions JsonOptions()
        => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static IReadOnlyList<(string Metric, string Value)> SummaryRows(NmtCensusReport report)
    {
        int definitions = TotalDefinitions(report), agree = TotalAgree(report), crossRefs = TotalCrossRefs(report);
        return new (string, string)[]
        {
            ("assemblies", report.Assemblies.Count.ToString()),
            ("opened", report.Assemblies.Count(a => a.Opened).ToString()),
            ("definitions", definitions.ToString()),
            ("agree", agree.ToString()),
            ("agree rate", Pct(agree, definitions)),
            ("diverge", TotalDiverge(report).ToString()),
            ("cross-assembly refs", crossRefs.ToString()),
            ("recovered", TotalRecovered(report).ToString()),
            ("recovery rate", Pct(TotalRecovered(report), crossRefs)),
        };
    }

    static NmtCensusMarkdownView BuildMarkdownView(DecompilerHarnessReport<NmtCensusReport> envelope, int maxExamples)
        => new()
        {
            Report = [.. DecompilerHarnessReportViews.Metadata(envelope).Select(row => new NmtCensusReportMetadataRow(row.Metric, row.Value))],
            Summary = SummaryRows(envelope.Payload).Select(r => new NmtCensusMetricRow(r.Metric, r.Value)).ToList(),
            PerAssembly = envelope.Payload.Assemblies
                .Where(a => a.Opened)
                .Select(a => new NmtCensusAssemblyRow(a.Name, a.Definitions, a.Agree, a.Diverge, Pct(a.Agree, a.Definitions), a.CrossRefs, a.Recovered))
                .ToList(),
            Divergences = envelope.Payload.Assemblies
                .SelectMany(a => a.Examples)
                .Take(maxExamples)
                .Select(e => new NmtCensusDivergenceRow(e.Type, e.Oracle, e.Legacy))
                .ToList(),
        };

    static NmtCensusTableView BuildTableView(DecompilerHarnessReport<NmtCensusReport> envelope, int maxExamples)
        => new()
        {
            Report = [.. DecompilerHarnessReportViews.Metadata(envelope).Select(row => new NmtCensusSectionReportMetadataRow("Report", row.Metric, row.Value))],
            Summary = SummaryRows(envelope.Payload).Select(r => new NmtCensusSectionMetricRow("Summary", r.Metric, r.Value)).ToList(),
            PerAssembly = envelope.Payload.Assemblies
                .Where(a => a.Opened)
                .Select(a => new NmtCensusSectionAssemblyRow("Per assembly", a.Name, a.Definitions, a.Agree, a.Diverge, Pct(a.Agree, a.Definitions), a.CrossRefs, a.Recovered))
                .ToList(),
            Divergences = envelope.Payload.Assemblies
                .SelectMany(a => a.Examples)
                .Take(maxExamples)
                .Select(e => new NmtCensusSectionDivergenceRow("Divergences", e.Type, e.Oracle, e.Legacy))
                .ToList(),
        };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
internal sealed class NmtCensusMarkdownView
{
    [MarkoutIgnore]
    public string Title => "Not My Type type-shape census";

    [MarkoutSection(Name = "Report")]
    public List<NmtCensusReportMetadataRow>? Report { get; init; }

    [MarkoutSection(Name = "Summary")]
    public List<NmtCensusMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Per assembly", EmptyText = "None")]
    public List<NmtCensusAssemblyRow>? PerAssembly { get; init; }

    [MarkoutSection(Name = "Divergences", EmptyText = "None - the oracle agrees with the legacy classifier on every same-assembly definition.")]
    public List<NmtCensusDivergenceRow>? Divergences { get; init; }
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
internal sealed class NmtCensusTableView
{
    [MarkoutIgnore]
    public string Title => "Not My Type type-shape census";

    [MarkoutSection(Name = "Report")]
    public List<NmtCensusSectionReportMetadataRow>? Report { get; init; }

    [MarkoutSection(Name = "Summary")]
    public List<NmtCensusSectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Per assembly")]
    public List<NmtCensusSectionAssemblyRow>? PerAssembly { get; init; }

    [MarkoutSection(Name = "Divergences")]
    public List<NmtCensusSectionDivergenceRow>? Divergences { get; init; }
}

[MarkoutSerializable]
internal sealed record NmtCensusMetricRow(string Metric, string Value);

[MarkoutSerializable]
internal sealed record NmtCensusReportMetadataRow(string Metric, string Value);

[MarkoutSerializable]
internal sealed record NmtCensusSectionReportMetadataRow(string Section, string Metric, string Value);

[MarkoutSerializable]
internal sealed record NmtCensusSectionMetricRow(string Section, string Metric, string Value);

[MarkoutSerializable]
internal sealed record NmtCensusAssemblyRow(string Assembly, int Definitions, int Agree, int Diverge, [property: MarkoutPropertyName("Agree rate")] string AgreeRate, [property: MarkoutPropertyName("Cross refs")] int CrossRefs, int Recovered);

[MarkoutSerializable]
internal sealed record NmtCensusSectionAssemblyRow(string Section, string Assembly, int Definitions, int Agree, int Diverge, [property: MarkoutPropertyName("Agree rate")] string AgreeRate, [property: MarkoutPropertyName("Cross refs")] int CrossRefs, int Recovered);

[MarkoutSerializable]
internal sealed record NmtCensusDivergenceRow(string Type, string Oracle, string Legacy);

[MarkoutSerializable]
internal sealed record NmtCensusSectionDivergenceRow(string Section, string Type, string Oracle, string Legacy);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(NmtCensusMarkdownView))]
[MarkoutContext(typeof(NmtCensusTableView))]
[MarkoutContext(typeof(NmtCensusReportMetadataRow))]
[MarkoutContext(typeof(NmtCensusSectionReportMetadataRow))]
[MarkoutContext(typeof(NmtCensusMetricRow))]
[MarkoutContext(typeof(NmtCensusSectionMetricRow))]
[MarkoutContext(typeof(NmtCensusAssemblyRow))]
[MarkoutContext(typeof(NmtCensusSectionAssemblyRow))]
[MarkoutContext(typeof(NmtCensusDivergenceRow))]
[MarkoutContext(typeof(NmtCensusSectionDivergenceRow))]
internal sealed partial class NmtCensusViewContext : MarkoutSerializerContext
{
}
