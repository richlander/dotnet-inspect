using System.IO.Compression;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageExtractorOfflineTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-offline-tests-{Guid.NewGuid():N}");

    public PackageExtractorOfflineTests()
    {
        Core.HttpClientFactory.Initialize(new Core.HttpClientFactoryOptions { Offline = true });
        Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize("dotnet-inspect-test", _cacheDir, skipNuGetCache: true);
    }

    public void Dispose()
    {
        Core.HttpClientFactory.Initialize(new Core.HttpClientFactoryOptions());
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
    public async Task ExtractPackageAsync_OfflineMalformedBarePackage_ReportsCacheMiss()
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(
            Core.HttpClientFactory.Shared,
            "some/pkg");

        Assert.False(outcome.IsSuccess);
        Assert.Contains("not available offline", outcome.ErrorMessage);
        Assert.DoesNotContain("Invalid package name", outcome.ErrorMessage);
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
            "versions-v5",
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
    public async Task ExtractPackageAsync_OfflineBarePackage_DoesNotAutoSelectPayloadVersion()
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
        Assert.Contains("cannot resolve its latest version while offline", outcome.ErrorMessage);
        Assert.Contains($"dotnet-inspect package {packageName}@{Version}", outcome.ErrorMessage);
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

    [Fact]
    public void AppCache_DoesNotReadPayloadsFromPreEndpointFenceNamespace()
    {
        string packageName = $"Offline.OldFence.{Guid.NewGuid():N}";
        const string Version = "1.2.3";
        string sourceKey = NuGetCache.GetSourceKey(
            "https://private.invalid/v3/index.json");
        string oldEntry = Path.Combine(
            Core.CoreCache.GetCategoryPath("package-content-v4"),
            packageName.ToLowerInvariant(),
            Version,
            sourceKey);
        Directory.CreateDirectory(oldEntry);
        File.WriteAllText(
            Path.Combine(
                oldEntry,
                $"{packageName.ToLowerInvariant()}.nuspec"),
            "<package />");
        File.WriteAllText(
            Path.Combine(
                oldEntry,
                NuGetCache.CommitMarkerFileName),
            $"package-content-v4:{packageName.ToLowerInvariant()}@{Version}:{sourceKey}");

        string? cached = NuGetCache.TryGetCachedPackage(
            packageName,
            Version,
            [sourceKey]);

        Assert.Null(cached);
    }

    private void CommitPackage(
        string packageName,
        string version,
        string sourceKey)
    {
        // Retained nupkg and extract must agree: product-owned admission matches
        // path/size/CRC against the archive after the commit marker is written.
        string entryName = $"{packageName.ToLowerInvariant()}.nuspec";
        const string NuspecContent = "<package />";
        Directory.CreateDirectory(_cacheDir);
        string nupkg = Path.Combine(
            _cacheDir,
            $"stage-{Guid.NewGuid():N}.nupkg");
        File.WriteAllBytes(nupkg, CreateArchive(entryName, NuspecContent));
        string staged = Path.Combine(
            _cacheDir,
            $"stage-{Guid.NewGuid():N}");
        ZipFile.ExtractToDirectory(nupkg, staged);
        NuGetCache.CommitPackage(
            staged,
            nupkg,
            packageName,
            version,
            sourceKey);
    }

    private static byte[] CreateArchive(string entryPath, string content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream stream = archive.CreateEntry(entryPath).Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }

        return buffer.ToArray();
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
