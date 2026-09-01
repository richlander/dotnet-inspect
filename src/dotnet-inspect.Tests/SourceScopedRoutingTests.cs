using System.Collections.Concurrent;
using System.Net;

using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class SourceScopedRoutingTests : IDisposable
{
    private const string ExcludedSource = "https://excluded.invalid/v3/index.json";
    private const string RefusedSource = "https://refused.invalid/v3/index.json";
    private const string SecondSource =
        "https://second.invalid/v3/index.json";
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
                    "--count",
                    "--source",
                    SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
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
        Assert.Contains("HTTP 401", error);
        Assert.Contains(RefusedSource, error);
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
            HttpStatusCode? refusedStatus = null)
    {
        var requests = new ConcurrentQueue<string>();
        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
            innerHandler => new VersionFeedHandler(
                SecondSource,
                packageName,
                version,
                refusedStatus,
                requests,
                innerHandler));
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
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
        string version,
        HttpStatusCode? refusedStatus,
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
                    StringComparison.OrdinalIgnoreCase) => $$"""
                    {"versions":["{{version}}"]}
                    """,
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
