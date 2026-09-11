using System.Reflection;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageHouseExecutionTests
{
    private const string PackageId = "microsoft.extensions.logging";
    private const string Version = "10.0.0";

    [Fact]
    public async Task ExactSettleAuthorizesWithoutPayloadWork()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        int stores = 0;
        PackageHouseRequest request = ExactRequest(
            PackageHouseOperationProfile.Settle);
        PackageHouse house = environment.CreateHouse(
            (_, _) =>
            {
                stores++;
                return new InMemoryPackageStore();
            });

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        PackageHouseSettlement.ResourceFree resourceFree =
            Assert.IsType<PackageHouseSettlement.ResourceFree>(
                settlement);
        Assert.IsType<PackageHouseResult.Settled>(
            resourceFree.Result);
        Assert.NotNull(settlement.Result.Decision!.Candidate);
        Assert.Null(settlement.Result.Evidence.Acquisition);
        Assert.Equal(0, stores);
        Assert.Equal(0, environment.Clients[0].VersionRequests);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ExactAcquireBindsLivePayloadToResourceFreeReceipt()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageHouseRequest request = ExactRequest(
            PackageHouseOperationProfile.Acquire);
        PackageHouse house = environment.CreateHouse(
            (_, _) => new InMemoryPackageStore());

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.IsType<PackageHouseResult.Settled>(
            acquired.Result);
        AcquiredPackageSourcePayload payload = acquired.Payload;
        PackageHouseAcquisitionReceipt acquisition =
            Assert.IsType<PackageHouseAcquisitionReceipt>(
                settlement.Result.Evidence.Acquisition);
        Assert.Same(
            environment.Clients[0].Source,
            acquisition.Source);
        Assert.Same(
            payload.Content.GenerationIdentity,
            acquisition.Generation);
        Assert.Equal(payload.Origin, acquisition.Origin);
        Assert.Equal(payload.ProducerKey, acquisition.Producer.Key);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task AcquireRequiresPayloadAcquisitionPlan()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageHouse house = environment.CreateHouse();
        PackageHouseRequest request = ExactRequest(
            PackageHouseOperationProfile.Acquire);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => house.ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken)));

        Assert.Contains(
            "package store capability",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task SelectingAcquireUsesOnlyAuthoritiesThatReportedSelection()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["9.0.0", Version]),
            new SourceBehavior(["9.0.0"]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire));
        PackageHouse house = environment.CreateHouse(
            (_, _) => new InMemoryPackageStore());

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.IsType<PackageHouseResult.Settled>(
            settlement.Result);
        Assert.Equal(
            Version,
            settlement.Result.Decision!.Coordinate!.Version);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        Assert.Equal(0, environment.Clients[1].PayloadRequests);
        PackageVersionResolutionReceipt.Resolved resolution =
            Assert.IsType<PackageVersionResolutionReceipt.Resolved>(
                settlement.Result.Decision.VersionResolution);
        Assert.Single(resolution.Candidate.Authorities);
    }

    [Fact]
    public async Task PartialDiscoveryDoesNotReachPayloadOrStore()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]),
            new SourceBehavior(
                [],
                VersionFailure:
                    PackageSourceFailureKind.Transport));
        int stores = 0;
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire));
        PackageHouse house = environment.CreateHouse(
            (_, _) =>
            {
                stores++;
                return new InMemoryPackageStore();
            });

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.IsType<PackageHouseResult.Incomplete>(
            settlement.Result);
        Assert.IsType<PackageHouseSettlement.ResourceFree>(
            settlement);
        Assert.Equal(0, stores);
        Assert.All(
            environment.Clients,
            client => Assert.Equal(0, client.PayloadRequests));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task SelectionKeepsNotFoundAndNoMatchDistinct()
    {
        await using HouseEnvironment absent = HouseEnvironment.Create(
            new SourceBehavior([]));
        await using HouseEnvironment prerelease = HouseEnvironment.Create(
            new SourceBehavior(["11.0.0-preview.1"]));
        PackageHouseRequest CreateRequest() => new(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));

        PackageHouseRequest absentRequest = CreateRequest();
        PackageHouseSettlement notFound =
            await absent.CreateHouse().ExecuteAsync(
                absentRequest,
                absent.IssueOperation(
                    absentRequest,
                    TestContext.Current.CancellationToken));
        PackageHouseRequest prereleaseRequest = CreateRequest();
        PackageHouseSettlement noMatch =
            await prerelease.CreateHouse().ExecuteAsync(
                prereleaseRequest,
                prerelease.IssueOperation(
                    prereleaseRequest,
                    TestContext.Current.CancellationToken));

        Assert.IsType<PackageHouseResult.NotFound>(
            notFound.Result);
        Assert.IsType<PackageHouseResult.NoMatch>(
            noMatch.Result);
    }

    [Fact]
    public async Task OperationDeadlinesMustMatchRequest()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageHouseRequest request = ExactRequest(
            PackageHouseOperationProfile.Settle);
        PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                request.Operation.RequestTimeout
                    + TimeSpan.FromSeconds(1),
                request.Operation.OperationTimeout);

        await Assert.ThrowsAsync<ArgumentException>(
            () => environment.CreateHouse().ExecuteAsync(
                request,
                operation));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task NullRequestReleasesTransferredOperation()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => environment.CreateHouse().ExecuteAsync(
                null!,
                operation));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CallerCancellationRemainsCallerCancellation()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                BeforeVersions: async (_, token) =>
                {
                    cancellation.Cancel();
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        token);
                }));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => environment.CreateHouse().ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        cancellation.Token)));

        Assert.Equal(
            cancellation.Token,
            exception.CancellationToken);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task OperationTimeoutBecomesTypedTerminalFailure()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                BeforeVersions: async (_, token) =>
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(60),
                        token)));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout:
                    TimeSpan.FromMilliseconds(20)));

        PackageHouseSettlement settlement =
            await environment.CreateHouse().ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.IsType<PackageHouseResult.Failed>(
            settlement.Result);
        Assert.Null(settlement.Result.Decision);
        Assert.Contains(
            settlement.Result.Evidence.Failures,
            failure =>
                failure
                    is PackageHouseFailure.Authority
                    {
                        Failure.Timeout.Kind:
                            PackageSourceTimeoutKind.Operation,
                    } authority
                && authority.Failure.Timeout.Duration
                    == request.Operation.OperationTimeout);
        Assert.IsType<PackageHouseFailure.Timeout>(
            settlement.Result.Evidence.Failures.Last());
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task SelectingMayTimeOutBeforeDiscoveryReceiptExists()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout:
                    TimeSpan.FromMilliseconds(20)));
        PackageSourceOperationLease operation =
            environment.IssueOperation(
                request,
                TestContext.Current.CancellationToken);
        await Task.Delay(
            TimeSpan.FromMilliseconds(60),
            TestContext.Current.CancellationToken);

        PackageHouseSettlement settlement =
            await environment.CreateHouse().ExecuteAsync(
                request,
                operation);

        Assert.IsType<PackageHouseResult.Failed>(
            settlement.Result);
        Assert.Null(settlement.Result.Decision);
        Assert.IsType<PackageHouseFailure.Timeout>(
            Assert.Single(
                settlement.Result.Evidence.Failures));
        Assert.Equal(0, environment.Clients[0].VersionRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task OperationTimeoutMayPreserveCompletedSelectionReceipt()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));
        PackageHouseSettlement settlement =
            await environment.CreateHouse().ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));
        PackageHouseDecisionReceipt decision =
            Assert.IsType<PackageHouseDecisionReceipt>(
                settlement.Result.Decision);
        var timeout = new PackageHouseFailure.Timeout(
            request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            request.Operation.OperationTimeout);
        var evidence = new PackageHouseEvidence(
            request,
            decision,
            failures: [timeout]);

        var failed = new PackageHouseResult.Failed(
            evidence,
            new(
                InertText.TextPolicy.Field,
                "The PackageHouse operation deadline expired."));

        Assert.IsType<PackageVersionResolutionReceipt.NotFound>(
            failed.Decision!.VersionResolution);
        Assert.Same(
            timeout,
            Assert.Single(failed.Evidence.Failures));
    }

    [Fact]
    public async Task UnsupportedRealizeReleasesTransferredOperation()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => environment.CreateHouse().ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken)));

        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ThrownSourceExceptionReleasesTransferredOperation()
    {
        var expected =
            new InvalidDataException("Source failed after invocation.");
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                BeforeVersions: (_, _) =>
                    Task.FromException(expected)));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));

        Assert.Same(
            expected,
            await Assert.ThrowsAsync<InvalidDataException>(
                () => environment.CreateHouse().ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken))));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public void ResultsAndReceiptsRetainNoSourceLeaseAuthority()
    {
        Type[] resourceTypes =
        [
            typeof(PackageSourceOperationLease),
            typeof(PackageSourceSettlementLease),
        ];
        Type[] resultAndReceiptTypes =
        [
            typeof(PackageHouseSettlement),
            .. typeof(PackageHouseSettlement)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageHouseResult),
            .. typeof(PackageHouseResult)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageHouseDecisionReceipt),
            typeof(PackageHouseAcquisitionReceipt),
            typeof(PackageHouseEvidence),
            typeof(PackageHouseFailure),
            .. typeof(PackageHouseFailure)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageAcquisitionCandidate),
            typeof(PackageAcquisitionCandidateCorrespondence),
            typeof(PackageVersionResolutionReceipt),
            .. typeof(PackageVersionResolutionReceipt)
                .GetNestedTypes(BindingFlags.Public),
            typeof(AcquiredPackageSourcePayload),
        ];

        Assert.All(
            resultAndReceiptTypes,
            type => Assert.DoesNotContain(
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field => resourceTypes.Any(
                    resource =>
                        resource.IsAssignableFrom(
                            field.FieldType))));
    }

    private static PackageHouseRequest ExactRequest(
        PackageHouseOperationProfile profile) =>
        new(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(profile));

    private sealed record SourceBehavior(
        IReadOnlyList<string> Versions,
        PackageSourceFailureKind? VersionFailure = null,
        bool PayloadNotFound = false,
        Func<
            NuGetOperationContext?,
            CancellationToken,
            Task>? BeforeVersions = null);

    private sealed class HouseEnvironment : IAsyncDisposable
    {
        private HouseEnvironment(
            FixedAuthorization authorization,
            PackageSourceSettlementLease root,
            IReadOnlyList<HouseSourceClient> clients,
            IReadOnlyList<IPackageSourceClient> ownedClients)
        {
            Authorization = authorization;
            Root = root;
            Clients = clients;
            OwnedClients = ownedClients;
        }

        public FixedAuthorization Authorization { get; }

        public PackageSourceSettlementLease Root { get; }

        public IReadOnlyList<HouseSourceClient> Clients { get; }

        private IReadOnlyList<IPackageSourceClient> OwnedClients { get; }

        public static HouseEnvironment Create(
            params SourceBehavior[] behaviors)
        {
            PackageSource[] sources =
            [
                .. behaviors.Select((_, index) =>
                    new PackageSource(
                        $"source-{index + 1}",
                        $"https://source-{index + 1}.example/v3/index.json")),
            ];
            PackageSourceAuthorization authorization =
                PackageSourceAuthorization.Authorize(sources);
            var clientsByAssociation =
                new Dictionary<
                    PackageSourceAssociation,
                    IPackageSourceClient>(
                    ReferenceEqualityComparer.Instance);
            var clients = new List<HouseSourceClient>();
            for (int index = 0;
                 index < authorization.Authorities.Count;
                 index++)
            {
                ConfiguredPackageAuthority authority =
                    authorization.Authorities[index];
                HouseSourceClient? client = null;
                IPackageSourceClient owned =
                    PackageSourceClientFactory.CreateCustom(
                        PackageSourceDescriptor.NuGetV3(
                            $"source-{index + 1}",
                            $"Source {index + 1}",
                            authority.HttpEndpoint!),
                        authority.Association,
                        factory =>
                        {
                            client = new HouseSourceClient(
                                factory,
                                behaviors[index]);
                            return client;
                        });
                clientsByAssociation.Add(
                    authority.Association,
                    owned);
                clients.Add(client!);
            }

            PackageSourceSettlementLease lease =
                PackageSourceSettlementService.IssueLease(
                    authority =>
                        clientsByAssociation.TryGetValue(
                            authority.Association,
                            out IPackageSourceClient? client)
                            ? client
                            : throw new InvalidOperationException(
                                "Unknown source association."));
            return new(
                new FixedAuthorization(authorization),
                lease,
                clients,
                [.. clientsByAssociation.Values]);
        }

        public PackageHouse CreateHouse(
            PackageStoreProvider? getStore = null) =>
            new(
                Authorization,
                getStore is null
                    ? null
                    : new PackagePayloadAcquisitionPlan(getStore));

        public PackageSourceOperationLease IssueOperation(
            PackageHouseRequest request,
            CancellationToken cancellationToken) =>
            Root.IssueOperationLease(
                cancellationToken,
                request.Operation.RequestTimeout,
                request.Operation.OperationTimeout);

        public async Task AssertRootSettledAsync()
        {
            ValueTask settlement = Root.DisposeAsync();
            Assert.True(settlement.IsCompletedSuccessfully);
            await settlement;
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            foreach (IPackageSourceClient client in OwnedClients)
            {
                client.Dispose();
            }
        }
    }

    private sealed class FixedAuthorization(
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            Assert.Equal(
                PackageId,
                packageId,
                ignoreCase: false);
            return authorization;
        }
    }

    private sealed class HouseSourceClient(
        PackageSourceResultFactory factory,
        SourceBehavior behavior) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => factory.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.PackagePayload;

        public int VersionRequests { get; private set; }

        public int PayloadRequests { get; private set; }

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            return GetVersionsCoreAsync(
                packageId,
                operationContext,
                cancellationToken);
        }

        private async Task<
            PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsCoreAsync(
            string packageId,
            NuGetOperationContext? operationContext,
            CancellationToken cancellationToken)
        {
            VersionRequests++;
            if (behavior.BeforeVersions is not null)
            {
                await behavior.BeforeVersions(
                    operationContext,
                    cancellationToken);
            }
            if (behavior.VersionFailure is { } failure)
            {
                return factory.FailedVersions(failure);
            }

            PackageCandidateObservation[] candidates =
            [
                .. behavior.Versions.Select(version =>
                    factory.Candidate(
                        PackageSourceCoordinate.Create(
                            packageId,
                            version),
                        PackageDiscoveryContract
                            .CompleteVersionEnumeration,
                        PackageListingState.Listed)),
            ];
            return factory.SucceededVersions(
                factory.Versions(
                    candidates,
                    hasAuthoritativeListingState: true));
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            PayloadRequests++;
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            if (behavior.PayloadNotFound
                || !behavior.Versions.Contains(
                    version,
                    StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    factory.FailedPackage(
                        coordinate,
                        PackageSourceFailureKind.NotFound));
            }

            byte[] archive = TestPackageArchive.Create(
                $"lib/net10.0/{PackageId}.dll");
            PackageSourcePayload payload = factory.Payload(
                coordinate,
                PackageSourcePayloadKind.Package,
                new MemoryStream(
                    archive,
                    writable: false),
                archive.LongLength);
            return Task.FromResult(
                factory.SucceededPackage(
                    coordinate,
                    payload));
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
            TryGetSymbolsAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
