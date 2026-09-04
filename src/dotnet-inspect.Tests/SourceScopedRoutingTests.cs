using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;

using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGetFetch.Plugins;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class SourceScopedRoutingTests : IDisposable
{
    private const string ExcludedSource = "https://excluded.invalid/v3/index.json";
    private const string RefusedSource = "https://refused.invalid/v3/index.json";
    private const string SecondSource =
        "https://second.invalid/v3/index.json";
    private const string ExcludedFlatContainer =
        "https://excluded.invalid/v3/flat2/";
    private const string SecondFlatContainer =
        "https://second.invalid/v3/flat2/";

    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-source-routing-{Guid.NewGuid():N}");
    private readonly IReadOnlyList<string> _ambientSourceKeys;

    public SourceScopedRoutingTests()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions { Offline = true });
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            Path.Combine(_testRoot, "cache"),
            skipNuGetCache: true);
        _ambientSourceKeys = NuGetSourceResolver.ResolveSourceKeys(null);
        Assert.NotEmpty(_ambientSourceKeys);
        Assert.DoesNotContain(NuGetCache.GetSourceKey(ExcludedSource), _ambientSourceKeys);
    }

    public void Dispose()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize("dotnet-inspect");
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Router_DoesNotUseCachedPackageFromSourceExcludedByCaller(bool useConfig)
    {
        string packageName = $"System.RouteScope{Guid.NewGuid():N}";
        SeedPackage(packageName);

        string[] sourceArgs;
        if (useConfig)
        {
            string configPath = Path.Combine(_testRoot, "excluded.nuget.config");
            File.WriteAllText(configPath, $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="excluded" value="{ExcludedSource}" />
                  </packageSources>
                </configuration>
                """);
            sourceArgs = ["--nugetconfig", configPath];
        }
        else
        {
            sourceArgs = ["--source", ExcludedSource];
        }

        var observations = await RunAppAsync([packageName, .. sourceArgs]);

        var rewrite = Assert.Single(
            observations,
            observation => observation.Stage == "router-rewrite");
        string rewritten = rewrite.Detail[(rewrite.Detail.IndexOf(" -> ", StringComparison.Ordinal) + 4)..];
        Assert.StartsWith($"type {packageName}", rewritten);
        Assert.Contains(string.Join(' ', sourceArgs), rewrite.Detail);
    }

    [Fact]
    public async Task Router_QueriesCallerSourceForPackageExistence()
    {
        string packageName = $"System.QueryScope{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();

        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
            innerHandler => new RouterFeedHandler(packageName, requests, innerHandler));
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        try
        {
            var observations = await RunAppAsync(
                [packageName, "--source", ExcludedSource]);

            var rewrite = Assert.Single(
                observations,
                observation => observation.Stage == "router-rewrite");
            string rewritten = rewrite.Detail[
                (rewrite.Detail.IndexOf(" -> ", StringComparison.Ordinal) + 4)..];
            Assert.StartsWith($"package {packageName}", rewritten);
            Assert.Contains(
                requests,
                url => url.Equals(
                    $"{RouterFeedHandler.FlatContainer}{packageName.ToLowerInvariant()}/index.json",
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                requests,
                url => url.Contains(packageName, StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith(
                        RouterFeedHandler.FlatContainer,
                        StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(null);
            DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task Router_PlatformPrefixBrowse_IgnoresCachedPackageCandidate()
    {
        const string Target = "System.Text.Json.Serialization";
        SeedLatestCandidate(
            Target,
            ExcludedSource,
            "1.0.0");

        var observations = await RunAppAsync(
            [Target, "--source", ExcludedSource]);

        var rewrite = Assert.Single(
            observations,
            observation => observation.Stage == "router-rewrite");
        Assert.Contains(
            $" -> type {Target}",
            rewrite.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Router_InvalidNuGetConfig_ReportsCleanParseError()
    {
        string missingConfig = Path.Combine(_testRoot, "missing.nuget.config");

        var (exit, output, error) = await RunCommandAsync(
            [$"System.InvalidConfig{Guid.NewGuid():N}", "--nugetconfig", missingConfig]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"NuGet config file not found: '{missingConfig}'.", error);
        Assert.DoesNotContain("FileNotFoundException", error);
        Assert.DoesNotContain(" at DotnetInspector.", error);
    }

    [Fact]
    public async Task UnmappedPackage_ReportsMappingFailureWithoutNetworkLookup()
    {
        string packageName = $"Unmapped{Guid.NewGuid():N}";
        string configPath = Path.Combine(_testRoot, "unmapped.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="excluded" value="{ExcludedSource}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="excluded">
                  <package pattern="Other.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        var (exit, output, error) = await RunCommandAsync(
            ["package", packageName, "--version", "--nugetconfig", configPath]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Package source mapping has no pattern for package '{packageName.ToLowerInvariant()}'.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PackageSourceMappingException", error);
        Assert.DoesNotContain(" at DotnetInspector.", error);
    }

    [Fact]
    public async Task Router_MissingSourceValue_ReportsCleanParseError()
    {
        var (exit, output, error) = await RunCommandAsync(
            [$"System.MissingSource{Guid.NewGuid():N}", "--source"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Required argument missing for option: '--source'.", error);
        Assert.DoesNotContain("InvalidOperationException", error);
        Assert.DoesNotContain(" at System.CommandLine.", error);
    }

    [Theory]
    [InlineData("type", "Widget")]
    [InlineData("member", "Widget.Render")]
    public async Task QualifiedName_DoesNotSplitFromPackageContentCache(
        string command,
        string suffix)
    {
        string packageName = $"RouteScope{Guid.NewGuid():N}.Package";
        string qualifiedName = $"{packageName}.{suffix}";
        SeedPackage(packageName);

        Assert.Null(SourceResolver.TryResolveQualifiedTypeName(
            qualifiedName,
            _ambientSourceKeys,
            allowPlatformPrefixFallback: false));

        var observations = await RunAppAsync(
            [command, qualifiedName, "--source", ExcludedSource]);

        Assert.DoesNotContain(
            observations,
            observation => observation.Stage == "qualified-type-split"
                && observation.Detail.Contains(
                    $"package-candidate-cache={packageName}",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedName_SplitsFromSourceScopedCandidateMetadata(
        bool includePrerelease)
    {
        string packageName = $"RouteCandidate{Guid.NewGuid():N}.Package";
        string qualifiedName = $"{packageName}.Widget";
        var nugetOrg = NuGetFetch.PackageSource.NuGetOrg;
        CoreCache.Set(
            "versions-v5",
            PackageExtractor.GetLatestVersionCacheKey(
                packageName,
                nugetOrg,
                includePrerelease),
            includePrerelease ? "2.0.0-preview.1" : "1.0.0",
            extension: "txt");

        Assert.NotNull(SourceResolver.TryResolveQualifiedTypeName(
            qualifiedName,
            _ambientSourceKeys,
            allowPlatformPrefixFallback: false));
        Assert.Null(SourceResolver.TryResolveQualifiedTypeName(
            qualifiedName,
            [NuGetCache.GetSourceKey(ExcludedSource)],
            allowPlatformPrefixFallback: false));
    }

    [Fact]
    public void QualifiedName_PackageSourceMappingSelectsCandidateCachePerPackageId()
    {
        string packageName = $"RouteMapped{Guid.NewGuid():N}.Package";
        string qualifiedName = $"{packageName}.Widget";
        string configPath = Path.Combine(_testRoot, "mapping.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="excluded" value="{ExcludedSource}" />
                <add key="second" value="{SecondSource}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="second">
                  <package pattern="{packageName}" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        var sourceOptions = new NuGetSourceOptions { ConfigFile = configPath };

        SeedLatestCandidate(packageName, ExcludedSource, "1.0.0");
        Assert.Null(SourceResolver.TryResolveQualifiedTypeName(
            qualifiedName,
            sourceOptions,
            allowPlatformPrefixFallback: false));

        SeedLatestCandidate(packageName, SecondSource, "1.0.0");
        Assert.NotNull(SourceResolver.TryResolveQualifiedTypeName(
            qualifiedName,
            sourceOptions,
            allowPlatformPrefixFallback: false));
    }

    [Fact]
    public void PackageContentCache_PackageSourceMappingSelectsEligibleProducer()
    {
        string packageName = $"PayloadMapped{Guid.NewGuid():N}";
        string configPath = Path.Combine(_testRoot, "payload-mapping.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="excluded" value="{ExcludedSource}" />
                <add key="second" value="{SecondSource}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="second">
                  <package pattern="{packageName}" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        var sourceOptions = new NuGetSourceOptions { ConfigFile = configPath };

        SeedPackage(packageName, ExcludedSource);
        IReadOnlyList<string> eligibleKeys =
            NuGetSourceResolver.ResolveSourceKeysForPackage(
                sourceOptions,
                packageName);
        Assert.Null(NuGetCache.TryGetCachedPackage(
            packageName,
            "1.0.0",
            eligibleKeys));

        SeedPackage(packageName, SecondSource);
        Assert.NotNull(NuGetCache.TryGetCachedPackage(
            packageName,
            "1.0.0",
            eligibleKeys));
    }

    [Theory]
    [InlineData(false, "4.5.6")]
    [InlineData(true, "4.5.6-preview.1")]
    public async Task BareVersion_UsesMatchingCandidateMetadataOffline(
        bool includePrerelease,
        string version)
    {
        string packageName = $"OfflineVersion{Guid.NewGuid():N}";
        SeedLatestCandidate(
            packageName,
            ExcludedSource,
            version,
            includePrerelease);

        var (exit, output, error) = await RunCommandAsync(
            [
                "package",
                packageName,
                "--version",
                .. includePrerelease ? new[] { "--prerelease" } : [],
                "--source",
                ExcludedSource,
            ]);

        Assert.True(
            exit == 0,
            $"Expected success. Output: {output}{Environment.NewLine}Error: {error}");
        Assert.Equal(version, output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task BareVersion_PreviewDoesNotUseStableOnlyCandidateOffline()
    {
        string packageName = $"OfflineStableOnly{Guid.NewGuid():N}";
        SeedLatestCandidate(packageName, ExcludedSource, "4.5.6");

        var (exit, output, error) = await RunCommandAsync(
            [
                "package",
                packageName,
                "--version",
                "--prerelease",
                "--source",
                ExcludedSource,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BareVersion_QueriesMissingSourceAndPreservesJsonl()
    {
        string packageName = $"PartialCache{Guid.NewGuid():N}";
        SeedLatestCandidate(packageName, ExcludedSource, "1.0.0");
        var (exit, output, error, requests) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageName,
                    "--version",
                    "--jsonl",
                    "--source",
                    ExcludedSource,
                    "--source",
                    SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Equal("""{"version":"2.0.0"}""", output.Trim());
        Assert.Empty(error);
        Assert.Contains(
            requests,
            request => request.EndsWith(
                $"/{packageName.ToLowerInvariant()}/index.json",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CachedBareVersion_PreservesJsonlOffline()
    {
        string packageName = $"CachedJsonl{Guid.NewGuid():N}";
        SeedLatestCandidate(packageName, ExcludedSource, "4.5.6");

        var (exit, output, error) = await RunCommandAsync(
            [
                "package",
                packageName,
                "--version",
                "--jsonl",
                "--source",
                ExcludedSource,
            ]);

        Assert.Equal(0, exit);
        Assert.Equal("""{"version":"4.5.6"}""", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task CachedPinnedVersion_PreservesJsonlOffline()
    {
        string packageName = $"PinnedJsonl{Guid.NewGuid():N}";
        SeedPackage(packageName);

        var (exit, output, error) = await RunCommandAsync(
            [
                "package",
                $"{packageName}@1.0.0",
                "--version",
                "--jsonl",
            ]);

        Assert.Equal(0, exit);
        Assert.Equal("""{"version":"1.0.0"}""", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("latest")]
    public async Task VerifiedSingleVersion_PreservesJsonl(
        string requestedVersion)
    {
        string packageName = $"VerifiedJsonl{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    $"{packageName}@{requestedVersion}",
                    "--version",
                    "--jsonl",
                    "--source",
                    SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Equal("""{"version":"2.0.0"}""", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", "2.0.0")]
    [InlineData("latest", "2.0.0", "1.0.0")]
    public async Task ExplicitCoordinateSemanticSingleVersion_PreservesRequestedRow(
        string requestedVersion,
        string expectedVersion,
        string excludedVersion)
    {
        string packageName = $"PinnedSemantic{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                ["1.0.0", "2.0.0"],
                [
                    "package",
                    $"{packageName}@{requestedVersion}",
                    "--versions",
                    "-n",
                    "1",
                    "--json",
                    "--source",
                    SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Contains(
            $"""
            "version": "{expectedVersion}"
            """,
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"""
            "version": "{excludedVersion}"
            """,
            output,
            StringComparison.Ordinal);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("latest", "2.0.0")]
    [InlineData("1.0.0..2.0.0", "1.0.0")]
    [InlineData("2.0.0..1.0.0", "2.0.0")]
    [InlineData("2.1.0-preview.1", "2.1.0-preview.1")]
    public async Task FeedCoordinateSemanticSingleVersion_PreservesFeedRowIdentity(
        string selector,
        string expectedVersion)
    {
        string packageName = $"FeedCoordinate{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                ["1.0.0", "2.0.0", "2.1.0-preview.1"],
                [
                    "package",
                    $"{packageName}@{selector}",
                    "--versions-with-feed",
                    "-n",
                    "1",
                    "--json",
                    "--source",
                    SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document =
            JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(
                document.RootElement.EnumerateArray());
        Assert.Equal(
            expectedVersion,
            row.GetProperty("version").GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(
                row.GetProperty("feed").GetString()));
        Assert.Equal(
            "listed",
            row.GetProperty("listing").GetString());
    }

    [Fact]
    public async Task StableSingleVersionListingDoesNotFallBackToPrerelease()
    {
        string packageName = $"PreviewOnly{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0-preview.1",
                [
                    "package",
                    packageName,
                    "--versions",
                    "-n",
                    "1",
                    "--count",
                    "--source",
                    SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("--versions")]
    [InlineData("--versions-with-feed")]
    public async Task LatestVersionListing_RefreshesWarmFeedCache(string selector)
    {
        string packageName = $"FreshFeed{Guid.NewGuid():N}";
        string[] ordinaryArgs =
        [
            "package", packageName, "--versions-with-feed", "-n", "1",
            "--json", "--source", SecondSource,
        ];
        var warm = await RunOnlineVersionFeedCommandAsync(
            packageName, "1.0.0", ordinaryArgs);
        Assert.Equal(0, warm.Exit);
        Assert.Empty(warm.Error);
        Assert.Contains(
            $"{SecondFlatContainer}{packageName.ToLowerInvariant()}/index.json",
            warm.Requests);

        string[] publishedVersions = ["1.0.0", "2.0.0"];
        var cached = await RunOnlineVersionFeedCommandAsync(
            packageName, publishedVersions, ordinaryArgs);
        Assert.Equal(0, cached.Exit);
        Assert.Empty(cached.Error);
        Assert.Empty(cached.Requests);
        using JsonDocument cachedDocument = JsonDocument.Parse(cached.Output);
        Assert.Equal(
            "1.0.0",
            Assert.Single(cachedDocument.RootElement.EnumerateArray())
                .GetProperty("version").GetString());

        var fresh = await RunOnlineVersionFeedCommandAsync(
            packageName,
            publishedVersions,
            [
                "package", $"{packageName}@latest", selector, "-n", "1",
                "--json", "--source", SecondSource,
            ]);
        Assert.Equal(0, fresh.Exit);
        Assert.Empty(fresh.Error);
        Assert.Contains(
            $"{SecondFlatContainer}{packageName.ToLowerInvariant()}/index.json",
            fresh.Requests);
        using JsonDocument freshDocument = JsonDocument.Parse(fresh.Output);
        JsonElement row = Assert.Single(freshDocument.RootElement.EnumerateArray());
        Assert.Equal("2.0.0", row.GetProperty("version").GetString());
        if (selector == "--versions-with-feed")
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("feed").GetString()));
            Assert.Equal("listed", row.GetProperty("listing").GetString());
        }
    }

    [Fact]
    public async Task FeedLatest_RefreshFailureDoesNotFallBackToCachedRows()
    {
        string packageName = $"RefreshedRefusal{Guid.NewGuid():N}";
        var warm = await RunOnlineVersionFeedCommandAsync(
            packageName,
            "1.0.0",
            [
                "package", packageName, "--versions-with-feed", "-n", "1",
                "--json", "--source", SecondSource,
            ]);
        Assert.Equal(0, warm.Exit);
        Assert.Empty(warm.Error);
        Assert.NotEmpty(warm.Requests);

        var refused = await RunOnlineVersionFeedCommandAsync(
            packageName,
            "2.0.0",
            [
                "package", $"{packageName}@latest", "--versions-with-feed",
                "-n", "1", "--json", "--source", SecondSource,
            ],
            requireAuthorization: true);
        Assert.Equal(1, refused.Exit);
        Assert.Empty(refused.Output);
        Assert.NotEmpty(refused.Requests);
        Assert.Contains("requires credentials", refused.Error);
    }

    [Theory]
    [InlineData("pinned")]
    [InlineData("latest")]
    [InlineData("all")]
    [InlineData("range")]
    public async Task PackageVersionQueries_ReportARefusingSource(
        string query)
    {
        string packageName = $"RefusedVersion{Guid.NewGuid():N}";
        string[] queryArgs = query switch
        {
            "pinned" => [$"{packageName}@2.0.0", "--version"],
            "latest" => [packageName, "--latest-version"],
            "range" => [$"{packageName}@1.0.0..2.0.0", "--versions"],
            _ => [packageName, "--versions"],
        };

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    .. queryArgs,
                    "--source",
                    RefusedSource,
                ],
                refusedStatus: HttpStatusCode.Unauthorized);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires credentials", error);
        if (query != "all")
            Assert.Contains("HTTP 401", error);
        Assert.Contains(RefusedSource, error);
        Assert.Contains(
            query == "all"
                ? "Supply credentials for the source and retry."
                : "The package may exist; the source was not readable.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageVersionQuery_PreservesNotFoundForA404(
        bool range)
    {
        string packageName = $"MissingVersion{Guid.NewGuid():N}";
        string packageQuery = range
            ? $"{packageName}@1.0.0..2.0.0"
            : packageName;

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageQuery,
                    "--versions",
                    "--source",
                    ExcludedSource,
                ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requires credentials", error);
        Assert.DoesNotContain("Could not retrieve versions", error);
    }

    [Fact]
    public async Task PackageVersionQuery_UsesAReadableSourceAfterA401()
    {
        string packageName = $"PartialVersion{Guid.NewGuid():N}";

        var (exit, output, error, requests) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageName,
                    "--latest-version",
                    "--source",
                    RefusedSource,
                    "--source",
                    SecondSource,
                ],
                refusedStatus: HttpStatusCode.Unauthorized);

        Assert.Equal(0, exit);
        Assert.Equal("2.0.0", output.Trim());
        Assert.Empty(error);
        Assert.Contains(
            requests,
            url => url.Equals(
                RefusedSource,
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            requests,
            url => url.Equals(
                $"{SecondFlatContainer}{packageName.ToLowerInvariant()}/index.json",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageVersionListing_ReportsPartialEvidenceAfterA401()
    {
        string packageName = $"PartialVersionList{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageName,
                    "--versions",
                    "--source",
                    RefusedSource,
                    "--source",
                    SecondSource,
                ],
                refusedStatus: HttpStatusCode.Unauthorized);

        Assert.Equal(0, exit);
        Assert.Equal("2.0.0", output.Trim());
        Assert.Contains("results", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partial", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires credentials", error);
        Assert.Contains(RefusedSource, error);
        Assert.DoesNotContain("HTTP 401", error);
    }

    [Fact]
    public async Task PackageVersionListing_LimitOneStillReportsPartialEvidence()
    {
        string packageName = $"PartialLimitedVersionList{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageName,
                    "--versions",
                    "-n",
                    "1",
                    "--source",
                    RefusedSource,
                    "--source",
                    SecondSource,
                ],
                refusedStatus: HttpStatusCode.Unauthorized);

        Assert.Equal(0, exit);
        Assert.Equal("2.0.0", output.Trim());
        Assert.Contains("partial", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires credentials", error);
        Assert.Contains(RefusedSource, error);
    }

    [Fact]
    public async Task PackageVersionFeedListing_LimitOneStillReportsPartialEvidence()
    {
        string packageName = $"PartialLimitedFeedList{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageName,
                    "--versions-with-feed",
                    "-n",
                    "1",
                    "--source",
                    RefusedSource,
                    "--source",
                    SecondSource,
                ],
                refusedStatus: HttpStatusCode.Unauthorized);

        Assert.Equal(0, exit);
        Assert.Contains("2.0.0", output, StringComparison.Ordinal);
        Assert.Contains("partial", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires credentials", error);
        Assert.Contains(RefusedSource, error);
    }

    [Fact]
    public async Task PackageVersionListing_UsesConfiguredCredentialImmediately()
    {
        string packageName = $"ConfiguredCredential{Guid.NewGuid():N}";
        string configPath = Path.Combine(
            _testRoot,
            "configured-credential.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="{SecondSource}" />
              </packageSources>
              <packageSourceCredentials>
                <private>
                  <add key="Username" value="reader" />
                  <add key="ClearTextPassword" value="secret" />
                </private>
              </packageSourceCredentials>
            </configuration>
            """);

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageName,
                    "--versions",
                    "--nugetconfig",
                    configPath,
                ],
                requireAuthorization: true);

        Assert.Equal(0, exit);
        Assert.Equal("2.0.0", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task PackageVersionListing_EncodedPathCredentialDoesNotCrossToLiteralPath()
    {
        const string ConfiguredIndex =
            "https://feed.example/%7E/private/index.json";
        const string RequestedIndex =
            "https://feed.example/~/private/index.json";
        const string Flat =
            "https://feed.example/flat2/";
        const string PackageName = "path-authority-isolation";
        string configPath = Path.Combine(
            _testRoot,
            "path-authority-isolation.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="encoded" value="{ConfiguredIndex}" />
              </packageSources>
              <packageSourceCredentials>
                <encoded>
                  <add key="Username" value="reader" />
                  <add key="ClearTextPassword" value="secret" />
                </encoded>
              </packageSourceCredentials>
            </configuration>
            """);
        var handler = new AuthenticationIsolationHandler(
            RequestedIndex,
            Flat,
            PackageName,
            "1.0.0",
            requireAuthentication: false);
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            new UnavailableCredentialSource(),
            (_, isGallery) =>
            {
                Assert.False(isGallery);
                return handler;
            });

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                PackageName,
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions
                {
                    ConfigFile = configPath,
                    Sources = [RequestedIndex],
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
        Assert.Equal(["1.0.0"], result.Versions);
        Assert.False(handler.SawAuthorization);
    }

    [Fact]
    public async Task SourceClientComposition_ConfiguredCredentialBypassesPluginProvider()
    {
        string packageName = $"ConfiguredBypass{Guid.NewGuid():N}";
        string configPath = Path.Combine(
            _testRoot,
            "configured-bypass.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="{SecondSource}" />
              </packageSources>
              <packageSourceCredentials>
                <private>
                  <add key="Username" value="reader" />
                  <add key="ClearTextPassword" value="secret" />
                </private>
              </packageSourceCredentials>
            </configuration>
            """);
        var credentialSource = new RecordingCredentialSource();
        var handler = new AuthenticationIsolationHandler(
            SecondSource,
            SecondFlatContainer,
            packageName,
            "2.0.0",
            requireAuthentication: true);
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            credentialSource,
            (_, _) => handler);

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                packageName,
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions { ConfigFile = configPath },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
        Assert.Equal(["2.0.0"], result.Versions);
        Assert.True(handler.SawAuthorization);
        Assert.Empty(credentialSource.Queries);
    }

    [Fact]
    public async Task SourceClientComposition_QueryDistinctAuthoritiesKeepAuthenticationSeparate()
    {
        const string PrivateIndex =
            "https://feed.example/v3/index.json?tenant=private";
        const string AnonymousIndex =
            "https://feed.example/v3/index.json?tenant=anonymous";
        const string PrivateFlat = "https://feed.example/private/flat2/";
        const string AnonymousFlat = "https://feed.example/anonymous/flat2/";
        string packageName = $"ContextIsolation{Guid.NewGuid():N}";
        string configPath = Path.Combine(
            _testRoot,
            "context-isolation.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="{PrivateIndex.Replace("&", "&amp;", StringComparison.Ordinal)}" />
                <add key="anonymous" value="{AnonymousIndex.Replace("&", "&amp;", StringComparison.Ordinal)}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private"><package pattern="*" /></packageSource>
                <packageSource key="anonymous"><package pattern="*" /></packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        var credentialSource = new RecordingCredentialSource();
        var handlers = new Dictionary<string, AuthenticationIsolationHandler>(
            StringComparer.Ordinal)
        {
            ["private"] = new(
                PrivateIndex,
                PrivateFlat,
                packageName,
                "2.0.0",
                requireAuthentication: true),
            ["anonymous"] = new(
                AnonymousIndex,
                AnonymousFlat,
                packageName,
                "1.0.0",
                requireAuthentication: false),
        };
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            credentialSource,
            (source, _) => handlers[source.Name]);

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                packageName,
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions { ConfigFile = configPath },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
        Assert.Equal(["2.0.0", "1.0.0"], result.Versions);
        Assert.Equal(
            PrivateIndex,
            Assert.Single(credentialSource.Queries).AbsoluteUri);
        Assert.True(handlers["private"].SawAuthorization);
        Assert.False(handlers["anonymous"].SawAuthorization);
    }

    [Fact]
    public async Task SourceClientComposition_PreservesRawProviderQuerySpelling()
    {
        const string ConfiguredIndex =
            "https://feed.example/%7E/private/index.json";
        const string Flat =
            "https://feed.example/flat2/";
        const string PackageName = "raw-provider-query";
        var credentialSource = new RecordingCredentialSource();
        var handler = new AuthenticationIsolationHandler(
            ConfiguredIndex,
            Flat,
            PackageName,
            "1.0.0",
            requireAuthentication: true);
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            credentialSource,
            (_, isGallery) =>
            {
                Assert.False(isGallery);
                return handler;
            });

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                PackageName,
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions { Sources = [ConfiguredIndex] },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
        Assert.Equal(["1.0.0"], result.Versions);
        Assert.Equal(
            ConfiguredIndex,
            Assert.Single(credentialSource.Queries).OriginalString);
        Assert.True(handler.SawAuthorization);
    }

    [Fact]
    public async Task PackageVersionListing_CanonicalGalleryExcludesUnlistedWithoutPluginContext()
    {
        const string PackageName = "gallery-listing-contract";
        const string Flat =
            "https://globalcdn.nuget.org/v3-flatcontainer/gallery-listing-contract/index.json";
        const string Registration =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/gallery-listing-contract/index.json";
        var credentialSource = new RecordingCredentialSource();
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            credentialSource,
            (_, _) => new CannedResponseHandler(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Flat] =
                        """{"versions":["1.0.0","1.1.0","2.0.0-beta.1"]}""",
                    [Registration] = """
                        {
                          "items": [
                            {
                              "items": [
                                {"catalogEntry":{"version":"1.0","listed":true}},
                                {"catalogEntry":{"version":"1.1.0","listed":false}},
                                {"catalogEntry":{"version":"2.0.0-beta.1"}}
                              ]
                            }
                          ]
                        }
                        """,
                }));

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                PackageName,
                includePrerelease: true,
                limit: null,
                new NuGetSourceOptions
                {
                    Sources =
                        ["https://api.nuget.org/v3/index.json"],
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
        Assert.Equal(["2.0.0-beta.1", "1.0.0"], result.Versions);
        Assert.Empty(credentialSource.Queries);
    }

    [Fact]
    public async Task PackageVersionListing_IncompleteGalleryListingStateIsPartial()
    {
        const string PackageName = "gallery-partial-listing-contract";
        const string Flat =
            "https://globalcdn.nuget.org/v3-flatcontainer/gallery-partial-listing-contract/index.json";
        var credentialSource = new RecordingCredentialSource();
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            credentialSource,
            (_, _) => new CannedResponseHandler(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Flat] = """{"versions":["1.0.0","1.1.0"]}""",
                }));

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                PackageName,
                includePrerelease: true,
                limit: null,
                new NuGetSourceOptions
                {
                    Sources =
                        ["https://api.nuget.org/v3/index.json"],
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Partial, result.State);
        Assert.Equal(["1.1.0", "1.0.0"], result.Versions);
        PackageAuthorityFailure failure = Assert.Single(result.Failures);
        Assert.Equal(
            PackageAuthorityFailureKind.IncompleteMetadata,
            failure.Kind);
        Assert.Empty(credentialSource.Queries);
    }

    [Theory]
    [InlineData("Newtonsoft.Json.", null, "package ID")]
    [InlineData("Newtonsoft.Json", "0", "positive whole number")]
    public async Task PackageVersionListing_InvalidInputReportsTypedFailure(
        string packageName,
        string? limit,
        string expected)
    {
        bool transportCreated = false;
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        DotnetInspector.Core.HttpClientFactory.SetPackageSourceHandlerForTesting(
            _ =>
            {
                transportCreated = true;
                return new HttpClientHandler();
            });
        try
        {
            var arguments = new List<string>
            {
                "package",
                packageName,
                "--versions",
            };
            if (limit is not null)
                arguments.AddRange(["-n", limit]);
            arguments.AddRange(["--source", SecondSource]);

            var (exit, output, error) =
                await RunCommandAsync([.. arguments]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", error, StringComparison.Ordinal);
            Assert.DoesNotContain(
                " at DotnetInspector.",
                error,
                StringComparison.Ordinal);
            if (limit is null)
            {
                Assert.Contains(
                    "Correct the package command input",
                    error,
                    StringComparison.Ordinal);
            }
            Assert.DoesNotContain(
                "Correct the package source configuration",
                error,
                StringComparison.Ordinal);
            Assert.False(transportCreated);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task PackageVersionListing_UnsupportedConfiguredSourceRetainsValidPeer()
    {
        string packageName = $"UnsupportedPeer{Guid.NewGuid():N}";
        string configPath = Path.Combine(
            _testRoot,
            "unsupported-peer.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="valid" value="{SecondSource}" />
                <add key="legacy" value="ftp://legacy.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var (exit, output, error, requests) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    packageName,
                    "--versions",
                    "--nugetconfig",
                    configPath,
                ]);

        Assert.Equal(0, exit);
        Assert.Equal("2.0.0", output.Trim());
        Assert.Contains("partial", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("legacy", error, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ftp://legacy.example",
            error,
            StringComparison.Ordinal);
        Assert.Contains(SecondSource, requests);
    }

    [Theory]
    [InlineData("https://example.invalid/%zz/index.json")]
    [InlineData("https://pkgs.dev.azure.com/")]
    [InlineData("https://-foo.example/v3/index.json")]
    public async Task PackageVersionListing_UnusableSourceSetupIsTypedBeforeTransport(
        string unusableSource)
    {
        string packageName = $"UnusableSetup{Guid.NewGuid():N}";
        bool invalidTransportCreated = false;
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        DotnetInspector.Core.HttpClientFactory.SetPackageSourceHandlerForTesting(
            sourceUrl =>
            {
                if (sourceUrl == unusableSource)
                    invalidTransportCreated = true;

                return new VersionFeedHandler(
                    SecondSource,
                    packageName,
                    ["2.0.0"],
                    refusedStatus: null,
                    requireAuthorization: false,
                    new ConcurrentQueue<string>(),
                    new HttpClientHandler());
            });
        try
        {
            var (exit, output, error) = await RunCommandAsync(
                [
                    "package",
                    packageName,
                    "--versions",
                    "--source",
                    SecondSource,
                    "--source",
                    unusableSource,
                ]);

            Assert.True(
                exit == 0,
                $"Expected success but got exit {exit}. stderr: {error}");
            Assert.Equal("2.0.0", output.Trim());
            Assert.Contains(
                "partial",
                error,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                " at DotnetInspector.",
                error,
                StringComparison.Ordinal);
            Assert.False(invalidTransportCreated);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task OperationContext_OperationTimeoutIsTerminalAcrossAuthorities()
    {
        string[] sources =
        [
            "https://success.example/v3/index.json",
            "https://timeout-2.example/v3/index.json",
            "https://timeout-3.example/v3/index.json",
            "https://timeout-4.example/v3/index.json",
            "https://timeout-5.example/v3/index.json",
            "https://timeout-6.example/v3/index.json",
        ];
        var handlers = new List<NeverCompletesHandler>();
        const string SuccessFlat = "https://success.example/v3/flat2/";
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromMilliseconds(50),
            new UnavailableCredentialSource(),
            (source, _) =>
            {
                if (source.Url == sources[0])
                {
                    return new CannedResponseHandler(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [sources[0]] = $$"""
                                {
                                  "version": "3.0.0",
                                  "resources": [
                                    { "@id": "{{SuccessFlat}}", "@type": "PackageBaseAddress/3.0.0" }
                                  ]
                                }
                                """,
                            [$"{SuccessFlat}timeout-contract/index.json"] =
                                """{"versions":["1.0.0"]}""",
                        });
                }

                var handler = new NeverCompletesHandler();
                handlers.Add(handler);
                return handler;
            });

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                "timeout-contract",
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions { Sources = sources },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Failed, result.State);
        Assert.Equal(["1.0.0"], result.Versions);
        Assert.Equal(sources.Length, result.Failures.Count);
        Assert.All(
            result.Failures,
            failure => Assert.Equal(
                PackageAuthorityFailureKind.Timeout,
                failure.Kind));
        Assert.Contains(
            result.Failures,
            failure => failure.Message.Contains(
                "operation deadline expired",
                StringComparison.Ordinal));
        Assert.InRange(
            handlers.Sum(handler => handler.RequestCount),
            low: 1,
            high: sources.Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperationContext_RequestTimeoutContinuesToLaterAuthorityWithinCeiling(
        bool localSuccess)
    {
        const string TimedOutSource =
            "https://timeout.example/v3/index.json";
        const string SuccessSource =
            "https://success.example/v3/index.json";
        const string SuccessFlat =
            "https://success.example/v3/flat2/";
        string successSource = localSuccess
            ? Path.Combine(_testRoot, "timeout-failover")
            : SuccessSource;
        if (localSuccess)
            WriteLocalPackage(successSource, "timeout-failover", "1.0.0");
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromMilliseconds(50),
            new UnavailableCredentialSource(),
            (source, _) => source.Url == TimedOutSource
                ? new NeverCompletesHandler()
                : new CannedResponseHandler(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [SuccessSource] = $$"""
                            {
                              "version": "3.0.0",
                              "resources": [
                                { "@id": "{{SuccessFlat}}", "@type": "PackageBaseAddress/3.0.0" }
                              ]
                            }
                            """,
                        [$"{SuccessFlat}timeout-failover/index.json"] =
                            """{"versions":["1.0.0"]}""",
                    }));

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                "timeout-failover",
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions
                {
                    Sources = [TimedOutSource, successSource],
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Partial, result.State);
        Assert.Equal(["1.0.0"], result.Versions);
        PackageAuthorityFailure failure = Assert.Single(result.Failures);
        Assert.Equal(PackageAuthorityFailureKind.Timeout, failure.Kind);
        Assert.Contains(TimedOutSource, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageVersionListing_LocalFolderReadsVersionsWithoutHttpTransport(
        bool fileUri)
    {
        string packageName = $"LocalDirectory{Guid.NewGuid():N}";
        string localSource = Path.Combine(_testRoot, "local-source");
        WriteLocalPackage(localSource, packageName, "1.0.0", hierarchical: fileUri);
        bool transportCreated = false;
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        DotnetInspector.Core.HttpClientFactory.SetPackageSourceHandlerForTesting(
            _ =>
            {
                transportCreated = true;
                return new HttpClientHandler();
            });
        try
        {
            var (exit, output, error) = await RunCommandAsync(
                [
                    "package",
                    packageName,
                    "--versions",
                    "-n",
                    "1",
                    "--source",
                    fileUri ? new Uri(localSource).AbsoluteUri : localSource,
                ]);

            Assert.Equal(0, exit);
            Assert.Equal("1.0.0", output.Trim());
            Assert.Empty(error);
            Assert.False(transportCreated);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    [InlineData(true, true, null)]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 1)]
    public async Task PackageVersionListing_LocalAndHttpUnionIsSortedBeforeLimit(
        bool reverseSources,
        bool preview,
        int? limit)
    {
        const string PackageName = "Mixed.LocalVersions";
        string local = Path.Combine(_testRoot, "mixed-source");
        WriteLocalPackage(local, PackageName, "1.0.0");
        WriteLocalPackage(local, PackageName, "2.0.0", hierarchical: true);
        WriteLocalPackage(local, PackageName, "3.0.0-preview.1");
        string[] sources = reverseSources
            ? [SecondSource, local]
            : [local, SecondSource];

        var (exit, output, error, requests) =
            await RunOnlineVersionFeedCommandAsync(
                PackageName,
                "2.0.0",
                [
                    "package", PackageName, "--versions",
                    .. limit is { } count ? new[] { "-n", count.ToString() } : [],
                    "--source", sources[0], "--source", sources[1],
                    .. preview ? new[] { "--preview" } : [],
                    "--jsonl",
                ]);

        string[] expected = preview
            ? ["3.0.0-preview.1", "2.0.0", "1.0.0"]
            : ["2.0.0", "1.0.0"];
        Assert.Equal(0, exit);
        Assert.Equal(
            expected.Take(limit ?? int.MaxValue)
                .Select(version => $"{{\"version\":\"{version}\"}}"),
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Empty(error);
        Assert.Contains(
            $"{SecondFlatContainer}{PackageName.ToLowerInvariant()}/index.json",
            requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageVersionListing_EmptyLocalRootIsAbsenceButMissingRootFails(
        bool missing)
    {
        string local = Path.Combine(_testRoot, "empty-source");
        if (!missing)
            Directory.CreateDirectory(local);
        var credentials = new RecordingCredentialSource();
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            credentials,
            (_, _) => throw new InvalidOperationException("Local source constructed HTTP transport."));

        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                "Absent.Package",
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions { Sources = [local] },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Versions);
        Assert.False(result.HasAnyCandidate);
        Assert.Empty(credentials.Queries);
        if (missing)
        {
            Assert.Equal(PackageVersionDiscoveryState.Failed, result.State);
            Assert.Equal(
                PackageAuthorityFailureKind.Transport,
                Assert.Single(result.Failures).Kind);
        }
        else
        {
            Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
            Assert.Empty(result.Failures);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageVersionListing_LocalFailureRetainsHttpPeerAsPartial(
        bool malformed)
    {
        const string PackageName = "Incomplete.LocalVersions";
        string local = Path.Combine(_testRoot, "failed-source");
        if (malformed)
        {
            Directory.CreateDirectory(local);
            File.WriteAllText(
                Path.Combine(local, $"{PackageName}.1.0.0.nupkg"),
                "not a package archive");
        }

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                PackageName,
                "2.0.0",
                [
                    "package", PackageName, "--versions", "-n", "1", "--jsonl",
                    "--source", local, "--source", SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Equal("""{"version":"2.0.0"}""", output.Trim());
        Assert.Contains("partial", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(local, error, StringComparison.Ordinal);
        Assert.Contains(
            malformed ? "invalid version metadata" : "could not be reached",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageVersionListing_HttpFailureRetainsLocalPeerAsPartial()
    {
        const string PackageName = "Readable.LocalVersions";
        string local = Path.Combine(_testRoot, "readable-source");
        WriteLocalPackage(local, PackageName, "1.0.0");

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                PackageName,
                "2.0.0",
                [
                    "package", PackageName, "--versions", "--jsonl",
                    "--source", RefusedSource, "--source", local,
                ],
                refusedStatus: HttpStatusCode.Unauthorized);

        Assert.Equal(0, exit);
        Assert.Equal("""{"version":"1.0.0"}""", output.Trim());
        Assert.Contains("partial", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires credentials", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageVersionListing_LocalMappingPrecedesCollapseAndKeepsDistinctRoots()
    {
        const string PackageName = "Mapped.LocalVersions";
        string local = Path.Combine(_testRoot, "mapped-source");
        string other = Path.Combine(_testRoot, "other-source");
        WriteLocalPackage(local, PackageName, "1.0.0");
        WriteLocalPackage(other, PackageName, "2.0.0");
        string configPath = Path.Combine(_testRoot, "NuGet.Config");
        new XDocument(
            new XElement("configuration",
                new XElement("packageSources",
                    new XElement("clear"),
                    Source("unmapped", "mapped-source"),
                    Source("path", "mapped-source"),
                    Source("uri", new Uri(local).AbsoluteUri),
                    Source("other", "other-source")),
                new XElement("packageSourceCredentials",
                    new XElement("unmapped",
                        new XElement("add", new XAttribute("key", "Username"), new XAttribute("value", "reader")),
                        new XElement("add", new XAttribute("key", "ClearTextPassword"), new XAttribute("value", "test")))),
                new XElement("packageSourceMapping",
                    Mapping("path"), Mapping("uri"), Mapping("other"))))
            .Save(configPath);

        var credentials = new RecordingCredentialSource();
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(5),
            credentials,
            (_, _) => throw new InvalidOperationException("Local source constructed HTTP transport."));
        var log = new List<string>();
        PackageVersionDiscoveryResult result =
            await composition.GetVersionsAsync(
                PackageName,
                includePrerelease: false,
                limit: null,
                new NuGetSourceOptions { ConfigFile = configPath },
                log.Add,
                TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
        Assert.Equal(["2.0.0", "1.0.0"], result.Versions);
        Assert.Empty(result.Failures);
        Assert.Empty(credentials.Queries);
        Assert.Equal(2, log.Count(message => message.StartsWith("Fetching versions from ", StringComparison.Ordinal)));

        static XElement Source(string name, string location) =>
            new("add", new XAttribute("key", name), new XAttribute("value", location));
        static XElement Mapping(string name) =>
            new("packageSource", new XAttribute("key", name),
                new XElement("package", new XAttribute("pattern", PackageName)));
    }

    [Fact]
    public async Task MissingPinnedVersion_IsNotDeclaredAbsentWhenAnotherSourceRefuses()
    {
        string packageName = $"PartialPinned{Guid.NewGuid():N}";

        var (exit, output, error, _) =
            await RunOnlineVersionFeedCommandAsync(
                packageName,
                "2.0.0",
                [
                    "package",
                    $"{packageName}@3.0.0",
                    "--version",
                    "--source",
                    RefusedSource,
                    "--source",
                    SecondSource,
                ],
                refusedStatus: HttpStatusCode.Unauthorized);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires credentials", error);
        Assert.Contains("HTTP 401", error);
        Assert.DoesNotContain(
            "Version '3.0.0'",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPinnedVersion_RemainsAbsentWhenOnlyListingStatusFails()
    {
        string packageName = $"RegistrationFailure{Guid.NewGuid():N}";
        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
            innerHandler => new NuGetOrgRegistrationFailureHandler(
                packageName,
                ["2.0.0"],
                innerHandler));
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        try
        {
            var (exit, output, error) = await RunCommandAsync(
                [
                    "package",
                    $"{packageName}@3.0.0",
                    "--version",
                    "--source",
                    "https://api.nuget.org/v3/index.json",
                ]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                $"Version '3.0.0' of package '{packageName.ToLowerInvariant()}' not found.",
                error);
            Assert.DoesNotContain("source did not answer", error);
            Assert.DoesNotContain("Supply credentials", error);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
                null);
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task PackageRange_DoesNotDeclareAbsenceWhenListingStatusIsUnavailable()
    {
        string packageName = $"RangeRegistrationFailure{Guid.NewGuid():N}";
        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
            innerHandler => new NuGetOrgRegistrationFailureHandler(
                packageName,
                ["1.0.0", "2.0.0"],
                innerHandler,
                HttpStatusCode.NotFound));
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        try
        {
            var (exit, output, error) = await RunCommandAsync(
                [
                    "package",
                    $"{packageName}@1.0.0..2.0.0",
                    "--versions",
                    "--source",
                    "https://api.nuget.org/v3/index.json",
                ]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                $"Could not retrieve versions for package '{packageName}'.",
                error);
            Assert.DoesNotContain(
                "not found",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
                null);
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task Router_PlatformPrefixProbeUsesSourceScopedCandidateMetadataOffline()
    {
        const string PackageName = "System.Text";
        SeedLatestCandidate(
            PackageName,
            ExcludedSource,
            "4.5.6");

        var observations = await RunAppAsync(
            [PackageName, "--source", ExcludedSource]);

        var rewrite = Assert.Single(
            observations,
            observation => observation.Stage == "router-rewrite");
        Assert.Contains(
            $" -> package {PackageName}",
            rewrite.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypePrefixProbe_UsesSourceScopedCandidateMetadataOffline()
    {
        const string PackageName = "System.Text";
        SeedLatestCandidate(
            PackageName,
            ExcludedSource,
            "4.5.6");
        bool exists = await TypeCommand.PackageExistsAsync(
            PackageName,
            new TypeOptions
            {
                SourceOptions = new NuGetSourceOptions
                {
                    Sources = [ExcludedSource],
                },
            },
            new CommandContext(verbose: false));

        Assert.True(exists);
    }

    [Fact]
    public async Task TypePrefixProbe_TreatsUnmappedPackageAsAMiss()
    {
        string configPath = Path.Combine(_testRoot, "type-probe-mapping.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="excluded" value="{ExcludedSource}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="excluded">
                  <package pattern="Other.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        bool exists = await TypeCommand.PackageExistsAsync(
            $"Unmapped{Guid.NewGuid():N}",
            new TypeOptions
            {
                SourceOptions = new NuGetSourceOptions { ConfigFile = configPath },
            },
            new CommandContext(verbose: false));

        Assert.False(exists);
    }

    [Fact]
    public void SourceEnrichment_TreatsUnmappedCacheCandidateAsAMiss()
    {
        string configPath = Path.Combine(_testRoot, "enrichment-mapping.nuget.config");
        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(configPath, $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="excluded" value="{ExcludedSource}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="excluded">
                  <package pattern="Other.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        string? version = SourceEnricher.FindCachedPackageVersion(
            $"Unmapped{Guid.NewGuid():N}",
            new ApiOptions
            {
                SourceOptions = new NuGetSourceOptions { ConfigFile = configPath },
            });

        Assert.Null(version);
    }

    private static void SeedLatestCandidate(
        string packageName,
        string sourceUrl,
        string version,
        bool includePrerelease = false)
    {
        var source = new NuGetFetch.PackageSource("test", sourceUrl);
        CoreCache.Set(
            "versions-v5",
            PackageExtractor.GetLatestVersionCacheKey(
                packageName,
                source,
                includePrerelease),
            version,
            extension: "txt");
    }

    private static void WriteLocalPackage(
        string root,
        string packageName,
        string version,
        bool hierarchical = false)
    {
        string id = packageName.ToLowerInvariant();
        string directory = hierarchical
            ? Path.Combine(root, id, version)
            : root;
        Directory.CreateDirectory(directory);
        using var archive = new ZipArchive(
            File.Create(Path.Combine(directory, $"{id}.{version}.nupkg")),
            ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry($"{id}.nuspec").Open());
        writer.Write($"""
            <package><metadata>
              <id>{packageName}</id><version>{version}</version>
            </metadata></package>
            """);
    }

    private void SeedPackage(string packageName, string? sourceUrl = null)
    {
        string staged = Path.Combine(_testRoot, $"stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staged);
        File.WriteAllText(
            Path.Combine(staged, $"{packageName.ToLowerInvariant()}.nuspec"),
            "<package />");
        Directory.CreateDirectory(Path.Combine(staged, "payload"));
        File.WriteAllText(Path.Combine(staged, "payload", "content.txt"), packageName);
        NuGetCache.CommitPackage(
            staged,
            nupkgPath: null,
            packageName,
            version: "1.0.0",
            sourceUrl is null
                ? _ambientSourceKeys[0]
                : NuGetCache.GetSourceKey(sourceUrl));
    }

    private static async Task<IReadOnlyList<BreadcrumbObservation>> RunAppAsync(string[] args)
    {
        var observations = new ConcurrentQueue<BreadcrumbObservation>();
        using var subscription = BreadcrumbTelemetry.Subscribe(
            new BreadcrumbObserver(observations));

        await ConsoleCapture.RunAsync(async () =>
        {
            args = CommandLineBuilder.PreprocessArgs(args);
            var parseResult = CommandLineBuilder.CreateRootCommand().Parse(args);
            Assert.Empty(parseResult.Errors);
            return await CommandLineBuilder.InvokeAsync(parseResult);
        });

        return [.. observations];
    }

    private static Task<(int Exit, string Output, string Error)> RunCommandAsync(string[] args)
        => ConsoleCapture.RunAsync(async () =>
        {
            args = CommandLineBuilder.PreprocessArgs(args);
            var parseResult = CommandLineBuilder.CreateRootCommand().Parse(args);
            Assert.Empty(parseResult.Errors);
            return await CommandLineBuilder.InvokeAsync(parseResult);
        });

    private static async Task<(
        int Exit,
        string Output,
        string Error,
        ConcurrentQueue<string> Requests)> RunOnlineVersionFeedCommandAsync(
            string packageName,
            string version,
            string[] args,
            HttpStatusCode? refusedStatus = null,
            bool requireAuthorization = false) =>
        await RunOnlineVersionFeedCommandAsync(
            packageName,
            [version],
            args,
            refusedStatus,
            requireAuthorization);

    private static async Task<(
        int Exit,
        string Output,
        string Error,
        ConcurrentQueue<string> Requests)> RunOnlineVersionFeedCommandAsync(
            string packageName,
            IReadOnlyList<string> versions,
            string[] args,
            HttpStatusCode? refusedStatus = null,
            bool requireAuthorization = false)
    {
        var requests = new ConcurrentQueue<string>();
        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
            innerHandler => new VersionFeedHandler(
                SecondSource,
                packageName,
                versions,
                refusedStatus,
                requireAuthorization,
                requests,
                innerHandler));
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        DotnetInspector.Core.HttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new VersionFeedHandler(
                SecondSource,
                packageName,
                versions,
                refusedStatus,
                requireAuthorization,
                requests,
                new HttpClientHandler()));
        try
        {
            var result = await RunCommandAsync(args);
            return (
                result.Exit,
                result.Output,
                result.Error,
                requests);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
                null);
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
    }

    private sealed class BreadcrumbObserver(
        ConcurrentQueue<BreadcrumbObservation> observations)
        : IObserver<BreadcrumbObservation>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(BreadcrumbObservation value)
            => observations.Enqueue(value);
    }

    private sealed class VersionFeedHandler(
        string sourceUrl,
        string packageName,
        IReadOnlyList<string> versions,
        HttpStatusCode? refusedStatus,
        bool requireAuthorization,
        ConcurrentQueue<string> requests,
        HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            requests.Enqueue(url);
            if (requireAuthorization
                && request.Headers.Authorization?.Scheme != "Basic")
            {
                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(""),
                    RequestMessage = request,
                });
            }

            if (refusedStatus is { } status
                && url.StartsWith(
                    new Uri(RefusedSource).GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(""),
                    RequestMessage = request,
                });
            }

            string? body = url switch
            {
                _ when url.Equals(
                    ExcludedSource,
                    StringComparison.OrdinalIgnoreCase) => $$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "{{ExcludedFlatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """,
                _ when url.Equals(
                    sourceUrl,
                    StringComparison.OrdinalIgnoreCase) => $$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "{{SecondFlatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """,
                _ when url.Equals(
                    $"{SecondFlatContainer}{packageName.ToLowerInvariant()}/index.json",
                    StringComparison.OrdinalIgnoreCase) =>
                    $$"""{"versions":[{{string.Join(",", versions.Select(static version => $"\"{version}\""))}}]}""",
                _ => null,
            };

            return Task.FromResult(new HttpResponseMessage(
                body is null
                    ? HttpStatusCode.NotFound
                    : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? ""),
                RequestMessage = request,
            });
        }
    }

    private sealed class RecordingCredentialSource : ICredentialSource
    {
        public List<Uri> Queries { get; } = [];
        public bool HasCredentialSources => true;

        public Task<NuGetFetch.PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            Queries.Add(uri);
            return Task.FromResult<NuGetFetch.PackageSourceCredential?>(
                new("reader", "token"));
        }
    }

    private sealed class AuthenticationIsolationHandler(
        string index,
        string flatContainer,
        string packageName,
        string version,
        bool requireAuthentication) : HttpMessageHandler
    {
        public bool SawAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            bool hasAuthorization = request.Headers.Authorization is not null;
            SawAuthorization |= hasAuthorization;
            string url = request.RequestUri!.AbsoluteUri;
            HttpResponseMessage response;
            if (url.Equals(index, StringComparison.Ordinal))
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "version": "3.0.0",
                          "resources": [
                            { "@id": "{{flatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
                          ]
                        }
                        """),
                };
            }
            else if (url.Equals(
                         $"{flatContainer}{packageName.ToLowerInvariant()}/index.json",
                         StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(
                    requireAuthentication && !hasAuthorization
                        ? HttpStatusCode.Unauthorized
                        : HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        requireAuthentication && !hasAuthorization
                            ? ""
                            : $$"""{"versions":["{{version}}"]}"""),
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(""),
                };
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class CannedResponseHandler(
        IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            bool found = responses.TryGetValue(
                request.RequestUri!.AbsoluteUri,
                out string? body);
            return Task.FromResult(new HttpResponseMessage(
                found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? ""),
                RequestMessage = request,
            });
        }
    }

    private sealed class UnavailableCredentialSource : ICredentialSource
    {
        public bool HasCredentialSources => false;

        public Task<NuGetFetch.PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "An unavailable credential source must not be queried.");
    }

    private sealed class NeverCompletesHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request unexpectedly completed.");
        }
    }

    private sealed class NuGetOrgRegistrationFailureHandler(
        string packageName,
        string[] versions,
        HttpMessageHandler innerHandler,
        HttpStatusCode registrationStatus = HttpStatusCode.Forbidden)
        : DelegatingHandler(innerHandler)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            string flatContainer =
                $"https://api.nuget.org/v3-flatcontainer/{packageName.ToLowerInvariant()}/index.json";
            string registration =
                $"https://api.nuget.org/v3/registration5-gz-semver2/{packageName.ToLowerInvariant()}/index.json";

            HttpResponseMessage response;
            if (url.Equals(flatContainer, StringComparison.OrdinalIgnoreCase))
            {
                string body = "{\"versions\":["
                    + string.Join(",", versions.Select(version => $"\"{version}\""))
                    + "]}";
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                };
            }
            else if (url.Equals(
                registration,
                StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(registrationStatus)
                {
                    Content = new StringContent(""),
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(""),
                };
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class RouterFeedHandler(
        string packageName,
        ConcurrentQueue<string> requests,
        HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        public const string FlatContainer = "https://excluded.invalid/v3/flat2/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            requests.Enqueue(url);

            string? body = url switch
            {
                ExcludedSource => $$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "{{FlatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """,
                _ when url.Equals(
                    $"{FlatContainer}{packageName.ToLowerInvariant()}/index.json",
                    StringComparison.OrdinalIgnoreCase) => """{"versions":["1.0.0"]}""",
                _ => null,
            };

            var response = new HttpResponseMessage(
                body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? ""),
                RequestMessage = request,
            };
            return Task.FromResult(response);
        }
    }
}
