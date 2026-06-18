using System.Text;
using System.Text.Json;
using DotnetInspector.Output;
using DotnetInspector.Vulnerabilities;
using Markout;

namespace DotnetInspector.Commands;

public sealed record VulnerabilityOptions
{
    public string[] Inputs { get; init; } = [];
    public bool Recursive { get; init; }
    public bool FailOnVulnerabilities { get; init; }
    public bool NoNuGet { get; init; }
    public bool NoDotNet { get; init; }
    public string DotNetReleaseIndex { get; init; } = VulnerabilitiesCommand.DefaultDotNetReleaseIndexUrl;
    public bool JsonOutput { get; init; }
    public bool OneLine { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public int? Rows { get; init; }
    public bool Verbose { get; init; }
}

public static class VulnerabilitiesCommand
{
    public const string Name = "vulnerabilities";
    public const string DefaultDotNetReleaseIndexUrl = "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json";

    public static async Task<int> ExecuteAsync(VulnerabilityOptions options)
    {
        if (options.Inputs.Length == 0)
        {
            Console.Error.WriteLine("Error: one or more package, component, file, or directory inputs are required.");
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  dotnet-inspect vulnerabilities Newtonsoft.Json@13.0.3");
            Console.Error.WriteLine("  dotnet-inspect vulnerabilities app.deps.json");
            Console.Error.WriteLine("  dotnet-inspect vulnerabilities dotnet-runtime@8.0.24 dotnet-sdk@8.0.111");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var request = new VulnerabilityScanRequest
        {
            Inputs = options.Inputs,
            Recursive = options.Recursive,
            IncludeNuGet = !options.NoNuGet,
            IncludeDotNet = !options.NoDotNet,
            DotNetReleaseIndexUrl = options.DotNetReleaseIndex,
        };

        var report = await VulnerabilityScanner.ScanAsync(request, context.HttpClient, context.Logger.Log).ConfigureAwait(false);

        WriteReport(report, options);

        return options.FailOnVulnerabilities && report.Vulnerabilities > 0 ? 2 : 0;
    }

    private static void WriteReport(VulnerabilityReport report, VulnerabilityOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, VulnerabilitiesJsonContext.Default.VulnerabilityReport));
            return;
        }

