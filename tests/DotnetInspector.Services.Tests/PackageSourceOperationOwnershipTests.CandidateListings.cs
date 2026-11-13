using System.Net;
using System.Text;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageSourceOperationOwnershipTests
{
    [Theory]
    [InlineData(false, "2.1.0")]
    [InlineData(true, "2.1.1")]
    public async Task ScopedCandidatesPreserveSelectionAndShareListingFacts(
        bool unlistedMinimum, string expected)
    {
        await using var fixture = new GalleryHistoryFixture(unlistedMinimum);
        using PackageSourceOperationLease operation = fixture.Root.IssueOperationLease(
            TestContext.Current.CancellationToken, null, null, maximumConcurrentSteps: 2);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.BeforeResponse = (url, token) =>
        {
            if (url != GalleryHistoryHandler.EarlyPage)
                return Task.CompletedTask;
            started.TrySetResult();
            return release.Task.WaitAsync(token);
        };
        Task<PackageVersionDiscoveryResult> first = operation.DiscoverDependencyCandidateVersionsAsync(
            GalleryHistoryHandler.PackageId, "[2.1.0,3.0.0)", fixture.Policy);
        Task<PackageVersionDiscoveryResult>? second = null;
        try
        {
            Task signal = await Task.WhenAny(started.Task, first)
                .WaitAsync(TestContext.Current.CancellationToken);
            Assert.Same(started.Task, signal);
            second = operation.DiscoverDependencyCandidateVersionsAsync(
                GalleryHistoryHandler.PackageId, "2.1.0", fixture.Policy);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            release.TrySetResult();
            PackageVersionDiscoveryResult[] results = await Task.WhenAll(first, second);
            Assert.All(results, result =>
            {
                Assert.Equal(PackageVersionDiscoveryState.Authoritative, result.State);
                Assert.Equal([expected], result.Versions);
                Assert.False(result.Contract.SupportsCompleteVersionEnumeration);
                Assert.False(result.SupportsDependencyRangeResolutionFor("[20.0.0,21.0.0)"));
            });
            Assert.Equal(
                results[0].SelectCandidate(expected).Correspondence,
                results[1].SelectCandidate(expected).Correspondence);
            Assert.Equal(
                [GalleryHistoryHandler.Flat, GalleryHistoryHandler.Registration, GalleryHistoryHandler.EarlyPage],
                fixture.Handler.Requested);

            PackageVersionDiscoveryResult full = await operation.DiscoverDependencyVersionsAsync(
                GalleryHistoryHandler.PackageId, fixture.Authorization);
            Assert.Equal(expected, PackageDependencyVersionRange.SelectBestSatisfying(
                full.Versions, "[2.1.0,3.0.0)"));
            Assert.Contains(GalleryHistoryHandler.FuturePage, fixture.Handler.Requested);
            Assert.Equal(PackageVersionDiscoveryState.Authoritative, full.State);
        }
        finally
        {
            release.TrySetResult();
            await first;
            if (second is not null)
                await second;
        }
    }

    [Theory]
    [InlineData(null, "2.1.1")]
    [InlineData("[2.1.0,3.0.0)", "2.1.1")]
    [InlineData("2.*", "2.1.1")]
    [InlineData("[9.0.0,12.0.0)", "9.0.0")]
    [InlineData("11.0.0-rc.*", "11.0.0")]
    [InlineData("[11.0.0-rc.1,11.0.0)", "11.0.0-rc.3")]
    [InlineData("[20.0.0,21.0.0)", null)]
    public async Task ScopedCandidatesMatchCompleteDiscoveryAcrossNuGetConstraints(
        string? constraint, string? expected)
    {
        await using var fixture = new GalleryHistoryFixture(unlistedMinimum: true);
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        PackageVersionDiscoveryResult scoped = await operation.DiscoverDependencyCandidateVersionsAsync(
            GalleryHistoryHandler.PackageId, constraint, fixture.Policy);
        Assert.Equal(PackageVersionDiscoveryState.Authoritative, scoped.State);
        Assert.True(scoped.SupportsDependencyRangeResolutionFor(constraint));
        Assert.Equal(expected is null ? [] : new[] { expected }, scoped.Versions);

        PackageVersionDiscoveryResult complete = await operation.DiscoverDependencyVersionsAsync(
            GalleryHistoryHandler.PackageId, fixture.Authorization);
        Assert.Equal(expected, PackageDependencyVersionRange.SelectBestSatisfying(
            complete.Versions, constraint));
        if (expected is not null)
        {
            Assert.Equal(
                complete.SelectCandidate(expected).Coordinate,
                scoped.SelectCandidate(expected).Coordinate);
        }
    }

    [Fact]
    public async Task ScopedCandidatesDoNotSelectOrRetainMissingRequiredListing()
    {
        await using var fixture = new GalleryHistoryFixture();
        fixture.Handler.Responses.Remove(GalleryHistoryHandler.EarlyPage);
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            PackageVersionDiscoveryResult result = await operation.DiscoverDependencyCandidateVersionsAsync(
                GalleryHistoryHandler.PackageId, "[2.1.0,3.0.0)", fixture.Policy);
            Assert.NotEqual(PackageVersionDiscoveryState.Authoritative, result.State);
            Assert.Contains(result.Failures, failure => failure.Kind == PackageAuthorityFailureKind.IncompleteMetadata);
            Assert.Throws<InvalidOperationException>(() => result.SelectCandidate("2.1.0"));
        }
        Assert.Equal(2, fixture.Handler.Requested.Count(url => url == GalleryHistoryHandler.Flat));
        Assert.Equal(2, fixture.Handler.Requested.Count(url => url == GalleryHistoryHandler.EarlyPage));
        Assert.DoesNotContain(GalleryHistoryHandler.FuturePage, fixture.Handler.Requested);
    }

    [Fact]
    public async Task ScopedCandidatesRetainFullEnumerationFallbackForOtherClients()
    {
        await using var fixture = new SourceFixture();
        using PackageSourceOperationLease operation = Issue(fixture.Root);
        var policy = new FixedAuthorization(fixture.Authorization);
        PackageVersionDiscoveryResult first = await operation.DiscoverDependencyCandidateVersionsAsync(
            Coordinate.PackageId, "1.0.0", policy);
        PackageVersionDiscoveryResult second = await operation.DiscoverDependencyCandidateVersionsAsync(
            Coordinate.PackageId, "[1.0.0,2.0.0)", policy);
        Assert.Equal(Coordinate, first.SelectCandidate("1.0.0").Coordinate);
        Assert.Equal(Coordinate, second.SelectCandidate("1.0.0").Coordinate);
        Assert.Single(fixture.Client.Contexts);
    }

    [Fact]
    public async Task ScopedCandidateExpiryRetainsSettledEvidenceAndUnconsultedAuthorities()
    {
        PackageSourceAuthorization authorization = PackageSourceAuthorization.Authorize(
        [
            new("ready", "https://ready.example/v3/index.json"),
            new("slow", "https://slow.example/v3/index.json"),
            new("later", "https://later.example/v3/index.json"),
        ]);
        var clients = new List<IPackageSourceClient>();
        await using PackageSourceSettlementLease root = PackageSourceSettlementService.IssueLease(
            authority =>
            {
                string name = authority.Source.Name;
                Assert.NotEqual("later", name);
                IPackageSourceClient client = PackageSourceClientFactory.CreateCustom(
                    PackageSourceDescriptor.NuGetV3(
                        name, name, new Uri($"https://{name}.example/v3/index.json")),
                    authority.Association,
                    factory => new SourceClient(factory)
                    {
                        BeforeStep = name == "slow"
                            ? _ => Task.Delay(TimeSpan.FromMilliseconds(300))
                            : null,
                    });
                clients.Add(client);
                return client;
            });
        using PackageSourceOperationLease operation = root.IssueOperationLease(
            TestContext.Current.CancellationToken,
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100));
        try
        {
            PackageVersionDiscoveryResult result =
                await operation.DiscoverDependencyCandidateVersionsAsync(
                    Coordinate.PackageId, "1.0.0", new FixedAuthorization(authorization));
            Assert.Equal(PackageVersionDiscoveryState.Failed, result.State);
            Assert.Equal(1, result.CandidateObservationCount);
            Assert.Equal(["", "slow", "later"],
                result.Failures.Select(failure => failure.Authority.ToString()));
            Assert.All(result.Failures, failure =>
                Assert.Equal(PackageSourceTimeoutKind.Operation, failure.Timeout?.Kind));
            Assert.Throws<InvalidOperationException>(() => result.SelectCandidate("1.0.0"));
            Assert.Equal(2, clients.Count);
        }
        finally
        {
            foreach (IPackageSourceClient client in clients)
                client.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedListingWaitersPreserveCallerCancellationAndOperationDeadline(
        bool expire)
    {
        await using var fixture = new GalleryHistoryFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using PackageSourceOperationLease operation = fixture.Root.IssueOperationLease(
            cancellation.Token, TimeSpan.FromSeconds(3),
            expire ? TimeSpan.FromSeconds(1) : null, maximumConcurrentSteps: 2);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.BeforeResponse = (url, token) =>
        {
            if (url != GalleryHistoryHandler.EarlyPage)
                return Task.CompletedTask;
            started.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        Task<PackageVersionDiscoveryResult> first = operation.DiscoverDependencyCandidateVersionsAsync(
            GalleryHistoryHandler.PackageId, "[2.1.0,3.0.0)", fixture.Policy);
        Task<PackageVersionDiscoveryResult>? second = null;
        try
        {
            Task signal = await Task.WhenAny(started.Task, first)
                .WaitAsync(TestContext.Current.CancellationToken);
            Assert.Same(started.Task, signal);
            second = operation.DiscoverDependencyCandidateVersionsAsync(
                GalleryHistoryHandler.PackageId, "2.1.0", fixture.Policy);
            Assert.False(second.IsCompleted);
            if (expire)
            {
                PackageVersionDiscoveryResult[] results = await Task.WhenAll(first, second);
                Assert.All(results, result =>
                {
                    Assert.NotEqual(PackageVersionDiscoveryState.Authoritative, result.State);
                    Assert.Contains(result.Failures,
                        failure => failure.Timeout?.Kind == PackageSourceTimeoutKind.Operation);
                });
            }
            else
            {
                cancellation.Cancel();
                foreach (Task task in new Task[] { first, second })
                {
                    OperationCanceledException failure =
                        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                    Assert.Equal(cancellation.Token, failure.CancellationToken);
                }
            }
            Assert.Equal(1, fixture.Handler.Requested.Count(url => url == GalleryHistoryHandler.EarlyPage));
        }
        finally
        {
            cancellation.Cancel();
            Task settlement = second is null ? first : Task.WhenAll(first, second);
            await settlement.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private sealed class GalleryHistoryFixture : IAsyncDisposable
    {
        internal GalleryHistoryHandler Handler { get; }
        internal PackageSourceAuthorization Authorization { get; } =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        internal FixedAuthorization Policy => new(Authorization);
        internal IPackageSourceClient Client { get; }
        internal PackageSourceSettlementLease Root { get; }

        internal GalleryHistoryFixture(bool unlistedMinimum = false)
        {
            Handler = new(unlistedMinimum);
            Client = NuGetFetch.PackageSourceClientFactory.CreateGallery(
                Authorization.Authorities[0].Association, Handler);
            Root = PackageSourceSettlementService.IssueLease(_ => Client);
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            Client.Dispose();
        }
    }

    private sealed class GalleryHistoryHandler : HttpMessageHandler
    {
        internal const string PackageId = "microsoft.extensions.http";
        internal const string Flat =
            "https://globalcdn.nuget.org/v3-flatcontainer/" + PackageId + "/index.json";
        internal const string Registration =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/" + PackageId + "/index.json";
        internal const string EarlyPage =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/" + PackageId + "/page/2.1.0/5.0.0.json";
        internal const string FuturePage =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/" + PackageId + "/page/9.0.0/11.0.0.json";
        internal List<string> Requested { get; } = [];
        internal Dictionary<string, string> Responses { get; }
        internal Func<string, CancellationToken, Task>? BeforeResponse { get; set; }

        internal GalleryHistoryHandler(bool unlistedMinimum)
        {
            Responses = new()
            {
                [Flat] = """{"versions":["2.1.0","2.1.1","9.0.0","11.0.0-rc.3","11.0.0"]}""",
                [Registration] = $$"""
                    {"items":[
                      {"@id":"{{EarlyPage}}","lower":"2.1.0","upper":"5.0.0"},
                      {"@id":"{{FuturePage}}","lower":"9.0.0","upper":"11.0.0"}
                    ]}
                    """,
                [EarlyPage] = $$$"""
                    {"items":[
                      {"catalogEntry":{"version":"2.1.0","listed":{{{(unlistedMinimum ? "false" : "true")}}}}},
                      {"catalogEntry":{"version":"2.1.1","listed":true}}
                    ]}
                    """,
                [FuturePage] = """
                    {"items":[
                      {"catalogEntry":{"version":"9.0.0","listed":true}},
                      {"catalogEntry":{"version":"11.0.0-rc.3","listed":true}},
                      {"catalogEntry":{"version":"11.0.0","listed":true}}
                    ]}
                    """,
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.OriginalString;
            lock (Requested)
                Requested.Add(url);
            if (BeforeResponse is not null)
                await BeforeResponse(url, cancellationToken);
            return Responses.TryGetValue(url, out string? json)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
