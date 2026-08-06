using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageExtractorOfflineTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-offline-tests-{Guid.NewGuid():N}");

    public PackageExtractorOfflineTests()
    {
        Core.HttpClientFactory.Initialize(offline: true);
        Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize("dotnet-inspect-test", _cacheDir, skipNuGetCache: true);
    }

    public void Dispose()
    {
        Core.HttpClientFactory.Initialize(offline: false);
        Core.HttpClientFactory.ResetSharedForTesting();
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    [Fact]
    public async Task ExtractPackageAsync_OfflineUncachedPackage_ReportsCacheMiss()
    {
        var packageName = $"Definitely.Uncached.{Guid.NewGuid():N}";

        var outcome = await PackageExtractor.ExtractPackageAsync(Core.HttpClientFactory.Shared, packageName);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("not available offline", outcome.ErrorMessage);
        Assert.Contains("no cached version", outcome.ErrorMessage);
        Assert.DoesNotContain("not found", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractPackageAsync_OfflineUncachedVersion_ReportsCacheMiss()
    {
        var packageName = $"Definitely.Uncached.{Guid.NewGuid():N}";

        var outcome = await PackageExtractor.ExtractPackageAsync(Core.HttpClientFactory.Shared, packageName, version: "1.0.0");

        Assert.False(outcome.IsSuccess);
        Assert.Contains("not available offline", outcome.ErrorMessage);
        Assert.Contains("no cached package", outcome.ErrorMessage);
        Assert.DoesNotContain("not found", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractPackageAsync_OfflineWithCachedVersions_OffersExactPins()
    {
        string packageName = $"Cached.Offline.{Guid.NewGuid():N}";
        string sourceKey = NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json");
        foreach (string version in new[] { "1.0.0", "2.0.0" })
        {
            string staged = Path.Combine(_cacheDir, $"staged-{version}");
            Directory.CreateDirectory(staged);
            File.WriteAllText(
                Path.Combine(staged, $"{packageName}.nuspec"),
                "<package />");
            NuGetCache.CommitPackage(
                staged,
                nupkgPath: null,
                packageName,
                version,
                sourceKey);
        }

        var outcome = await PackageExtractor.ExtractPackageAsync(
            Core.HttpClientFactory.Shared,
            packageName);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("cannot resolve its latest version while offline", outcome.ErrorMessage);
        Assert.Contains("Locally cached versions: 2.0.0, 1.0.0", outcome.ErrorMessage);
        Assert.Contains(
            $"dotnet-inspect package {packageName}@2.0.0",
            outcome.ErrorMessage);
    }
}
