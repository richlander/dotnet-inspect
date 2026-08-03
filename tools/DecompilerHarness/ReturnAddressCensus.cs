using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.HarnessReports;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;

namespace ILInspector.DecompilerHarness;

// Return Address (RA) equivalence census: do the product's two Metadata member-identity
// producers agree per member?
//   A = ApiMemberIdentity.GetMemberAnchor   (ApiSurface path)
//   B = ApiMemberIdentity.CreateMethodAnchor (SRM-direct path, used by CSharpBodyDiff)
// This is a thin observer: it only compares product-produced canonical signatures by
// string equality and reports the agreement rate. It embeds no type/name knowledge —
// the axis breakdown of divergence is a separate analysis (see issue #2440). As the
// identity consolidation lands, the agreement rate should climb toward 100%.

internal enum RaCensusFormat { Markdown, Tsv, Jsonl }

internal sealed record RaCensusExample(string Member, string A, string B);

internal sealed record RaCensusAssembly(
    string Name,
    bool Opened,
    int Matched,
    int Agree,
    int Unmatched,
    IReadOnlyList<RaCensusExample> Examples);

internal sealed record RaCensusReport(IReadOnlyList<RaCensusAssembly> Assemblies);

// Committed-baseline snapshot for the Deep Inspect drift gate. Serialized with camelCase
// (see JsonOptions); the agree rate is stored in basis points to keep the pinned baseline
// integer-stable across runs.
internal sealed record RaCensusSnapshot(
    int SchemaVersion,
    string Description,
    DateTimeOffset GeneratedUtc,
    RaCensusTolerances? Tolerances,
    int Matched,
    int Agree,
    int Unmatched,
    int AgreeBasisPoints,
    IReadOnlyList<RaCensusAssemblySnapshot> Assemblies);

internal sealed record RaCensusAssemblySnapshot(string Assembly, int Matched, int Agree, int Unmatched, int AgreeBasisPoints);

internal sealed record RaCensusTolerances(int AgreeRateDropBasisPoints)
{
    // 50 bps (0.50%) absorbs corpus-composition drift (member counts shift with framework/NuGet
    // version bumps) while still catching real agreement regressions, which move by whole points.
    public static readonly RaCensusTolerances Default = new(AgreeRateDropBasisPoints: 50);
}

internal static class ReturnAddressCensus
{
    internal static readonly HarnessReportDescriptor Descriptor = new("census.return-address-equivalence", 1);

