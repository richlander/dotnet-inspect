using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for NuGetCache validation and caching logic.
/// </summary>
public class NuGetCacheTests
{
    [Fact]
    public void IsCachedPackageValid_WithPackageName_ChecksSpecificNuspec()
    {
        // Create a temp directory with a properly named nuspec
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a nuspec file with the expected name
            var nuspecPath = Path.Combine(tempDir, "testpackage.nuspec");
            File.WriteAllText(nuspecPath, "<package />");

            // Should find it with the package name
            Assert.True(NuGetCache.IsCachedPackageValid(tempDir, "testpackage"));

            // Should not find it with wrong package name (fast path)
            Assert.False(NuGetCache.IsCachedPackageValid(tempDir, "wrongname"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsCachedPackageValid_WithoutPackageName_ScansForAnyNuspec()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a nuspec with any name
            var nuspecPath = Path.Combine(tempDir, "some.random.nuspec");
            File.WriteAllText(nuspecPath, "<package />");

            // Should find it without package name (scan mode)
            Assert.True(NuGetCache.IsCachedPackageValid(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsCachedPackageValid_WithLibDirectory_IsValid()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-cache-test-{Guid.NewGuid():N}");
        var libDir = Path.Combine(tempDir, "lib");
        Directory.CreateDirectory(libDir);

        try
        {
            // Package with lib directory but no nuspec is valid
            Assert.True(NuGetCache.IsCachedPackageValid(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsCachedPackageValid_EmptyDirectory_IsNotValid()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Assert.False(NuGetCache.IsCachedPackageValid(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsCachedPackageValid_NonExistentDirectory_IsNotValid()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}");

        Assert.False(NuGetCache.IsCachedPackageValid(fakePath));
    }

    [Fact]
    public void GetAppCacheBasePath_ReturnsNonEmptyPath()
    {
        var path = NuGetCache.GetAppCacheBasePath();

        Assert.False(string.IsNullOrEmpty(path));
        Assert.Contains("dotnet-inspect", path);
    }
}
