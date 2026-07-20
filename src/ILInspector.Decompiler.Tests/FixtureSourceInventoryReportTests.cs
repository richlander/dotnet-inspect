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
        var dynamicSites = report.Rows
            .Where(row => row.Population == FixtureSourcePopulation.Dynamic)
            .Select(row => row.Id)
            .ToArray();
        Assert.Equal(
            DecompilerFixtureSourceInventory.ClassifiedDynamicCompilationSiteCount,
            dynamicSites.Length);
        Assert.Equal(
            DecompilerFixtureSourceInventory.ClassifiedDynamicCompilationSiteSetFingerprint,
            DecompilerFixtureSourceInventory.ComputeSiteSetFingerprint(dynamicSites));
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

    [Fact]
    public void DynamicSiteDiscovery_UsesSyntaxAndStableMemberIdentity()
    {
        const string source = """
            class C
            {
                void M()
                {
                    // CSharpCompilation.Create("comment");
                    var text = "CSharpCompilation.Create";
                    CSharpCompilation
                        .Create("one");
                    CSharpCompilation.Create("two");
                }
            }
            """;

        var sites = DecompilerFixtureSourceInventory.DiscoverCSharpCompilationSites(
            source,
            "Fixture.cs");

        Assert.Equal(2, sites.Count);
        Assert.All(sites, site => Assert.StartsWith("Fixture.cs::C`0.M()@", site));
        Assert.NotEqual(sites[0], sites[1]);
    }
}
