using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Per-assembly portfolio view: combines the cheap IR residual classifier with
/// the Roslyn-backed validity oracle so a directory of libraries becomes a
/// prioritized unsupported-pattern report.
/// </summary>
internal static class LibraryReport
{
    public static int Run(IReadOnlyList<string> assemblies, int compileCap, int maxExamples, bool json, int topPatterns, int? topLibraries)
    {
        var reports = new List<AssemblyReport>();
        using var metadata = CorpusMetadata.Create(assemblies);
        var references = ValidityCheck.RuntimeReferences();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: true,
            nullableContextOptions: NullableContextOptions.Disable)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>())
            .WithMetadataImportOptions(MetadataImportOptions.Internal);

        foreach (var assembly in assemblies)
        {
            reports.Add(AnalyzeAssembly(assembly, metadata, references, parseOptions, compileOptions, compileCap, maxExamples));
        }

        int patternLimit = Math.Max(1, topPatterns);
        var selectedReports = SelectLibraries(reports, topLibraries);
        var topPatternReports = TopPatterns(selectedReports, patternLimit, maxExamples);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new LibraryPortfolioReport(topPatternReports, selectedReports),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            PrintMarkdown(selectedReports, topPatternReports, patternLimit, topLibraries);
        }

        return reports.Any(r => r.PassBugs > 0) ? 1 : 0;
    }

    static IReadOnlyList<AssemblyReport> SelectLibraries(IReadOnlyList<AssemblyReport> reports, int? topLibraries)
    {
        if (topLibraries is not { } limit)
            return reports;
        return [.. reports
            .OrderByDescending(UnsupportedLoad)
            .ThenBy(r => r.Assembly, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))];
    }

    static int UnsupportedLoad(AssemblyReport report)
        => report.Patterns.Sum(p => p.Count);

    static AssemblyReport AnalyzeAssembly(
        string path,
        MetadataContext metadata,
        ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions,
        int compileCap,
        int maxExamples)
    {
        var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        var report = new MutableReport(Path.GetFileName(path), path);

        try
        {
            using var source = MetadataSource.Open(path, context: metadata);
            report.Assembly = source.AssemblyName;
            var constraints = ShellConstraints.Build(source);
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                report.TotalMethods++;
                var id = $"{typeName}::{methodName}";
                try
                {
                    IrPasses.Run(function);
                }
                catch (Exception ex)
                {
                    report.PassBugs++;
                    Record(buckets, $"pass-bug: {ex.GetType().Name}", id, maxExamples);
                    continue;
                }

                bool full = function.Fidelity == DecompilationFidelity.Full;
                if (full)
                {
                    report.FullMethods++;
                }
                else
                {
                    report.PartialMethods++;
                }

                string? residual = Completeness.Residual(function)
                    ?? (!full ? $"fidelity: {BucketFor(function, function.Diagnostics.FirstOrDefault())}" : null);
                if (residual is null)
                {
                    report.FullyRaisedMethods++;
                }
                else
                {
                    Record(buckets, residual, id, maxExamples);
                }

                if (typeName.Contains('<', StringComparison.Ordinal) || methodName.Contains('<', StringComparison.Ordinal))
                {
                    continue;
                }

                string? rendered;
                try
                {
                    rendered = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method)).Output;
                }
                catch (Exception ex)
                {
                    if (full)
                    {
                        Record(buckets, $"validity: render-exception:{ex.GetType().Name}", id, maxExamples);
                    }
                    continue;
                }

                if (rendered is null)
                {
                    continue;
                }
                report.RenderedMethods++;

                string shell = ValidityCheck.Shell(function, rendered, typeName, methodName, constraints);
                var tree = CSharpSyntaxTree.ParseText(shell, parseOptions);
                var syntaxErrors = tree.GetDiagnostics().Where(ValidityCheck.IsError).ToList();
                var illegal = ValidityCheck.IllegalStatements(tree);
                if (syntaxErrors.Count > 0 || illegal.Count > 0)
                {
                    if (full)
                    {
                        report.FullMalformed++;
                        string code = syntaxErrors.Count > 0 ? syntaxErrors[0].Id : "CS0201";
                        Record(buckets, $"validity: malformed:{code}", id, maxExamples);
                    }
                    else
                    {
                        report.PartialMalformed++;
                    }
                    continue;
                }

                if (!full || report.SemanticChecked >= compileCap)
                {
                    continue;
                }
                report.SemanticChecked++;

                var compilation = CSharpCompilation.Create("check", [tree], references, compileOptions);
                var defects = compilation.GetDiagnostics()
                    .Where(ValidityCheck.IsError)
                    .Where(d => !ValidityCheck.BindingNoise.Contains(d.Id))
                    .Where(d => !ValidityCheck.IsShellArtifact(d))
                    .ToList();
                if (defects.Count == 0)
                {
                    continue;
                }

                report.SemanticDefectMethods++;
                foreach (var defect in defects)
                {
                    Record(buckets, $"validity: {defect.Id}", id, maxExamples);
                }
            }
        }
        catch (Exception ex)
        {
            report.PassBugs++;
            Record(buckets, $"assembly-open: {ex.GetType().Name}", report.Assembly, maxExamples);
        }

        return report.ToReport(buckets);
    }

    static string BucketFor(IrFunction function, DecompilerDiagnostic diagnostic)
    {
        if (diagnostic.Id is null)
        {
            // No pass recorded a reason — but fidelity is computed from the final
            // tree, never asserted by a pass, so the lowering cause still lives in
            // the tree (an unrepresentable compiler-generated name, an unverified
            // by-ref argument, a residual function pointer, ...). Recover it with the
            // same walk the score uses (FidelityRemarks) and bucket by the stable
            // DEC#### code, so the opaque "(typed)" catch-all becomes the same
            // per-cause buckets the recorded diagnostics already produce. The walk
            // always finds at least one site for a non-Full function, so the
            // "(typed)" fallback is unreachable in practice — kept only for safety.
            return FidelityRemarks.Collect(function).FirstOrDefault()?.Code ?? "(typed)";
        }
        if (diagnostic.Id == DiagnosticIds.UnsupportedType)
        {
            var message = diagnostic.Message ?? "(typed)";
            int detail = message.IndexOf('(');
            return detail < 0 ? message : message[..detail].TrimEnd();
        }
        return diagnostic.Message?.Split(' ').ElementAtOrDefault(1) ?? "(typed)";
    }

    static void Record(Dictionary<string, Bucket> buckets, string key, string example, int maxExamples)
    {
        if (!buckets.TryGetValue(key, out var bucket))
        {
            buckets[key] = bucket = new Bucket(key);
        }
        bucket.Count++;
        if (bucket.Examples.Count < maxExamples)
        {
            bucket.Examples.Add(example);
        }
    }

    static IReadOnlyList<PatternSummary> TopPatterns(IReadOnlyList<AssemblyReport> reports, int topPatterns, int maxExamples)
    {
        return [.. reports
            .SelectMany(report => report.Patterns.Select(pattern => (report, pattern)))
            .GroupBy(entry => entry.pattern.Name, StringComparer.Ordinal)
            .Select(group =>
            {
                var affected = group
                    .GroupBy(entry => entry.report.Assembly, StringComparer.Ordinal)
                    .Select(library => new PatternLibrarySummary(
                        library.Key,
                        library.Sum(entry => entry.pattern.Count),
                        [.. library.SelectMany(entry => entry.pattern.Examples).Distinct(StringComparer.Ordinal).Take(maxExamples)]))
                    .OrderByDescending(library => library.Count)
                    .ThenBy(library => library.Assembly, StringComparer.Ordinal)
                    .ToArray();
                return new PatternSummary(
                    group.Key,
                    group.Sum(entry => entry.pattern.Count),
                    affected.Length,
                    affected,
                    [.. group.SelectMany(entry => entry.pattern.Examples).Distinct(StringComparer.Ordinal).Take(maxExamples)]);
            })
            .OrderByDescending(pattern => pattern.Count)
            .ThenBy(pattern => pattern.Name, StringComparer.Ordinal)
            .Take(topPatterns)];
    }

    static void PrintMarkdown(
        IReadOnlyList<AssemblyReport> reports,
        IReadOnlyList<PatternSummary> topPatterns,
        int patternLimit,
        int? topLibraries)
    {
        Console.WriteLine("# Decompiler library report");
        Console.WriteLine();
        if (topLibraries is { } limit)
            Console.WriteLine($"Showing top {Math.Max(1, limit)} libraries by unsupported-pattern load.");
        else
            Console.WriteLine($"Showing all {reports.Count} libraries.");
        Console.WriteLine();

        Console.WriteLine($"## Top {patternLimit} unsupported patterns");
        Console.WriteLine();
        if (topPatterns.Count == 0)
        {
            Console.WriteLine("- (none)");
        }
        else
        {
            foreach (var pattern in topPatterns)
            {
                Console.WriteLine($"- **{pattern.Name}**: {pattern.Count} across {pattern.LibraryCount} librar{(pattern.LibraryCount == 1 ? "y" : "ies")}");
                foreach (var library in pattern.AffectedLibraries.Take(5))
                    Console.WriteLine($"  - {library.Assembly}: {library.Count} — {string.Join("; ", library.Examples.Take(2).Select(e => $"`{e}`"))}");
            }
        }
        Console.WriteLine();
        Console.WriteLine("## Libraries");
        Console.WriteLine();
        Console.WriteLine("| Assembly | Methods | Full | Fully raised | Full malformed | Bound Full defects | Pass bugs | Top patterns |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var report in reports)
        {
            var top = report.Patterns.Take(Math.Min(3, patternLimit))
                .Select(p => $"{p.Count} {Escape(p.Name)}");
            Console.WriteLine($"| {Escape(report.Assembly)} | {report.TotalMethods} | {CountPercent(report.FullMethods, report.TotalMethods)} | {CountPercent(report.FullyRaisedMethods, report.TotalMethods)} | {report.FullMalformed} | {report.SemanticDefectMethods}/{report.SemanticChecked} | {report.PassBugs} | {string.Join("<br>", top)} |");
        }

        foreach (var report in reports)
        {
            Console.WriteLine();
            Console.WriteLine($"## {report.Assembly}");
            Console.WriteLine();
            Console.WriteLine($"Path: `{report.Path}`");
            Console.WriteLine();
            Console.WriteLine($"Top {patternLimit} unsupported patterns:");
            if (report.Patterns.Count == 0)
            {
                Console.WriteLine("- (none)");
                continue;
            }

            foreach (var pattern in report.Patterns.Take(patternLimit))
            {
                Console.WriteLine($"- **{pattern.Name}**: {pattern.Count}");
                foreach (var example in pattern.Examples.Take(3))
                {
                    Console.WriteLine($"  - `{example}`");
                }
            }
        }
    }

    static string CountPercent(int part, int whole)
        => whole == 0 ? "0/0" : $"{part}/{whole} ({100.0 * part / whole:F2}%)";

    static string Escape(string value)
        => value.Replace("|", "\\|").Replace("\n", " ");

    sealed class MutableReport(string assembly, string path)
    {
        public string Assembly { get; set; } = assembly;
        public string Path { get; } = path;
        public int TotalMethods { get; set; }
        public int FullMethods { get; set; }
        public int PartialMethods { get; set; }
        public int FullyRaisedMethods { get; set; }
        public int RenderedMethods { get; set; }
        public int FullMalformed { get; set; }
        public int PartialMalformed { get; set; }
        public int SemanticChecked { get; set; }
        public int SemanticDefectMethods { get; set; }
        public int PassBugs { get; set; }

        public AssemblyReport ToReport(Dictionary<string, Bucket> buckets)
            => new(
                Assembly,
                Path,
                TotalMethods,
                FullMethods,
                PartialMethods,
                FullyRaisedMethods,
                RenderedMethods,
                FullMalformed,
                PartialMalformed,
                SemanticChecked,
                SemanticDefectMethods,
                PassBugs,
                [.. buckets.Values
                    .OrderByDescending(b => b.Count)
                    .ThenBy(b => b.Name, StringComparer.Ordinal)
                    .Select(b => new PatternReport(b.Name, b.Count, [.. b.Examples]))]);
    }

    sealed class Bucket(string name)
    {
        public string Name { get; } = name;
        public int Count { get; set; }
        public List<string> Examples { get; } = [];
    }
}

internal sealed record AssemblyReport(
    string Assembly,
    string Path,
    int TotalMethods,
    int FullMethods,
    int PartialMethods,
    int FullyRaisedMethods,
    int RenderedMethods,
    int FullMalformed,
    int PartialMalformed,
    int SemanticChecked,
    int SemanticDefectMethods,
    int PassBugs,
    IReadOnlyList<PatternReport> Patterns);

internal sealed record PatternReport(string Name, int Count, IReadOnlyList<string> Examples);

internal sealed record LibraryPortfolioReport(
    IReadOnlyList<PatternSummary> TopPatterns,
    IReadOnlyList<AssemblyReport> Libraries);

internal sealed record PatternSummary(
    string Name,
    int Count,
    int LibraryCount,
    IReadOnlyList<PatternLibrarySummary> AffectedLibraries,
    IReadOnlyList<string> Examples);

internal sealed record PatternLibrarySummary(string Assembly, int Count, IReadOnlyList<string> Examples);
