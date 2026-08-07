using System.Collections.Concurrent;
using System.Net;

using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class SourceScopedRoutingTests : IDisposable
{
    private const string ExcludedSource = "https://excluded.invalid/v3/index.json";
    private const string SecondSource =
        "https://second.invalid/v3/index.json";

    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-source-routing-{Guid.NewGuid():N}");
    private readonly IReadOnlyList<string> _ambientSourceKeys;

    public SourceScopedRoutingTests()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: true);
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
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false);
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
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false);
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
            DotnetInspector.Core.HttpClientFactory.Initialize(offline: true);
            DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        }
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
                    "1",
                    "--count",
                    "--source",
                    SecondSource,
                ]);

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
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

    private void SeedPackage(string packageName)
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
            _ambientSourceKeys[0]);
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
            string[] args)
    {
        var requests = new ConcurrentQueue<string>();
        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
            innerHandler => new VersionFeedHandler(
                SecondSource,
                packageName,
                version,
                requests,
                innerHandler));
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false);
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
            DotnetInspector.Core.HttpClientFactory.Initialize(offline: true);
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
        ConcurrentQueue<string> requests,
        HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        private const string FlatContainer =
            "https://second.invalid/v3/flat2/";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            requests.Enqueue(url);
            string? body = url switch
            {
                _ when url.Equals(
                    sourceUrl,
                    StringComparison.OrdinalIgnoreCase) => $$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "{{FlatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """,
                _ when url.Equals(
                    $"{FlatContainer}{packageName.ToLowerInvariant()}/index.json",
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
