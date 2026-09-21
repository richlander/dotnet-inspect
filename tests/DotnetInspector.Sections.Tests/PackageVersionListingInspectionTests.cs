using System.Text.Json;
using DotnetInspector.Packages;
using QuerySpace.Rows;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class PackageVersionListingInspectionTests
{
    private const string PackageId = "System.Text.Json";

    [Fact]
    public async Task ListingPreservesRequestRowsAndSources()
    {
        InspectionEnvelope<PackageVersionListingOutcome> envelope =
            await ListAsync(
                [
                    new(
                        [
                            ("1.0.0", true),
                            ("1.1.0", false),
                            ("2.0.0-preview.1", true),
                        ]),
                ],
                includeUnlisted: true,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var listed =
            Assert.IsType<PackageVersionListingOutcome.Listed>(
                envelope.Content);
        Assert.Equal(
            PackageVersionListingCompleteness.Authoritative,
            listed.Document.Completeness);
        Assert.Equal(
            ["1.1.0", "1.0.0"],
            listed.Document.Versions.Select(row => row.Version));
        Assert.False(listed.Document.Versions[0].Listed);
        Assert.Equal(
            ["1.1.0", "1.0.0"],
            listed.Document.SourceListings.Select(row => row.Version));
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);

        using JsonDocument json = JsonDocument.Parse(Serialize(envelope));
        Assert.Equal(
            "available",
            json.RootElement.GetProperty("content")
                .GetProperty("kind").GetString());
        Assert.Equal(
            "Authoritative",
            json.RootElement.GetProperty("content")
                .GetProperty("document")
                .GetProperty("completeness").GetString());
    }

    [Fact]
    public async Task CountProjectsScalarEnvelopeFromListingDocument()
    {
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Tail(1)]);
        InspectionEnvelope<PackageVersionListingOutcome> envelope =
            await ListAsync(
                [
                    new(
                        [
                            ("1.0.0", true),
                            ("1.1.0", true),
                            ("2.0.0", true),
                        ]),
                ],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var listed =
            Assert.IsType<PackageVersionListingOutcome.Listed>(
                envelope.Content);
        Assert.Equal(3, listed.Document.Versions.Length);
        var completed =
            Assert.IsType<PackageVersionPopulationCountOutcome.Completed>(
                PackageVersionListingInspection.Count(
                    listed.Document,
                    new(
                        PackageVersionPopulationCountCohort.Versions,
                        selection)));
        InspectionEnvelope<int> countEnvelope =
            PackageVersionListingInspection.ProjectCountEnvelope(
                envelope,
                completed);
        Assert.Equal(
            1,
            countEnvelope.Content);
        Assert.Equal(
            "package-version-count/share",
            Assert.IsType<InspectionShare.NonProjectable>(
                countEnvelope.Share).Path);
        Assert.Equal(envelope.Diagnostics, countEnvelope.Diagnostics);
    }

    [Fact]
    public async Task PartialListingRetainsRowsAndDisclosesSourceFailure()
    {
        InspectionEnvelope<PackageVersionListingOutcome> envelope =
            await ListAsync(
                [
                    new([("1.0.0", true), ("2.0.0", true)]),
                    new(
                        [],
                        PackageSourceFailureKind.AuthenticationRequired),
                ],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var listed =
            Assert.IsType<PackageVersionListingOutcome.Listed>(
                envelope.Content);
        Assert.Equal(
            PackageVersionListingCompleteness.Partial,
            listed.Document.Completeness);
        Assert.Equal(
            ["2.0.0", "1.0.0"],
            listed.Document.Versions.Select(row => row.Version));
        InspectionDiagnostic diagnostic =
            Assert.Single(envelope.Diagnostics);
        Assert.Equal(
            PackageAuthorityFailureKind.AuthenticationRequired,
            Assert.Single(listed.AuthorityFailures).Kind);
        Assert.Equal(
            "package-version-listing.source-failure",
            diagnostic.Code);
        Assert.Contains(
            "credentials",
            diagnostic.Summary.ToString(),
            StringComparison.OrdinalIgnoreCase);
        using JsonDocument json = JsonDocument.Parse(Serialize(envelope));
        Assert.False(
            json.RootElement.GetProperty("content")
                .TryGetProperty("authorityFailures", out _));
    }

    [Theory]
    [InlineData(null, PackageVersionListingFailureKind.NotFound)]
    [InlineData(
        PackageSourceFailureKind.Transport,
        PackageVersionListingFailureKind.Failed)]
    public async Task NonAvailableListingRetainsTypedFailure(
        PackageSourceFailureKind? sourceFailure,
        PackageVersionListingFailureKind expected)
    {
        InspectionEnvelope<PackageVersionListingOutcome> envelope =
            await ListAsync(
                [new([], sourceFailure)],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        PackageVersionListingFailure failure =
            Assert.IsType<PackageVersionListingOutcome.NotAvailable>(
                envelope.Content).Failure;
        Assert.Equal(expected, failure.Kind);
        if (sourceFailure is not null)
        {
            Assert.Equal(
                PackageAuthorityFailureKind.Transport,
                Assert.Single(failure.AuthorityFailures).Kind);
        }
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAnEnvelope()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ListAsync(
                [new([("1.0.0", true)])],
                cancellationToken: cancellation.Token));
    }

    private static string Serialize(
        InspectionEnvelope<PackageVersionListingOutcome> envelope) =>
        JsonSerializer.Serialize(
            envelope,
            PackageVersionListingJsonContext.Default
                .InspectionEnvelopePackageVersionListingOutcome);

    private static async Task<
        InspectionEnvelope<PackageVersionListingOutcome>> ListAsync(
        VersionSourceBehavior[] behaviors,
        bool includePrerelease = false,
        bool includeUnlisted = false,
        CancellationToken cancellationToken = default)
    {
        PackageSource[] sources =
        [
            .. behaviors.Select((_, index) =>
                new PackageSource(
                    $"fixture-{index + 1}",
                    $"https://packages-{index + 1}.example/v3/index.json")),
        ];
        var authorization =
            new UniformPackageSourceAuthorization(sources);
        PackageSourceAuthorization authorized =
            authorization.AuthorizeSourcesFor(PackageId);
        var clients =
            new Dictionary<
                PackageSourceAssociation,
                IPackageSourceClient>(
                ReferenceEqualityComparer.Instance);
        foreach ((ConfiguredPackageAuthority authority, int index)
                 in authorized.Authorities.Select(
                     (authority, index) => (authority, index)))
        {
            IPackageSourceClient client =
                PackageSourceClientFactory.CreateCustom(
                    PackageSourceDescriptor.NuGetV3(
                        $"fixture-{index + 1}",
                        $"Fixture {index + 1}",
                        authority.HttpEndpoint!),
                    authority.Association,
                    factory => new VersionSource(
                        factory,
                        behaviors[index]));
            clients.Add(authority.Association, client);
        }

        try
        {
            await using PackageSourceSettlementLease root =
                PackageSourceSettlementService.IssueLease(
                    authority => clients[authority.Association]);
            using PackageSourceOperationLease operation =
                root.IssueOperationLease(cancellationToken);
            return await PackageVersionListingInspection.ExecuteAsync(
                PackageId,
                new PackageHouse(authorization),
                operation,
                includePrerelease,
                includeUnlisted);
        }
        finally
        {
            foreach (IPackageSourceClient client in clients.Values)
                client.Dispose();
        }
    }

    private sealed record VersionSourceBehavior(
        (string Version, bool Listed)[] Versions,
        PackageSourceFailureKind? Failure = null);

    private sealed class VersionSource(
        PackageSourceResultFactory factory,
        VersionSourceBehavior behavior) : IPackageSourceClient
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
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(behavior.Failure is { } failure
                ? factory.FailedVersions(failure)
                : factory.SucceededVersions(
                    factory.Versions(
                        [
                            .. behavior.Versions.Select(version =>
                                factory.Candidate(
                                    PackageSourceCoordinate.Create(
                                        packageId,
                                        version.Version),
                                    PackageDiscoveryContract
                                        .CompleteVersionEnumeration,
                                    version.Listed
                                        ? PackageListingState.Listed
                                        : PackageListingState.Unlisted)),
                        ],
                        hasAuthoritativeListingState: true)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
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
