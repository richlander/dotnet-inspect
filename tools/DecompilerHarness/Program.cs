using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

using DotnetInspector.Decompiler;

namespace DotnetInspector.DecompilerHarness;

/// <summary>
/// Differential harness for the decompiler pipelines — the asmdiffs analog
/// from docs/decompiler-pipeline.md. With one pipeline it inventories health
/// (render rate, exception buckets) across whole assemblies; with a baseline
/// and a candidate it reports agreement and categorized diffs. The parity
/// gate for the pipeline replacement is measured here.
/// </summary>
static class Program
{
    // The replacement pipeline registers here under "next" when it exists.
    static readonly Dictionary<string, Func<MethodBodyContext, string>> Pipelines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["current"] = CSharpEmitter.Emit,
    };

    /// <summary>Methods slower than this are listed in the report. True hangs stall the sweep visibly (CI applies job-level timeouts); a nested-task timeout here would misreport pool starvation as a product hang.</summary>
    const double SlowThresholdSeconds = 2.0;

    static int Main(string[] args)
    {
        List<string> inputs = [];
        string baselineName = "current";
        string? candidateName = null;
        string? reportPath = null;
        string? jsonPath = null;
        int maxExamples = 5;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baseline": baselineName = args[++i]; break;
                case "--candidate": candidateName = args[++i]; break;
                case "--report": reportPath = args[++i]; break;
                case "--json": jsonPath = args[++i]; break;
                case "--max-examples": maxExamples = int.Parse(args[++i]); break;
                case "--help" or "-h": PrintUsage(); return 0;
                default: inputs.Add(args[i]); break;
            }
        }

        if (!Pipelines.TryGetValue(baselineName, out var baseline))
            return Fail($"Unknown baseline pipeline '{baselineName}'. Known: {string.Join(", ", Pipelines.Keys)}");
        Func<MethodBodyContext, string>? candidate = null;
        if (candidateName is not null && !Pipelines.TryGetValue(candidateName, out candidate))
            return Fail($"Unknown candidate pipeline '{candidateName}'. Known: {string.Join(", ", Pipelines.Keys)}");

        var assemblies = ResolveAssemblies(inputs);
        if (assemblies.Count == 0)
            return Fail("No managed assemblies found in the given inputs.");

        var report = new StringBuilder();
        report.AppendLine("# Decompiler Harness Report");
        report.AppendLine();
        report.AppendLine(candidate is null
            ? $"Mode: inventory (pipeline: {baselineName})"
            : $"Mode: diff (baseline: {baselineName}, candidate: {candidateName})");
        report.AppendLine();

        List<FailureRecord> failures = [];
        long grandTotal = 0, grandRendered = 0, grandAgree = 0, grandCompared = 0;

        foreach (var assemblyPath in assemblies)
        {
            var sweep = Sweep(assemblyPath, baseline, candidate);
            grandTotal += sweep.Total;
            grandRendered += sweep.Rendered;
            grandAgree += sweep.Agree;
            grandCompared += sweep.Compared;
            failures.AddRange(sweep.Failures);
            AppendAssemblyReport(report, sweep, candidate is not null, maxExamples);
            Console.WriteLine(SummaryLine(sweep, candidate is not null));
        }

        report.AppendLine("## Totals");
        report.AppendLine();
        report.AppendLine($"- Methods: {grandTotal}");
        report.AppendLine($"- Rendered: {grandRendered} ({Percent(grandRendered, grandTotal)})");
        if (candidate is not null)
            report.AppendLine($"- Agreement: {grandAgree}/{grandCompared} ({Percent(grandAgree, grandCompared)})");

        Console.WriteLine();
        Console.WriteLine(candidate is null
            ? $"TOTAL: {grandRendered}/{grandTotal} rendered ({Percent(grandRendered, grandTotal)})"
            : $"TOTAL: {grandAgree}/{grandCompared} agree ({Percent(grandAgree, grandCompared)}); {grandRendered}/{grandTotal} rendered");

        if (reportPath is not null)
        {
            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine($"Report: {reportPath}");
        }
        if (jsonPath is not null)
        {
            using var stream = File.Create(jsonPath);
            JsonSerializer.Serialize(stream, failures, FailureJson.Default.ListFailureRecord);
            Console.WriteLine($"JSON: {jsonPath}");
        }
        return 0;
    }

    static SweepResult Sweep(string assemblyPath, Func<MethodBodyContext, string> baseline, Func<MethodBodyContext, string>? candidate)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var outcomes = new ConcurrentBag<MethodResult>();
        var slow = new ConcurrentBag<(string Method, double Seconds)>();
        Parallel.ForEach(reader.TypeDefinitions, tdh =>
        {
            var td = reader.GetTypeDefinition(tdh);
            string typeName = TypeDisplayName(reader, td);
            foreach (var mh in td.GetMethods())
            {
                var method = reader.GetMethodDefinition(mh);
                if (method.RelativeVirtualAddress == 0)
                    continue;
                string id = $"{typeName}::{reader.GetString(method.Name)}";

                MethodBodyContext? context;
                try
                {
                    context = MethodBodyContext.Create(peReader, reader, method);
                }
                catch (Exception ex)
                {
                    outcomes.Add(new MethodResult(id, Kind.ContextFailed, Bucket(ex)));
                    continue;
                }
                if (context is null)
                {
                    outcomes.Add(new MethodResult(id, Kind.ContextFailed, "null context"));
                    continue;
                }

                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                var (baseText, baseBucket) = Run(baseline, context);
                if (candidate is null)
                {
                    RecordIfSlow(slow, id, started);
                    outcomes.Add(baseText is not null
                        ? new MethodResult(id, Kind.Rendered, null)
                        : new MethodResult(id, Kind.Threw, baseBucket));
                    continue;
                }

                var (candText, candBucket) = Run(candidate, context);
                RecordIfSlow(slow, id, started);
                outcomes.Add((baseText, candText) switch
                {
                    (not null, not null) when baseText == candText => new MethodResult(id, Kind.Agree, null),
                    (not null, not null) => new MethodResult(id, Kind.Differ, FirstDiffLine(baseText, candText)),
                    (not null, null) => new MethodResult(id, Kind.CandidateThrew, candBucket),
                    (null, not null) => new MethodResult(id, Kind.BaselineThrew, baseBucket),
                    _ => new MethodResult(id, Kind.BothThrew, $"baseline: {baseBucket}; candidate: {candBucket}"),
                });
            }
        });

        return SweepResult.From(Path.GetFileName(assemblyPath), outcomes, slow);
    }

    static (string? Text, string? Bucket) Run(Func<MethodBodyContext, string> pipeline, MethodBodyContext context)
    {
        try
        {
            return (pipeline(context), null);
        }
        catch (Exception ex)
        {
            return (null, Bucket(ex));
        }
    }

    static void RecordIfSlow(ConcurrentBag<(string Method, double Seconds)> slow, string id, long started)
    {
        double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        if (seconds >= SlowThresholdSeconds)
            slow.Add((id, seconds));
    }

    /// <summary>Buckets an exception by type and the topmost decompiler frame, so one bug is one bucket.</summary>
    static string Bucket(Exception ex)
    {
        string frame = "?";
        foreach (var line in (ex.StackTrace ?? "").Split('\n'))
        {
            int at = line.IndexOf("at DotnetInspector.", StringComparison.Ordinal);
            if (at < 0)
                continue;
            string site = line[(at + 3)..].Trim();
            int paren = site.IndexOf('(');
            frame = paren > 0 ? site[..paren] : site;
            break;
        }
        return $"{ex.GetType().Name} @ {frame}";
    }

    static string FirstDiffLine(string baseline, string candidate)
    {
        var a = baseline.Split('\n');
        var b = candidate.Split('\n');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string left = i < a.Length ? a[i].Trim() : "<end>";
            string right = i < b.Length ? b[i].Trim() : "<end>";
            if (left != right)
                return $"`{Truncate(left, 60)}` vs `{Truncate(right, 60)}`";
        }
        return "<whitespace only>";
    }

    static void AppendAssemblyReport(StringBuilder report, SweepResult sweep, bool diffMode, int maxExamples)
    {
        report.AppendLine($"## {sweep.Assembly}");
        report.AppendLine();
        report.AppendLine($"- Methods with bodies: {sweep.Total}");
        report.AppendLine($"- Rendered: {sweep.Rendered} ({Percent(sweep.Rendered, sweep.Total)})");
        if (diffMode)
            report.AppendLine($"- Agreement: {sweep.Agree}/{sweep.Compared} ({Percent(sweep.Agree, sweep.Compared)})");
        foreach (var (method, seconds) in sweep.Slow.OrderByDescending(x => x.Seconds))
            report.AppendLine($"- Slow: {Escape(method)} ({seconds:F1}s)");
        if (sweep.Failures.Count == 0)
        {
            report.AppendLine();
            return;
        }

        report.AppendLine();
        report.AppendLine("| Count | Kind | Bucket | Example |");
        report.AppendLine("| --- | --- | --- | --- |");
        foreach (var group in sweep.Failures
            .GroupBy(f => (f.Kind, f.Bucket))
            .OrderByDescending(g => g.Count()))
        {
            var examples = group.Take(maxExamples).Select(f => f.Method);
            report.AppendLine($"| {group.Count()} | {group.Key.Kind} | {Escape(group.Key.Bucket ?? "")} | {Escape(string.Join("; ", examples))} |");
        }
        report.AppendLine();
    }

    static string SummaryLine(SweepResult sweep, bool diffMode) => diffMode
        ? $"{sweep.Assembly}: {sweep.Agree}/{sweep.Compared} agree ({Percent(sweep.Agree, sweep.Compared)}), {sweep.Failures.Count} failures"
        : $"{sweep.Assembly}: {sweep.Rendered}/{sweep.Total} rendered ({Percent(sweep.Rendered, sweep.Total)}), {sweep.Failures.Count} failures";

    static List<string> ResolveAssemblies(List<string> inputs)
    {
        if (inputs.Count == 0)
            return [typeof(object).Assembly.Location];

        List<string> result = [];
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
                result.AddRange(Directory.EnumerateFiles(input, "*.dll").Where(IsManaged).Order());
            else if (File.Exists(input))
                result.Add(input);
            else
                Console.Error.WriteLine($"Warning: '{input}' not found, skipping.");
        }
        return result;
    }

    static bool IsManaged(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return pe.HasMetadata;
        }
        catch
        {
            return false;
        }
    }

    static string TypeDisplayName(MetadataReader reader, TypeDefinition td)
    {
        string ns = reader.GetString(td.Namespace);
        string name = reader.GetString(td.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static string Percent(long part, long whole) => whole == 0 ? "n/a" : $"{100.0 * part / whole:F2}%";

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    static int Fail(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }

    static void PrintUsage() => Console.WriteLine("""
        usage: decompiler-harness [assembly-or-directory ...] [options]

        With no inputs, sweeps System.Private.CoreLib of the running runtime.

        options:
          --baseline <name>     pipeline to run (default: current)
          --candidate <name>    second pipeline; enables diff mode
          --report <path>       write a markdown report
          --json <path>         write failure/diff records as JSON
          --max-examples <n>    example methods per bucket in the report (default 5)
        """);
}

enum Kind { Rendered, Threw, ContextFailed, Agree, Differ, CandidateThrew, BaselineThrew, BothThrew }

record MethodResult(string Id, Kind Kind, string? Bucket);

record FailureRecord(string Assembly, string Method, string Kind, string? Bucket);

record SweepResult(string Assembly, long Total, long Rendered, long Agree, long Compared, List<FailureRecord> Failures, List<(string Method, double Seconds)> Slow)
{
    public static SweepResult From(string assembly, IEnumerable<MethodResult> outcomes, IEnumerable<(string Method, double Seconds)> slow)
    {
        long total = 0, rendered = 0, agree = 0, compared = 0;
        List<FailureRecord> failures = [];
        foreach (var outcome in outcomes)
        {
            total++;
            switch (outcome.Kind)
            {
                case Kind.Rendered:
                    rendered++;
                    break;
                case Kind.Agree:
                    rendered++; agree++; compared++;
                    break;
                case Kind.Differ:
                    rendered++; compared++;
                    failures.Add(new FailureRecord(assembly, outcome.Id, outcome.Kind.ToString(), outcome.Bucket));
                    break;
                default:
                    failures.Add(new FailureRecord(assembly, outcome.Id, outcome.Kind.ToString(), outcome.Bucket));
                    break;
            }
        }
        return new SweepResult(assembly, total, rendered, agree, compared, failures, [.. slow]);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<FailureRecord>))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
partial class FailureJson : System.Text.Json.Serialization.JsonSerializerContext;
