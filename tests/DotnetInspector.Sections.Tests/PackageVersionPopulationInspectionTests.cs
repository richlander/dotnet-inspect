using System.Text.Json;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class PackageVersionPopulationInspectionTests
{
    private const string PackageId = "System.Text.Json";

    [Fact]
    public async Task PopulationPreservesDirectionAddressesAndSources()
    {
        var (envelope, requests) = await PopulateAsync(
            $"{PackageId}@2.0.0..1.0.0",
            [("1.0.0", true), ("1.1.0", true), ("2.0.0", true)],
            cancellationToken: TestContext.Current.CancellationToken);

        var populated =
            Assert.IsType<PackageVersionPopulationOutcome.Populated>(
                envelope.Content);
        Assert.Equal(
            ["2.0.0", "1.1.0", "1.0.0"],
            populated.Document.Versions.Select(version => version.Version));
        Assert.Equal(
            ["#1", "#2", "#3"],
            populated.Document.Versions.Select(version => version.Selector));
        Assert.Equal(
            ["2.0.0", "1.1.0", "1.0.0"],
            populated.Document.SourceListings.Select(row => row.Version));
        Assert.Equal(1, requests);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
        Assert.Empty(envelope.Diagnostics);

        using JsonDocument json = JsonDocument.Parse(Serialize(envelope));
        Assert.Equal(
            "available",
            json.RootElement.GetProperty("content").GetProperty("kind").GetString());
        Assert.False(
            json.RootElement.GetProperty("content")
                .TryGetProperty("count", out _));
    }

    [Fact]
    public async Task CountAppliesTheBoundSemanticRowSelectionWithoutClippingTheDocument()
    {
        RowSelectionIntent<string> selection = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Tail(2)]);
        var (envelope, _) = await PopulateAsync(
            $"{PackageId}@1.0.0..2.0.0",
            [("1.0.0", true), ("1.1.0", true), ("2.0.0", true)],
            cancellationToken: TestContext.Current.CancellationToken);

        var populated =
            Assert.IsType<PackageVersionPopulationOutcome.Populated>(
                envelope.Content);
        Assert.Equal(3, populated.Document.Versions.Length);
        PackageVersionPopulationCountOutcome count =
            PackageVersionPopulationInspection.Count(
                populated.Document,
                new(
                    PackageVersionPopulationCountCohort.Versions,
                    selection));
        var completed =
            Assert.IsType<PackageVersionPopulationCountOutcome.Completed>(
                count);
        Assert.Equal(2, completed.Result.Value);

        InspectionEnvelope<int> countEnvelope =
            PackageVersionPopulationInspection.ProjectCountEnvelope(
                envelope,
                completed);
        Assert.Equal(2, countEnvelope.Content);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(countEnvelope.PortableProjection);
        Assert.Empty(countEnvelope.Diagnostics);
    }

    [Fact]
    public async Task PopulationDocumentHasNoCountContent()
    {
        var (envelope, _) = await PopulateAsync(
            $"{PackageId}@1.0.0..1.1.0",
            [("1.0.0", true), ("1.1.0", true)],
            cancellationToken: TestContext.Current.CancellationToken);

        var populated =
            Assert.IsType<PackageVersionPopulationOutcome.Populated>(
                envelope.Content);
        Assert.Equal(2, populated.Document.Versions.Length);
        using JsonDocument json = JsonDocument.Parse(Serialize(envelope));
        Assert.False(
            json.RootElement.GetProperty("content")
                .TryGetProperty("count", out _));
    }

    [Fact]
    public async Task MissingEndpointReturnsTypedNonSuccessWithoutAnEmptyDocument()
    {
        var (envelope, _) = await PopulateAsync(
            $"{PackageId}@1.0.0..2.0.0",
            [("1.0.0", true)],
            cancellationToken: TestContext.Current.CancellationToken);

        PackageVersionPopulationFailure failure =
            Assert.IsType<PackageVersionPopulationOutcome.NotAvailable>(
                envelope.Content).Failure;
        Assert.Equal(PackageVersionPopulationFailureKind.NoMatch, failure.Kind);
        Assert.Contains(
            "does not contain range endpoint 2.0.0",
            failure.Reason.ToString());
    }

    [Fact]
    public async Task RefusalPreservesTypedCredentialSafeAuthorityFailure()
    {
        var (envelope, _) = await PopulateAsync(
            $"{PackageId}@1.0.0..2.0.0",
            [],
            failure: PackageSourceFailureKind.AuthenticationRequired,
            cancellationToken: TestContext.Current.CancellationToken);

        PackageVersionPopulationFailure failure =
            Assert.IsType<PackageVersionPopulationOutcome.NotAvailable>(
                envelope.Content).Failure;
        PackageVersionPopulationAuthorityFailure authorityFailure =
            Assert.Single(failure.AuthorityFailures);
        Assert.Equal(
            PackageAuthorityFailureKind.AuthenticationRequired,
            authorityFailure.Kind);
        Assert.False(string.IsNullOrWhiteSpace(
            authorityFailure.Message.ToString()));
    }

    private static string Serialize(
        InspectionEnvelope<PackageVersionPopulationOutcome> envelope) =>
        JsonSerializer.Serialize(
            envelope,
            PackageVersionPopulationJsonContext.Default
                .InspectionEnvelopePackageVersionPopulationOutcome);

    private static async Task<(
        InspectionEnvelope<PackageVersionPopulationOutcome> Envelope,
        int Requests)> PopulateAsync(
        string packageReference,
        (string Version, bool Listed)[] versions,
        PackageSourceFailureKind? failure = null,
        CancellationToken cancellationToken = default)
    {
        Assert.True(PackageVersionRange.TryParse(
            packageReference,
            out PackageVersionRange? range,
            out string? error),
            error);
        var authorization = new UniformPackageSourceAuthorization(
            [new PackageSource("fixture", "https://packages.example/v3/index.json")]);
        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.AuthorizeSourcesFor(PackageId).Authorities);
        int requests = 0;
        using IPackageSourceClient client = PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetV3(
                "fixture",
                "fixture",
                authority.HttpEndpoint!),
            authority.Association,
            factory => new VersionSource(
                factory,
                versions,
                failure,
                () => requests++));
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        using PackageSourceOperationLease operation =
            root.IssueOperationLease(cancellationToken);
        InspectionEnvelope<PackageVersionPopulationOutcome> envelope =
            await PackageVersionPopulationInspection.ExecuteAsync(
                range!,
                new PackageHouse(authorization),
                operation);
        return (envelope, requests);
    }

    private sealed class VersionSource(
        PackageSourceResultFactory factory,
        (string Version, bool Listed)[] versions,
        PackageSourceFailureKind? failure,
        Action onVersions) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => factory.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.VersionEnumeration;

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            onVersions();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(failure is { } kind
                ? factory.FailedVersions(kind)
                : factory.SucceededVersions(factory.Versions(
                    [.. versions.Select(version => factory.Candidate(
                        PackageSourceCoordinate.Create(
                            packageId,
                            version.Version),
                        PackageDiscoveryContract.CompleteVersionEnumeration,
                        version.Listed
                            ? PackageListingState.Listed
                            : PackageListingState.Unlisted))],
                    hasAuthoritativeListingState: true)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose() { }
    }
}
