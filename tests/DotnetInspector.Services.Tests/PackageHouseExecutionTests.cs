using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    private const string PackageId = "microsoft.extensions.logging";
    private const string Version = "10.0.0";
    private const string PrunablePackageId = "system.text.json";

    [Fact]
    public async Task CompleteVersionDiscoveryIssuesReporterBoundCandidate()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["9.0.0", Version]),
            new SourceBehavior(["9.0.0"]));
        using PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageVersionDiscoveryResult discovery =
            await operation.DiscoverVersionsAsync(
                PackageId,
                environment.Authorization,
                PackageVersionDiscoveryContract.CompleteVersionEnumeration);

        Assert.Equal(
            PackageVersionDiscoveryState.Authoritative,
            discovery.State);
        Assert.Same(
            PackageVersionDiscoveryContract.CompleteVersionEnumeration,
            discovery.Contract);
        Assert.True(
            discovery.Contract.SupportsCompleteVersionEnumeration);
        Assert.Equal([Version, "9.0.0"], discovery.Versions);
        PackageAcquisitionCandidate candidate =
            discovery.SelectCandidate(Version);
        Assert.Equal(
            PackageAcquisitionCandidateKind.Discovered,
            candidate.Kind);
        PackageAcquisitionAuthorityEvidence reporter =
            Assert.Single(candidate.Authorities);
        Assert.Same(
            environment.Clients[0].Source,
            reporter.Observation!.Source);
        Assert.All(
            environment.Clients,
            client => Assert.Equal(1, client.VersionRequests));
    }

    [Fact]
    public async Task CompleteVersionDiscoveryPreservesAuthoritativeEmpty()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([]));
        using PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        PackageVersionDiscoveryResult discovery =
            await operation.DiscoverVersionsAsync(
                PackageId,
                environment.Authorization,
                PackageVersionDiscoveryContract.CompleteVersionEnumeration);

        Assert.Equal(
            PackageVersionDiscoveryState.Authoritative,
            discovery.State);
        Assert.Empty(discovery.Versions);
        Assert.Empty(discovery.Listings);
        Assert.Empty(discovery.Failures);
        Assert.False(discovery.HasAnyCandidate);
        Assert.Throws<ArgumentException>(
            () => discovery.SelectCandidate(Version));
    }

    [Fact]
    public async Task VersionListingPreservesPartialRowsWithoutPayload()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]),
            new SourceBehavior(
                [],
                VersionFailure:
                    PackageSourceFailureKind.AuthenticationRequired));
        PackageHouseVersionListingRequest request =
            ListingRequest();

        var available = Assert.IsType<
            PackageHouseVersionListingResult.Available>(
                await environment.CreateHouse()
                    .SettleVersionListingAsync(
                        request,
                        environment.Root.IssueOperationLease(
                            TestContext.Current.CancellationToken,
                            request.Operation.RequestTimeout,
                            request.Operation.OperationTimeout)));

        Assert.False(available.IsAuthoritative);
        Assert.Equal(
            ["2.0.0", "1.0.0"],
            available.Evidence.Discovery!.Versions);
        PackageHouseFailure.Authority failure =
            Assert.IsType<PackageHouseFailure.Authority>(
                Assert.Single(available.Evidence.Failures));
        Assert.Equal(
            PackageAuthorityFailureKind.AuthenticationRequired,
            failure.Failure.Kind);
        Assert.All(
            environment.Clients,
            client => Assert.Equal(0, client.PayloadRequests));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionListingAuthoritativeAbsenceIsNotFound()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([]));
        PackageHouseVersionListingRequest request =
            ListingRequest();

        PackageHouseVersionListingResult result =
            await environment.CreateHouse()
                .SettleVersionListingAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        Assert.IsType<PackageHouseVersionListingResult.NotFound>(
            result);
        Assert.Equal(1, environment.Clients[0].VersionRequests);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionListingFailedDiscoveryPublishesNoAvailableRows()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [],
                VersionFailure: PackageSourceFailureKind.Transport));
        PackageHouseVersionListingRequest request =
            ListingRequest();

        PackageHouseVersionListingResult result =
            await environment.CreateHouse()
                .SettleVersionListingAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        Assert.IsType<PackageHouseVersionListingResult.Failed>(
            result);
        Assert.IsNotType<PackageHouseVersionListingResult.Available>(
            result);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionListingOperationTimeoutIsTypedAndReleasesOperation()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                ["1.0.0", "2.0.0"],
                BeforeVersions: async (_, token) =>
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(60),
                        token)));
        PackageHouseVersionListingRequest request =
            ListingRequest(
                operationTimeout:
                    TimeSpan.FromMilliseconds(20));

        PackageHouseVersionListingResult result =
            await environment.CreateHouse()
                .SettleVersionListingAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        Assert.IsType<PackageHouseVersionListingResult.Failed>(
            result);
        Assert.Contains(
            result.Evidence.Failures,
            failure =>
                failure is PackageHouseFailure.Timeout
                {
                    Kind: PackageHouseTimeoutKind.Operation,
                });
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationSettlementServesMultipleCellsFromOneDiscovery()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]),
            new SourceBehavior(["2.0.0", "3.0.0"]));
        int stores = 0;
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..3.0.0");
        PackageHouse house = environment.CreateHouse(
            (_, _) =>
            {
                stores++;
                return new InMemoryPackageStore();
            });

        PackageHouseVersionPopulationResult result =
            await house.SettleVersionPopulationAsync(
                request,
                environment.Root.IssueOperationLease(
                    TestContext.Current.CancellationToken,
                    request.Operation.RequestTimeout,
                    request.Operation.OperationTimeout));

        var available =
            Assert.IsType<PackageHouseVersionPopulationResult.Available>(
                result);
        Assert.Equal(
            ["1.0.0", "2.0.0", "3.0.0"],
            available.Vector.Addresses.Select(
                address => address.Version.ToNormalizedString()));
        Assert.All(
            environment.Clients,
            client =>
            {
                Assert.Equal(1, client.VersionRequests);
                Assert.Equal(0, client.PayloadRequests);
            });

        PackageHouseVersionPopulationCell[] cells =
        [
            .. available.Vector.Addresses.Select(
                available.SelectCell),
        ];
        Assert.Single(cells[0].Candidate.Authorities);
        Assert.Equal(2, cells[1].Candidate.Authorities.Count);
        Assert.Single(cells[2].Candidate.Authorities);

        foreach (PackageHouseVersionPopulationCell cell in cells)
        {
            PackageHouseOperation operation = PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle);
            PackageHouseSettlement settlement =
                await house.ExecuteAsync(
                    cell.CreateRequest(
                        operation,
                        targetContext: null,
                        assetSelection: null,
                        libraryHandoff:
                            PackageHouseLibraryHandoffMode.PackageOnly),
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        operation.RequestTimeout,
                        operation.OperationTimeout));
            Assert.IsType<PackageHouseResult.Settled>(
                settlement.Result);
        }

        Assert.All(
            environment.Clients,
            client =>
            {
                Assert.Equal(1, client.VersionRequests);
                Assert.Equal(0, client.PayloadRequests);
            });
        Assert.Equal(0, stores);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MajorBoundPopulationProjectsOriginalAddressesWithoutPayloadAcquisition()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
            [
                "8.0.0",
                "8.1.0",
                "9.0.0",
                "10.0.0",
                "11.0.0-rc.1",
            ]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest(
                "8.0.0..11.0.0",
                includePrerelease: true,
                policy: PackageVersionPopulationPolicy.MajorBounds);

        var available = Assert.IsType<
            PackageHouseVersionPopulationResult.Available>(
                await environment.CreateHouse()
                    .SettleVersionPopulationAsync(
                        request,
                        environment.Root.IssueOperationLease(
                            TestContext.Current.CancellationToken,
                            request.Operation.RequestTimeout,
                            request.Operation.OperationTimeout)));

        PackageVersionMajorRepresentativeProjection projection =
            available.ProjectMajorRepresentatives(
                PackageVersionMajorRepresentativePolicy.Latest);
        Assert.Equal(
            ["8.1.0", "9.0.0", "10.0.0", "11.0.0-rc.1"],
            projection.Addresses.Select(address => address.NormalizedVersion));
        Assert.All(
            projection.Addresses,
            address => Assert.Contains(
                available.Vector.Addresses,
                candidate => ReferenceEquals(candidate, address)));
        Assert.All(
            environment.Clients,
            client =>
            {
                Assert.Equal(1, client.VersionRequests);
                Assert.Equal(0, client.PayloadRequests);
            });
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MajorBoundPopulationReportsMissingBoundaryMajorAsNoMatch()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["8.0.0", "9.0.0", "10.0.0"]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest(
                "8.0.0..11.0.0",
                includePrerelease: true,
                policy: PackageVersionPopulationPolicy.MajorBounds);

        var noMatch = Assert.IsType<
            PackageHouseVersionPopulationResult.NoMatch>(
                await environment.CreateHouse()
                    .SettleVersionPopulationAsync(
                        request,
                        environment.Root.IssueOperationLease(
                            TestContext.Current.CancellationToken,
                            request.Operation.RequestTimeout,
                            request.Operation.OperationTimeout)));

        Assert.Contains("both boundary majors", noMatch.Reason.ToString());
        Assert.All(
            environment.Clients,
            client =>
            {
                Assert.Equal(1, client.VersionRequests);
                Assert.Equal(0, client.PayloadRequests);
            });
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationCanIncludeUnlistedDiscovery()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest(
                "1.0.0..2.0.0",
                includeUnlisted: true);

        var available = Assert.IsType<
            PackageHouseVersionPopulationResult.Available>(
                await environment.CreateHouse()
                    .SettleVersionPopulationAsync(
                        request,
                        environment.Root.IssueOperationLease(
                            TestContext.Current.CancellationToken,
                            request.Operation.RequestTimeout,
                            request.Operation.OperationTimeout)));

        Assert.True(request.IncludeUnlisted);
        Assert.Same(
            PackageVersionDiscoveryContract
                .CompleteVersionEnumerationIncludingUnlisted,
            available.Evidence.Discovery!.Contract);
        Assert.True(
            available.Evidence.Discovery.Contract
                .SupportsCompleteVersionEnumeration);
        Assert.All(
            environment.Clients,
            client =>
            {
                Assert.Equal(1, client.VersionRequests);
                Assert.Equal(0, client.PayloadRequests);
            });
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationCellRequiresItsExactVectorAddress()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]));
        PackageHouseVersionPopulationRequest firstRequest =
            PopulationRequest("1.0.0..2.0.0");
        PackageHouseVersionPopulationRequest secondRequest =
            PopulationRequest("1.0.0..2.0.0");
        PackageHouse house = environment.CreateHouse();

        var first = Assert.IsType<
            PackageHouseVersionPopulationResult.Available>(
                await house.SettleVersionPopulationAsync(
                    firstRequest,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        firstRequest.Operation.RequestTimeout,
                        firstRequest.Operation.OperationTimeout)));
        var second = Assert.IsType<
            PackageHouseVersionPopulationResult.Available>(
                await house.SettleVersionPopulationAsync(
                    secondRequest,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        secondRequest.Operation.RequestTimeout,
                        secondRequest.Operation.OperationTimeout)));

        Assert.Throws<ArgumentException>(
            () => first.SelectCell(second.Vector.Addresses[0]));
        Assert.NotNull(first.SelectCell(first.Vector.Addresses[0]));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task PreparedExecutionRequiresExactHouseRequest()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0"]));
        PackageHouseVersionPopulationRequest populationRequest =
            PopulationRequest("1.0.0..1.0.0");
        PackageHouse house = environment.CreateHouse();
        var population = Assert.IsType<
            PackageHouseVersionPopulationResult.Available>(
                await house.SettleVersionPopulationAsync(
                    populationRequest,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        populationRequest.Operation.RequestTimeout,
                        populationRequest.Operation.OperationTimeout)));
        PackageHouseVersionPopulationCell cell =
            population.SelectCell(population.Vector.Addresses[0]);
        PackageHouseOperation operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Settle);
        PackageHouseVersionPopulationCellExecution execution =
            cell.PrepareExecution(operation);
        var demand = Assert.IsType<PackageHouseDemand.Candidate>(
            execution.Request.Demand);

        PackageHouseSettlement exact = await house.ExecuteAsync(
            execution.Request,
            environment.IssueOperation(
                execution.Request,
                TestContext.Current.CancellationToken));
        PackageHouseRequest substitutedRequest = cell.CreateRequest(
            operation,
            targetContext: null,
            assetSelection: null,
            libraryHandoff:
                PackageHouseLibraryHandoffMode.PackageOnly);
        PackageHouseSettlement substituted = await house.ExecuteAsync(
            substitutedRequest,
            environment.IssueOperation(
                substitutedRequest,
                TestContext.Current.CancellationToken));

        Assert.Same(cell, execution.Cell);
        Assert.Same(cell.Candidate, demand.Value);
        Assert.Same(cell.Association, execution.Request.Association);
        Assert.True(execution.Accepts(exact));
        Assert.False(execution.Accepts(substituted));
        Assert.Same(
            execution.Request.Association,
            substituted.Result.Request.Association);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationCellAcquiresOnlyFromItsReporters()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]),
            new SourceBehavior(["2.0.0"]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..2.0.0");
        PackageHouse house = environment.CreateHouse(
            (_, _) => new InMemoryPackageStore());
        var available = Assert.IsType<
            PackageHouseVersionPopulationResult.Available>(
                await house.SettleVersionPopulationAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout)));
        PackageHouseVersionPopulationCell cell =
            available.SelectCell(available.Vector.Addresses[0]);
        PackageHouseOperation operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Acquire);

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                cell.CreateRequest(
                    operation,
                    targetContext: null,
                    assetSelection: null,
                    libraryHandoff:
                        PackageHouseLibraryHandoffMode.PackageOnly),
                environment.Root.IssueOperationLease(
                    TestContext.Current.CancellationToken,
                    operation.RequestTimeout,
                    operation.OperationTimeout));

        Assert.IsType<PackageHouseSettlement.Acquired>(settlement);
        Assert.Same(cell.Candidate, settlement.Result.Decision!.Candidate);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        Assert.Equal(0, environment.Clients[1].PayloadRequests);
        Assert.All(
            environment.Clients,
            client => Assert.Equal(1, client.VersionRequests));
        await environment.AssertRootSettledAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionPopulationIncompleteOrFailedDiscoveryCannotIssueCells(
        bool hasHealthyPeer)
    {
        SourceBehavior[] behaviors = hasHealthyPeer
            ?
            [
                new SourceBehavior(["1.0.0", "2.0.0"]),
                new SourceBehavior(
                    [],
                    VersionFailure: PackageSourceFailureKind.Transport),
            ]
            :
            [
                new SourceBehavior(
                    [],
                    VersionFailure: PackageSourceFailureKind.Transport),
            ];
        await using HouseEnvironment environment =
            HouseEnvironment.Create(behaviors);
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..2.0.0");

        PackageHouseVersionPopulationResult result =
            await environment.CreateHouse()
                .SettleVersionPopulationAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        if (hasHealthyPeer)
        {
            Assert.IsType<
                PackageHouseVersionPopulationResult.Incomplete>(
                    result);
        }
        else
        {
            Assert.IsType<
                PackageHouseVersionPopulationResult.Failed>(
                    result);
        }
        Assert.NotNull(result.Evidence.Discovery);
        Assert.All(
            environment.Clients,
            client => Assert.Equal(0, client.PayloadRequests));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationMissingEndpointIsNoMatch()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0"]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..2.0.0");

        PackageHouseVersionPopulationResult result =
            await environment.CreateHouse()
                .SettleVersionPopulationAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        Assert.IsType<PackageHouseVersionPopulationResult.NoMatch>(
            result);
        Assert.Equal(1, environment.Clients[0].VersionRequests);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationAuthoritativeAbsenceIsNotFound()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..2.0.0");

        PackageHouseVersionPopulationResult result =
            await environment.CreateHouse()
                .SettleVersionPopulationAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        Assert.IsType<PackageHouseVersionPopulationResult.NotFound>(
            result);
        Assert.Equal(1, environment.Clients[0].VersionRequests);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationOperationTimeoutIsTypedAndReleasesOperation()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                ["1.0.0", "2.0.0"],
                BeforeVersions: async (_, token) =>
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(60),
                        token)));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest(
                "1.0.0..2.0.0",
                operationTimeout: TimeSpan.FromMilliseconds(20));

        PackageHouseVersionPopulationResult result =
            await environment.CreateHouse()
                .SettleVersionPopulationAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        Assert.IsType<PackageHouseVersionPopulationResult.Failed>(
            result);
        Assert.Contains(
            result.Evidence.Failures,
            failure =>
                failure is PackageHouseFailure.Authority
                {
                    Failure.Timeout.Kind:
                        PackageSourceTimeoutKind.Operation,
                });
        PackageHouseFailure.Timeout timeout =
            Assert.IsType<PackageHouseFailure.Timeout>(
                result.Evidence.Failures.Last());
        Assert.Equal(
            PackageHouseTimeoutKind.Operation,
            timeout.Kind);
        Assert.Equal(
            request.Operation.OperationTimeout,
            timeout.Duration);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationOperationTimeoutRetainsEnumeratedVersions()
    {
        const int versionCount = 8_000;
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(VersionPopulation(versionCount)));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest(
                $"1.0.0..1.0.{versionCount - 1}",
                operationTimeout: TimeSpan.FromMilliseconds(150));

        PackageHouseVersionPopulationResult result =
            await environment.CreateHouse()
                .SettleVersionPopulationAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout));

        Assert.IsType<PackageHouseVersionPopulationResult.Failed>(
            result);
        PackageVersionDiscoveryResult discovery =
            Assert.IsType<PackageVersionDiscoveryResult>(
                result.Evidence.Discovery);
        // The deadline can be observed by discovery's final check or by the
        // population's final check; both must retain the completed evidence.
        Assert.True(
            discovery.State is PackageVersionDiscoveryState.Authoritative
                or PackageVersionDiscoveryState.Failed);
        Assert.Equal(
            versionCount,
            discovery.Versions.Count);
        PackageHouseFailure.Timeout timeout =
            Assert.IsType<PackageHouseFailure.Timeout>(
                result.Evidence.Failures.Last());
        Assert.Equal(
            PackageHouseTimeoutKind.Operation,
            timeout.Kind);
        Assert.Equal(
            request.Operation.OperationTimeout,
            timeout.Duration);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationOperationDeadlinesMustMatchRequest()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..2.0.0");
        PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                request.Operation.RequestTimeout
                    + TimeSpan.FromSeconds(1),
                request.Operation.OperationTimeout);

        await Assert.ThrowsAsync<ArgumentException>(
            () => environment.CreateHouse()
                .SettleVersionPopulationAsync(
                    request,
                    operation));

        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task NullVersionPopulationRequestReleasesTransferredOperation()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]));
        PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => environment.CreateHouse()
                .SettleVersionPopulationAsync(
                    null!,
                    operation));

        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationCallerCancellationRemainsCancellation()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                ["1.0.0", "2.0.0"],
                BeforeVersions: async (_, token) =>
                {
                    cancellation.Cancel();
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        token);
                }));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..2.0.0");

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => environment.CreateHouse()
                    .SettleVersionPopulationAsync(
                        request,
                        environment.Root.IssueOperationLease(
                            cancellation.Token,
                            request.Operation.RequestTimeout,
                            request.Operation.OperationTimeout)));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationCallerCancellationDuringVectorConstructionRemainsCancellation()
    {
        const int versionCount = 8_000;
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(150));
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(VersionPopulation(versionCount)));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest($"1.0.0..1.0.{versionCount - 1}");

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => environment.CreateHouse()
                    .SettleVersionPopulationAsync(
                        request,
                        environment.Root.IssueOperationLease(
                            cancellation.Token,
                            request.Operation.RequestTimeout,
                            request.Operation.OperationTimeout)));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task VersionPopulationCellRejectsAnotherRootGeneration()
    {
        await using HouseEnvironment source = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]));
        await using HouseEnvironment other = HouseEnvironment.Create(
            new SourceBehavior(["1.0.0", "2.0.0"]));
        PackageHouseVersionPopulationRequest request =
            PopulationRequest("1.0.0..2.0.0");
        var available = Assert.IsType<
            PackageHouseVersionPopulationResult.Available>(
                await source.CreateHouse()
                    .SettleVersionPopulationAsync(
                        request,
                        source.Root.IssueOperationLease(
                            TestContext.Current.CancellationToken,
                            request.Operation.RequestTimeout,
                            request.Operation.OperationTimeout)));
        PackageHouseVersionPopulationCell cell =
            available.SelectCell(available.Vector.Addresses[0]);
        PackageHouseOperation operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Settle);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => other.CreateHouse().ExecuteAsync(
                cell.CreateRequest(
                    operation,
                    targetContext: null,
                    assetSelection: null,
                    libraryHandoff:
                        PackageHouseLibraryHandoffMode.PackageOnly),
                other.Root.IssueOperationLease(
                    TestContext.Current.CancellationToken,
                    operation.RequestTimeout,
                    operation.OperationTimeout)));

        await source.AssertRootSettledAsync();
        await other.AssertRootSettledAsync();
    }

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
        PackageHouseRootContributionOutcome.NoContribution noContribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.NoContribution>(
                PackageHouseRootContributionAdapter.Create(settlement));
        Assert.Same(resourceFree.Result, noContribution.Result);
        Assert.Equal(
            PackageHouseRootNoContributionReason.ResourceFreeSettlement,
            noContribution.Reason);
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
        Assert.Equal(acquisition.Producer, payload.Producer);
        Assert.Same(
            environment.Clients[0].Source,
            acquired.SourcePayloadResult!.Source);
        Assert.True(acquired.SelectionUsesOriginalSources);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ExactCompileRealizeBindsSelectionAndLibraryHandoff()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"ref/net10.0/{PackageId}.dll",
                        $"lib/net10.0/{PackageId}.dll",
                        $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
                    ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(
                "net10.0",
                "linux-x64"),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                acquired.Result.Evidence.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.Selected,
            realization.Selection.Status);
        Assert.Equal(
            PackageCompileAssetSelectionPolicy.ExplicitTarget,
            realization.Receipt.Policy);
        Assert.Equal(
            "net10.0",
            realization.Receipt.RequestedTargetFramework);
        Assert.Equal("net10.0", realization.Selection.TargetFramework);
        PackageHouseLibraryHandoff.Compile handoff =
            Assert.IsType<PackageHouseLibraryHandoff.Compile>(
                Assert.Single(realization.LibraryHandoffs));
        Assert.Equal(
            $"ref/net10.0/{PackageId}.dll",
            handoff.Asset.Path);
        Assert.Equal(
            $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
            handoff.ImplementationAsset!.Path);
        Assert.Equal(
            "linux-x64",
            handoff.ImplementationAsset.RuntimeIdentifier);
        Assert.Same(
            acquired.Payload.Content.GenerationIdentity,
            realization.Receipt.Generation);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(
                    settlement)).Contribution;
        Assert.Same(acquired.Result, contribution.Result);
        Assert.Same(realization, contribution.Realization);
        Assert.Same(realization.Receipt, contribution.SelectionReceipt);
        Assert.Same(
            acquired.Payload.Content.GenerationIdentity,
            contribution.Binding.ContentGenerationIdentity);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg.PortableKey,
            contribution.Binding.Coordinate.Producer);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg.Key,
            contribution.Binding.Root.ProducerKey);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg,
            acquired.Payload.Producer);
        Assert.True(
            contribution.Binding.Root.ReferencesContent(
                acquired.Payload.Content));
        Assert.Equal(
            PackageId,
            contribution.Binding.Coordinate.PackageId,
            ignoreCase: true);
        Assert.Equal(
            Version,
            contribution.Binding.Coordinate.Version);
        Assert.Equal(
            "net10.0",
            contribution.Binding.Coordinate.Framework);
        Assert.Equal(
            "linux-x64",
            contribution.Binding.Coordinate.RuntimeIdentifier);
        Assert.Equal(
            "net10.0",
            contribution.Binding.Root.RequestedTargetFramework);
        Assert.Equal(
            "linux-x64",
            contribution.Binding.Root.RequestedRuntimeIdentifier);
        Assert.Same(
            realization.Selection.DefaultAsset,
            contribution.Binding.Root.AssetSelection.DefaultAsset);
        Assert.Equal(
            realization.Selection.Assets.Count,
            contribution.Binding.Root.AssetSelection.Assets.Count);
        PackageRootReacquisitionRequest rootRequest =
            contribution.Binding.CreateReacquisitionRequest();
        Assert.True(rootRequest.AllowsCompatibleTargetSelection);
        Assert.False(rootRequest.UsesCompatibleImplementationSelection);
        for (int index = 0;
             index < realization.Selection.Assets.Count;
             index++)
        {
            Assert.Same(
                realization.Selection.Assets[index],
                contribution.Binding.Root.AssetSelection.Assets[index]);
        }
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        ExactCompileRealizeKeepsRequestedAndSelectedFrameworksDistinct()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                PayloadEntries:
                [
                    $"lib/net8.0/{PackageId}.dll",
                ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(settlement);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                Assert.IsType<PackageHouseResult.Settled>(
                    acquired.Result).Evidence.Realization);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(settlement))
                .Contribution;
        PackageRootReacquisitionRequest rootRequest =
            contribution.Binding.CreateReacquisitionRequest();

        Assert.Equal("net10.0", rootRequest.CompileTargetFramework);
        Assert.Equal("net8.0", rootRequest.SelectionTargetFramework);
        Assert.True(rootRequest.AllowsCompatibleTargetSelection);
        Assert.True(rootRequest.UsesCompatibleImplementationSelection);
        Assert.Equal(
            "net8.0",
            contribution.Binding.Root.AssetSelection.TargetFramework);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        ExactCompileRealizePreservesCompatibleImplementationUniverse()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                PayloadEntries:
                [
                    $"ref/net10.0/{PackageId}.dll",
                    $"lib/net8.0/{PackageId}.dll",
                ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(settlement);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                Assert.IsType<PackageHouseResult.Settled>(
                    acquired.Result).Evidence.Realization);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(settlement))
                .Contribution;
        PackageRootReacquisitionRequest rootRequest =
            contribution.Binding.CreateReacquisitionRequest();

        Assert.Equal("net10.0", realization.Selection.TargetFramework);
        Assert.Equal(
            "net8.0",
            realization.Selection.ImplementationTargetFramework);
        Assert.True(
            realization.Selection.UsesCompatibleImplementationSelection);
        Assert.Equal("net10.0", rootRequest.CompileTargetFramework);
        Assert.Equal("net8.0", rootRequest.SelectionTargetFramework);
        Assert.True(rootRequest.AllowsCompatibleTargetSelection);
        Assert.True(rootRequest.UsesCompatibleImplementationSelection);
        Assert.Equal(
            "net8.0",
            contribution.Binding.Root.RequestedTargetFramework);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task OwnerDefaultCompileRealizePreservesHighestAvailableInventory()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        "ref/net6.0/_._",
                        $"lib/net8.0/{PackageId}.dll",
                        $"ref/net10.0/nested/{PackageId}.Companion.dll",
                        $"ref/net10.0/{PackageId}.dll",
                    ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.OwnerDefault(),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                acquired.Result.Evidence.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionPolicy.HighestAvailable,
            realization.Receipt.Policy);
        Assert.Null(realization.Receipt.RequestedTargetFramework);
        Assert.Equal("net10.0", realization.Selection.TargetFramework);
        Assert.Equal(
            ["net10.0", "net8.0", "net6.0"],
            realization.Selection.AvailableTargetFrameworks);
        Assert.Equal(
            ["net6.0"],
            realization.Selection.ExplicitEmptyTargetFrameworks);
        Assert.Equal(
            realization.Selection.AvailableTargetFrameworks,
            realization.Selection.AvailableSlices.Select(
                slice => slice.TargetFramework));
        PackageCompileAssetSlice emptySlice =
            realization.Selection.AvailableSlices[2];
        Assert.Empty(emptySlice.CandidateAssets);
        Assert.True(emptySlice.HasExplicitEmptyReferenceGroup);
        Assert.Equal(
            [
                $"ref/net10.0/{PackageId}.dll",
                $"ref/net10.0/nested/{PackageId}.Companion.dll",
            ],
            realization.LibraryHandoffs
                .Select(handoff =>
                    Assert.IsType<PackageHouseLibraryHandoff.Compile>(
                        handoff).Asset.Path));
        Assert.Same(
            acquired.Payload.Content.GenerationIdentity,
            realization.Receipt.Generation);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(settlement))
                .Contribution;
        PackageRootReacquisitionRequest rootRequest =
            contribution.Binding.CreateReacquisitionRequest();
        Assert.Equal(
            "net8.0",
            realization.Selection.ImplementationTargetFramework);
        Assert.False(
            realization.Selection.UsesCompatibleImplementationSelection);
        Assert.Equal("net10.0", rootRequest.CompileTargetFramework);
        Assert.Equal("net8.0", rootRequest.SelectionTargetFramework);
        Assert.False(rootRequest.AllowsCompatibleTargetSelection);
        Assert.False(rootRequest.UsesCompatibleImplementationSelection);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        OwnerDefaultCompileRealizePreservesAbsentImplementationUniverse()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"ref/net10.0/{PackageId}.dll",
                    ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.OwnerDefault(),
            PackageHouseAssetSelectionKind.Compile);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                Assert.IsType<PackageHouseResult.Settled>(
                    Assert.IsType<PackageHouseSettlement.Acquired>(
                        settlement).Result).Evidence.Realization);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(settlement))
                .Contribution;
        PackageRootReacquisitionRequest rootRequest =
            contribution.Binding.CreateReacquisitionRequest();

        Assert.Equal("net10.0", realization.Selection.TargetFramework);
        Assert.Null(
            realization.Selection.ImplementationTargetFramework);
        Assert.Equal("net10.0", rootRequest.CompileTargetFramework);
        Assert.Equal("net10.0", rootRequest.SelectionTargetFramework);
        Assert.False(rootRequest.HasSelectedImplementationUniverse);
        Assert.False(rootRequest.AllowsCompatibleTargetSelection);
        Assert.False(rootRequest.UsesCompatibleImplementationSelection);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ExactRuntimeRealizeAppliesExactRidOverlay()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                PayloadEntries:
                [
                    $"lib/net10.0/{PackageId}.dll",
                    $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
                ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(
                "net10.0",
                "linux-x64"),
            PackageHouseAssetSelectionKind.Runtime,
            PackageHouseLibraryHandoffMode.SelectedLibraries);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        PackageHouseRealizationReceipt.Runtime realization =
            Assert.IsType<PackageHouseRealizationReceipt.Runtime>(
                acquired.Result.Evidence.Realization);
        PackageAssetSelection.Selected selected =
            Assert.IsType<PackageAssetSelection.Selected>(
                realization.Selection);
        PackageAssetEntry asset = Assert.Single(
            selected.Universe.Assets);
        Assert.Equal(
            $"runtimes/linux-x64/lib/net10.0/{PackageId}.dll",
            asset.EntryPath);
        Assert.Equal("linux-x64", asset.RuntimeIdentifier);
        PackageHouseLibraryHandoff.Runtime handoff =
            Assert.IsType<PackageHouseLibraryHandoff.Runtime>(
                Assert.Single(realization.LibraryHandoffs));
        Assert.Same(asset, handoff.Asset);
        PackageHouseRootContributionOutcome.NoContribution noContribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.NoContribution>(
                PackageHouseRootContributionAdapter.Create(settlement));
        Assert.Same(acquired.Result, noContribution.Result);
        Assert.Equal(
            PackageHouseRootNoContributionReason
                .CompileRealizationUnavailable,
            noContribution.Reason);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ExactCompileRealizePreservesExplicitEmptyGroup()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"lib/net8.0/{PackageId}.dll",
                        "ref/net10.0/_._",
                    ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                acquired.Result.Evidence.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            realization.Selection.Status);
        Assert.Equal(
            PackageCompileAssetSelectionPolicy.ExplicitTarget,
            realization.Receipt.Policy);
        Assert.Equal(
            ["net10.0", "net8.0"],
            realization.Selection.AvailableTargetFrameworks);
        Assert.Equal(
            ["net10.0"],
            realization.Selection.ExplicitEmptyTargetFrameworks);
        Assert.Empty(realization.LibraryHandoffs);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(
                    settlement)).Contribution;
        Assert.Same(realization, contribution.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            contribution.Binding.Root.AssetSelection.Status);
        Assert.Empty(contribution.Binding.Root.AssetSelection.Assets);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ExactCompileRealizePreservesNoMatchWithPayload()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"ref/net11.0/{PackageId}.dll",
                    ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        PackageHouseResult.NoMatch noMatch =
            Assert.IsType<PackageHouseResult.NoMatch>(
                acquired.Result);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                noMatch.Evidence.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus
                .NoMatchingTargetFramework,
            realization.Selection.Status);
        Assert.Equal(
            PackageCompileAssetSelectionPolicy.ExplicitTarget,
            realization.Receipt.Policy);
        Assert.Equal(
            ["net11.0"],
            realization.Selection.AvailableTargetFrameworks);
        Assert.Equal(
            [$"ref/net11.0/{PackageId}.dll"],
            realization.Selection.CandidateAssets.Select(
                asset => asset.Path));
        PackageCompileAssetSlice availableSlice =
            Assert.Single(realization.Selection.AvailableSlices);
        Assert.Equal("net11.0", availableSlice.TargetFramework);
        Assert.Equal(
            realization.Selection.CandidateAssets,
            availableSlice.CandidateAssets);
        Assert.Empty(realization.LibraryHandoffs);
        Assert.NotNull(noMatch.Evidence.Acquisition);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(
                    settlement)).Contribution;
        Assert.Same(noMatch, contribution.Result);
        Assert.Same(realization, contribution.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus
                .NoMatchingTargetFramework,
            contribution.Binding.Root.AssetSelection.Status);
        Assert.Empty(contribution.Binding.Root.AssetSelection.Assets);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimeRealizeKeepsRequestedAndSelectedFrameworksDistinct()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                PayloadEntries:
                [
                    $"lib/net8.0/{PackageId}.dll",
                ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Runtime);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseRealizationReceipt.Runtime realization =
            Assert.IsType<PackageHouseRealizationReceipt.Runtime>(
                Assert.IsType<PackageHouseResult.Settled>(
                    settlement.Result).Evidence.Realization);
        Assert.Equal(
            "net10.0",
            realization.Receipt.RequestedTargetFramework);
        Assert.Equal(
            "net8.0",
            Assert.IsType<PackageAssetSelection.Selected>(
                realization.Selection).Universe.TargetFramework);
        Assert.Empty(realization.LibraryHandoffs);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task SameCoordinateWithTwoTargetsKeepsDistinctRealizations()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                PayloadEntries:
                [
                    $"lib/net8.0/{PackageId}.dll",
                    $"lib/net10.0/{PackageId}.dll",
                ]));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateHouse(
            (_, _) => store);
        PackageHouseRequest netEight = RuntimeRealizeRequest(
            "net8.0");
        PackageHouseRequest netTen = RuntimeRealizeRequest(
            "net10.0");

        PackageHouseSettlement first = await house.ExecuteAsync(
            netEight,
            environment.IssueOperation(
                netEight,
                TestContext.Current.CancellationToken));
        PackageHouseSettlement second = await house.ExecuteAsync(
            netTen,
            environment.IssueOperation(
                netTen,
                TestContext.Current.CancellationToken));

        PackageHouseRealizationReceipt.Runtime firstRealization =
            RuntimeRealization(first);
        PackageHouseRealizationReceipt.Runtime secondRealization =
            RuntimeRealization(second);
        Assert.NotSame(firstRealization, secondRealization);
        Assert.Same(
            firstRealization.Acquisition.Generation,
            secondRealization.Acquisition.Generation);
        Assert.Equal(
            "net8.0",
            Assert.IsType<PackageAssetSelection.Selected>(
                firstRealization.Selection).Universe.TargetFramework);
        Assert.Equal(
            "net10.0",
            Assert.IsType<PackageAssetSelection.Selected>(
                secondRealization.Selection).Universe.TargetFramework);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task TimeoutAfterSelectionRetainsPayloadAndRealization()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout:
                    TimeSpan.FromMilliseconds(250)),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Runtime);
        PackageHouse house = environment.CreateHouse(
            (_, producer) =>
                new DelayedEnumerationPackageStore(
                    producer.Key,
                    TimeSpan.FromMilliseconds(750),
                    $"lib/net10.0/{PackageId}.dll"));

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        PackageHouseResult.Failed failed =
            Assert.IsType<PackageHouseResult.Failed>(
                acquired.Result);
        PackageHouseRealizationReceipt.Runtime realization =
            Assert.IsType<PackageHouseRealizationReceipt.Runtime>(
                failed.Evidence.Realization);
        Assert.IsType<PackageAssetSelection.Selected>(
            realization.Selection);
        Assert.Same(
            acquired.Payload.Content.GenerationIdentity,
            realization.Receipt.Generation);
        Assert.Same(
            acquired.Payload,
            acquired.SourcePayloadResult!.Payload);
        Assert.True(acquired.SelectionUsesOriginalSources);
        Assert.Contains(
            failed.Evidence.Failures,
            failure => failure is PackageHouseFailure.Timeout
            {
                Kind: PackageHouseTimeoutKind.Operation,
            });
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CompileTimeoutAfterSelectionDoesNotProduceRootContribution()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout:
                    TimeSpan.FromMilliseconds(250)),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile);
        PackageHouse house = environment.CreateHouse(
            (_, producer) =>
                new DelayedEnumerationPackageStore(
                    producer.Key,
                    TimeSpan.FromMilliseconds(750),
                    $"lib/net10.0/{PackageId}.dll"));

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        PackageHouseResult.Failed failed =
            Assert.IsType<PackageHouseResult.Failed>(
                acquired.Result);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                failed.Evidence.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.Selected,
            realization.Selection.Status);
        PackageHouseRootContributionOutcome.NoContribution noContribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.NoContribution>(
                PackageHouseRootContributionAdapter.Create(settlement));
        Assert.Same(failed, noContribution.Result);
        Assert.Same(
            realization,
            noContribution.Result.Evidence.Realization);
        Assert.Equal(
            PackageHouseRootNoContributionReason.OperationFailed,
            noContribution.Reason);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CustomProducerCompileRealizationContributesPortableRootCoordinate()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(
                [Version],
                PayloadEntries:
                [
                    $"lib/net10.0/{PackageId}.dll",
                ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
            acquired.Result.Evidence.Realization);
        PackageHouseRootContribution contribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.Contributed>(
                PackageHouseRootContributionAdapter.Create(settlement))
                .Contribution;
        Assert.Same(acquired.Result, contribution.Result);
        Assert.Same(realization, contribution.Realization);
        Assert.Equal(
            environment.Clients[0].Source.Producer.PortableKey,
            contribution.Binding.Coordinate.Producer);
        Assert.Equal(
            environment.Clients[0].Source.Producer.Key,
            contribution.Binding.Root.ProducerKey);
        Assert.Equal(
            environment.Clients[0].Source.Producer,
            acquired.Payload.Producer);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task UnicodePackageIdCompileRealizationReportsUnrepresentableRootCoordinate()
    {
        const string unicodePackageId = "Caf\u00E9";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                "caf\u00E9",
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        "lib/net10.0/Contoso.dll",
                    ]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    unicodePackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net10.0"),
            PackageHouseAssetSelectionKind.Compile);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.Equal(
            "caf\u00E9",
            acquired.Payload.Coordinate.PackageId);
        Assert.IsType<PackageHouseRealizationReceipt.Compile>(
            acquired.Result.Evidence.Realization);
        PackageHouseRootContributionOutcome.NoContribution noContribution =
            Assert.IsType<
                PackageHouseRootContributionOutcome.NoContribution>(
                PackageHouseRootContributionAdapter.Create(settlement));
        Assert.Same(acquired.Result, noContribution.Result);
        Assert.Equal(
            PackageHouseRootNoContributionReason
                .CoordinateNotRepresentable,
            noContribution.Reason);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimeOwnerDefaultRealizeIsVisiblyRejected()
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
            PackageHouseTargetContext.OwnerDefault(),
            PackageHouseAssetSelectionKind.Runtime);

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore())
                .ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken));

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        PackageHouseResult.Rejected rejected =
            Assert.IsType<PackageHouseResult.Rejected>(
                acquired.Result);
        Assert.Null(rejected.Evidence.Realization);
        PackageHouseFailure.Stage failure =
            Assert.IsType<PackageHouseFailure.Stage>(
                Assert.Single(rejected.Evidence.Failures));
        Assert.Equal(
            PackageHouseFailureStage.Selection,
            failure.StageKind);
        Assert.Contains(
            "exact target framework",
            rejected.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Same(
            acquired.Payload,
            acquired.SourcePayloadResult!.Payload);
        Assert.True(acquired.SelectionUsesOriginalSources);
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
        Assert.Single(settlement.SourcePayloadResult!.ReportingAuthorities!);
        Assert.False(settlement.SelectionUsesOriginalSources);
    }

    [Fact]
    public async Task CandidateAcquirePreservesResolvedAuthorityCorrespondence()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior(["9.0.0"]),
            new SourceBehavior([Version]));
        PackageHouse house = environment.CreateHouse(
            (_, _) => new InMemoryPackageStore());
        var selectingRequest = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(
                new PackageVersionSelectionRequest.LatestStable(
                    PackageId)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));
        PackageHouseSettlement selected =
            await house.ExecuteAsync(
                selectingRequest,
                environment.IssueOperation(
                    selectingRequest,
                    TestContext.Current.CancellationToken));
        PackageAcquisitionCandidate candidate =
            Assert.IsType<PackageHouseResult.Settled>(selected.Result)
                .Decision!.Candidate!;
        Assert.Single(candidate.Authorities);

        var candidateRequest = new PackageHouseRequest(
            new PackageHouseDemand.Candidate(candidate),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire));
        PackageHouseSettlement acquired =
            await house.ExecuteAsync(
                candidateRequest,
                environment.IssueOperation(
                    candidateRequest,
                    TestContext.Current.CancellationToken));

        Assert.IsType<PackageHouseSettlement.Acquired>(acquired);
        Assert.Same(candidate, acquired.Result.Decision!.Candidate);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        Assert.Equal(1, environment.Clients[1].PayloadRequests);
        Assert.All(
            environment.Clients,
            client => Assert.Equal(1, client.VersionRequests));
    }

    [Theory]
    [InlineData(
        "system.text.json",
        "9.0.0",
        "11.0.0",
        PlatformFamily.DotNetRuntime)]
    [InlineData(
        "system.runtime",
        "4.0.0",
        "4.3.1",
        PlatformFamily.DotNetRuntime)]
    [InlineData(
        "microsoft.extensions.caching.memory",
        "10.0.0",
        "10.0.0",
        PlatformFamily.AspNetCore)]
    public async Task KnownPlatformPackageAcquireDelegatesBeforePayloadCapability(
        string packageId,
        string requestedVersion,
        string suppliedVersion,
        PlatformFamily family)
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForPackage(
                packageId,
                new SourceBehavior([requestedVersion]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(
                environment,
                packageId,
                requestedVersion);
        PackageHouseRequest request = CandidateRequest(
            candidate,
            PackageHouseOperationProfile.Acquire,
            platformFamily: family);
        PackageHousePruningReceipt pruning =
            PackageHousePruningReceipt.Evaluate(
                request,
                PlatformInventory(
                    packageId,
                    suppliedVersion,
                    family));

        PackageHouseSettlement settlement =
            await environment.CreateHouse().ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken),
                pruning);

        PackageHouseResult.Delegated delegated =
            Assert.IsType<PackageHouseResult.Delegated>(
                settlement.Result);
        Assert.IsType<PackageHouseSettlement.ResourceFree>(
            settlement);
        Assert.Same(pruning, delegated.Evidence.Decision!.Pruning);
        Assert.Same(candidate, delegated.Evidence.Decision.Candidate);
        Assert.Equal(family, delegated.Delegation.Target.Family);
        Assert.Equal(
            suppliedVersion,
            delegated.Delegation.Supply.SuppliedVersion?
                .ToNormalizedString());
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CandidateRealizeDelegatesBeforePayloadCapability()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForPackage(
                PrunablePackageId,
                new SourceBehavior(["9.0.0"]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(
                environment,
                PrunablePackageId,
                "9.0.0");
        PackageHouseRequest request = CandidateRequest(
            candidate,
            PackageHouseOperationProfile.Realize,
            PackageHouseAssetSelectionKind.Compile);
        PackageHousePruningReceipt pruning =
            PackageHousePruningReceipt.Evaluate(
                request,
                PlatformInventory(
                    PrunablePackageId,
                    suppliedVersion: "11.0.0"));

        PackageHouseSettlement settlement =
            await environment.CreateHouse().ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken),
                pruning);

        PackageHouseResult.Delegated delegated =
            Assert.IsType<PackageHouseResult.Delegated>(
                settlement.Result);
        Assert.IsType<PackageHouseSettlement.ResourceFree>(
            settlement);
        Assert.Same(pruning, delegated.Evidence.Decision!.Pruning);
        Assert.Same(candidate, delegated.Evidence.Decision.Candidate);
        Assert.Null(delegated.Evidence.Realization);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task NonSubsumedCandidateRetainsPruningAndAcquiresPayload()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForPackage(
                PrunablePackageId,
                new SourceBehavior(["12.0.0"]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(
                environment,
                PrunablePackageId,
                "12.0.0");
        PackageHouseRequest request = CandidateRequest(
            candidate,
            PackageHouseOperationProfile.Acquire);
        PackageHousePruningReceipt pruning =
            PackageHousePruningReceipt.Evaluate(
                request,
                PlatformInventory(
                    PrunablePackageId,
                    suppliedVersion: "11.0.0"));

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore()).ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken),
                    pruning);

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.Same(pruning, acquired.Result.Decision!.Pruning);
        Assert.False(
            acquired.Result.Decision.Pruning!.Supply
                .DelegatesToPlatform);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task NonSubsumedCandidateRealizeRetainsPruningAndSelection()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForPackage(
                PrunablePackageId,
                new SourceBehavior(
                    ["12.0.0"],
                    PayloadEntries:
                    [
                        $"ref/net11.0/{PrunablePackageId}.dll",
                    ]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(
                environment,
                PrunablePackageId,
                "12.0.0");
        PackageHouseRequest request = CandidateRequest(
            candidate,
            PackageHouseOperationProfile.Realize,
            PackageHouseAssetSelectionKind.Compile);
        PackageHousePruningReceipt pruning =
            PackageHousePruningReceipt.Evaluate(
                request,
                PlatformInventory(
                    PrunablePackageId,
                    suppliedVersion: "11.0.0"));

        PackageHouseSettlement settlement =
            await environment.CreateHouse(
                (_, _) => new InMemoryPackageStore()).ExecuteAsync(
                    request,
                    environment.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken),
                    pruning);

        PackageHouseSettlement.Acquired acquired =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                settlement);
        Assert.Same(pruning, acquired.Result.Decision!.Pruning);
        Assert.False(
            acquired.Result.Decision.Pruning!.Supply
                .DelegatesToPlatform);
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                Assert.IsType<PackageHouseResult.Settled>(
                    acquired.Result).Evidence.Realization);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.Selected,
            realization.Selection.Status);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task PruningCannotBypassCandidateGenerationOrAuthorization()
    {
        await using HouseEnvironment first = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        await using HouseEnvironment second = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(first);
        PackageHouseRequest request = CandidateRequest(
            candidate,
            PackageHouseOperationProfile.Acquire);
        PackageHousePruningReceipt pruning =
            PackageHousePruningReceipt.Evaluate(
                request,
                PlatformInventory(
                    PackageId,
                    suppliedVersion: "11.0.0"));

        InvalidOperationException foreignGeneration =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => second.CreateHouse().ExecuteAsync(
                    request,
                    second.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken),
                    pruning));
        Assert.Contains(
            "another Package Source root generation",
            foreignGeneration.Message,
            StringComparison.Ordinal);

        var deniedHouse = new PackageHouse(
            new FixedAuthorization(
                PackageSourceAuthorization.Deny(
                    "The package is not authorized.")));
        PackageHouseSettlement denied =
            await deniedHouse.ExecuteAsync(
                request,
                first.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken),
                pruning);
        Assert.IsType<PackageHouseResult.Rejected>(denied.Result);
        Assert.Null(denied.Result.Decision!.Pruning);
        Assert.Equal(0, first.Clients[0].PayloadRequests);
        Assert.Equal(0, second.Clients[0].PayloadRequests);
        await first.AssertRootSettledAsync();
        await second.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MismatchedPruningReceiptReleasesTransferredOperation()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(environment);
        PackageHouseRequest receiptRequest = CandidateRequest(
            candidate,
            PackageHouseOperationProfile.Acquire);
        PackageHousePruningReceipt pruning =
            PackageHousePruningReceipt.Evaluate(
                receiptRequest,
                PlatformInventory(
                    PackageId,
                    suppliedVersion: "11.0.0"));
        PackageHouseRequest executionRequest = CandidateRequest(
            candidate,
            PackageHouseOperationProfile.Acquire);

        await Assert.ThrowsAsync<ArgumentException>(
            () => environment.CreateHouse().ExecuteAsync(
                executionRequest,
                environment.IssueOperation(
                    executionRequest,
                    TestContext.Current.CancellationToken),
                pruning));

        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ReceiptAwareExecutionDoesNotBroadenExactDemand()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire),
            PackageHouseTargetContext.Exact(
                "net11.0",
                platformTarget: new PlatformFamilyTarget(
                    PlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse("net11.0"),
                    PlatformVersion.Parse("11.0.0"))));
        PackageHousePruningReceipt pruning =
            PackageHousePruningReceipt.Evaluate(
                request,
                PlatformInventory(
                    PackageId,
                    suppliedVersion: "11.0.0"));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => environment.CreateHouse().ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken),
                pruning));

        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CandidateDemandRejectsAnotherLeaseIssuer()
    {
        await using HouseEnvironment first = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        await using HouseEnvironment second = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageAcquisitionCandidate candidate;
        using (PackageSourceOperationLease operation =
            first.Root.IssueOperationLease(
                TestContext.Current.CancellationToken))
        {
            candidate = Assert.IsType<PackageAcquisitionCandidate>(
                operation.ResolvePinnedCandidate(
                    first.Authorization.AuthorizeSourcesFor(PackageId),
                    PackageSourceCoordinate.Create(PackageId, Version))
                .Candidate);
        }
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Candidate(candidate),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => second.CreateHouse().ExecuteAsync(
                    request,
                    second.IssueOperation(
                        request,
                        TestContext.Current.CancellationToken)));

        Assert.Contains(
            "another Package Source root generation",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CandidateManifestReleasesTransferredOperationWhenSourceThrows()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(environment);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => PackageHouse.AcquireCandidateManifestAsync(
                candidate,
                environment.Root.IssueOperationLease(
                    TestContext.Current.CancellationToken)));
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CandidateManifestRejectsForeignGenerationAndReleasesOperation()
    {
        await using HouseEnvironment first = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        await using HouseEnvironment second = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(first);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PackageHouse.AcquireCandidateManifestAsync(
                    candidate,
                    second.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken)));

        Assert.Contains(
            "another Package Source root generation",
            exception.Message,
            StringComparison.Ordinal);
        await first.AssertRootSettledAsync();
        await second.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CandidateManifestExpiredOperationReturnsTypedTimeout()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageAcquisitionCandidate candidate =
            ResolveCandidate(environment);
        using PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromTicks(1));
        await Task.Delay(
            TimeSpan.FromMilliseconds(20),
            TestContext.Current.CancellationToken);

        ConfiguredPackageManifestResult result =
            await PackageHouse.AcquireCandidateManifestAsync(
                candidate,
                operation);

        Assert.Null(result.Manifest);
        Assert.Null(result.Authority);
        PackageAuthorityFailure failure =
            Assert.Single(result.Failures);
        Assert.Equal(
            PackageAuthorityFailureKind.Timeout,
            failure.Kind);
        Assert.Equal(
            PackageSourceTimeoutKind.Operation,
            failure.Timeout?.Kind);
        Assert.Equal(
            TimeSpan.FromTicks(1),
            failure.Timeout?.Duration);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CandidateDemandCannotBypassHouseAuthorization()
    {
        await using HouseEnvironment environment = HouseEnvironment.Create(
            new SourceBehavior([Version]));
        PackageAcquisitionCandidate candidate;
        using (PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken))
        {
            candidate = Assert.IsType<PackageAcquisitionCandidate>(
                operation.ResolvePinnedCandidate(
                    environment.Authorization.AuthorizeSourcesFor(PackageId),
                    PackageSourceCoordinate.Create(PackageId, Version))
                .Candidate);
        }
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Candidate(candidate),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire));
        var house = new PackageHouse(
            new FixedAuthorization(
                PackageSourceAuthorization.Deny(
                    "The package is not authorized.")),
            new PackagePayloadAcquisitionPlan(
                (_, _) => new InMemoryPackageStore()));

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(
                request,
                environment.IssueOperation(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.IsType<PackageHouseSettlement.ResourceFree>(settlement);
        Assert.IsType<PackageHouseResult.Rejected>(settlement.Result);
        Assert.Null(settlement.Result.Decision!.Candidate);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
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
    public async Task RealizeRequiresPayloadAcquisitionPlan()
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

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
            () => environment.CreateHouse().ExecuteAsync(
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

    private static PackageHouseRequest RuntimeRealizeRequest(
        string targetFramework) =>
        new(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(targetFramework),
            PackageHouseAssetSelectionKind.Runtime);

    private static PackageHouseRealizationReceipt.Runtime
        RuntimeRealization(PackageHouseSettlement settlement) =>
        Assert.IsType<PackageHouseRealizationReceipt.Runtime>(
            Assert.IsType<PackageHouseResult.Settled>(
                settlement.Result).Evidence.Realization);

    private static PackageHouseRequest CandidateRequest(
        PackageAcquisitionCandidate candidate,
        PackageHouseOperationProfile profile,
        PackageHouseAssetSelectionKind? assetSelection = null,
        PlatformFamily platformFamily = PlatformFamily.DotNetRuntime) =>
        new(
            new PackageHouseDemand.Candidate(candidate),
            PackageHouseOperation.Create(profile),
            PackageHouseTargetContext.Exact(
                "net11.0",
                platformTarget: new PlatformFamilyTarget(
                    platformFamily,
                    PlatformTargetFramework.Parse("net11.0"),
                    PlatformVersion.Parse("11.0.0"))),
            assetSelection);

    private static PackageHouseVersionPopulationRequest PopulationRequest(
        string endpoints,
        bool includePrerelease = false,
        TimeSpan? operationTimeout = null,
        bool includeUnlisted = false,
        PackageVersionPopulationPolicy policy =
            PackageVersionPopulationPolicy.ExactEndpoints)
    {
        Assert.True(
            PackageVersionRange.TryParse(
                $"{PackageId}@{endpoints}",
                out PackageVersionRange? range,
                out string? error),
            error);
        return new(
            range!,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle,
                operationTimeout: operationTimeout),
            policy: policy,
            includePrerelease: includePrerelease,
            includeUnlisted: includeUnlisted,
            association: null);
    }

    private static PackageHouseVersionListingRequest ListingRequest(
        bool includePrerelease = false,
        bool includeUnlisted = false,
        TimeSpan? operationTimeout = null) =>
        new(
            PackageId,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle,
                operationTimeout: operationTimeout),
            includePrerelease,
            includeUnlisted);

    private static string[] VersionPopulation(int count) =>
    [
        .. Enumerable.Range(0, count).Select(
            index => $"1.0.{index}"),
    ];

    private static PackageAcquisitionCandidate ResolveCandidate(
        HouseEnvironment environment,
        string packageId = PackageId,
        string version = Version)
    {
        using PackageSourceOperationLease operation =
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken);
        return Assert.IsType<PackageAcquisitionCandidate>(
            operation.ResolvePinnedCandidate(
                environment.Authorization.AuthorizeSourcesFor(
                    packageId),
                PackageSourceCoordinate.Create(
                    packageId,
                    version)).Candidate);
    }

    private static PlatformPruneInventory PlatformInventory(
        string packageId,
        string suppliedVersion,
        PlatformFamily family = PlatformFamily.DotNetRuntime) =>
        PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(
                family switch
                {
                    PlatformFamily.DotNetRuntime =>
                        "Microsoft.NETCore.App",
                    PlatformFamily.AspNetCore =>
                        "Microsoft.AspNetCore.App",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(family)),
                },
                "net11.0",
                NuGetVersion.Parse("11.0.0")),
            [$"{packageId}|{suppliedVersion}"]);

    private sealed record SourceBehavior(
        IReadOnlyList<string> Versions,
        PackageSourceFailureKind? VersionFailure = null,
        bool PayloadNotFound = false,
        Func<
            NuGetOperationContext?,
            CancellationToken,
            Task>? BeforeVersions = null,
        IReadOnlyList<string>? PayloadEntries = null,
        IReadOnlyList<(string EntryPath, byte[] Content)>?
            PayloadContentEntries = null);

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
            => CreateForPackage(PackageId, behaviors);

        public static HouseEnvironment CreateForAnyPackage(
            params SourceBehavior[] behaviors) =>
            CreateForPackage(
                packageId: null,
                behaviors: behaviors,
                useNuGetOrgEndpoint: false);

        public static HouseEnvironment CreateNuGetOrg(
            SourceBehavior behavior) =>
            CreateNuGetOrg(PackageId, behavior);

        public static HouseEnvironment CreateNuGetOrg(
            string packageId,
            SourceBehavior behavior) =>
            CreateForPackage(
                packageId,
                [behavior],
                useNuGetOrgEndpoint: true);

        public static HouseEnvironment CreateForPackage(
            string packageId,
            params SourceBehavior[] behaviors) =>
            CreateForPackage(
                packageId,
                behaviors,
                useNuGetOrgEndpoint: false);

        private static HouseEnvironment CreateForPackage(
            string? packageId,
            SourceBehavior[] behaviors,
            bool useNuGetOrgEndpoint)
        {
            PackageSource[] sources =
            [
                .. behaviors.Select((_, index) =>
                    new PackageSource(
                        $"source-{index + 1}",
                        useNuGetOrgEndpoint
                            ? "https://api.nuget.org/v3/index.json"
                            : $"https://source-{index + 1}.example/v3/index.json")),
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
                new FixedAuthorization(
                    authorization,
                    packageId),
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
        PackageSourceAuthorization authorization,
        string? expectedPackageId = PackageId)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            if (expectedPackageId is not null)
            {
                Assert.Equal(
                    expectedPackageId,
                    packageId,
                    ignoreCase: false);
            }
            return authorization;
        }
    }

    private sealed class DelayedEnumerationPackageStore : IPackageStore
    {
        private readonly IPackageContent _content;

        public DelayedEnumerationPackageStore(
            string producerKey,
            TimeSpan delay,
            params string[] entries)
        {
            _content = new DelayedEnumerationPackageContent(
                producerKey,
                delay,
                entries);
        }

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            allowedSourceKeys?.Contains(
                _content.ProducerKey,
                StringComparer.Ordinal) is true
                ? _content
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The delayed cached-content fixture must not download.");
    }

    private sealed class DelayedEnumerationPackageContent
        : IPackageContent
    {
        private readonly TimeSpan _delay;
        private readonly string[] _entries;
        private readonly byte[] _archive;

        public DelayedEnumerationPackageContent(
            string producerKey,
            TimeSpan delay,
            string[] entries)
        {
            ProducerKey = producerKey;
            _delay = delay;
            _entries = entries;
            _archive = TestPackageArchive.Create(entries);
        }

        public string? RootPath => null;

        public string? NupkgPath => null;

        public bool FromCache => true;

        public string ProducerKey { get; }

        public bool RequiresArchiveTreeMatch => false;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream)
        {
            stream = new MemoryStream(
                _archive,
                writable: false);
            return true;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public IEnumerable<string> EnumerateEntries()
        {
            Thread.Sleep(_delay);
            return _entries;
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

        public List<string> PayloadPackageIds { get; } = [];

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
            PayloadPackageIds.Add(packageId);
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

            byte[] archive = behavior.PayloadContentEntries is { } content
                ? TestPackageArchive.Create([.. content])
                : TestPackageArchive.Create(
                    [.. (behavior.PayloadEntries
                        ?? [$"lib/net10.0/{PackageId}.dll"])]);
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
