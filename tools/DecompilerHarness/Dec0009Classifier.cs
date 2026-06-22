using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Groups DEC0009 (unrepresentable metadata-name) fidelity remarks by the
/// generated-name family that produced them. This is a discovery view: it does
/// not change decompiler output, and it counts affected methods so each category
/// can become a focused implementation or decline issue.
/// </summary>
internal static class Dec0009Classifier
{
    public static int Run(IReadOnlyList<string> assemblies, int maxExamples, bool json)
    {
        var reports = new List<Dec0009AssemblyReport>();
        using var metadata = CorpusMetadata.Create(assemblies);

        foreach (var assembly in assemblies)
            reports.Add(AnalyzeAssembly(assembly, metadata, maxExamples));

        var categories = SummarizeCategories(reports, maxExamples);
        var portfolio = new Dec0009PortfolioReport(
            reports.Sum(r => r.TotalMethods),
            reports.Sum(r => r.AffectedMethods),
            reports.Sum(r => r.PrimaryMethods),
            reports.Sum(r => r.Remarks),
            reports.Sum(r => r.PassBugs),
            categories,
            reports);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(portfolio, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            PrintMarkdown(portfolio);
        }

        return portfolio.PassBugs > 0 ? 1 : 0;
    }

    static Dec0009AssemblyReport AnalyzeAssembly(string path, MetadataContext metadata, int maxExamples)
    {
        var buckets = new Dictionary<string, Dec0009Bucket>(StringComparer.Ordinal);
        int total = 0;
        int affected = 0;
        int primaryAffected = 0;
        int remarks = 0;
        int passBugs = 0;
        string assemblyName = Path.GetFileName(path);

        try
        {
            using var source = MetadataSource.Open(path, context: metadata);
            assemblyName = source.AssemblyName;
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                total++;
                string id = $"{typeName}::{methodName}";
                try
                {
                    var context = new PassContext(
                        new Stepper(enabled: false),
                        importMethodBody: method => IrImporter.Import(source, method));
                    IrPasses.Run(function, IrPasses.Default, context);
                }
                catch
                {
                    passBugs++;
                    continue;
                }

                var allRemarks = FidelityRemarks.Collect(function);
                var methodRemarks = allRemarks
                    .Where(r => r.Code == DiagnosticIds.UnrepresentableMetadataName)
                    .ToArray();
                if (methodRemarks.Length == 0)
                    continue;

                bool primary = Completeness.Residual(function) is null
                    && function.Fidelity != DecompilationFidelity.Full
                    && allRemarks.FirstOrDefault()?.Code == DiagnosticIds.UnrepresentableMetadataName;
                affected++;
                if (primary)
                    primaryAffected++;
                remarks += methodRemarks.Length;
                var category = Classify(id, methodRemarks);
                if (!buckets.TryGetValue(category, out var bucket))
                {
                    buckets[category] = bucket = new Dec0009Bucket(category);
                }
                bucket.Methods++;
                if (primary)
                    bucket.PrimaryMethods++;
                bucket.Remarks += methodRemarks.Length;
                if (bucket.Examples.Count < maxExamples)
                    bucket.Examples.Add(new Dec0009Example(id, methodRemarks[0].Reason, methodRemarks[0].Node));
            }
        }
        catch
        {
            passBugs++;
        }

