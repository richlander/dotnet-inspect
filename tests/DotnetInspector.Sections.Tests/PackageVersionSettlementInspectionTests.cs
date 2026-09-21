using System.Text.Json;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class PackageVersionSettlementInspectionTests
{
    private const string PackageId = "System.Text.Json";
    private const string Stable = "8.0.5";
    private const string Preview = "9.0.0-preview.7.24405.7";

    [Fact]
    public async Task LatestReturnsDetachedCoordinateFreshnessAndSelectedSourceEvidence()
    {
        var (envelope, requests) = await SettleAsync(
            new(PackageId), [(Stable, true), (Preview, true), ("10.0.0", false)],
            cancellationToken: TestContext.Current.CancellationToken);

        var settled = Assert.IsType<PackageVersionSettlementOutcome.Settled>(envelope.Content);
        Assert.Equal(Stable, settled.Result.Coordinate.Version);
        Assert.Equal("system.text.json", settled.Result.Request.PackageId);
        Assert.Equal(PackageVersionDiscoveryFreshness.RefreshedForRequest, settled.Result.Freshness);
        Assert.Equal(new PackageVersionInfo(Stable, true), Assert.Single(settled.Result.Listings));
        Assert.Equal(Stable, Assert.Single(settled.Result.SourceListings).Version);
        Assert.Equal(1, requests);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
        Assert.Empty(envelope.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewOnlyRequiresExplicitSelectionPolicy(bool includePrerelease)
    {
        var (envelope, _) = await SettleAsync(
            new(PackageId), [(Preview, true)], includePrerelease,
            cancellationToken: TestContext.Current.CancellationToken);

        if (includePrerelease)
        {
            Assert.Equal(Preview,
                Assert.IsType<PackageVersionSettlementOutcome.Settled>(envelope.Content)
                    .Result.Coordinate.Version);
        }
        else
        {
            Assert.Equal(PackageVersionSettlementFailureKind.NoMatch,
                Assert.IsType<PackageVersionSettlementOutcome.NotSettled>(envelope.Content)
                    .Failure.Kind);
        }
    }

    [Fact]
    public async Task ExactPinDoesNotDiscoverOrClaimListingState()
    {
        var (envelope, requests) = await SettleAsync(
            new(PackageId, Preview), [], cancellationToken: TestContext.Current.CancellationToken);
        var settled = Assert.IsType<PackageVersionSettlementOutcome.Settled>(envelope.Content);

        Assert.Equal(Preview, settled.Result.Coordinate.Version);
        Assert.Equal(0, requests);
        Assert.Null(settled.Result.Freshness);
        Assert.Empty(settled.Result.Listings);
        Assert.Empty(settled.Result.SourceListings);
    }

    [Fact]
    public async Task EquivalentHostRequestsSerializeTheSameCompleteEnvelope()
    {
        var (first, _) = await SettleAsync(
            new(PackageId), [(Stable, true)], cancellationToken: TestContext.Current.CancellationToken);
        var (second, _) = await SettleAsync(
            new(PackageId.ToLowerInvariant()), [(Stable, true)],
            cancellationToken: TestContext.Current.CancellationToken);
        string json = Serialize(first);

        Assert.Equal(json, Serialize(second));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "outcome",
            document.RootElement.GetProperty("contentKind").GetString());
        Assert.Equal("settled", document.RootElement.GetProperty("content").GetProperty("kind").GetString());
        Assert.Equal(Stable, document.RootElement.GetProperty("content")
            .GetProperty("result").GetProperty("coordinate").GetProperty("version").GetString());
        Assert.Equal("nonProjectable", document.RootElement.GetProperty("portableProjection").GetProperty("kind").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task RefusalPreservesTypedSourceFailureInTheSerializedOutcome()
    {
        var (envelope, _) = await SettleAsync(
            new(PackageId), [], failure: PackageSourceFailureKind.AuthenticationRequired,
            cancellationToken: TestContext.Current.CancellationToken);
        var notSettled = Assert.IsType<PackageVersionSettlementOutcome.NotSettled>(envelope.Content);
        PackageVersionSettlementAuthorityFailure failure =
            Assert.Single(notSettled.Failure.AuthorityFailures);
        Assert.Equal(PackageAuthorityFailureKind.AuthenticationRequired, failure.Kind);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message.ToString()));

        using JsonDocument document = JsonDocument.Parse(Serialize(envelope));
        Assert.Equal("notSettled", document.RootElement.GetProperty("content").GetProperty("kind").GetString());
        Assert.Equal("AuthenticationRequired", document.RootElement.GetProperty("content")
            .GetProperty("failure").GetProperty("authorityFailures")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task InvalidInputReturnsTypedRejectionBeforeDiscovery()
    {
        var (envelope, requests) = await SettleAsync(
            new("bad/id"), [], cancellationToken: TestContext.Current.CancellationToken);
        var failure = Assert.IsType<PackageVersionSettlementOutcome.NotSettled>(envelope.Content).Failure;

        Assert.Equal(PackageVersionSettlementFailureKind.Rejected, failure.Kind);
        Assert.Equal(PackageAuthorityFailureKind.Input, Assert.Single(failure.AuthorityFailures).Kind);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task AuthoritativeEmptyIsNotFoundRatherThanAnEmptySuccess()
    {
        var (envelope, _) = await SettleAsync(
            new(PackageId), [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PackageVersionSettlementFailureKind.NotFound,
            Assert.IsType<PackageVersionSettlementOutcome.NotSettled>(envelope.Content).Failure.Kind);
    }

    [Fact]
    public async Task ExactSettlementRetainsNeighboringConfigurationDiagnostics()
    {
        await using var composition = new DesktopPackageSourceComposition(TimeSpan.FromSeconds(5));
        using var operation = composition.IssueSettlementOperation(TestContext.Current.CancellationToken);
        var envelope = await PackageVersionSettlementInspection.ExecuteAsync(
            new PackageCoordinate(PackageId, Stable),
            composition.CreateSettlementHouse(PackageId, new NuGetSourceOptions
            {
                Sources = ["https://packages.example/v3/index.json", "ftp://packages.invalid"],
            }),
            operation);

        Assert.IsType<PackageVersionSettlementOutcome.Settled>(envelope.Content);
        InspectionDiagnostic diagnostic = Assert.Single(envelope.Diagnostics);
        Assert.Equal("package-version-settlement.source-failure", diagnostic.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Warning, diagnostic.Severity);
        using JsonDocument document = JsonDocument.Parse(Serialize(envelope));
        Assert.Equal(diagnostic.Summary.ToString(),
            document.RootElement.GetProperty("diagnostics")[0].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAnEnvelope()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SettleAsync(new(PackageId), [(Stable, true)],
                cancellationToken: cancellation.Token));
    }

    private static string Serialize(InspectionEnvelope<PackageVersionSettlementOutcome> envelope) =>
        JsonSerializer.Serialize(
            envelope,
            PackageVersionSettlementJsonContext.Default.InspectionEnvelopePackageVersionSettlementOutcome);

    private static async Task<(InspectionEnvelope<PackageVersionSettlementOutcome> Envelope, int Requests)>
        SettleAsync(
            PackageCoordinate request,
            (string Version, bool Listed)[] versions,
            bool includePrerelease = false,
            PackageSourceFailureKind? failure = null,
            CancellationToken cancellationToken = default)
    {
        var authorization = new UniformPackageSourceAuthorization(
            [new PackageSource("fixture", "https://packages.example/v3/index.json")]);
        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.AuthorizeSourcesFor(PackageId).Authorities);
        int requests = 0;
        using IPackageSourceClient client = PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetV3("fixture", "fixture", authority.HttpEndpoint!),
            authority.Association,
            factory => new VersionSource(factory, versions, failure, () => requests++));
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        using PackageSourceOperationLease operation = root.IssueOperationLease(cancellationToken);
        InspectionEnvelope<PackageVersionSettlementOutcome> envelope =
            await PackageVersionSettlementInspection.ExecuteAsync(
                request, new PackageHouse(authorization), operation, includePrerelease);
        return (envelope, requests);
    }

    private sealed class VersionSource(
        PackageSourceResultFactory factory,
        (string Version, bool Listed)[] versions,
        PackageSourceFailureKind? failure,
        Action onVersions) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => factory.Source;
        public PackageSourceCapabilities Capabilities => PackageSourceCapabilities.VersionEnumeration;

        public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
            string packageId, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            onVersions();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(failure is { } kind
                ? factory.FailedVersions(kind)
                : factory.SucceededVersions(factory.Versions(
                    [.. versions.Select(version => factory.Candidate(
                        PackageSourceCoordinate.Create(packageId, version.Version),
                        PackageDiscoveryContract.CompleteVersionEnumeration,
                        version.Listed ? PackageListingState.Listed : PackageListingState.Unlisted))],
                    hasAuthoritativeListingState: true)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query, int take = 20, bool prerelease = false,
            CancellationToken cancellationToken = default, NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
            string prefix, int take = 100, bool prerelease = false,
            CancellationToken cancellationToken = default, NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();

        public void Dispose() { }
    }
}