    public static int Run(
        IReadOnlyList<string> assemblies,
        int maxExamples,
        RaCensusFormat format,
        string? emitSnapshot = null,
        string? diffBaseline = null,
        string? emitHarnessReport = null)
    {
        var payload = Measure(assemblies, maxExamples);
        var report = new DecompilerHarnessReport<RaCensusReport>(
            Descriptor,
            payload,
            BuildComparison(payload));
        Console.Write(Format(report, maxExamples, format));

        if (emitHarnessReport is not null)
        {
            HarnessReportStorage.Write(emitHarnessReport, report);
            HarnessLog.Status($"Wrote harness report: {emitHarnessReport}");
        }

        if (emitSnapshot is null && diffBaseline is null)
            return 0;

        var snapshot = BuildSnapshot(report.Payload);

        if (emitSnapshot is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitSnapshot)) ?? ".");
            File.WriteAllText(emitSnapshot, JsonSerializer.Serialize(snapshot, JsonOptions()));
            // Status/gate messages go to stderr so stdout stays pristine for --tsv/--jsonl.
            Console.Error.WriteLine($"Wrote return-address baseline: {emitSnapshot}");
        }

        if (diffBaseline is null)
            return 0;

        var baseline = JsonSerializer.Deserialize<RaCensusSnapshot>(File.ReadAllText(diffBaseline), JsonOptions())
            ?? throw new InvalidOperationException($"Could not read return-address baseline '{diffBaseline}'.");
        var regressions = Compare(baseline, snapshot);
        if (regressions.Count == 0)
        {
            Console.Error.WriteLine($"Return-address census matched baseline: {diffBaseline}");
            return 0;
        }

        Console.Error.WriteLine("Return-address census regressions:");
        foreach (var regression in regressions)
            Console.Error.WriteLine($"- {regression}");
        return 1;
    }

    static RaCensusReport Measure(IReadOnlyList<string> assemblies, int maxExamples)
    {
        var rows = new List<RaCensusAssembly>(assemblies.Count);
        foreach (var path in assemblies)
            rows.Add(MeasureOne(path, maxExamples));
        return new RaCensusReport(rows);
    }

    static RaCensusAssembly MeasureOne(string path, int maxExamples)
    {
        var name = Path.GetFileName(path);
        try
        {
            using var session = AssemblyInspectionSession.Open(path);
            if (!session.HasMetadata)
                return new RaCensusAssembly(name, Opened: false, 0, 0, 0, []);
            var surface = session.ApiSurface(includeAll: true);

            // token -> (ApiType, ApiMember) for method-like members.
            var byToken = new Dictionary<int, (ApiType Type, ApiMember Member)>();
            foreach (var type in surface.Types)
                foreach (var member in type.Members)
                {
                    if (member.MetadataToken is not { } token)
                        continue;
                    if (member.Kind is not ("method" or "operator" or "constructor" or "explicit-interface-implementation"))
                        continue;
                    byToken[token] = (type, member);
                }

            using var pe = new PEReader(File.OpenRead(path));
            var reader = pe.GetMetadataReader();

            int matched = 0, agree = 0, unmatched = 0;
            var examples = new List<RaCensusExample>();
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var token = MetadataTokens.GetToken(methodHandle);
                    if (!byToken.TryGetValue(token, out var pair))
                    {
                        // A method-def not represented in the API surface (accessors,
                        // compiler-generated, or filtered) — counted for coverage, never
                        // miscompared.
                        unmatched++;
                        continue;
                    }

                    string a, b;
                    try
                    {
                        a = ApiMemberIdentity.GetMemberAnchor(pair.Type, pair.Member).CanonicalSignature;
                        b = ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, reader.GetMethodDefinition(methodHandle)).CanonicalSignature;
                    }
                    catch
                    {
                        continue;
                    }

                    matched++;
                    if (a == b)
                        agree++;
                    else if (examples.Count < maxExamples)
                        examples.Add(new RaCensusExample($"{pair.Type.FullName}::{pair.Member.Name}", a, b));
                }
            }

            return new RaCensusAssembly(name, Opened: true, matched, agree, unmatched, examples);
        }
        catch (Exception)
        {
            // A census over arbitrary user paths must never crash the sweep (a directory,
            // an unreadable file, or a truncated PE all surface here).
            return new RaCensusAssembly(name, Opened: false, 0, 0, 0, []);
        }
    }

    static string Format(DecompilerHarnessReport<RaCensusReport> report, int maxExamples, RaCensusFormat format)
    {
        var output = new StringWriter { NewLine = "\n" };
        if (format == RaCensusFormat.Markdown)
        {
            MarkoutSerializer.Serialize(
                BuildMarkdownView(report, maxExamples),
                output,
                new MarkdownFormatter(),
                RaCensusViewContext.Default,
                new MarkoutWriterOptions());
        }
        else
        {
            MarkoutSerializer.Serialize(
                BuildTableView(report, maxExamples),
                output,
                new TableFormatter(showHeader: true),
                RaCensusViewContext.Default,
                format == RaCensusFormat.Tsv
                    ? new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv }
                    : new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl });
        }

        string rendered = output.ToString();
        return format == RaCensusFormat.Jsonl
            ? string.Join(Environment.NewLine, rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
            : rendered;
    }

    static int TotalMatched(RaCensusReport report) => report.Assemblies.Sum(a => a.Matched);
    static int TotalAgree(RaCensusReport report) => report.Assemblies.Sum(a => a.Agree);
    static int TotalUnmatched(RaCensusReport report) => report.Assemblies.Sum(a => a.Unmatched);

    static string Pct(int n, int total) => total == 0 ? "n/a" : $"{100.0 * n / total:0.00}%";

    // --- Baseline snapshot + drift gate (mirrors CorpusSensor's emit/diff idiom) ---

    static RaCensusSnapshot BuildSnapshot(RaCensusReport report)
    {
        int matched = TotalMatched(report), agree = TotalAgree(report), unmatched = TotalUnmatched(report);
        return new RaCensusSnapshot(
            SchemaVersion: 1,
            Description: "#2440 Return Address member-identity equivalence census: agreement between the two "
                + "Metadata producers (A = ApiMemberIdentity.GetMemberAnchor, B = ApiMemberIdentity.CreateMethodAnchor) "
                + "over the pinned corpus. The agree rate is a floor that should ratchet toward 100% as identity "
                + "consolidation lands; refresh this baseline upward when it legitimately improves.",
            GeneratedUtc: DateTimeOffset.UtcNow,
            Tolerances: RaCensusTolerances.Default,
            Matched: matched,
            Agree: agree,
            Unmatched: unmatched,
            AgreeBasisPoints: RateBasisPoints(agree, matched),
            Assemblies: report.Assemblies
                .Where(a => a.Opened)
                .OrderBy(a => a.Name, StringComparer.Ordinal)
                .Select(a => new RaCensusAssemblySnapshot(a.Name, a.Matched, a.Agree, a.Unmatched, RateBasisPoints(a.Agree, a.Matched)))
                .ToList());
    }

    static IReadOnlyList<string> Compare(RaCensusSnapshot baseline, RaCensusSnapshot current)
    {
        var regressions = new List<string>();
        int tolerance = baseline.Tolerances?.AgreeRateDropBasisPoints ?? RaCensusTolerances.Default.AgreeRateDropBasisPoints;

        // The consolidation signal is the agree rate: it must not regress below the baseline
        // floor. Improvements never fail (raise the floor by re-emitting the baseline). Matched
        // and unmatched counts drift with corpus composition (framework/NuGet version bumps), so
        // they are reported in the card for triage but not gated.
        int drop = baseline.AgreeBasisPoints - current.AgreeBasisPoints;
        if (drop > tolerance)
            regressions.Add(
                $"agree rate dropped {FormatBps(drop)} ({FormatBps(baseline.AgreeBasisPoints)} -> "
                + $"{FormatBps(current.AgreeBasisPoints)}), tolerance {FormatBps(tolerance)}");

        return regressions;
    }

    static int RateBasisPoints(int part, int whole)
        => whole <= 0 ? 0 : (int)Math.Round(10_000.0 * part / whole, MidpointRounding.AwayFromZero);

    static string FormatBps(int basisPoints) => $"{basisPoints / 100.0:F2}%";

    static JsonSerializerOptions JsonOptions()
        => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static IReadOnlyList<(string Metric, string Value)> SummaryRows(RaCensusReport report)
    {
        int matched = TotalMatched(report), agree = TotalAgree(report);
        return new (string, string)[]
        {
            ("assemblies", report.Assemblies.Count.ToString()),
            ("opened", report.Assemblies.Count(a => a.Opened).ToString()),
            ("matched members", matched.ToString()),
            ("agree", agree.ToString()),
            ("agree rate", Pct(agree, matched)),
            ("diverge", (matched - agree).ToString()),
            ("unmatched (not in surface)", TotalUnmatched(report).ToString()),
        };
    }

    static HarnessComparisonProjection BuildComparison(RaCensusReport report)
    {
        int matched = TotalMatched(report);
        int agree = TotalAgree(report);
        int unmatched = TotalUnmatched(report);
        string aggregatePopulation = HarnessPopulationKey.Create(
            "return-address",
            report.Assemblies.Select(row => $"{row.Name}|{row.Opened}|{row.Matched}|{row.Unmatched}"));
        string matchedPopulation = HarnessPopulationKey.Create(
            "return-address.matched",
            report.Assemblies.Where(row => row.Opened).Select(row => $"{row.Name}|{row.Matched}"));

        return new HarnessComparisonProjection(
            "#2440 Return Address member-identity equivalence census.",
            aggregatePopulation,
            [
                new("agree", "Agree", MetricGoal.Higher, new MetricValue(agree, matched), matchedPopulation),
                new("diverge", "Diverge", MetricGoal.Lower, new MetricValue(matched - agree, matched), matchedPopulation),
                new("unmatched", "Unmatched (not in surface)", MetricGoal.Context, new MetricValue(unmatched), aggregatePopulation),
            ]);
    }

    static RaCensusMarkdownView BuildMarkdownView(DecompilerHarnessReport<RaCensusReport> envelope, int maxExamples)
        => new()
        {
            Report = [.. DecompilerHarnessReportViews.Metadata(envelope).Select(row => new RaCensusReportMetadataRow(row.Metric, row.Value))],
            Summary = SummaryRows(envelope.Payload).Select(r => new RaCensusMetricRow(r.Metric, r.Value)).ToList(),
            PerAssembly = envelope.Payload.Assemblies
                .Where(a => a.Opened)
                .Select(a => new RaCensusAssemblyRow(a.Name, a.Matched, a.Agree, a.Matched - a.Agree, a.Unmatched, Pct(a.Agree, a.Matched)))
                .ToList(),
            Divergences = envelope.Payload.Assemblies
                .SelectMany(a => a.Examples)
                .Take(maxExamples)
                .Select(e => new RaCensusDivergenceRow(e.Member, e.A, e.B))
                .ToList(),
        };

    static RaCensusTableView BuildTableView(DecompilerHarnessReport<RaCensusReport> envelope, int maxExamples)
        => new()
        {
            Report = [.. DecompilerHarnessReportViews.Metadata(envelope).Select(row => new RaCensusSectionReportMetadataRow("Report", row.Metric, row.Value))],
            Summary = SummaryRows(envelope.Payload).Select(r => new RaCensusSectionMetricRow("Summary", r.Metric, r.Value)).ToList(),
            PerAssembly = envelope.Payload.Assemblies
                .Where(a => a.Opened)
                .Select(a => new RaCensusSectionAssemblyRow("Per assembly", a.Name, a.Matched, a.Agree, a.Matched - a.Agree, a.Unmatched, Pct(a.Agree, a.Matched)))
                .ToList(),
            Divergences = envelope.Payload.Assemblies
                .SelectMany(a => a.Examples)
                .Take(maxExamples)
                .Select(e => new RaCensusSectionDivergenceRow("Divergences", e.Member, e.A, e.B))
                .ToList(),
        };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
