using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformHouseAdapterTests
{
    [Fact]
    public async Task DiscoveryProducesPairedResourceFreeContribution()
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions:
                    [
                        "11.0.0-preview.7",
                        PackagePlatformTestEnvironment.Version,
                    ]),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<
            PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded>(
                await adapter.DiscoverTargetsAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));
        var contribution = Assert.IsType<PlatformSourceContribution.TargetDiscovery>(
            result.Contribution);

        Assert.Same(adapter.TargetDiscovery, contribution.Capability);
        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Equal(PlatformSourceFacet.TargetDiscovery, contribution.Facet);
        Assert.Equal(PlatformSourceContributionKind.TargetDiscovery, contribution.Kind);
        Assert.Equal(
            result.Value.Targets.Select(static target => target.Target),
            contribution.Candidates);
        Assert.Equal(result.Value.Generation.Name, contribution.Generation.Name);
        Assert.All(
            contribution.Candidates,
            candidate =>
            {
                Assert.Equal(PlatformFamily.DotNetRuntime, candidate.Family);
                Assert.Equal("net11.0", candidate.TargetFramework.ToString());
            });
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        FamilyDefaultDiscoveryUsesFallbackFrameworkAndPreparesAssociations()
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions:
                    [
                        "10.0.12",
                        "10.0.13-preview.1",
                        "11.0.0",
                    ]),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = FamilyDefaultRequest(
            adapter,
            TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            PackagePlatformHouseResult<
                PackagePlatformTargetInventory>.Succeeded>(
                    await adapter.DiscoverTargetsAsync(
                        request,
                        environment.IssueOperation(
                            TestContext.Current.CancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var attempt =
            Assert.IsType<PlatformTargetDiscoveryAttempt.Succeeded>(
                PackagePlatformTargetDiscovery.PrepareAttempt(
                    succeeded));

        Assert.Equal(
            ["10.0.12", "10.0.13-preview.1"],
            attempt.Candidates.Select(
                candidate => candidate.Target.Version.Value));
        Assert.All(
            attempt.Candidates,
            candidate => Assert.IsType<
                PlatformTargetDiscoveryCandidate<
                    PackagePlatformTargetDiscoveryAssociation>>(
                        candidate));
        bool operationIssued = false;
        PlatformTargetDiscoverySource source =
            PackagePlatformTargetDiscovery.CreateSource(
                adapter,
                (current, remainingWork) =>
                {
                    operationIssued = true;
                    return environment.IssueOperation(
                        current.CancellationToken,
                        operationTimeout:
                            remainingWork.MaxDuration);
                });
        Assert.Same(adapter.TargetDiscovery, source.Capability);
        Assert.False(operationIssued);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task DiscoveryRejectsUnauthorizedCapabilityWithoutSourceWork()
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken,
            authorizedCapability: PlatformSourceCapabilityIdentity.Create("other"));

        var result = Assert.IsType<
            PackagePlatformHouseResult<PackagePlatformTargetInventory>.NotSucceeded>(
                await adapter.DiscoverTargetsAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.InvalidSelection, result.Diagnostic.Kind);
        Assert.Equal(PlatformSourceContributionKind.Rejected, result.Contribution.Kind);
        Assert.Same(adapter.TargetDiscovery, result.Contribution.Capability);
        Assert.Same(request.Snapshot, result.Contribution.Request);
        Assert.Equal(0, environment.Clients[0].VersionRequests);
        await environment.AssertSettledAsync();
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 0)]
    public async Task DiscoveryIntersectsHouseAndSelectingCandidateLimits(
        int houseMaximum,
        int selectingMaximum)
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: ["11.0.0", "11.0.1"]),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken,
            maxTargetCandidates: houseMaximum,
            selectingMaxCandidates: selectingMaximum);

        var result = Assert.IsType<
            PackagePlatformHouseResult<PackagePlatformTargetInventory>.NotSucceeded>(
                await adapter.DiscoverTargetsAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.WorkLimitExceeded, result.Diagnostic.Kind);
        Assert.Equal(PlatformSourceContributionKind.Incomplete, result.Contribution.Kind);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task DiscoveryZeroSourceOrDurationAllowanceStopsBeforeSourceWork()
    {
        foreach ((int operations, TimeSpan duration) in new[]
        {
            (0, TimeSpan.FromSeconds(1)),
            (1, TimeSpan.Zero),
        })
        {
            await using PackagePlatformTestEnvironment environment =
                PackagePlatformTestEnvironment.Create(
                [
                    TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
                ]);
            PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
            PlatformHouseRequest request = SelectingRequest(
                adapter,
                TestContext.Current.CancellationToken,
                maxSourceOperations: operations,
                maxDuration: duration);

            var result = Assert.IsType<
                PackagePlatformHouseResult<PackagePlatformTargetInventory>.NotSucceeded>(
                    await adapter.DiscoverTargetsAsync(
                        request,
                        environment.IssueOperation(
                            TestContext.Current.CancellationToken,
                            operationTimeout: TimeSpan.FromSeconds(1))));

            Assert.Equal(PackagePlatformSourceDiagnosticKind.WorkLimitExceeded, result.Diagnostic.Kind);
            Assert.Equal(PlatformSourceContributionKind.Incomplete, result.Contribution.Kind);
            Assert.Equal(0, environment.Clients[0].VersionRequests);
            await environment.AssertSettledAsync();
        }
    }

    [Fact]
    public async Task DiscoveryRequiresMatchingOperationTokenAndBoundedDeadline()
    {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var otherCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using PackagePlatformTestEnvironment tokenMismatch =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter firstAdapter = CreateAdapter(tokenMismatch);
        PlatformHouseRequest firstRequest = SelectingRequest(
            firstAdapter,
            requestCancellation.Token,
            maxDuration: TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => firstAdapter.DiscoverTargetsAsync(
                firstRequest,
                tokenMismatch.IssueOperation(
                    otherCancellation.Token,
                    operationTimeout: firstRequest.Work.MaxDuration)));
        Assert.Equal(0, tokenMismatch.Clients[0].VersionRequests);
        await tokenMismatch.AssertSettledAsync();

        await using PackagePlatformTestEnvironment deadlineMismatch =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter secondAdapter = CreateAdapter(deadlineMismatch);
        PlatformHouseRequest secondRequest = SelectingRequest(
            secondAdapter,
            TestContext.Current.CancellationToken,
            maxDuration: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<ArgumentException>(
            () => secondAdapter.DiscoverTargetsAsync(
                secondRequest,
                deadlineMismatch.IssueOperation(
                    TestContext.Current.CancellationToken,
                    operationTimeout: TimeSpan.FromSeconds(1))));
        Assert.Equal(0, deadlineMismatch.Clients[0].VersionRequests);
        await deadlineMismatch.AssertSettledAsync();
    }

    [Fact]
    public async Task DiscoveryPreservesCancellationBeforeWorkLimitOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            cancellation.Token,
            maxSourceOperations: 0,
            maxDuration: TimeSpan.Zero);

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => adapter.DiscoverTargetsAsync(
                    request,
                    environment.IssueOperation(
                        cancellation.Token,
                        operationTimeout: TimeSpan.FromSeconds(1))));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, environment.Clients[0].VersionRequests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task TypedPackageFailureMapsToHouseAndRemainsAttached()
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versionFailure: PackageSourceFailureKind.Transport),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<
            PackagePlatformHouseResult<PackagePlatformTargetInventory>.NotSucceeded>(
                await adapter.DiscoverTargetsAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.PackageFailure, result.Diagnostic.Kind);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            Assert.Single(result.Diagnostic.PackageFailures).Kind);
        Assert.Equal(PlatformSourceContributionKind.Failed, result.Contribution.Kind);
        Assert.Same(request.Snapshot, result.Contribution.Request);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task DiscoveryRejectsExactTargetDemandAndReleasesOperation()
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest selecting = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken);
        PlatformHouseRequest exact = new(
            selecting.Identity,
            new PlatformTargetDemand.Exact(
                new PlatformFamilyTarget(
                    PlatformFamily.DotNetRuntime,
                    PlatformTargetFramework.Parse("net11.0"),
                    PlatformVersion.Parse(PackagePlatformTestEnvironment.Version))),
            selecting.Origin,
            selecting.Operation,
            selecting.Sources,
            selecting.Work,
            selecting.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.DiscoverTargetsAsync(
                exact,
                environment.IssueOperation(
                    TestContext.Current.CancellationToken,
                    operationTimeout: exact.Work.MaxDuration)));

        Assert.Equal(0, environment.Clients[0].VersionRequests);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ExactReferenceRealizationContributesCorrespondingSourceEvidence()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Runtime.dll",
                            image),
                    ]),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformFamilyTarget target = Target();
        PlatformHouseRequest request = ExactRequest(
            adapter,
            target,
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.Succeeded>(
                await adapter.RealizeReferenceAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));
        var contribution = Assert.IsType<PlatformSourceContribution.Realization>(
            result.Contribution);

        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Same(adapter.ReferenceRealization, contribution.Capability);
        Assert.Equal(PlatformSourceFacet.Reference, contribution.Facet);
        Assert.Equal(PlatformViewDemand.Reference, contribution.View);
        Assert.Same(target, contribution.Target);
        Assert.Same(
            ((PlatformHouseOperationSnapshot.Realize)request.Snapshot.Operation).Population,
            contribution.Population);
        Assert.Equal(result.Value.Generation.Name, contribution.Generation.Name);
        Assert.Equal(PackageAcquisitionCandidateKind.CallerPinned, result.Value.Candidate.Kind);
        PackageReferenceLibrary library = Assert.Single(result.Value.Libraries);
        Assert.Equal(image, await PackagePlatformTestData.ReadAllAsync(library));

        await environment.AssertSettledAsync();
        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Equal(image, await PackagePlatformTestData.ReadAllAsync(library));
    }

    [Fact]
    public async Task DiscoverySelectedReferencePreservesExactPairedSourceSelection()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: [PackagePlatformTestEnvironment.Version],
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Runtime.dll",
                            image),
                    ]),
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    versions: ["11.0.0-preview.1"]),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken,
            authorizeReference: true,
            maxSourceOperations: 2);
        var discovery = Assert.IsType<
            PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded>(
                await adapter.DiscoverTargetsAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));
        PackagePlatformTargetSelection selection =
            discovery.Value.SelectTarget(Target());

        var result = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.Succeeded>(
                await adapter.RealizeReferenceAsync(
                    request,
                    discovery,
                    selection,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));

        Assert.Same(selection.Candidate, result.Value.Candidate);
        Assert.Single(selection.Candidate.Authorities);
        Assert.Same(environment.Clients[0].Source, result.Value.Source);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        Assert.Equal(0, environment.Clients[1].PayloadRequests);
        var contribution = Assert.IsType<PlatformSourceContribution.Realization>(
            result.Contribution);
        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Equal(selection.Target, contribution.Target);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task SelectingReferenceRequiresLiveDiscoveryAndItsExactInventorySelection()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        TestSourceBehavior behavior = TestSourceBehavior.Create(
            PackagePlatformTestEnvironment.RuntimePackageId,
            versions: [PackagePlatformTestEnvironment.Version],
            entries:
            [
                PackagePlatformTestData.Entry("ref/net11.0/System.Runtime.dll", image),
            ]);
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create([behavior]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken,
            authorizeReference: true,
            maxSourceOperations: 2);

        await Assert.ThrowsAsync<ArgumentException>(
            () => adapter.RealizeReferenceAsync(
                request,
                environment.IssueOperation(
                    TestContext.Current.CancellationToken,
                    operationTimeout: request.Work.MaxDuration)));
        Assert.Equal(0, environment.Clients[0].PayloadRequests);

        var discovery = Assert.IsType<
            PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded>(
                await adapter.DiscoverTargetsAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));
        PlatformHouseRequest otherRequest = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken,
            authorizeReference: true,
            maxSourceOperations: 2);
        var wrongRequest = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.NotSucceeded>(
                await adapter.RealizeReferenceAsync(
                    otherRequest,
                    discovery,
                    discovery.Value.SelectTarget(Target()),
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: otherRequest.Work.MaxDuration)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.InvalidSelection, wrongRequest.Diagnostic.Kind);
        Assert.Equal(PlatformSourceContributionKind.Rejected, wrongRequest.Contribution.Kind);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);

        await using PackagePlatformTestEnvironment foreignEnvironment =
            PackagePlatformTestEnvironment.Create([behavior]);
        PackagePlatformTargetSelection foreignSelection = Assert.Single(
            Assert.IsType<
                PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(
                    await foreignEnvironment.CreateSource().DiscoverAsync(
                        new(
                            PlatformFamily.DotNetRuntime,
                            PlatformTargetFramework.Parse("net11.0"),
                            maxCandidates: 8),
                        foreignEnvironment.IssueOperation(
                            TestContext.Current.CancellationToken))).Value.Targets);
        var wrongSelection = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.NotSucceeded>(
                await adapter.RealizeReferenceAsync(
                    request,
                    discovery,
                    foreignSelection,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.InvalidSelection, wrongSelection.Diagnostic.Kind);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        Assert.Equal(0, foreignEnvironment.Clients[0].PayloadRequests);
        await environment.AssertSettledAsync();
        await foreignEnvironment.AssertSettledAsync();
    }

    [Fact]
    public async Task ReferenceRejectsUnauthorizedOpaqueAndImplementationRequestsBeforeSourceWork()
    {
        await using PackagePlatformTestEnvironment unauthorized =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter firstAdapter = CreateAdapter(unauthorized);
        PlatformHouseRequest unauthorizedRequest = ExactRequest(
            firstAdapter,
            Target(),
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken,
            authorizedCapability: PlatformSourceCapabilityIdentity.Create("other"));
        await AssertRejectedWithoutPayloadAsync(
            firstAdapter,
            unauthorized,
            unauthorizedRequest);

        await using PackagePlatformTestEnvironment opaque =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter secondAdapter = CreateAdapter(opaque);
        PlatformHouseRequest opaqueRequest = ExactRequest(
            secondAdapter,
            Target(),
            new PlatformPopulationDemand.Library(
                new PlatformLibraryDemand.PlatformLibrary(
                    PlatformLibraryIdentityAuthority.Create("test").Issue("opaque"))),
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken);
        await AssertRejectedWithoutPayloadAsync(secondAdapter, opaque, opaqueRequest);

        await using PackagePlatformTestEnvironment implementation =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter thirdAdapter = CreateAdapter(implementation);
        PlatformHouseRequest implementationRequest = ExactRequest(
            thirdAdapter,
            Target(),
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Implementation,
            TestContext.Current.CancellationToken);
        await AssertRejectedWithoutPayloadAsync(
            thirdAdapter,
            implementation,
            implementationRequest);
    }

    [Fact]
    public async Task ExactAssemblyReferenceUsesHouseIdentityAndRequestWorkLimits()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        AssemblyReferenceIdentity identity = PackagePlatformTestData.Identity(image);
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Runtime.dll",
                            image),
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Console.dll",
                            PackagePlatformTestData.Assembly("System.Console")),
                    ]),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest exact = ExactRequest(
            adapter,
            Target(),
            new PlatformPopulationDemand.Library(
                new PlatformLibraryDemand.Assembly(identity)),
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken,
            maxAssemblies: 1,
            maxBytes: image.LongLength);

        var succeeded = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.Succeeded>(
                await adapter.RealizeReferenceAsync(
                    exact,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: exact.Work.MaxDuration)));
        Assert.Equal(identity, Assert.Single(succeeded.Value.Libraries).Identity);
        var contribution = Assert.IsType<PlatformSourceContribution.Realization>(
            succeeded.Contribution);
        Assert.Same(
            ((PlatformHouseOperationSnapshot.Realize)exact.Snapshot.Operation).Population,
            contribution.Population);
        Assert.Equal(
            PlatformSourceContributionCompleteness.Authoritative,
            contribution.RealizationCompleteness);
        await environment.AssertSettledAsync();

        await using PackagePlatformTestEnvironment noWork =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter noWorkAdapter = CreateAdapter(noWork);
        PlatformHouseRequest noWorkRequest = ExactRequest(
            noWorkAdapter,
            Target(),
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken,
            maxSourceOperations: 0);
        var incomplete = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.NotSucceeded>(
                await noWorkAdapter.RealizeReferenceAsync(
                    noWorkRequest,
                    noWork.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: noWorkRequest.Work.MaxDuration)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.WorkLimitExceeded, incomplete.Diagnostic.Kind);
        Assert.Equal(PlatformSourceContributionKind.Incomplete, incomplete.Contribution.Kind);
        Assert.Equal(0, noWork.Clients[0].PayloadRequests);
        await noWork.AssertSettledAsync();
    }

    [Fact]
    public async Task ReferenceRequiresMatchingOperationTokenAndBoundedDeadline()
    {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var otherCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using PackagePlatformTestEnvironment tokenMismatch =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter firstAdapter = CreateAdapter(tokenMismatch);
        PlatformHouseRequest firstRequest = ExactRequest(
            firstAdapter,
            Target(),
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Reference,
            requestCancellation.Token,
            maxDuration: TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ArgumentException>(
            () => firstAdapter.RealizeReferenceAsync(
                firstRequest,
                tokenMismatch.IssueOperation(
                    otherCancellation.Token,
                    operationTimeout: firstRequest.Work.MaxDuration)));
        await tokenMismatch.AssertSettledAsync();

        await using PackagePlatformTestEnvironment deadlineMismatch =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(PackagePlatformTestEnvironment.RuntimePackageId),
            ]);
        PackagePlatformHouseAdapter secondAdapter = CreateAdapter(deadlineMismatch);
        PlatformHouseRequest secondRequest = ExactRequest(
            secondAdapter,
            Target(),
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken,
            maxDuration: TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<ArgumentException>(
            () => secondAdapter.RealizeReferenceAsync(
                secondRequest,
                deadlineMismatch.IssueOperation(
                    TestContext.Current.CancellationToken,
                    operationTimeout: TimeSpan.FromSeconds(1))));
        await deadlineMismatch.AssertSettledAsync();
    }

    [Fact]
    public async Task ReferencePackageFailureMapsToHouseAndRetainsPackageEvidence()
    {
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    payloadFailure: PackageSourceFailureKind.Transport),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = ExactRequest(
            adapter,
            Target(),
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Reference,
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.NotSucceeded>(
                await adapter.RealizeReferenceAsync(
                    request,
                    environment.IssueOperation(
                        TestContext.Current.CancellationToken,
                        operationTimeout: request.Work.MaxDuration)));

        Assert.Equal(PackagePlatformSourceDiagnosticKind.PackageUnavailable, result.Diagnostic.Kind);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            Assert.Single(result.Diagnostic.PackageFailures).Kind);
        Assert.Equal(PlatformSourceContributionKind.Failed, result.Contribution.Kind);
        Assert.Equal(Target(), result.Contribution.ExactTarget);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ExactImplementationRealizationContributesRuntimePackEvidence()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    entries: PackagePlatformTestData.RuntimePackEntries(
                        "Microsoft.NETCore.App",
                        PackagePlatformTestData.RuntimeConfiguration(),
                        PackagePlatformTestData.DependencyManifest(
                            "System.Runtime.dll"),
                        ("System.Runtime.dll", image))),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = ExactImplementationRequest(
            adapter,
            new PlatformPopulationDemand.CompletePopulation(),
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            request.CancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var contribution =
            Assert.IsType<PlatformSourceContribution.Realization>(
                result.Contribution);

        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Same(
            adapter.ImplementationRealization,
            contribution.Capability);
        Assert.Equal(
            PlatformSourceFacet.Implementation,
            contribution.Facet);
        Assert.Equal(
            PlatformViewDemand.Implementation,
            contribution.View);
        Assert.Equal(Target(), contribution.Target);
        Assert.Contains(
            "linux-x64",
            contribution.Coordinate.Name,
            StringComparison.Ordinal);
        Assert.Equal(
            "System.Runtime",
            Assert.Single(result.Value.Libraries).Identity.Name);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ImplementationLibraryDemandRequiresExactClosureMember()
    {
        byte[] image = PackagePlatformTestData.Assembly("System.Runtime");
        byte[] runtimeConfiguration =
            PackagePlatformTestData.RuntimeConfiguration();
        byte[] dependencyManifest =
            PackagePlatformTestData.DependencyManifest(
                "System.Runtime.dll");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    payloadFailure:
                        PackageSourceFailureKind.Transport),
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    entries: PackagePlatformTestData.RuntimePackEntries(
                        "Microsoft.NETCore.App",
                        runtimeConfiguration,
                        dependencyManifest,
                        ("System.Runtime.dll", image))),
            ]);
        PackagePlatformHouseAdapter adapter = CreateAdapter(environment);
        PlatformHouseRequest request = ExactImplementationRequest(
            adapter,
            new PlatformPopulationDemand.Library(
                new PlatformLibraryDemand.Assembly(
                    new AssemblyReferenceIdentity(
                        "Missing",
                        new Version(1, 0, 0, 0),
                        null,
                        null))),
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.NotSucceeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            request.CancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.MemberUnavailable,
            result.Diagnostic.Kind);
        Assert.Equal(
            PackageAuthorityFailureKind.Transport,
            Assert.Single(result.Diagnostic.PackageFailures).Kind);
        Assert.Equal(
            PlatformSourceContributionKind.Unavailable,
            result.Contribution.Kind);
        Assert.NotNull(result.SourceWork);
        Assert.Equal(1, result.SourceWork.Assemblies);
        Assert.Equal(
            image.LongLength
            + runtimeConfiguration.LongLength
            + dependencyManifest.LongLength,
            result.SourceWork.Bytes);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ImplementationRejectsUnauthorizedRidAndReferenceOnlyDemandBeforeSourceWork()
    {
        foreach (string scenario in new[]
        {
            "unauthorized",
            "rid",
            "reference",
        })
        {
            await using PackagePlatformTestEnvironment environment =
                PackagePlatformTestEnvironment.Create(
                [
                    TestSourceBehavior.Create(
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId),
                ]);
            PackagePlatformHouseAdapter adapter =
                CreateAdapter(environment);
            PlatformHouseRequest request = ExactImplementationRequest(
                adapter,
                new PlatformPopulationDemand.CompletePopulation(),
                TestContext.Current.CancellationToken,
                authorizedCapability:
                    scenario == "unauthorized"
                        ? PlatformSourceCapabilityIdentity.Create("other")
                        : null,
                view:
                    scenario == "reference"
                        ? PlatformViewDemand.Reference
                        : PlatformViewDemand.Implementation);

            var result = Assert.IsType<
                PackagePlatformHouseResult<
                    PackageImplementationRealization>.NotSucceeded>(
                        await adapter.RealizeImplementationAsync(
                            request,
                            scenario == "rid"
                                ? "linux/x64"
                                : "linux-x64",
                            environment.IssueOperation(
                                request.CancellationToken,
                                operationTimeout:
                                    request.Work.MaxDuration)));

            Assert.Equal(
                PackagePlatformSourceDiagnosticKind.InvalidSelection,
                result.Diagnostic.Kind);
            Assert.Equal(
                PlatformSourceContributionKind.Rejected,
                result.Contribution.Kind);
            Assert.Equal(0, environment.Clients[0].PayloadRequests);
            await environment.AssertSettledAsync();
        }
    }

    private static PackagePlatformHouseAdapter CreateAdapter(
        PackagePlatformTestEnvironment environment) =>
        new(environment.CreateSource(), "package-test");

    private static PlatformHouseRequest SelectingRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken,
        PlatformSourceCapabilityIdentity? authorizedCapability = null,
        bool authorizeReference = false,
        int maxSourceOperations = 1,
        int maxTargetCandidates = 8,
        int selectingMaxCandidates = 8,
        TimeSpan? maxDuration = null) =>
        new(
            PlatformHouseRequestIdentity.Create("package-discovery"),
            new PlatformTargetDemand.Selecting(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net11.0"),
                new PlatformVersionSelectionDemand.Requirement(
                    PlatformVersionRequirementIdentity.Create("net11")),
                [adapter.TargetDiscovery],
                new PlatformTargetDiscoveryBudget(
                    selectingMaxCandidates,
                    maxComparisons: 16)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("package-test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            Plan(
                adapter,
                authorizedCapability ?? adapter.TargetDiscovery,
                authorizeReference),
            new PlatformHouseWorkBudget(
                maxSourceOperations,
                maxTargetCandidates,
                maxAssemblies: 8,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 16 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration ?? TimeSpan.FromSeconds(30)),
            cancellationToken);

    private static PlatformHouseRequest FamilyDefaultRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken)
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-preferred");
        var preferred = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.AllFrameworks(),
            [installed]);
        var fallback = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.ExactFramework(
                PlatformTargetFramework.Parse("net10.0")),
            [adapter.TargetDiscovery]);
        return new(
            PlatformHouseRequestIdentity.Create(
                "package-family-default-discovery"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred,
                    fallback),
                new PlatformTargetDiscoveryBudget(8, 16)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("package-test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("package-plan"),
                PlatformSourcePolicyGeneration.Create("package-policy"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Precedence,
                        [installed, adapter.TargetDiscovery]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 2,
                maxTargetCandidates: 8,
                maxAssemblies: 8,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 16 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    private static PlatformHouseRequest ExactRequest(
        PackagePlatformHouseAdapter adapter,
        PlatformFamilyTarget target,
        PlatformPopulationDemand population,
        PlatformViewDemand view,
        CancellationToken cancellationToken,
        PlatformSourceCapabilityIdentity? authorizedCapability = null,
        int maxSourceOperations = 1,
        int maxAssemblies = 8,
        long maxBytes = 16 * 1024 * 1024,
        TimeSpan? maxDuration = null) =>
        new(
            PlatformHouseRequestIdentity.Create("package-reference"),
            new PlatformTargetDemand.Exact(target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("package-test")),
            new PlatformHouseOperation.Realize(population, view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("package-plan"),
                PlatformSourcePolicyGeneration.Create("package-policy"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [authorizedCapability ?? adapter.ReferenceRealization]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations,
                maxTargetCandidates: 0,
                maxAssemblies,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes,
                maxForwardingHops: 0,
                maxDuration ?? TimeSpan.FromSeconds(30)),
            cancellationToken);

    private static PlatformHouseRequest ExactImplementationRequest(
        PackagePlatformHouseAdapter adapter,
        PlatformPopulationDemand population,
        CancellationToken cancellationToken,
        PlatformSourceCapabilityIdentity? authorizedCapability = null,
        PlatformViewDemand view =
            PlatformViewDemand.Implementation) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "package-implementation"),
            new PlatformTargetDemand.Exact(Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "package-test")),
            new PlatformHouseOperation.Realize(population, view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("package-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "package-policy"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [
                            authorizedCapability
                                ?? adapter.ImplementationRealization,
                        ]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 1,
                maxTargetCandidates: 0,
                maxAssemblies: 512,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 64 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    private static PlatformSourcePlan Plan(
        PackagePlatformHouseAdapter adapter,
        PlatformSourceCapabilityIdentity targetDiscovery,
        bool authorizeReference)
    {
        var selections = new List<PlatformSourceSelection>
        {
            new(
                PlatformSourceFacet.TargetDiscovery,
                PlatformSourceSelectionMode.Precedence,
                [targetDiscovery]),
        };
        if (authorizeReference)
        {
            selections.Add(
                new(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ReferenceRealization]));
        }
        return new(
            PlatformSourcePlanIdentity.Create("package-plan"),
            PlatformSourcePolicyGeneration.Create("package-policy"),
            selections);
    }

    private static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(PackagePlatformTestEnvironment.Version));

    private static async Task AssertRejectedWithoutPayloadAsync(
        PackagePlatformHouseAdapter adapter,
        PackagePlatformTestEnvironment environment,
        PlatformHouseRequest request)
    {
        var rejected = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.NotSucceeded>(
                await adapter.RealizeReferenceAsync(
                    request,
                    environment.IssueOperation(
                        request.CancellationToken,
                        operationTimeout: request.Work.MaxDuration == TimeSpan.Zero
                            ? TimeSpan.FromSeconds(1)
                            : request.Work.MaxDuration)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.InvalidSelection, rejected.Diagnostic.Kind);
        Assert.Equal(PlatformSourceContributionKind.Rejected, rejected.Contribution.Kind);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertSettledAsync();
    }
}
