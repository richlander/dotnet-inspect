using System.Reflection;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class ApiTypeInventoryCountTests
{
    [Fact]
    public void CountSummaryTypes_MatchesCompactInventory()
    {
        string path = FixtureCatalog.MetadataMemorySafety.AssemblyPath();
        using var summaryStream = File.OpenRead(path);
        using var summaryReader = new PEReader(summaryStream);
        ApiSurface summary =
            ApiSurfaceExtractor.ExtractSummary(summaryReader);

        using var countStream = File.OpenRead(path);
        using var countReader = new PEReader(countStream);
        ApiTypeInventoryCountResult result =
            ApiSurfaceExtractor.CountSummaryTypes(countReader);
        if (result is ApiTypeInventoryCountResult.Declined declined)
            Assert.Fail($"{declined.Reason}: {declined.Detail}");
        var counted = Assert.IsType<
            ApiTypeInventoryCountResult.Counted>(
                result);

        Assert.NotEqual(Guid.Empty, counted.Count.ModuleVersionId);
        Assert.Equal(summary.Types.Count, counted.Count.Total);
        Assert.Equal(
            summary.Types.Count(type => type.Kind == "class"),
            counted.Count.Classes);
        Assert.Equal(
            summary.Types.Count(type => type.Kind == "struct"),
            counted.Count.Structs);
        Assert.Equal(
            summary.Types.Count(type => type.Kind == "interface"),
            counted.Count.Interfaces);
        Assert.Equal(
            summary.Types.Count(type => type.Kind == "enum"),
            counted.Count.Enums);
        Assert.Equal(
            summary.Types.Count(type => type.Kind == "delegate"),
            counted.Count.Delegates);
    }

    [Fact]
    public void ExtractSummary_MatchesFullInventoryKinds()
    {
        string path = FixtureCatalog.MetadataMemorySafety.AssemblyPath();
        using var fullStream = File.OpenRead(path);
        using var fullReader = new PEReader(fullStream);
        ApiSurface full = ApiSurfaceExtractor.Extract(fullReader);

        using var summaryStream = File.OpenRead(path);
        using var summaryReader = new PEReader(summaryStream);
        ApiSurface summary =
            ApiSurfaceExtractor.ExtractSummary(summaryReader);

        Assert.Equal(
            full.Types
                .Select(type => (type.FullName, type.Kind))
                .Order(),
            summary.Types
                .Select(type => (type.FullName, type.Kind))
                .Order());
    }

    [Fact]
    public void CountSummaryTypes_MatchesCoreLibraryFullSurface()
    {
        string path = typeof(object).Assembly.Location;
        using var fullStream = File.OpenRead(path);
        using var fullReader = new PEReader(fullStream);
        ApiSurface full = ApiSurfaceExtractor.Extract(fullReader);

        using var countStream = File.OpenRead(path);
        using var countReader = new PEReader(countStream);
        var counted = Assert.IsType<
            ApiTypeInventoryCountResult.Counted>(
                ApiSurfaceExtractor.CountSummaryTypes(countReader));

        Assert.Equal(
            full.Types.Count(type => type.Kind == "class"),
            counted.Count.Classes);
        Assert.Equal(
            full.Types.Count(type => type.Kind == "struct"),
            counted.Count.Structs);
        Assert.Equal(
            full.Types.Count(type => type.Kind == "interface"),
            counted.Count.Interfaces);
        Assert.Equal(
            full.Types.Count(type => type.Kind == "enum"),
            counted.Count.Enums);
        Assert.Equal(
            full.Types.Count(type => type.Kind == "delegate"),
            counted.Count.Delegates);
    }

    [Fact]
    public void CountSummaryTypes_DeclinesTypeForwarders()
    {
        string path =
            Assembly.Load(new AssemblyName("System.Runtime")).Location;
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);

        Assert.IsType<ApiTypeInventoryCountResult.Declined>(
            ApiSurfaceExtractor.CountSummaryTypes(reader));
    }

    [Fact]
    public void CountApiTypeInventory_UsesOwnedImageLifetime()
    {
        string path = FixtureCatalog.MetadataMemorySafety.AssemblyPath();

        var counted = Assert.IsType<
            ApiTypeInventoryCountResult.Counted>(
                AssemblyReader.CountApiTypeInventory(path));

        Assert.True(counted.Count.Total > 0);
    }
}
