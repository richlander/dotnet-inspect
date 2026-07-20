using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class FixtureSourceInventoryReportTests
{
    [Fact]
    public void Inventory_CoversBuiltAndGeneratedPopulations()
    {
        var report = DecompilerFixtureSourceInventory.Create();

        Assert.Equal(
            FixtureCatalog.SelectByTag("decompiler").Count,
            report.Rows.Count(row => row.Population == FixtureSourcePopulation.Built));
        Assert.Equal(
            GeneratedFixtureCatalog.Catalog.Count,
            report.Rows.Count(row => row.Population == FixtureSourcePopulation.Generated));
        Assert.All(report.Rows, row =>
            Assert.Equal(FixtureSourceInventoryStatus.SourceDiscovered, row.Status));
        Assert.Equal(2, report.Rows.Select(row => row.Population).Distinct().Count());
    }

    [Fact]
    public void Inventory_FormatsMachineReadableAndMarkdownReports()
    {
        var report = DecompilerFixtureSourceInventory.Create();

        string markdown = DecompilerFixtureSourceInventory.Format(report, json: false);
        string json = DecompilerFixtureSourceInventory.Format(report, json: true);

        Assert.Contains("| Built |", markdown);
        Assert.Contains("| Generated |", markdown);
        Assert.DoesNotContain("| Dynamic |", markdown);
        Assert.Contains("\"Rows\"", json);
        Assert.Contains("\"Population\": \"Built\"", json);
    }

}