internal sealed class RaCensusMarkdownView
{
    [MarkoutIgnore]
    public string Title => "Return Address equivalence census";

    [MarkoutSection(Name = "Report")]
    public List<RaCensusReportMetadataRow>? Report { get; init; }

    [MarkoutSection(Name = "Summary")]
    public List<RaCensusMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Per assembly", EmptyText = "None")]
    public List<RaCensusAssemblyRow>? PerAssembly { get; init; }

    [MarkoutSection(Name = "Divergences", EmptyText = "None - the two producers agree on every matched member.")]
    public List<RaCensusDivergenceRow>? Divergences { get; init; }
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
internal sealed class RaCensusTableView
{
    [MarkoutIgnore]
    public string Title => "Return Address equivalence census";

    [MarkoutSection(Name = "Report")]
    public List<RaCensusSectionReportMetadataRow>? Report { get; init; }

    [MarkoutSection(Name = "Summary")]
    public List<RaCensusSectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Per assembly")]
    public List<RaCensusSectionAssemblyRow>? PerAssembly { get; init; }

    [MarkoutSection(Name = "Divergences")]
    public List<RaCensusSectionDivergenceRow>? Divergences { get; init; }
}

[MarkoutSerializable]
internal sealed record RaCensusMetricRow(string Metric, string Value);

[MarkoutSerializable]
internal sealed record RaCensusReportMetadataRow(string Metric, string Value);

[MarkoutSerializable]
internal sealed record RaCensusSectionReportMetadataRow(string Section, string Metric, string Value);

[MarkoutSerializable]
internal sealed record RaCensusSectionMetricRow(string Section, string Metric, string Value);

[MarkoutSerializable]
internal sealed record RaCensusAssemblyRow(string Assembly, int Matched, int Agree, int Diverge, int Unmatched, [property: MarkoutPropertyName("Agree rate")] string AgreeRate);

[MarkoutSerializable]
internal sealed record RaCensusSectionAssemblyRow(string Section, string Assembly, int Matched, int Agree, int Diverge, int Unmatched, [property: MarkoutPropertyName("Agree rate")] string AgreeRate);

[MarkoutSerializable]
internal sealed record RaCensusDivergenceRow(string Member, [property: MarkoutPropertyName("A (surface)")] string A, [property: MarkoutPropertyName("B (SRM-direct)")] string B);

[MarkoutSerializable]
internal sealed record RaCensusSectionDivergenceRow(string Section, string Member, [property: MarkoutPropertyName("A (surface)")] string A, [property: MarkoutPropertyName("B (SRM-direct)")] string B);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(RaCensusMarkdownView))]
[MarkoutContext(typeof(RaCensusTableView))]
[MarkoutContext(typeof(RaCensusReportMetadataRow))]
[MarkoutContext(typeof(RaCensusSectionReportMetadataRow))]
[MarkoutContext(typeof(RaCensusMetricRow))]
[MarkoutContext(typeof(RaCensusSectionMetricRow))]
[MarkoutContext(typeof(RaCensusAssemblyRow))]
[MarkoutContext(typeof(RaCensusSectionAssemblyRow))]
[MarkoutContext(typeof(RaCensusDivergenceRow))]
[MarkoutContext(typeof(RaCensusSectionDivergenceRow))]
internal sealed partial class RaCensusViewContext : MarkoutSerializerContext
{
}
