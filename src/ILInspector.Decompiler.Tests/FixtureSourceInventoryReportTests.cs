using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class FixtureSourceInventoryReportTests
{
    [Fact]
    public void Inventory_CoversBuiltGeneratedAndDynamicPopulations()
    {
        var report = DecompilerFixtureSourceInventory.Create();

        Assert.Equal(
            FixtureCatalog.SelectByTag("decompiler").Count,
            report.Rows.Count(row => row.Population == FixtureSourcePopulation.Built));
        Assert.Equal(
            GeneratedFixtureCatalog.Catalog.Count,
            report.Rows.Count(row => row.Population == FixtureSourcePopulation.Generated));
        Assert.Contains(report.Rows, row => row.Population == FixtureSourcePopulation.Dynamic);
        Assert.All(
            report.Rows.Where(row => row.Population != FixtureSourcePopulation.Dynamic),
            row => Assert.Equal(FixtureSourceInventoryStatus.SourceDiscovered, row.Status));
        Assert.All(
            report.Rows.Where(row => row.Population == FixtureSourcePopulation.Dynamic),
            row => Assert.Equal(FixtureSourceInventoryStatus.Unclassified, row.Status));
        Assert.Equal(
            DecompilerFixtureSourceInventory.DynamicCompilationSiteBaseline,
            report.Rows.Count(row => row.Population == FixtureSourcePopulation.Dynamic));
    }

    [Fact]
    public void Inventory_FormatsMachineReadableAndMarkdownReports()
    {
        var report = DecompilerFixtureSourceInventory.Create();

        string markdown = DecompilerFixtureSourceInventory.Format(report, json: false);
        string json = DecompilerFixtureSourceInventory.Format(report, json: true);

        Assert.Contains("| Built |", markdown);
        Assert.Contains("| Generated |", markdown);
        Assert.Contains("| Dynamic |", markdown);
        Assert.Contains("\"Rows\"", json);
        Assert.Contains("\"Population\": \"Built\"", json);
    }
}