        if (options.OneLine)
        {
            OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
            {
                var markoutWriter = new MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
                markoutWriter.WriteTable(Headers, StableHeaders, ToCells(report.Results));
                markoutWriter.Flush();
            });
            return;
        }

        Console.WriteLine(OutputFormatter.ApplyRowLimit(RenderMarkdown(report), options.Rows));
    }

    private static string RenderMarkdown(VulnerabilityReport report)
    {
        var vulnerable = report.Results.Where(r => r.Status == StatusVulnerable).ToList();
        var clean = report.Results.Where(r => IsCleanStatus(r.Status)).ToList();
        var unresolved = report.Results
            .Where(r => r.Status != StatusVulnerable && !IsCleanStatus(r.Status))
            .ToList();

        var vulnerableComponents = vulnerable
            .Select(RowKey)
            .Distinct()
            .Count();

        var builder = new StringBuilder();
        builder.AppendLine("# Vulnerabilities");
        builder.AppendLine();
        builder.AppendLine(SummaryLine(report, vulnerable.Count, vulnerableComponents, clean.Count, unresolved.Count));

        // Vulnerable findings, grouped by component so each component reads as a unit.
        foreach (var group in vulnerable
            .GroupBy(RowKey)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            builder.AppendLine();
            builder.AppendLine($"## {Inline(first.Component)} {Inline(first.Version)} — {Inline(first.Source)}");
            builder.AppendLine();
            var confidence = ConfidenceNote(first.Provenance);
            if (confidence is not null)
            {
                builder.AppendLine($"_{confidence}_");
                builder.AppendLine();
            }
            foreach (var row in group.OrderBy(r => SeverityRank(r.Severity)).ThenBy(r => r.Cve, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("- ");
                builder.Append($"**{Inline(SeverityLabel(row.Severity))}** {Inline(IdentifierLabel(row))}");
                if (!string.IsNullOrWhiteSpace(row.FixedVersion))
                {
                    builder.Append($" · fixed in {Inline(row.FixedVersion)}");
                }
                if (!string.IsNullOrWhiteSpace(row.AffectedRange))
                {
                    builder.Append($" · affected {Inline(row.AffectedRange)}");
                }
                builder.AppendLine();
                if (!string.IsNullOrWhiteSpace(row.Summary))
                {
                    builder.AppendLine($"  {Inline(row.Summary)}");
                }
                if (!string.IsNullOrWhiteSpace(row.AdvisoryUrl))
                {
                    builder.AppendLine($"  {Inline(row.AdvisoryUrl)}");
                }
            }
        }

        if (clean.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## No known vulnerabilities");
            builder.AppendLine();
            foreach (var row in clean.OrderBy(RowKey, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {Inline(row.Component)} {Inline(row.Version)} — {Inline(row.Source)}");
            }
        }

        if (unresolved.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Not evaluated");
            builder.AppendLine();
            foreach (var row in unresolved.OrderBy(RowKey, StringComparer.OrdinalIgnoreCase))
            {
                var label = string.IsNullOrWhiteSpace(row.Component)
                    ? Inline(row.Input)
                    : $"{Inline(row.Component)} {Inline(row.Version)}";
                builder.AppendLine($"- {label} — {Inline(row.Source)} — {Inline(row.Status)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private const string StatusVulnerable = "Vulnerable";

    private static bool IsCleanStatus(string status) =>
        status.StartsWith("No known", StringComparison.OrdinalIgnoreCase);

    private static string RowKey(VulnerabilityRow row) =>
        $"{row.Source}|{row.Component}|{row.Version}";

    private static string SummaryLine(VulnerabilityReport report, int vulnerable, int vulnerableComponents, int clean, int unresolved)
    {
        var components = $"{report.Components} component{(report.Components == 1 ? "" : "s")}";
        var inputs = $"{report.Inputs} input{(report.Inputs == 1 ? "" : "s")}";

        string head = vulnerable > 0
            ? $"{vulnerable} vulnerabilit{(vulnerable == 1 ? "y" : "ies")} across {vulnerableComponents} of {components}"
            : $"No known vulnerabilities across {components}";

        var parts = new List<string> { head, inputs };
        if (unresolved > 0)
        {
            parts.Add($"{unresolved} not evaluated");
        }
        return string.Join(" · ", parts);
    }

    private static string IdentifierLabel(VulnerabilityRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Cve))
        {
            return row.Cve!;
        }
        return string.IsNullOrWhiteSpace(row.Ghsa) ? "advisory" : row.Ghsa!;
    }

    private static string SeverityLabel(string? severity) =>
        string.IsNullOrWhiteSpace(severity) ? "UNKNOWN" : severity!.ToUpperInvariant();

    private static int SeverityRank(string? severity) => SeverityLabel(severity) switch
    {
        "CRITICAL" => 0,
        "HIGH" => 1,
        "MEDIUM" => 2,
        "MODERATE" => 2,
        "LOW" => 3,
        _ => 4,
    };

    private static string? ConfidenceNote(string provenance)
    {
        var confidence = VulnerabilityProvenance.Confidence(provenance);
        return provenance switch
        {
            VulnerabilityProvenance.Project =>
                "Confidence: declared — from a project file PackageReference; "
                + "a framework-provided package may be pruned at build and not actually deployed.",
            VulnerabilityProvenance.Nuspec =>
                "Confidence: declared — from a package manifest.",
            VulnerabilityProvenance.Binary =>
                "Confidence: best-effort — inferred from a loose binary; package identity is heuristic.",
            VulnerabilityProvenance.DepsJson or VulnerabilityProvenance.RuntimeConfig =>
                "Confidence: authoritative — from the built application's deployed dependency closure.",
            VulnerabilityProvenance.Identity =>
                "Confidence: authoritative — from an explicit component identity.",
            _ => confidence.Length == 0 ? null : $"Confidence: {confidence}.",
        };
    }

    private static string Inline(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string[][] ToCells(List<VulnerabilityRow> rows) =>
        rows.Select(row => new[]
        {
            row.Source,
            row.Status,
            row.Input,
            row.Component,
            row.Version,
            row.Severity ?? "",
            row.Cve ?? "",
            row.Ghsa ?? "",
            row.AdvisoryUrl ?? "",
            row.AffectedRange ?? "",
            row.FixedVersion ?? "",
            row.Summary ?? "",
            row.Provenance
        }).ToArray();

    private static readonly string[] Headers =
    [
        "Source",
        "Status",
        "Input",
        "Component",
        "Version",
        "Severity",
        "CVE",
        "GHSA",
        "Advisory URL",
        "Affected Range",
        "Fixed Version",
        "Summary",
        "Provenance"
    ];

    private static readonly string[] StableHeaders =
    [
        "source",
        "status",
        "input",
        "component",
        "version",
        "severity",
        "cve",
        "ghsa",
        "advisory_url",
        "affected_range",
        "fixed_version",
        "summary",
        "provenance"
    ];
}
