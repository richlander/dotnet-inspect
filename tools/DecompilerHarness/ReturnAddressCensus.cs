using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
    IReadOnlyList<RaCensusExample> Examples);

internal sealed record RaCensusReport(IReadOnlyList<RaCensusAssembly> Assemblies);

internal static class ReturnAddressCensus
{
    public static int Run(IReadOnlyList<string> assemblies, int maxExamples, RaCensusFormat format)
    {
        var report = Measure(assemblies, maxExamples);
        Console.Write(Format(report, maxExamples, format));
        return 0;
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
                return new RaCensusAssembly(name, Opened: false, 0, 0, []);
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

            int matched = 0, agree = 0;
            var examples = new List<RaCensusExample>();
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var token = MetadataTokens.GetToken(methodHandle);
                    if (!byToken.TryGetValue(token, out var pair))
                        continue;

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

            return new RaCensusAssembly(name, Opened: true, matched, agree, examples);
        }
        catch (Exception)
        {
            // A census over arbitrary user paths must never crash the sweep (a directory,
            // an unreadable file, or a truncated PE all surface here).
            return new RaCensusAssembly(name, Opened: false, 0, 0, []);
        }
    }

    static string Format(RaCensusReport report, int maxExamples, RaCensusFormat format)
    {
        var output = new StringWriter();
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

    static string Pct(int n, int total) => total == 0 ? "n/a" : $"{100.0 * n / total:0.00}%";

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
        };
    }

    static RaCensusMarkdownView BuildMarkdownView(RaCensusReport report, int maxExamples)
        => new()
        {
            Summary = SummaryRows(report).Select(r => new RaCensusMetricRow(r.Metric, r.Value)).ToList(),
            PerAssembly = report.Assemblies
                .Where(a => a.Opened)
                .Select(a => new RaCensusAssemblyRow(a.Name, a.Matched, a.Agree, a.Matched - a.Agree, Pct(a.Agree, a.Matched)))
                .ToList(),
            Divergences = report.Assemblies
                .SelectMany(a => a.Examples)
                .Take(maxExamples)
                .Select(e => new RaCensusDivergenceRow(e.Member, e.A, e.B))
                .ToList(),
        };

    static RaCensusTableView BuildTableView(RaCensusReport report, int maxExamples)
        => new()
        {
            Summary = SummaryRows(report).Select(r => new RaCensusSectionMetricRow("Summary", r.Metric, r.Value)).ToList(),
            PerAssembly = report.Assemblies
                .Where(a => a.Opened)
                .Select(a => new RaCensusSectionAssemblyRow("Per assembly", a.Name, a.Matched, a.Agree, a.Matched - a.Agree, Pct(a.Agree, a.Matched)))
                .ToList(),
            Divergences = report.Assemblies
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
internal sealed record RaCensusSectionMetricRow(string Section, string Metric, string Value);

[MarkoutSerializable]
internal sealed record RaCensusAssemblyRow(string Assembly, int Matched, int Agree, int Diverge, [property: MarkoutPropertyName("Agree rate")] string AgreeRate);

[MarkoutSerializable]
internal sealed record RaCensusSectionAssemblyRow(string Section, string Assembly, int Matched, int Agree, int Diverge, [property: MarkoutPropertyName("Agree rate")] string AgreeRate);

[MarkoutSerializable]
internal sealed record RaCensusDivergenceRow(string Member, [property: MarkoutPropertyName("A (surface)")] string A, [property: MarkoutPropertyName("B (SRM-direct)")] string B);

[MarkoutSerializable]
internal sealed record RaCensusSectionDivergenceRow(string Section, string Member, [property: MarkoutPropertyName("A (surface)")] string A, [property: MarkoutPropertyName("B (SRM-direct)")] string B);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(RaCensusMarkdownView))]
[MarkoutContext(typeof(RaCensusTableView))]
[MarkoutContext(typeof(RaCensusMetricRow))]
[MarkoutContext(typeof(RaCensusSectionMetricRow))]
[MarkoutContext(typeof(RaCensusAssemblyRow))]
[MarkoutContext(typeof(RaCensusSectionAssemblyRow))]
[MarkoutContext(typeof(RaCensusDivergenceRow))]
[MarkoutContext(typeof(RaCensusSectionDivergenceRow))]
internal sealed partial class RaCensusViewContext : MarkoutSerializerContext
{
}
