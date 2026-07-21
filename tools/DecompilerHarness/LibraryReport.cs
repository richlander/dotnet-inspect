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
    public static int Run(
        IReadOnlyList<string> assemblies,
        int compileCap,
        int maxExamples,
        bool json,
        int topPatterns,
        int? topLibraries,
        int methodCap)
    {
        var report = Evaluate(
            assemblies,
            compileCap,
            maxExamples,
            topPatterns,
            topLibraries,
            methodCap);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            PrintMarkdown(report, Math.Max(1, topPatterns), topLibraries);
        }

        return report.TotalPassBugs > 0 ? 1 : 0;
    }

    internal static LibraryPortfolioReport Evaluate(
        IReadOnlyList<string> assemblies,
        int compileCap,
        int maxExamples,
        int topPatterns,
        int? topLibraries,
        int methodCap)
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
            // Import private members too — see ValidityCheck for the rationale: a
            // faithful call to a non-public overload (private generic helpers, internal
            // ctors) otherwise mis-binds to a public sibling and reports a phantom
            // binding error (e.g. CS0308 on `Enum.ToString<sbyte, byte>`). Keep this in
            // sync with ValidityCheck so the aggregate report matches --validity-check.
            .WithMetadataImportOptions(MetadataImportOptions.All);

        foreach (var assembly in assemblies)
        {
            reports.Add(AnalyzeAssembly(
                assembly,
                metadata,
                references,
                parseOptions,
                compileOptions,
                compileCap,
                maxExamples,
                methodCap));
        }

        int patternLimit = Math.Max(1, topPatterns);
        var selectedReports = SelectLibraries(reports, topLibraries);
        return BuildPortfolio(
            reports,
            patternLimit,
            maxExamples,
            methodCap,
            compileCap,
            selectedReports);
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
        int maxExamples,
        int methodCap)
    {
        var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        var report = new MutableReport(Path.GetFileName(path), path);

        try
        {
            using var source = MetadataSource.Open(path, context: metadata);
            report.Assembly = source.AssemblyName;
            report.AvailableMethods = source.Reader.MethodDefinitions
                .Count(handle => source.Reader.GetMethodDefinition(handle).RelativeVirtualAddress != 0);
            var constraints = ShellConstraints.Build(source);
            foreach (var (typeName, methodName, function) in
                IrImporter.ImportAssemblyStableSample(source, methodCap))
            {
                report.TotalMethods++;
                var id = $"{typeName}::{methodName}";
                try
                {
                    var context = new PassContext(
                        new Stepper(enabled: false),
                        importMethodBody: method => IrImporter.Import(source, method));
                    IrPasses.Run(function, IrPasses.Default, context);
                }
                catch (Exception ex)
                {
                    report.PassBugs++;
                    Record(buckets, $"pass-bug: {ex.GetType().Name}", id, maxExamples);
                    continue;
                }

                var fidelityCensus = FidelityCauseBuckets.Inspect(function, id);
                if (!fidelityCensus.Succeeded)
                {
                    report.PassBugs++;
                    Record(buckets, $"pass-bug: {fidelityCensus.ErrorCode}", id, maxExamples);
                    continue;
                }

                bool full = fidelityCensus.Causes.IsEmpty;
                if (full)
                {
                    report.FullMethods++;
                }
                else
                {
                    report.PartialMethods++;
                }

                string? residual = Completeness.Residual(function)
                    ?? (!full ? $"fidelity: {FidelityCauseBuckets.PrimaryBucket(fidelityCensus)}" : null);
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
                    rendered = CSharpPrinter.Print(function).Output;
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
                var semanticModel = compilation.GetSemanticModel(tree);
                var defects = ValidityCheck.ClassifySemanticDiagnostics(
                    compilation.GetDiagnostics(),
                    tree,
                    function,
                    semanticModel);
                if (defects.Length == 0)
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

    internal static LibraryPortfolioReport BuildPortfolio(
        IReadOnlyList<AssemblyReport> reports,
        int topPatterns,
        int maxExamples,
        int methodCap = int.MaxValue,
        int semanticCompileCap = int.MaxValue,
        IReadOnlyList<AssemblyReport>? displayedLibraries = null)
    {
        var allPatterns = TopPatterns(reports, int.MaxValue, maxExamples);
        return new LibraryPortfolioReport(
            methodCap,
            semanticCompileCap,
            reports.Sum(report => report.PassBugs),
            [.. allPatterns.Where(pattern => IsCorrectnessDefect(pattern.Name))],
            PromotionCandidates(reports),
            [.. allPatterns
                .Where(pattern => !IsCorrectnessDefect(pattern.Name))
                .Take(Math.Max(1, topPatterns))],
            displayedLibraries ?? reports);
    }

    static bool IsCorrectnessDefect(string pattern)
        => pattern.StartsWith("assembly-open:", StringComparison.Ordinal)
            || pattern.StartsWith("pass-bug:", StringComparison.Ordinal)
            || pattern.StartsWith("validity:", StringComparison.Ordinal);

    static IReadOnlyList<PromotionCandidate> PromotionCandidates(
        IReadOnlyList<AssemblyReport> reports)
        => [.. reports
            .Select(report =>
            {
                var reasons = new List<string>();
                if (report.PassBugs > 0)
                    reasons.Add($"{report.PassBugs} pass bug method(s)");
                if (report.FullMalformed > 0)
                    reasons.Add($"{report.FullMalformed} malformed Full method(s)");
                if (report.SemanticDefectMethods > 0)
                    reasons.Add($"{report.SemanticDefectMethods} bound Full defect method(s)");
                var defectPatterns = report.Patterns
                    .Where(pattern => IsCorrectnessDefect(pattern.Name))
                    .ToArray();
                if (defectPatterns.Length > 0)
                    reasons.Add($"defect classes: {string.Join(", ", defectPatterns.Select(pattern => pattern.Name))}");
                return new PromotionCandidate(report.Assembly, reasons);
            })
            .Where(candidate => candidate.Reasons.Count > 0)
            .OrderBy(candidate => candidate.Assembly, StringComparer.Ordinal)];

    static void PrintMarkdown(
        LibraryPortfolioReport report,
        int patternLimit,
        int? topLibraries)
    {
        Console.WriteLine("# Decompiler library report");
        Console.WriteLine();
        if (topLibraries is { } limit)
            Console.WriteLine($"Showing top {Math.Max(1, limit)} libraries by unsupported-pattern load.");
        else
            Console.WriteLine($"Showing all {report.Libraries.Count} libraries.");
        if (report.MethodCap != int.MaxValue)
            Console.WriteLine($"Methods are a deterministic hash-ranked sample of at most {report.MethodCap} per library.");
        if (report.SemanticCompileCap != int.MaxValue)
            Console.WriteLine($"Semantic validity is checked for at most {report.SemanticCompileCap} Full methods per library.");
        Console.WriteLine();

        Console.WriteLine("## Correctness defect-class docket");
        Console.WriteLine();
        if (report.DefectClasses.Count == 0)
        {
            Console.WriteLine("- (none)");
        }
        else
        {
            foreach (var defect in report.DefectClasses)
                Console.WriteLine($"- **{defect.Name}**: {defect.Count} across {defect.LibraryCount} librar{(defect.LibraryCount == 1 ? "y" : "ies")} — {string.Join("; ", defect.Examples.Take(2).Select(example => $"`{example}`"))}");
        }
        Console.WriteLine();

        Console.WriteLine("## Promotion candidates");
        Console.WriteLine();
        if (report.PromotionCandidates.Count == 0)
        {
            Console.WriteLine("- (none)");
        }
        else
        {
            foreach (var candidate in report.PromotionCandidates)
                Console.WriteLine($"- **{candidate.Assembly}**: {string.Join("; ", candidate.Reasons)}");
        }
        Console.WriteLine();

        Console.WriteLine($"## Top {patternLimit} unsupported patterns");
        Console.WriteLine();
        if (report.TopPatterns.Count == 0)
        {
            Console.WriteLine("- (none)");
        }
        else
        {
            foreach (var pattern in report.TopPatterns)
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
        foreach (var library in report.Libraries)
        {
            var top = library.Patterns.Take(Math.Min(3, patternLimit))
                .Select(p => $"{p.Count} {Escape(p.Name)}");
            Console.WriteLine($"| {Escape(library.Assembly)} | {MethodCount(library)} | {CountPercent(library.FullMethods, library.TotalMethods)} | {CountPercent(library.FullyRaisedMethods, library.TotalMethods)} | {library.FullMalformed} | {library.SemanticDefectMethods}/{library.SemanticChecked} | {library.PassBugs} | {string.Join("<br>", top)} |");
        }

        foreach (var library in report.Libraries)
        {
            Console.WriteLine();
            Console.WriteLine($"## {library.Assembly}");
            Console.WriteLine();
            Console.WriteLine($"Path: `{library.Path}`");
            Console.WriteLine();
            Console.WriteLine($"Top {patternLimit} unsupported patterns:");
            if (library.Patterns.Count == 0)
            {
                Console.WriteLine("- (none)");
                continue;
            }

            foreach (var pattern in library.Patterns.Take(patternLimit))
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

    static string MethodCount(AssemblyReport report)
        => report.TotalMethods == report.AvailableMethods
            ? report.TotalMethods.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{report.TotalMethods}/{report.AvailableMethods} sampled";

    static string Escape(string value)
        => value.Replace("|", "\\|").Replace("\n", " ");

    sealed class MutableReport(string assembly, string path)
    {
        public string Assembly { get; set; } = assembly;
        public string Path { get; } = path;
        public int AvailableMethods { get; set; }
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
                AvailableMethods,
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
    int AvailableMethods,
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
    int MethodCap,
    int SemanticCompileCap,
    int TotalPassBugs,
    IReadOnlyList<PatternSummary> DefectClasses,
    IReadOnlyList<PromotionCandidate> PromotionCandidates,
    IReadOnlyList<PatternSummary> TopPatterns,
    IReadOnlyList<AssemblyReport> Libraries);

internal sealed record PromotionCandidate(
    string Assembly,
    IReadOnlyList<string> Reasons);

internal sealed record PatternSummary(
    string Name,
    int Count,
    int LibraryCount,
    IReadOnlyList<PatternLibrarySummary> AffectedLibraries,
    IReadOnlyList<string> Examples);

internal sealed record PatternLibrarySummary(string Assembly, int Count, IReadOnlyList<string> Examples);
