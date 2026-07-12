using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageIndexCacheTests : IDisposable
{
    private readonly string _cacheBasePath = Path.Combine(
        Path.GetTempPath(),
        "dotnet-inspect-package-index-cache-tests-" + Guid.NewGuid().ToString("N"));

    public PackageIndexCacheTests()
    {
        CoreCache.Initialize(
            "dotnet-inspect-package-index-cache-tests-" + Guid.NewGuid().ToString("N"),
            basePath: _cacheBasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheBasePath))
            Directory.Delete(_cacheBasePath, recursive: true);
    }

    [Theory]
    [InlineData("[1.0.0,2.0.0)")]
    [InlineData("[3.0.0,)")]
    [InlineData("(,4.0.0]")]
    public void DependencyRange_RoundTripsWithoutSplitting(string versionRange)
    {
        const string packageName = "Package.With.Range";
        const string packageVersion = "1.0.0";
        var result = new InspectionResult
        {
            PackageName = packageName,
            Version = packageVersion,
            DependencyGroups =
            [
                new DependencyGroup
                {
                    TargetFramework = "net10.0",
                    Dependencies =
                    [
                        new PackageDependency
                        {
                            Id = "Dependency.With.Range",
                            Version = versionRange
                        }
                    ]
                }
            ]
        };

        PackageIndexCache.Set(packageName, packageVersion, result);

        var cached = Assert.IsType<InspectionResult>(
            PackageIndexCache.TryGet(packageName, packageVersion));
        var group = Assert.Single(Assert.IsType<List<DependencyGroup>>(cached.DependencyGroups));
        Assert.Equal("net10.0", group.TargetFramework);
        var dependency = Assert.Single(group.Dependencies);
        Assert.Equal("Dependency.With.Range", dependency.Id);
        Assert.Equal(versionRange, dependency.Version);
    }
}
