using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public class PackageCacheTests
{
    [Fact]
    public void TryGet_NonExistentPackage_ReturnsNull()
    {
        var cache = new PackageCache("nugetfetch-test");
        Assert.Null(cache.TryGet("nonexistent-pkg-abc", "1.0.0"));
    }

    [Fact]
    public void Cache_ThenTryGet_ReturnsPath()
    {
        string sourceDir = Path.Combine(Path.GetTempPath(), $"nf-cache-src-{Guid.NewGuid():N}");
        try
        {
            // Create a fake package directory
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "test.nuspec"), "<package/>");

            var cache = new PackageCache("nugetfetch-test-cache");
            string? cached = cache.Cache("test-package", "1.0.0", sourceDir);
            Assert.NotNull(cached);

            string? found = cache.TryGet("test-package", "1.0.0");
            Assert.NotNull(found);
            Assert.True(File.Exists(Path.Combine(found, "test.nuspec")));

            // Clean up cached directory
            Directory.Delete(cached, true);
        }
        finally
        {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, true);
        }
    }

    [Fact]
    public void TryGetLatestCachedVersion_NoVersions_ReturnsNull()
    {
        var cache = new PackageCache("nugetfetch-test");
        Assert.Null(cache.TryGetLatestCachedVersion("nonexistent-pkg-abc"));
    }

    [Fact]
    public void GetCachePath_ReturnsExpectedFormat()
    {
        var cache = new PackageCache("nugetfetch-test");
        string path = cache.GetCachePath("Foo.Bar", "1.2.3");
        Assert.Contains("foo.bar", path);
        Assert.Contains("1.2.3", path);
    }

    [Fact]
    public void CachePath_ContainsAppName()
    {
        var cache = new PackageCache("my-test-app");
        Assert.Contains("my-test-app", cache.CachePath);
    }
}
