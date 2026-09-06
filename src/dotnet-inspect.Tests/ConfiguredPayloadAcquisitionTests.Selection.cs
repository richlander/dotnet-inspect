using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

using DotnetInspector.Packages;
using NuGetFetch;

using CoreHttpClientFactory = DotnetInspector.Core.HttpClientFactory;
using DesktopPackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Theory]
    [InlineData(null, false, false, "2.0.0")]
    [InlineData("latest", false, true, "2.0.0")]
    [InlineData(null, true, false, "3.0.0-preview.2")]
    [InlineData("1.*", false, true, "1.5.0")]
    [InlineData("*", false, false, "3.0.0-preview.2")]
    [InlineData("3.0.0-preview*", false, true, "3.0.0-preview.2")]
    public async Task PackageCommand_LocalSelectionPrintsSelectedPayload(
        string? selector, bool preview, bool fileUri, string expectedVersion)
    {
        string id = $"Selected.Local.{Guid.NewGuid():N}";
        string source = Path.Combine(_root, "selection-feed");
        foreach (string version in new[] { "1.0.0", "1.5.0", "2.0.0", "3.0.0-preview.1", "3.0.0-preview.2" })
            WriteLocalPackage(source, id, $"payload {version}", hierarchical: fileUri, version: version);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Local selection created an HTTP transport."));

        List<string> args = ["package", selector is null ? id : $"{id}@{selector}",
            "--source", fileUri ? new Uri(source).AbsoluteUri : source,
            "--path", "@readme", "--content", "--bare"];
        if (preview)
            args.Add("--preview");
        var (exit, output, error) = await RunCommandAsync([.. args]);

        Assert.True(exit == 0, $"Exit {exit}: {error}");
        Assert.Equal($"payload {expectedVersion}", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("*")]
    [InlineData(Version)]
    public async Task PackageCommand_ExplicitPackageReferenceWithArchiveSuffixIsNotAFile(string selector)
    {
        const string Id = "Selected.Package.nupkg";
        string source = Path.Combine(_root, "suffix");
        WriteLocalPackage(source, Id, "package identity, not a file path");

        var (exit, output, error) = await RunCommandAsync(
            ["package", $"{Id}@{selector}", "--source", source,
                "--path", "@readme", "--content", "--bare"]);

        Assert.True(exit == 0, error);
        Assert.Equal("package identity, not a file path", output.Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    public async Task AcquireSelected_PartialDiscoveryDoesNotProbePayloadCaches(string? selector)
    {
        const string Id = "Selected.Partial";
        string source = Path.Combine(_root, "healthy");
        string missing = Path.Combine(_root, "missing");
        WriteLocalPackage(source, Id, "not a complete selection");
        await using var composition = LocalComposition();

        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            Id, selector,
            (_, _) => throw new InvalidOperationException("Incomplete discovery reached a payload store."),
            new NuGetSourceOptions { Sources = [source, missing] },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Payload);
        Assert.Null(result.Authority);
        Assert.Contains(result.Failures, failure =>
            failure.Kind == PackageAuthorityFailureKind.Transport
            && failure.Authority.ToString().Contains(missing, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcquireSelected_NonReportingWarmCacheCannotSupplySelectedVersion(bool reporterMissing)
    {
        const string Id = "Selected.NonReporter";
        const string SelectedVersion = "2.0.0";
        var requests = new ConcurrentQueue<string>();
        var stores = new Dictionary<ConfiguredPackageAuthority, IPackageStore>();
        await using var composition = CreateComposition((source, _) =>
            new SelectionFeedHandler(source.Url, Id,
                source.Url == FirstFeed ? [SelectedVersion] : [Version],
                version => CreatePackage(Id, source.Url, version: version), requests,
                missingPayload: source.Url == FirstFeed && reporterMissing));
        IPackageStore GetStore(ConfiguredPackageAuthority authority, PackageProducerIdentity _)
        {
            if (!stores.TryGetValue(authority, out IPackageStore? store))
                stores.Add(authority, store = new InMemoryPackageStore());
            return store;
        }

        ConfiguredPackagePayloadResult warm = await composition.AcquirePinnedAsync(
            Id, SelectedVersion, GetStore, new NuGetSourceOptions { Sources = [SecondFeed] },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(warm.Payload);
        Assert.Equal(SecondFeed, ReadReadme(warm.Payload.Content));
        int nonReporterStoreReads = 0;

        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            Id, null,
            (authority, producer) =>
            {
                if (ReferenceEquals(authority, warm.Authority))
                    nonReporterStoreReads++;
                return GetStore(authority, producer);
            },
            new NuGetSourceOptions { Sources = [SecondFeed, FirstFeed] },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, nonReporterStoreReads);
        if (reporterMissing)
        {
            Assert.Null(result.Payload);
            Assert.Null(result.Authority);
        }
        else
        {
            Assert.NotNull(result.Payload);
            Assert.Equal(SelectedVersion, result.Payload.Coordinate.Version);
            Assert.Equal(FirstFeed, ReadReadme(result.Payload.Content));
            Assert.Equal(FirstFeed, result.Authority!.Source.Url);
        }
    }

    [Fact]
    public async Task AcquireSelected_QueryDistinctAuthoritiesDoNotShareReportingEvidence()
    {
        const string Id = "Selected.QueryAuthority";
        const string Older = "https://query-selection.invalid/v3/index.json?channel=older";
        const string Newer = "https://query-selection.invalid/v3/index.json?channel=newer";
        var requests = new ConcurrentQueue<string>();
        await using var composition = CreateComposition((source, _) =>
            new SelectionFeedHandler(source.Url, Id,
                source.Url == Older ? [Version] : ["2.0.0"],
                version => CreatePackage(Id, source.Url, version: version), requests));
        ConfiguredPackagePayloadResult older = await composition.AcquirePinnedAsync(
            Id, Version, (_, _) => new InMemoryPackageStore(),
            new NuGetSourceOptions { Sources = [Older] },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(older.Payload);

        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            Id, "*", (authority, _) =>
            {
                Assert.Equal(Newer, authority.Source.Url);
                return new InMemoryPackageStore();
            },
            new NuGetSourceOptions { Sources = [Older, Newer] },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Payload);
        Assert.Equal("2.0.0", result.Payload.Coordinate.Version);
        Assert.Equal(Newer, ReadReadme(result.Payload.Content));
        Assert.Equal(Newer, result.Authority!.Source.Url);
        Assert.Equal(older.Payload.ProducerKey, result.Payload.ProducerKey);
        Assert.NotSame(older.Authority, result.Authority);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcquireSelected_GalleryListingEvidenceControlsAuthorization(bool missingListingState)
    {
        const string Id = "selected.gallery";
        const string Gallery = "https://api.nuget.org/v3/index.json";
        var requests = new ConcurrentQueue<string>();
        await using var composition = CreateComposition((source, isGallery) =>
            new SelectionFeedHandler(source.Url, Id, [Version],
                _ => CreatePackage(Id, source.Url), requests,
                listed: !isGallery, missingListingState: isGallery && missingListingState));

        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            Id, null, (authority, _) =>
            {
                Assert.False(missingListingState, "Incomplete listing metadata reached a payload store.");
                Assert.Equal(FirstFeed, authority.Source.Url);
                return new InMemoryPackageStore();
            },
            new NuGetSourceOptions { Sources = [Gallery, FirstFeed] },
            cancellationToken: TestContext.Current.CancellationToken);

        if (missingListingState)
        {
            Assert.Null(result.Payload);
            Assert.Contains(result.Failures,
                failure => failure.Kind == PackageAuthorityFailureKind.IncompleteMetadata);
        }
        else
        {
            Assert.Equal(FirstFeed, ReadReadme(AssertPayload(result, Id).Content));
            Assert.Equal(FirstFeed, result.Authority!.Source.Url);
            Assert.Empty(result.Failures);
        }
    }

    [Fact]
    public async Task PackageCommand_SelectionRefreshesWithWarmLocalPayload()
    {
        const string Id = "Selected.Fresh";
        string source = Path.Combine(_root, "fresh");
        WriteLocalPackage(source, Id, "first version");
        string[] args = ["package", Id, "--source", source, "--path", "@readme", "--content", "--bare"];
        var first = await RunCommandAsync(args);
        Assert.True(first.Exit == 0, first.Error);
        Assert.Equal("first version", first.Output.Trim());

        WriteLocalPackage(source, Id, "newer version", version: "2.0.0");
        var next = await RunCommandAsync(args);
        Assert.True(next.Exit == 0, next.Error);
        Assert.Equal("newer version", next.Output.Trim());
    }

    [Theory]
    [InlineData("1.0.0..3.0.0", "first", "1.0.0")]
    [InlineData("1.0.0..3.0.0", "#2", "2.0.0")]
    [InlineData("1.0.0..3.0.0", "last", "3.0.0")]
    [InlineData("3.0.0..1.0.0", "#1", "3.0.0")]
    [InlineData("3.0.0..1.0.0", "last", "1.0.0")]
    [InlineData("3.0.0..1.0.0", "2.0.0", "2.0.0")]
    public async Task AcquireSelected_RangeRetainsAddressDirectionAndReporter(
        string range, string address, string expectedVersion)
    {
        const string Id = "Selected.Range";
        string source = Path.Combine(_root, "range");
        string peer = Path.Combine(_root, "outside-range");
        foreach (string version in new[] { "1.0.0", "2.0.0", "3.0.0" })
            WriteLocalPackage(source, Id, $"range {version}", version: version);
        WriteLocalPackage(peer, Id, "outside range", version: "4.0.0");
        await using var composition = LocalComposition();

        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            Id, range, (_, _) => new InMemoryPackageStore(),
            new NuGetSourceOptions { Sources = [peer, source] },
            rangeAddress: address,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Payload);
        Assert.Equal(expectedVersion, result.Payload.Coordinate.Version);
        Assert.Equal($"range {expectedVersion}", ReadReadme(result.Payload.Content));
        Assert.Equal(source, result.Authority!.Source.Url);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData("1.0.0..bad", "first")]
    [InlineData("1.0.0..2.0.0", null)]
    [InlineData(null, "first")]
    public async Task AcquireSelected_InvalidSelectorFailsBeforeTransport(string? selector, string? address)
    {
        await using var composition = LocalComposition();
        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            "Selected.Invalid", selector,
            (_, _) => throw new InvalidOperationException("Invalid selection reached a store."),
            new NuGetSourceOptions { Sources = [FirstFeed] },
            rangeAddress: address,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Payload);
        Assert.Contains(result.Failures, failure => failure.Kind == PackageAuthorityFailureKind.Input);
    }

    [Fact]
    public async Task ExtractSelected_ReportedVersionWithoutPayloadReturnsAcquisitionFailure()
    {
        const string Id = "Selected.Unavailable";
        var requests = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));

        PackageExtractionOutcome outcome = await DesktopPackageExtractor.ExtractSelectedPackageAsync(
            client, Id, "",
            sourceOptions: new NuGetSourceOptions { Sources = [FirstFeed] },
            createComposition: () => CreateComposition((source, _) =>
                new SelectionFeedHandler(source.Url, Id, [Version],
                    _ => CreatePackage(Id, "unavailable"), requests, missingPayload: true)));

        Assert.False(outcome.IsSuccess);
        Assert.Null(outcome.Result);
        Assert.Contains("selection 'latest'", outcome.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("No eligible reporting source supplied a matching payload.",
            outcome.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(requests, request => request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractSelected_WrapperReauthorizesTargetAndTransfersOwnedTemporary()
    {
        const string Wrapper = "Selected.Wrapper";
        const string Target = "Selected.Target";
        string source = Path.Combine(_root, "target");
        WriteLocalPackage(source, Target, "target bytes");
        string config = WriteConfig([("wrapper", FirstFeed, Wrapper), ("target", source, Target)]);
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        var options = new NuGetSourceOptions { ConfigFile = config };
        PackageExtractionOutcome warm = await DesktopPackageExtractor.ExtractPinnedPackageAsync(
            client, Target, Version, sourceOptions: options);
        Assert.True(warm.IsSuccess, warm.ErrorMessage);
        DesktopPackageExtractor.Cleanup(warm.Result!.TempDir);
        var requests = new ConcurrentQueue<string>();
        PackageExtractionOutcome outcome = await DesktopPackageExtractor.ExtractSelectedPackageAsync(
            client, Wrapper, sourceOptions: options,
            createComposition: () => CreateComposition((authority, _) =>
                new SelectionFeedHandler(authority.Url, Wrapper, [Version],
                    _ => CreatePackage(Wrapper, "wrapper", Target), requests)));
        string? temporaryRoot = outcome.Result?.TempDir;
        try
        {
            Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
            Assert.Equal(Target, outcome.Result!.PackageName, ignoreCase: true);
            Assert.True(outcome.Result.FromCache);
            Assert.Equal(ConfiguredPackageAuthorityKind.LocalFolder, outcome.Result.Authority!.Kind);
            Assert.NotNull(temporaryRoot);
            Assert.True(Directory.Exists(temporaryRoot));
            Assert.Equal(FirstFeed, Assert.Single(outcome.Result.ToolWrapperChain).Authority!.Source.Url);
            Assert.DoesNotContain(requests, request => request.Contains(Target.ToLowerInvariant(), StringComparison.Ordinal));
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(temporaryRoot);
        }
        Assert.False(Directory.Exists(temporaryRoot));
    }

    [Fact]
    public async Task AcquireSelected_ExternalOperationSurvivesDiscoveryThroughCommit()
    {
        const string Id = "Selected.Operation";
        var requests = new ConcurrentQueue<string>();
        await using var composition = CreateComposition((source, _) =>
            new SelectionFeedHandler(source.Url, Id, [Version],
                _ => CreatePackage(Id, "one operation"), requests));
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        var store = new GatedCommitStore(operation);
        Task<ConfiguredPackagePayloadResult> acquisition = composition.AcquireSelectedAsync(
            Id, null, (_, _) => store, new NuGetSourceOptions { Sources = [FirstFeed] },
            cancellationToken: TestContext.Current.CancellationToken, operationContext: operation);
        try
        {
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.False(acquisition.IsCompleted);
            operation.ThrowIfExpired();
        }
        finally
        {
            store.Release.TrySetResult();
        }

        ConfiguredPackagePayloadResult result = await acquisition;
        Assert.Equal("one operation", ReadReadme(AssertPayload(result, Id).Content));
        Assert.True(store.Committed);
        operation.ThrowIfExpired();
    }

    [Fact]
    public async Task AcquireSelected_ExternalOperationDeadlineBoundsDiscovery()
    {
        const string Id = "selected.deadline";
        var requests = new ConcurrentQueue<string>();
        await using var composition = CreateComposition((source, _) =>
            new SelectionFeedHandler(source.Url, Id, [Version],
                _ => throw new InvalidOperationException("Expired discovery reached payload transport."),
                requests, beforeResponse: async (url, token) =>
                {
                    if (url.EndsWith($"/{Id}/index.json", StringComparison.Ordinal))
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }),
            requestTimeout: TimeSpan.FromSeconds(30));
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        ConfiguredPackagePayloadResult result = await composition.AcquireSelectedAsync(
            Id, null,
            (_, _) => throw new InvalidOperationException("Expired discovery reached a payload store."),
            new NuGetSourceOptions { Sources = [FirstFeed] },
            cancellationToken: TestContext.Current.CancellationToken, operationContext: operation)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Null(result.Payload);
        Assert.Contains(result.Failures, failure => failure.Kind == PackageAuthorityFailureKind.Timeout);
        Assert.Contains(requests, url => url.EndsWith($"/{Id}/index.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcquireSelected_DiscoveryCancellationPreservesCallerToken()
    {
        const string Id = "selected.cancel";
        var requests = new ConcurrentQueue<string>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using var composition = CreateComposition((source, _) =>
            new SelectionFeedHandler(source.Url, Id, [Version],
                _ => throw new InvalidOperationException("Cancelled discovery reached payload transport."),
                requests, beforeResponse: async (url, token) =>
                {
                    if (url.EndsWith($"/{Id}/index.json", StringComparison.Ordinal))
                    {
                        cancellation.Cancel();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                }));
        using var operation = composition.CreateOperationContext(cancellation.Token);

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => composition.AcquireSelectedAsync(
                Id, null,
                (_, _) => throw new InvalidOperationException("Cancelled discovery reached a payload store."),
                new NuGetSourceOptions { Sources = [FirstFeed] },
                cancellationToken: cancellation.Token, operationContext: operation));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private sealed class SelectionFeedHandler(
        string source, string id, IReadOnlyList<string> versions,
        Func<string, byte[]> payload, ConcurrentQueue<string> requests,
        bool missingPayload = false, bool listed = true, bool missingListingState = false,
        Func<string, CancellationToken, Task>? beforeResponse = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            requests.Enqueue(url);
            if (beforeResponse is not null)
                await beforeResponse(url, cancellationToken);
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            HttpContent content;
            HttpStatusCode status = HttpStatusCode.OK;
            if (url == source)
            {
                content = new StringContent($$"""
                    {"version":"3.0.0","resources":[
                      {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                    ]}
                    """);
            }
            else if (url == $"{flat}{id.ToLowerInvariant()}/index.json"
                || url == $"https://globalcdn.nuget.org/v3-flatcontainer/{id}/index.json")
            {
                content = new StringContent(JsonSerializer.Serialize(new { versions }));
            }
            else if (url == $"https://globalcdn.nuget.org/v3/registration5-gz-semver2/{id}/index.json")
            {
                status = missingListingState ? HttpStatusCode.NotFound : HttpStatusCode.OK;
                content = new StringContent(JsonSerializer.Serialize(new
                {
                    items = new[] { new { items = versions.Select(version => new
                    {
                        catalogEntry = new { version, listed },
                    }) } },
                }));
            }
            else if (url.StartsWith($"{flat}{id.ToLowerInvariant()}/", StringComparison.Ordinal)
                && url.EndsWith(".nupkg", StringComparison.Ordinal))
            {
                status = missingPayload ? HttpStatusCode.NotFound : HttpStatusCode.OK;
                string version = request.RequestUri.Segments[^2].TrimEnd('/');
                content = new ByteArrayContent(missingPayload ? [] : payload(version));
            }
            else
            {
                throw new InvalidOperationException($"Unexpected selected-payload request: {url}");
            }
            return new HttpResponseMessage(status)
            {
                Content = content,
                RequestMessage = request,
            };
        }
    }
}
