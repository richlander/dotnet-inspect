using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

[Collection(CoreCacheCollection.Name)]
public class PackageCacheServiceTests
{
    [Fact]
    public void GetCacheInfo_ReturnsLocation()
    {
        NuGetCache.Initialize("dotnet-inspect-test");
        var info = PackageCacheService.GetCacheInfo();

        Assert.NotNull(info.Location);
        Assert.Contains("dotnet-inspect-test", info.Location);
    }

    [Fact]
    public void GetCacheInfo_EmptyCache_ReturnsZeroStats()
    {
        NuGetCache.Initialize("dotnet-inspect-cache-test-empty-" + Guid.NewGuid().ToString("N")[..8]);
        var info = PackageCacheService.GetCacheInfo();

        Assert.Empty(info.Categories);
        Assert.Equal(0, info.TotalSize);
    }

    [Fact]
    public void ClearCache_EmptyCache_ReturnsZero()
    {
        NuGetCache.Initialize("dotnet-inspect-cache-test-clean-" + Guid.NewGuid().ToString("N")[..8]);
        long freed = PackageCacheService.ClearCache();

        Assert.Equal(0, freed);
    }

    [Fact]
    public void CacheInfo_Record_Properties()
    {
        var categories = new List<PackageCacheService.CacheCategoryInfo>
        {
            new("Packages", 1024, 5),
            new("Sources", 512, 10)
        };
        var info = new PackageCacheService.CacheInfo("/some/path", categories, 1536);

        Assert.Equal("/some/path", info.Location);
        Assert.Equal(2, info.Categories.Count);
        Assert.Equal(1536, info.TotalSize);
    }

    [Fact]
    public void CacheCategoryInfo_Record_Properties()
    {
        var category = new PackageCacheService.CacheCategoryInfo("Packages", 2048, 3);

        Assert.Equal("Packages", category.Name);
        Assert.Equal(2048, category.Size);
        Assert.Equal(3, category.Count);
    }
}
