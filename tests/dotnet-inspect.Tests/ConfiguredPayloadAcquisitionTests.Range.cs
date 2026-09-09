using System.Collections.Concurrent;

using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetFetch;

using CoreHttpClientFactory = DotnetInspector.Core.HttpClientFactory;
using DesktopPackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Fact]
    public async Task OpenRange_OneMetadataDiscoveryServesMultipleAddressesAndReporters()
    {
        const string Id = "range.discovery";
        var requests = new ConcurrentQueue<string>();
        int compositions = 0;
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        await using var range = await DesktopPackageExtractor.OpenPackageRangeAsync(
            client, ParseRange($"{Id}@1.0.0..3.0.0"),
            sourceOptions: new NuGetSourceOptions { Sources = [SecondFeed, FirstFeed] },
            createComposition: () =>
            {
                compositions++;
                return CreateComposition((source, _) =>
                    new SelectionFeedHandler(source.Url, Id,
                        source.Url == FirstFeed ? ["1.0.0", "2.0.0"] : ["2.0.0", "3.0.0"],
                        version => CreatePackage(Id, source.Url, version: version), requests));
            });

        Assert.Equal(["1.0.0", "2.0.0", "3.0.0"],
            range.Vector.Addresses.Select(address => address.Version.ToNormalizedString()));
        Assert.DoesNotContain(requests, url => url.EndsWith(".nupkg", StringComparison.Ordinal));
        foreach (var (selector, version, reporters) in new[]
        {
            ("first", "1.0.0", new[] { FirstFeed }),
            ("#2", "2.0.0", new[] { FirstFeed, SecondFeed }),
            ("last", "3.0.0", new[] { SecondFeed }),
            ("2.0.0", "2.0.0", new[] { FirstFeed, SecondFeed }),
        })
        {
            PackageExtractionOutcome outcome = await range.ExtractAsync(selector);
            try
            {
                Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
                Assert.Equal(version, outcome.Result!.Version);
                Assert.Equal(reporters, outcome.Result.SelectedVersionSourceUrls);
                Assert.Equal(reporters.Length == 2, outcome.Result.SelectedVersionUsesOriginalSources);
                Assert.Contains(outcome.Result.Authority!.Source.Url, reporters);
                AcquiredPackageSourcePayload payload =
                    Assert.IsType<AcquiredPackageSourcePayload>(outcome.Result.AcquiredPayload);
                Assert.Equal(outcome.Result.ExtractPath, payload.Content.RootPath);
                Assert.Equal(outcome.Result.ProducerKey, payload.ProducerKey);
                Assert.Equal(outcome.Result.Authority.Source.Url,
                    File.ReadAllText(Path.Combine(outcome.Result.ExtractPath, "README.md")));
            }
            finally
            {
                DesktopPackageExtractor.Cleanup(outcome.Result?.TempDir);
            }
        }
        Assert.Equal(1, compositions);
        Assert.Equal(2, requests.Count(url => url.EndsWith($"/{Id}/index.json", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenRange_IncompleteOrFailedDiscoveryCannotAcquireAnyPayload(bool hasHealthyPeer)
    {
        const string Id = "range.partial";
        string missing = Path.Combine(_root, "missing-range-source");
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DesktopPackageExtractor.OpenPackageRangeAsync(
                client, ParseRange($"{Id}@1.0.0..2.0.0"),
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = hasHealthyPeer ? [FirstFeed, missing] : [missing],
                },
                createComposition: () => CreateComposition((source, _) =>
                    new SelectionFeedHandler(source.Url, Id, ["1.0.0", "2.0.0"],
                        _ => throw new InvalidOperationException("Partial discovery requested a payload."),
                        requests))));

        Assert.Contains(hasHealthyPeer ? "partial" : "failed", failure.Message, StringComparison.Ordinal);
        Assert.Contains(missing, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, url => url.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenRange_MissingEndpointFailsWithoutPayloadAcquisition()
    {
        const string Id = "range.missing.endpoint";
        string local = Path.Combine(_root, "range-missing-endpoint");
        WriteLocalPackage(local, Id, "only one endpoint");
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));

        ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(
            () => DesktopPackageExtractor.OpenPackageRangeAsync(
                client, ParseRange($"{Id}@1.0.0..2.0.0"),
                sourceOptions: new NuGetSourceOptions { Sources = [local] },
                createComposition: LocalComposition));
        Assert.Contains("does not contain range endpoint 2.0.0", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Range_NonReportingWarmLocalCacheCannotAnswer(bool reporterMissing)
    {
        const string Id = "range.warm.nonreporter";
        const string SelectedVersion = "2.0.0";
        string local = Path.Combine(_root, "warm-nonreporter");
        WriteLocalPackage(local, Id, "ineligible cached payload", version: SelectedVersion);
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        PackageExtractionOutcome warm = await DesktopPackageExtractor.ExtractPinnedPackageAsync(
            client, Id, SelectedVersion, sourceOptions: new NuGetSourceOptions { Sources = [local] },
            createComposition: LocalComposition);
        Assert.True(warm.IsSuccess, warm.ErrorMessage);
        DesktopPackageExtractor.Cleanup(warm.Result!.TempDir);
        File.Delete(Path.Combine(local, $"{Id}.{SelectedVersion}.nupkg"));
        WriteLocalPackage(local, Id, "only the old version is reported");
        var requests = new ConcurrentQueue<string>();

        await using var range = await DesktopPackageExtractor.OpenPackageRangeAsync(
            client, ParseRange($"{Id}@{SelectedVersion}..{SelectedVersion}"),
            sourceOptions: new NuGetSourceOptions { Sources = [local, FirstFeed] },
            createComposition: () => CreateComposition((source, _) =>
                new SelectionFeedHandler(source.Url, Id, [SelectedVersion],
                    version => CreatePackage(Id, "eligible HTTP payload", version: version), requests,
                    missingPayload: reporterMissing)));
        PackageExtractionOutcome outcome = await range.ExtractAsync("first");
        try
        {
            if (reporterMissing)
            {
                Assert.False(outcome.IsSuccess);
                Assert.Contains("No eligible reporting source", outcome.ErrorMessage, StringComparison.Ordinal);
            }
            else
            {
                Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
                Assert.Equal(FirstFeed, outcome.Result!.Authority!.Source.Url);
                Assert.False(outcome.Result.FromCache);
                Assert.Equal("eligible HTTP payload",
                    File.ReadAllText(Path.Combine(outcome.Result.ExtractPath, "README.md")));
                Assert.Equal([FirstFeed], outcome.Result.SelectedVersionSourceUrls);
                Assert.False(outcome.Result.SelectedVersionUsesOriginalSources);
            }
            Assert.Contains(requests, url => url.EndsWith(".nupkg", StringComparison.Ordinal));
            Assert.Equal("ineligible cached payload",
                File.ReadAllText(Path.Combine(warm.Result.ExtractPath, "README.md")));
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(outcome.Result?.TempDir);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenRange_GalleryUnlistedEndpointNeedsIndependentReporter(bool hasListedPeer)
    {
        const string Id = "range.gallery.unlisted";
        const string Gallery = "https://api.nuget.org/v3/index.json";
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        Task<PackageRangeExtraction> opening = DesktopPackageExtractor.OpenPackageRangeAsync(
            client, ParseRange($"{Id}@{Version}..{Version}"),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = hasListedPeer ? [Gallery, FirstFeed] : [Gallery],
            },
            createComposition: () => CreateComposition((source, isGallery) =>
                new SelectionFeedHandler(source.Url, Id, [Version],
                    _ => CreatePackage(Id, source.Url), requests, listed: !isGallery)));
        if (!hasListedPeer)
        {
            ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(() => opening);
            Assert.Contains($"does not contain range endpoint {Version}", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(requests, url => url.EndsWith(".nupkg", StringComparison.Ordinal));
            return;
        }

        await using var range = await opening;
        PackageExtractionOutcome outcome = await range.ExtractAsync("first");
        try
        {
            Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
            Assert.Equal(FirstFeed, outcome.Result!.Authority!.Source.Url);
            Assert.Equal([FirstFeed], outcome.Result.SelectedVersionSourceUrls);
            Assert.False(outcome.Result.SelectedVersionUsesOriginalSources);
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(outcome.Result?.TempDir);
        }
    }

    [Fact]
    public async Task OpenRange_MissingGalleryListingStateCannotUsePeerOrPayload()
    {
        const string Id = "range.gallery.incomplete";
        const string Gallery = "https://api.nuget.org/v3/index.json";
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DesktopPackageExtractor.OpenPackageRangeAsync(
                client, ParseRange($"{Id}@{Version}..{Version}"),
                sourceOptions: new NuGetSourceOptions { Sources = [FirstFeed, Gallery] },
                createComposition: () => CreateComposition((source, isGallery) =>
                    new SelectionFeedHandler(source.Url, Id, [Version],
                        _ => throw new InvalidOperationException("Incomplete listing acquired a payload."),
                        requests, missingListingState: isGallery))));

        Assert.Contains("authoritative version listing state", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, url => url.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("3.0.0..1.0.0", false, "3.0.0,2.0.0,1.0.0")]
    [InlineData("3.0.0..1.0.0", true, "3.0.0,3.0.0-preview.1,2.0.0,2.0.0-preview.1,1.0.0")]
    [InlineData("3.0.0-preview.1..1.0.0", false, "3.0.0-preview.1,2.0.0,2.0.0-preview.1,1.0.0")]
    [InlineData("1.0.0..3.0.0", false, "1.0.0,2.0.0,3.0.0")]
    public async Task Range_PreservesCallerDirectionAndPrereleasePolicy(
        string endpoints, bool includePrerelease, string expected)
    {
        const string Id = "Range.Direction";
        string source = Path.Combine(_root, "range-direction");
        foreach (string version in new[] { "1.0.0", "2.0.0-preview.1", "2.0.0", "3.0.0-preview.1", "3.0.0" })
            WriteLocalPackage(source, Id, version, version: version);
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        await using var range = await DesktopPackageExtractor.OpenPackageRangeAsync(
            client, ParseRange($"{Id}@{endpoints}"),
            sourceOptions: new NuGetSourceOptions { Sources = [source] },
            includePrerelease: includePrerelease, createComposition: LocalComposition);

        Assert.Equal(expected.Split(','),
            range.Vector.Addresses.Select(address => address.Version.ToNormalizedString()));
        PackageExtractionOutcome outcome = await range.ExtractAsync("#2");
        try
        {
            Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
            Assert.Equal(expected.Split(',')[1], outcome.Result!.Version);
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(outcome.Result?.TempDir);
        }
    }

    [Fact]
    public async Task Range_InvalidAddressesDoNotAcquirePayloadsOrRediscover()
    {
        const string Id = "range.addresses";
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        await using var range = await DesktopPackageExtractor.OpenPackageRangeAsync(
            client, ParseRange($"{Id}@{Version}..{Version}"),
            sourceOptions: new NuGetSourceOptions { Sources = [FirstFeed] },
            createComposition: () => CreateComposition((source, _) =>
                new SelectionFeedHandler(source.Url, Id, [Version],
                    _ => throw new InvalidOperationException("Invalid address acquired a payload."), requests)));

        foreach (string selector in new[] { "", "#0", "#2", "2.0.0", "invalid", "1.*" })
        {
            PackageExtractionOutcome outcome = await range.ExtractAsync(selector);
            Assert.False(outcome.IsSuccess);
            Assert.NotEmpty(outcome.ErrorMessage!);
        }
        Assert.Single(requests, url => url.EndsWith($"/{Id}/index.json", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, url => url.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Range_RepeatedHttpExtractionsTransferIndependentRootsBeyondDisposal()
    {
        const string Id = "range.lifetime";
        var requests = new ConcurrentQueue<string>();
        var results = new List<PackageExtractionResult>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        try
        {
            await using (var range = await DesktopPackageExtractor.OpenPackageRangeAsync(
                client, ParseRange($"{Id}@1.0.0..2.0.0"),
                sourceOptions: new NuGetSourceOptions { Sources = [FirstFeed] },
                createComposition: () => CreateComposition((source, _) =>
                    new SelectionFeedHandler(source.Url, Id, ["1.0.0", "2.0.0"],
                        version => CreatePackage(Id, version, version: version), requests))))
            {
                foreach (string address in new[] { "first", "last", "first" })
                {
                    PackageExtractionOutcome outcome = await range.ExtractAsync(address);
                    Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
                    results.Add(outcome.Result!);
                    Assert.NotNull(outcome.Result!.TempDir);
                    Assert.False(outcome.Result.FromCache);
                }
            }

            Assert.Equal(3, results.Select(result => result.TempDir).Distinct().Count());
            Assert.Equal(3, requests.Count(url => url.EndsWith(".nupkg", StringComparison.Ordinal)));
            foreach (PackageExtractionResult result in results)
            {
                Assert.True(Directory.Exists(result.TempDir));
                Assert.True(File.Exists(result.NupkgPath));
                Assert.Equal(result.Version, File.ReadAllText(Path.Combine(result.ExtractPath, "README.md")));
            }
            DesktopPackageExtractor.Cleanup(results[0].TempDir);
            Assert.False(Directory.Exists(results[0].ExtractPath));
            Assert.All(results.Skip(1), result =>
                Assert.Equal(result.Version, File.ReadAllText(Path.Combine(result.ExtractPath, "README.md"))));
        }
        finally
        {
            foreach (PackageExtractionResult result in results)
                DesktopPackageExtractor.Cleanup(result.TempDir);
        }
    }

    [Fact]
    public async Task Range_FailedWrapperAttemptCleansItsRootBeforeNextExtraction()
    {
        const string Id = "range.failed.wrapper";
        string prefix = $"range-attempt-{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        await using var range = await DesktopPackageExtractor.OpenPackageRangeAsync(
            client, ParseRange($"{Id}@1.0.0..2.0.0"), tempDirPrefix: prefix,
            sourceOptions: new NuGetSourceOptions { Sources = [FirstFeed] },
            createComposition: () => CreateComposition((source, _) =>
                new SelectionFeedHandler(source.Url, Id, ["1.0.0", "2.0.0"],
                    version => CreatePackage(Id, version,
                        redirectId: version == Version ? "../invalid" : null, version: version), requests)));

        Assert.Empty(Directory.EnumerateDirectories(Path.GetTempPath(), $"{prefix}*"));
        PackageExtractionOutcome failed = await range.ExtractAsync("first");
        Assert.False(failed.IsSuccess);
        Assert.Contains("invalid redirect package id", failed.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateDirectories(Path.GetTempPath(), $"{prefix}*"));
        PackageExtractionOutcome next = await range.ExtractAsync("last");
        try
        {
            Assert.True(next.IsSuccess, next.ErrorMessage);
            Assert.Equal(next.Result!.TempDir,
                Assert.Single(Directory.EnumerateDirectories(Path.GetTempPath(), $"{prefix}*")));
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(next.Result?.TempDir);
        }
    }

    [Fact]
    public async Task Range_WrapperRecomputesTargetPolicyAndDropsRootReplayRestrictions()
    {
        const string Wrapper = "range.policy.wrapper";
        const string Target = "range.policy.target";
        string local = Path.Combine(_root, "range-target");
        WriteLocalPackage(local, Target, "mapped target");
        string config = WriteConfig(
            [("wrapper", FirstFeed, Wrapper), ("nonreporter", SecondFeed, Wrapper), ("target", local, Target)]);
        var options = new NuGetSourceOptions { ConfigFile = config };
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        PackageExtractionOutcome warm = await DesktopPackageExtractor.ExtractPinnedPackageAsync(
            client, Target, Version, sourceOptions: options, createComposition: LocalComposition);
        Assert.True(warm.IsSuccess, warm.ErrorMessage);
        Assert.Null(warm.Result!.SelectedVersionSourceUrls);
        DesktopPackageExtractor.Cleanup(warm.Result.TempDir);
        var requests = new ConcurrentQueue<string>();
        PackageExtractionOutcome outcome;
        await using (var range = await DesktopPackageExtractor.OpenPackageRangeAsync(
            client, ParseRange($"{Wrapper}@{Version}..{Version}"), sourceOptions: options,
            createComposition: () => CreateComposition((source, _) =>
                new SelectionFeedHandler(source.Url, Wrapper, source.Url == FirstFeed ? [Version] : [],
                    _ => CreatePackage(Wrapper, "wrapper", Target), requests))))
        {
            outcome = await range.ExtractAsync("first");
        }
        try
        {
            Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
            Assert.True(outcome.Result!.FromCache);
            Assert.Equal(Target, outcome.Result.PackageName);
            Assert.Equal(local, outcome.Result.Authority!.Source.Url);
            Assert.Null(outcome.Result.SelectedVersionSourceUrls);
            Assert.False(outcome.Result.SelectedVersionUsesOriginalSources);
            ToolWrapperPackage wrapper = Assert.Single(outcome.Result.ToolWrapperChain);
            Assert.Equal(FirstFeed, wrapper.Authority!.Source.Url);
            Assert.True(Directory.Exists(wrapper.ExtractPath));
            Assert.NotNull(outcome.Result.TempDir);
            Assert.Equal("mapped target", File.ReadAllText(Path.Combine(outcome.Result.ExtractPath, "README.md")));
            Assert.DoesNotContain(requests, url => url.Contains(Target, StringComparison.Ordinal));
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(outcome.Result?.TempDir);
        }
    }

    [Fact]
    public async Task ExtractSelected_ReplayUsesConfiguredAuthoritiesNotSharedProducerKeys()
    {
        const string Id = "selected.replay.authority";
        const string Older = "https://replay.invalid/v3/index.json?channel=older";
        const string Newer = "https://replay.invalid/v3/index.json?channel=newer";
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        PackageExtractionOutcome outcome = await DesktopPackageExtractor.ExtractSelectedPackageAsync(
            client, Id, sourceOptions: new NuGetSourceOptions { Sources = [Older, Newer] },
            createComposition: () => CreateComposition((source, _) =>
                new SelectionFeedHandler(source.Url, Id, source.Url == Older ? [Version] : ["2.0.0"],
                    version => CreatePackage(Id, source.Url, version: version), requests)));
        try
        {
            Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
            Assert.Equal(Newer, outcome.Result!.Authority!.Source.Url);
            Assert.Equal([Newer], outcome.Result.SelectedVersionSourceUrls);
            Assert.False(outcome.Result.SelectedVersionUsesOriginalSources);
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(outcome.Result?.TempDir);
        }
    }

    [Fact]
    public async Task OpenRange_InvalidIdAndLegacyRestrictionsFailBeforeComposition()
    {
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        foreach (var (reference, options) in new[]
        {
            ("invalid/id@1.0.0..2.0.0", new NuGetSourceOptions()),
            ("Valid@1.0.0..2.0.0", new NuGetSourceOptions { AuthorizedSourceKeys = [] }),
            ("Valid@1.0.0..2.0.0", new NuGetSourceOptions { ResolvedSources = [] }),
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => DesktopPackageExtractor.OpenPackageRangeAsync(
                    client, ParseRange(reference), sourceOptions: options,
                    createComposition: () => throw new InvalidOperationException("Invalid input created a composition.")));
        }
    }

    [Fact]
    public async Task OpenRange_OfflineFailsBeforeComposition()
    {
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions { Offline = true });
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DesktopPackageExtractor.OpenPackageRangeAsync(
                client, ParseRange("Range.Offline@1.0.0..2.0.0"),
                createComposition: () => throw new InvalidOperationException("Offline range created a composition.")));
        Assert.Contains("requires online mode", failure.Message, StringComparison.Ordinal);
    }

    private static PackageVersionRange ParseRange(string reference)
    {
        Assert.True(PackageVersionRange.TryParse(reference, out PackageVersionRange? range, out string? error), error);
        return range!;
    }
}
