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
    public async Task ExtractPackageAsync_OfflineBarePackage_UsesCandidateMetadataAndAuthorizedPayload()
    {
        string packageName = $"Offline.Cached.{Guid.NewGuid():N}";
        const string Version = "1.2.3";
        const string SourceUrl = "https://private.invalid/v3/index.json";
        string sourceKey = NuGetCache.GetSourceKey(SourceUrl);
        CommitPackage(packageName, Version, sourceKey);
        var source = new NuGetFetch.PackageSource("private", SourceUrl);
        Core.CoreCache.Set(
            "versions-v4",
            PackageExtractor.GetLatestVersionCacheKey(packageName, source),
            Version,
            extension: "txt");

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                Core.HttpClientFactory.Shared,
                packageName,
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [SourceUrl],
                });

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(sourceKey, outcome.Result!.ProducerKey);
    }

    [Fact]
    public async Task ExtractPackageAsync_OfflineBarePackage_DoesNotDiscoverFromPayload()
    {
        string packageName = $"Offline.PayloadOnly.{Guid.NewGuid():N}";
        const string Version = "1.2.3";
        const string SourceUrl = "https://private.invalid/v3/index.json";
        CommitPackage(
            packageName,
            Version,
            NuGetCache.GetSourceKey(SourceUrl));

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                Core.HttpClientFactory.Shared,
                packageName,
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [SourceUrl],
                });

        Assert.False(outcome.IsSuccess);
        Assert.Contains("no cached version", outcome.ErrorMessage);
    }

    [Fact]
    public async Task ExtractPackageAsync_OfflinePinnedPackage_DoesNotNeedCandidateMetadata()
    {
        string packageName = $"Offline.Pinned.{Guid.NewGuid():N}";
        const string Version = "1.2.3";
        const string SourceUrl = "https://private.invalid/v3/index.json";
        string sourceKey = NuGetCache.GetSourceKey(SourceUrl);
        CommitPackage(packageName, Version, sourceKey);

        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                Core.HttpClientFactory.Shared,
                packageName,
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [SourceUrl],
                },
                version: Version);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(sourceKey, outcome.Result!.ProducerKey);
    }

    private void CommitPackage(
        string packageName,
        string version,
        string sourceKey)
    {
        string staged = Path.Combine(
            _cacheDir,
            $"stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staged);
        File.WriteAllText(
            Path.Combine(
                staged,
                $"{packageName.ToLowerInvariant()}.nuspec"),
            "<package />");
        NuGetCache.CommitPackage(
            staged,
            nupkgPath: null,
            packageName,
            version,
            sourceKey);
    }
}
