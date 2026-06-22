using System.Text.Json;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Focused inventory for the <see cref="UnsupportedNode"/> completeness bucket.
/// The general gaps/library views intentionally show only a few examples per
/// bucket; this view keeps every marker visible so small buckets can be
/// classified into intentional unrepresentable IL versus missing coverage.
/// </summary>
internal static class UnsupportedNodeReport
{
    public static int Run(IReadOnlyList<string> assemblies, int maxExamples, bool json)
    {
        List<UnsupportedNodeSite> sites = [];
        int totalMethods = 0;
        int passBugs = 0;

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assembly in assemblies)
        {
            using var source = MetadataSource.Open(assembly, context: metadata);
            string assemblyName = source.AssemblyName;
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                totalMethods++;
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

                foreach (var unsupported in function.Descendants.OfType<UnsupportedNode>())
                {
                    sites.Add(new UnsupportedNodeSite(
                        assemblyName,
                        typeName,
                        methodName,
                        unsupported.ILOffset,
                        unsupported.Opcode,
                        unsupported.Reason));
                }
            }
        }

        var groups = sites
            .GroupBy(site => new { site.Opcode, Reason = CategoryReason(site) })
            .Select(group => new UnsupportedNodeGroup(
                group.Key.Opcode,
                group.Key.Reason,
                group.Count(),
                [.. group
                    .OrderBy(site => site.Assembly, StringComparer.Ordinal)
                    .ThenBy(site => site.Type, StringComparer.Ordinal)
                    .ThenBy(site => site.Method, StringComparer.Ordinal)
                    .Take(maxExamples)]))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Opcode, StringComparer.Ordinal)
            .ThenBy(group => group.Reason, StringComparer.Ordinal)
            .ToArray();

        var report = new UnsupportedNodePortfolioReport(totalMethods, passBugs, sites.Count, groups);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            PrintMarkdown(report);
        }

        return passBugs > 0 ? 1 : 0;
    }

    static void PrintMarkdown(UnsupportedNodePortfolioReport report)
    {
        Console.WriteLine("# Unsupported-node report");
        Console.WriteLine();
        Console.WriteLine($"Methods scanned: {report.TotalMethods}");
        Console.WriteLine($"Pass bugs: {report.PassBugs}");
        Console.WriteLine($"Unsupported node sites: {report.SiteCount}");
        Console.WriteLine();

        if (report.Groups.Count == 0)
        {
            Console.WriteLine("- (none)");
            return;
        }

        foreach (var group in report.Groups)
        {
            Console.WriteLine($"- **{Escape(group.Opcode)}**: {group.Count} — {Escape(group.Reason)}");
            foreach (var site in group.Examples)
            {
                Console.WriteLine($"  - `{Escape(site.Assembly)}::{Escape(site.Type)}::{Escape(site.Method)} @ IL_{site.ILOffset:X4}`");
            }
        }
    }

    static string Escape(string value)
        => value.Replace("|", "\\|").Replace("\n", " ");

    static string CategoryReason(UnsupportedNodeSite site)
        => site.Opcode == "iterator" && site.Reason.Contains("yield body (MoveNext) not reconstructed", StringComparison.Ordinal)
            ? "compiler-generated iterator state machine; yield body (MoveNext) not reconstructed"
            : site.Reason;
}

internal sealed record UnsupportedNodePortfolioReport(
    int TotalMethods,
    int PassBugs,
    int SiteCount,
    IReadOnlyList<UnsupportedNodeGroup> Groups);

internal sealed record UnsupportedNodeGroup(
    string Opcode,
    string Reason,
    int Count,
    IReadOnlyList<UnsupportedNodeSite> Examples);

internal sealed record UnsupportedNodeSite(
    string Assembly,
    string Type,
    string Method,
    int ILOffset,
    string Opcode,
    string Reason);