        return new Dec0009AssemblyReport(
            assemblyName,
            path,
            total,
            affected,
            primaryAffected,
            remarks,
            passBugs,
            [.. buckets.Values
                .OrderByDescending(b => b.Methods)
                .ThenBy(b => b.Category, StringComparer.Ordinal)
                .Select(b => new Dec0009CategoryReport(b.Category, b.Methods, b.PrimaryMethods, b.Remarks, [.. b.Examples]))]);
    }

    internal static string Classify(string methodId, IReadOnlyList<FidelityRemarks.Remark> remarks)
    {
        string haystack = methodId + "\n" + string.Join('\n', remarks.Select(r => r.Reason + "\n" + r.Node));

        if (ContainsAny(haystack, "<>f__AnonymousType", "AnonymousType"))
            return "compiler-generated anonymous type";
        if (ContainsAny(haystack, "<>c__DisplayClass", "DisplayClass"))
            return "compiler-generated display class";
        if (ContainsAny(haystack, "<>z__ReadOnlyArray"))
            return "compiler-generated read-only array helper";
        if (ContainsAny(haystack, "System.Text.RegularExpressions.Generated", "RegexGenerator", "GeneratedRegex"))
            return "regex/source-generator generated type";
        if (ContainsAny(haystack, "<PrivateImplementationDetails>"))
            return "compiler-generated private implementation details";
        if (ContainsAny(haystack, "<>O"))
            return "compiler-generated method-group cache";
        if (ContainsAny(haystack, "<>c", "<>9", ">b__"))
            return "compiler-generated lambda/cache holder";
        if (ContainsAny(haystack, ">d__", "<>1__state", "<>t__builder", "<>u__"))
            return "compiler-generated state machine";
        if (ContainsAny(haystack, "<", ">"))
            return "other compiler-generated metadata";
        return "other unspellable metadata";
    }

    static IReadOnlyList<Dec0009CategoryReport> SummarizeCategories(
        IReadOnlyList<Dec0009AssemblyReport> reports,
        int maxExamples)
    {
        return [.. reports
            .SelectMany(report => report.Categories)
            .GroupBy(category => category.Category, StringComparer.Ordinal)
            .Select(group => new Dec0009CategoryReport(
                group.Key,
                group.Sum(c => c.Methods),
                group.Sum(c => c.PrimaryMethods),
                group.Sum(c => c.Remarks),
                [.. group.SelectMany(c => c.Examples).Take(maxExamples)]))
            .OrderByDescending(c => c.Methods)
            .ThenBy(c => c.Category, StringComparer.Ordinal)];
    }

    static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));

    static void PrintMarkdown(Dec0009PortfolioReport report)
    {
        Console.WriteLine("# DEC0009 classification");
        Console.WriteLine();
        Console.WriteLine($"Methods scanned: {report.TotalMethods}");
        Console.WriteLine($"Affected methods: {report.AffectedMethods}");
        Console.WriteLine($"Primary fidelity: DEC0009 methods: {report.PrimaryMethods}");
        Console.WriteLine($"DEC0009 remarks: {report.Remarks}");
        Console.WriteLine($"Pass bugs: {report.PassBugs}");
        Console.WriteLine();
        Console.WriteLine("## Categories");
        Console.WriteLine();
        foreach (var category in report.Categories)
        {
            Console.WriteLine($"- **{category.Category}**: {category.Methods} methods ({category.PrimaryMethods} primary), {category.Remarks} remarks");
            foreach (var example in category.Examples.Take(3))
                Console.WriteLine($"  - `{example.Method}` — {example.Reason}");
        }
        Console.WriteLine();
        Console.WriteLine("## Assemblies");
        Console.WriteLine();
        Console.WriteLine("| Assembly | Methods | Affected | Primary | Remarks | Pass bugs | Top categories |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var assembly in report.Assemblies)
        {
            var top = assembly.Categories.Take(3)
                .Select(c => $"{c.Methods}/{c.PrimaryMethods} {Escape(c.Category)}");
            Console.WriteLine($"| {Escape(assembly.Assembly)} | {assembly.TotalMethods} | {assembly.AffectedMethods} | {assembly.PrimaryMethods} | {assembly.Remarks} | {assembly.PassBugs} | {string.Join("<br>", top)} |");
        }
    }

    static string Escape(string value)
        => value.Replace("|", "\\|").Replace("\n", " ");

    sealed class Dec0009Bucket(string category)
    {
        public string Category { get; } = category;
        public int Methods { get; set; }
        public int PrimaryMethods { get; set; }
        public int Remarks { get; set; }
        public List<Dec0009Example> Examples { get; } = [];
    }
}

internal sealed record Dec0009PortfolioReport(
    int TotalMethods,
    int AffectedMethods,
    int PrimaryMethods,
    int Remarks,
    int PassBugs,
    IReadOnlyList<Dec0009CategoryReport> Categories,
    IReadOnlyList<Dec0009AssemblyReport> Assemblies);

internal sealed record Dec0009AssemblyReport(
    string Assembly,
    string Path,
    int TotalMethods,
    int AffectedMethods,
    int PrimaryMethods,
    int Remarks,
    int PassBugs,
    IReadOnlyList<Dec0009CategoryReport> Categories);

internal sealed record Dec0009CategoryReport(
    string Category,
    int Methods,
    int PrimaryMethods,
    int Remarks,
    IReadOnlyList<Dec0009Example> Examples);

internal sealed record Dec0009Example(string Method, string Reason, string Node);
