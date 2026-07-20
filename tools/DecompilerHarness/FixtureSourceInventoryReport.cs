using System.Text;
using System.Text.Json;

using DotnetInspector.Fixtures;

namespace ILInspector.DecompilerHarness;

internal enum FixtureSourcePopulation
{
    Built,
    Generated,
    Dynamic,
}

internal sealed record DecompilerFixtureSourceRow(
    FixtureSourcePopulation Population,
    string Id,
    FixtureSourceInventoryStatus Status,
    int DocumentCount,
    int TargetCount,
    string? Reason);

internal sealed record DecompilerFixtureSourceReport(
    IReadOnlyList<DecompilerFixtureSourceRow> Rows)
{
    public int SourceDiscovered => Rows.Count(row =>
        row.Status == FixtureSourceInventoryStatus.SourceDiscovered);

    public int Unresolved => Rows.Count(row =>
        row.Status is FixtureSourceInventoryStatus.Unclassified
            or FixtureSourceInventoryStatus.SourceMissing);
}

internal static class DecompilerFixtureSourceInventory
{
    // Deliberate migration baseline. Adding or removing an anonymous compiler
    // site requires updating this count together with its source classification.
    internal const int DynamicCompilationSiteBaseline = 35;

    public static DecompilerFixtureSourceReport Create()
    {
        var built = FixtureSourceInventory
            .Create(FixtureCatalog.SelectByTag("decompiler"))
            .Fixtures
            .Select(row => new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Built,
                row.FixtureId,
                row.Status,
                row.DiscoveredDocumentCount,
                0,
                row.Reason));

        var generated = GeneratedFixtureCatalog.Catalog.Select(fixture =>
            new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Generated,
                fixture.Id,
                string.IsNullOrWhiteSpace(fixture.Source)
                    ? FixtureSourceInventoryStatus.SourceMissing
                    : FixtureSourceInventoryStatus.SourceDiscovered,
                string.IsNullOrWhiteSpace(fixture.Source) ? 0 : 1,
                fixture.Targets.Count,
                string.IsNullOrWhiteSpace(fixture.Source)
                    ? "The generated fixture has no retained source."
                    : null));

        var dynamic = DiscoverDynamicCompilationSites().Select(site =>
            new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Dynamic,
                site,
                FixtureSourceInventoryStatus.Unclassified,
                0,
                0,
                "Test-local compilation has not migrated to the source-retaining materializer."));

        return new([.. built, .. generated, .. dynamic]);
    }

    public static string Format(DecompilerFixtureSourceReport report, bool json)
    {
        if (json)
        {
            return JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
        }

        var output = new StringBuilder();
        output.AppendLine("# DECOMPILER FIXTURE SOURCE INVENTORY");
        output.AppendLine();
        output.AppendLine("| Population | Entries | Source discovered | Unresolved |");
        output.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var population in Enum.GetValues<FixtureSourcePopulation>())
        {
            var rows = report.Rows.Where(row => row.Population == population).ToArray();
            output.AppendLine($"| {population} | {rows.Length} | "
                + $"{rows.Count(row => row.Status == FixtureSourceInventoryStatus.SourceDiscovered)} | "
                + $"{rows.Count(row => row.Status is FixtureSourceInventoryStatus.Unclassified or FixtureSourceInventoryStatus.SourceMissing)} |");
        }
        return output.ToString();
    }

    static IReadOnlyList<string> DiscoverDynamicCompilationSites()
    {
        string root = RepositoryRoot();
        string testRoot = Path.Combine(root, "src", "ILInspector.Decompiler.Tests");
        var sites = new List<string>();
        foreach (string path in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            int lineNumber = 0;
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                if (!line.Contains("CSharpCompilation.Create", StringComparison.Ordinal)
                    && !line.Contains("RoslynTestCompiler", StringComparison.Ordinal))
                {
                    continue;
                }

                sites.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{lineNumber}");
            }
        }
        return sites;
    }

    static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root for dynamic fixture inventory.");
    }
}
