using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceReferenceDeclarationLoaderTests
{
    [Fact]
    public async Task ExactReferencesFromDistinctProducersRemainDistinctAndOutliveSourceSettlement()
    {
        byte[] image = WorkspaceReferenceTestData.Assembly(
            "Widget",
            "N",
            "Widget");
        KeyValuePair<string, byte[]>[] entries =
        [
            WorkspaceReferenceTestData.Entry(
                "ref/net11.0/Widget.dll",
                image),
        ];
        await using WorkspaceReferenceSourceFixture firstSource =
            WorkspaceReferenceSourceFixture.Create("reference-a", entries);
        await using WorkspaceReferenceSourceFixture secondSource =
            WorkspaceReferenceSourceFixture.Create("reference-b", entries);
        var workspace = new InspectionWorkspace();
        PackageReferencePopulationDemand population = ExactAssembly(image);

        WorkspaceDeclarationContext first =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                firstSource.Source,
                Coordinate(),
                population,
                Work(),
                firstSource.IssueOperation(
                    TestContext.Current.CancellationToken));
        WorkspaceDeclarationContext second =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                secondSource.Source,
                Coordinate(),
                population,
                Work(),
                secondSource.IssueOperation(
                    TestContext.Current.CancellationToken));

        await firstSource.AssertSettledAsync();
        await secondSource.AssertSettledAsync();
        Assert.Equal(1, firstSource.Client.PayloadRequests);
        Assert.Equal(1, secondSource.Client.PayloadRequests);

        WorkspaceDeclarationPopulation captured =
            Capture(workspace, first, second);
        Assert.True(captured.Receipt.IsRealizationComplete);
        Assert.Equal(2, captured.Receipt.Members.Length);
        WorkspaceDeclarationMember firstMember = captured.Receipt.Members[0];
        WorkspaceDeclarationMember secondMember = captured.Receipt.Members[1];
        Assert.Equal(firstMember.Coordinate, secondMember.Coordinate);
        Assert.Equal(firstMember.AssemblyIdentity, secondMember.AssemblyIdentity);
        Assert.NotSame(firstMember.Occurrence, secondMember.Occurrence);
        WorkspaceDeclarationOrigin.PlatformReference firstOrigin =
            ReferenceOrigin(firstMember);
        WorkspaceDeclarationOrigin.PlatformReference secondOrigin =
            ReferenceOrigin(secondMember);
        var firstRequest =
            Assert.IsType<WorkspaceDeclarationRequest.PlatformReference>(
                first.Receipt.Request);
        var secondRequest =
            Assert.IsType<WorkspaceDeclarationRequest.PlatformReference>(
                second.Receipt.Request);
        Assert.Equal(Coordinate().Target, firstRequest.Coordinate.Target);
        Assert.Equal(firstRequest.Coordinate, secondRequest.Coordinate);
        Assert.Same(population, firstRequest.Population);
        Assert.Same(population, secondRequest.Population);
        Assert.Equal("ref/net11.0/Widget.dll", firstOrigin.Path);
        Assert.Equal(firstOrigin.Path, secondOrigin.Path);
        Assert.Equal(Coordinate(), firstOrigin.Source.Coordinate);
        Assert.Equal(Coordinate(), secondOrigin.Source.Coordinate);
        Assert.NotEqual(
            firstOrigin.Source.Source.Producer.Key,
            secondOrigin.Source.Source.Producer.Key);
        Assert.Equal("reference-a", firstOrigin.Source.Authority.ToString());
        Assert.Equal("reference-b", secondOrigin.Source.Authority.ToString());
        Assert.Equal(PackagePayloadOrigin.Download, firstOrigin.Source.Origin);
        Assert.Equal(PackagePayloadOrigin.Download, secondOrigin.Source.Origin);
        AssertReferenceSelection(firstMember);
        AssertReferenceSelection(secondMember);

        foreach (WorkspaceDeclarationContext context in new[] { first, second })
        {
            AssemblyContextGroup group = Assert.IsType<AssemblyContextGroup>(
                context.Group);
            AssemblyContextParticipant participant =
                Assert.Single(group.Participants);
            var bytes = Assert.IsType<AssemblyImageAccessResult<int>.Available>(
                group.UseAssemblyImage(
                    participant.Assembly,
                    static view => view.Content.Length));
            Assert.Equal(image.Length, bytes.Value);
            var session = Assert.IsType<
                AssemblyImageAccessResult<AssemblyTypeDeclarationInventoryOutcome>.Available>(
                    group.UseAssemblySession(
                        participant.Assembly,
                        static value => value.TypeDeclarations()));
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(
                session.Value);
        }

        TypeDeclarationLocatorResult.Evaluated detached = Locate(captured);
        Assert.Equal(2, detached.Answers[0].Candidates.Length);
        AssemblyTypeDeclarationInventory firstInventory =
            ReadDeclarations(captured, firstMember);
        AssemblyTypeDeclarationInventory secondInventory =
            ReadDeclarations(captured, secondMember);

        Assert.True((await workspace.CloseAsync()).Succeeded);
        Assert.Equal(2, detached.Answers[0].Candidates.Length);
        Assert.NotEmpty(firstInventory.Declarations);
        Assert.NotEmpty(secondInventory.Declarations);
        Assert.Equal(
            [
                firstOrigin.Source.Source.Producer.Key,
                secondOrigin.Source.Source.Producer.Key,
            ],
            detached.Answers[0].Candidates.Select(candidate =>
                ReferenceOrigin(candidate.Observation).Source.Source.Producer.Key));
        Assert.Equal(
            WorkspaceDeclarationPopulationFailure.WorkspaceClosed,
            Assert.IsType<WorkspaceDeclarationInventoryOutcome.Unavailable>(
                captured.ReadDeclarations(
                    firstMember.Occurrence,
                    TestContext.Current.CancellationToken)).Failure);
    }

    [Fact]
    public async Task DiscoveredReferenceAppendMaintainsResidentLocatorAndPreservesEarlierEvidence()
    {
        byte[] image = WorkspaceReferenceTestData.Assembly(
            "Widget",
            "N",
            "Widget");
        KeyValuePair<string, byte[]>[] entries =
        [
            WorkspaceReferenceTestData.Entry(
                "ref/net11.0/Widget.dll",
                image),
        ];
        await using WorkspaceReferenceSourceFixture firstSource =
            WorkspaceReferenceSourceFixture.Create("append-a", entries);
        await using WorkspaceReferenceSourceFixture discoveredSource =
            WorkspaceReferenceSourceFixture.Create("append-b", entries);
        await using var workspace = new InspectionWorkspace();
        PackageReferencePopulationDemand population = ExactAssembly(image);
        WorkspaceDeclarationContext first =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                firstSource.Source,
                Coordinate(),
                population,
                Work(),
                firstSource.IssueOperation(
                    TestContext.Current.CancellationToken));
        AssemblyContextGroup unadmitted =
            CreateUnadmittedGroup(workspace, image);
        Assert.Single(unadmitted.Participants);
        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator();

        TypeDeclarationLocatorResult.Evaluated earlier =
            await ResidentFind(locator);
        Assert.Single(earlier.Population.Contexts);
        Assert.Single(earlier.Answers[0].Candidates);
        Assert.Equal(1, locator.InventoryReadCount);

        var discovered = Assert.IsType<
            PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded>(
                await discoveredSource.Source.DiscoverAsync(
                    new(
                        PlatformFamily.DotNetRuntime,
                        PlatformTargetFramework.Parse("net11.0"),
                        maxCandidates: 8),
                    discoveredSource.IssueOperation(
                        TestContext.Current.CancellationToken)));
        PackagePlatformTargetSelection selection =
            discovered.Value.SelectTarget(Coordinate().Target);
        WorkspaceDeclarationContext appended =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                discoveredSource.Source,
                selection,
                population,
                Work(),
                discoveredSource.IssueOperation(
                    TestContext.Current.CancellationToken));

        await locator.Maintenance.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Single(earlier.Population.Contexts);
        Assert.Single(earlier.Answers[0].Candidates);

        TypeDeclarationLocatorResult.Evaluated current =
            await ResidentFind(locator);
        Assert.Equal(2, current.Population.Contexts.Length);
        Assert.Equal(2, current.Answers[0].Candidates.Length);
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Same(
            first.Receipt.Members[0].Occurrence,
            current.Answers[0].Candidates[0].Observation.Occurrence);
        Assert.Same(
            appended.Receipt.Members[0].Occurrence,
            current.Answers[0].Candidates[1].Observation.Occurrence);

        WorkspaceReferenceSourceEvidence evidence =
            ReferenceOrigin(appended.Receipt.Members[0]).Source;
        Assert.Equal(
            PackageAcquisitionCandidateKind.Discovered,
            evidence.CandidateKind);
        Assert.Equal(
            PackageVersionDiscoveryContract.CompleteVersionEnumeration,
            evidence.DiscoveryContract);
        PackageCandidateObservation observation =
            Assert.Single(evidence.ReportingObservations);
        Assert.Equal(
            PackageDiscoveryContract.CompleteVersionEnumeration,
            observation.DiscoveryContract);
        Assert.Equal(
            PackageSourceCoordinate.Create(
                WorkspaceReferenceSourceFixture.RuntimePackageId,
                WorkspaceReferenceSourceFixture.Version),
            observation.Coordinate);
        Assert.Equal(evidence.Source, observation.Source);
        Assert.Equal("append-b", evidence.Authority.ToString());
        Assert.Equal(Coordinate(), evidence.Coordinate);
        Assert.Same(population, evidence.Population);

        WorkspaceDeclarationPopulation coldPopulation =
            Capture(workspace, first, appended);
        AssertEquivalent(Locate(coldPopulation), current);
        await firstSource.AssertSettledAsync();
        await discoveredSource.AssertSettledAsync();
    }

    [Fact]
    public async Task SourceFailureRemainsVisibleBesideHealthyReference()
    {
        byte[] image = WorkspaceReferenceTestData.Assembly(
            "Widget",
            "N",
            "Widget");
        KeyValuePair<string, byte[]>[] entries =
        [
            WorkspaceReferenceTestData.Entry(
                "ref/net11.0/Widget.dll",
                image),
        ];
        await using WorkspaceReferenceSourceFixture healthySource =
            WorkspaceReferenceSourceFixture.Create("healthy-reference", entries);
        await using WorkspaceReferenceSourceFixture deniedSource =
            WorkspaceReferenceSourceFixture.Create(
                "denied-reference",
                entries,
                denied: true);
        await using var workspace = new InspectionWorkspace();
        PackageReferencePopulationDemand population = ExactAssembly(image);
        WorkspaceDeclarationContext healthy =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                healthySource.Source,
                Coordinate(),
                population,
                Work(),
                healthySource.IssueOperation(
                    TestContext.Current.CancellationToken));
        WorkspaceDeclarationContext failed =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                deniedSource.Source,
                Coordinate(),
                population,
                Work(),
                deniedSource.IssueOperation(
                    TestContext.Current.CancellationToken));

        Assert.Null(failed.Group);
        Assert.False(failed.Receipt.IsRealized);
        Assert.Empty(failed.Receipt.Members);
        var sourceFailure = Assert.IsType<
            WorkspaceDeclarationFailure.ReferenceSource>(
                Assert.Single(failed.Receipt.Failures));
        Assert.Equal(
            WorkspaceReferenceSourceFailureKind.Unavailable,
            sourceFailure.Kind);
        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
            sourceFailure.Diagnostic.Kind);
        WorkspaceDeclarationPopulation populationReceipt =
            Capture(workspace, healthy, failed);
        Assert.False(populationReceipt.Receipt.IsRealizationComplete);
        Assert.Single(populationReceipt.Receipt.Members);

        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator();
        TypeDeclarationLocatorResult.Evaluated result =
            await ResidentFind(locator);
        Assert.Equal(2, result.Population.Contexts.Length);
        Assert.Single(result.Answers[0].Candidates);
        Assert.False(result.Answers[0].IsRealizationComplete);
        Assert.False(result.Answers[0].IsComplete);
        Assert.Equal(1, locator.InventoryReadCount);
        Assert.Equal(0, deniedSource.Client.PayloadRequests);
        await healthySource.AssertSettledAsync();
        await deniedSource.AssertSettledAsync();
    }

    [Fact]
    public async Task ZeroRetainedImageBudgetRejectsReferenceContextAtomically()
    {
        byte[] image = WorkspaceReferenceTestData.Assembly(
            "Widget",
            "N",
            "Widget");
        await using WorkspaceReferenceSourceFixture source =
            WorkspaceReferenceSourceFixture.Create(
                "bounded-reference",
                [
                    WorkspaceReferenceTestData.Entry(
                        "ref/net11.0/Widget.dll",
                        image),
                ]);
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                source.Source,
                Coordinate(),
                ExactAssembly(image),
                Work(),
                source.IssueOperation(
                    TestContext.Current.CancellationToken),
                new() { MaxRetainedImageBytes = 0 });

        Assert.Null(context.Group);
        Assert.False(context.Receipt.IsRealized);
        Assert.Empty(context.Receipt.Members);
        var failure = Assert.IsType<
            WorkspaceDeclarationFailure.ReferenceImage>(
                Assert.Single(context.Receipt.Failures));
        Assert.Equal("ref/net11.0/Widget.dll", failure.Path);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            failure.Failure.Kind);
        Assert.Equal(Coordinate(), failure.Source.Coordinate);
        Assert.Equal(
            source.Client.Source.Producer.Key,
            failure.Source.Source.Producer.Key);
        Assert.Equal(
            "bounded-reference",
            failure.Source.Authority.ToString());
        WorkspaceDeclarationPopulation captured =
            Capture(workspace, context);
        Assert.Empty(captured.Receipt.Members);
        Assert.False(captured.Receipt.IsRealizationComplete);
        TypeDeclarationLocatorResult.Evaluated result = Locate(captured);
        Assert.Empty(result.Answers[0].Candidates);
        Assert.False(result.Answers[0].IsRealizationComplete);
        await source.AssertSettledAsync();
    }

    [Fact]
    public async Task CancellationAndClosedWorkspaceReleasePackageOperations()
    {
        byte[] image = WorkspaceReferenceTestData.Assembly(
            "Widget",
            "N",
            "Widget");
        KeyValuePair<string, byte[]>[] entries =
        [
            WorkspaceReferenceTestData.Entry(
                "ref/net11.0/Widget.dll",
                image),
        ];
        var payloadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using WorkspaceReferenceSourceFixture cancellableSource =
            WorkspaceReferenceSourceFixture.Create(
                "cancelled-reference",
                entries,
                beforePayload: async cancellationToken =>
                {
                    payloadStarted.TrySetResult();
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                });
        await using (var workspace = new InspectionWorkspace())
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken))
        {
            Task<WorkspaceDeclarationContext> pending =
                WorkspaceReferenceDeclarationLoader.LoadAsync(
                    workspace,
                    cancellableSource.Source,
                    Coordinate(),
                    ExactAssembly(image),
                    Work(),
                    cancellableSource.IssueOperation(cancellation.Token));
            await payloadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending);
        }
        await cancellableSource.AssertSettledAsync();
        Assert.Equal(1, cancellableSource.Client.PayloadRequests);

        await using WorkspaceReferenceSourceFixture closedSource =
            WorkspaceReferenceSourceFixture.Create(
                "closed-reference",
                entries);
        var closedWorkspace = new InspectionWorkspace();
        Assert.True((await closedWorkspace.CloseAsync()).Succeeded);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            WorkspaceReferenceDeclarationLoader.LoadAsync(
                closedWorkspace,
                closedSource.Source,
                Coordinate(),
                ExactAssembly(image),
                Work(),
                closedSource.IssueOperation(
                    TestContext.Current.CancellationToken)));
        Assert.Equal(0, closedSource.Client.PayloadRequests);
        await closedSource.AssertSettledAsync();
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task RealPackageReferenceAppendAddsSecondJsonSerializerChoiceWithoutFindNetwork()
    {
        await using var workspace = new InspectionWorkspace();
        using var packageNetwork = new WorkspaceReferenceCountingHandler();
        using var packageClient = new HttpClient(
            packageNetwork,
            disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        WorkspaceDeclarationContext package =
            await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                new()
                {
                    Framework = "net10.0",
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            "System.Text.Json",
                            "10.0.0"),
                    ],
                },
                new()
                {
                    HttpClient = packageClient,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization(
                            [PackageSource.NuGetOrg]),
                    PackageStore = new InMemoryPackageStore(),
                },
                TestContext.Current.CancellationToken);
        WorkspaceDeclarationMember packageMember =
            Assert.Single(package.Receipt.Members);
        Assert.IsType<WorkspaceDeclarationOrigin.ContextLoad>(
            packageMember.Origin);

        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator();
        int beforeFirstFind = packageNetwork.RequestCount;
        TypeDeclarationLocatorResult.Evaluated first =
            await ResidentFind(
                locator,
                "System.Text.Json.JsonSerializer");
        Assert.Equal(beforeFirstFind, packageNetwork.RequestCount);
        Assert.Single(first.Answers[0].Candidates);
        Assert.IsType<ExactLibrarySourceCoordinate.Package>(
            first.Answers[0].Candidates[0].Coordinate);

        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        using var referenceNetwork =
            new WorkspaceReferenceCountingHandler();
        using IPackageSourceClient referenceClient =
            PackageSourceClientFactory.CreateGallery(
                authorization.Authorities[0].Association,
                referenceNetwork);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => referenceClient);
        var referenceStore = new InMemoryPackageStore();
        var referenceSource = new PackagePlatformSource(
            new WorkspaceReferenceAuthorization(authorization),
            new PackagePayloadAcquisitionPlan(
                (_, _) => referenceStore));
        var coordinate = new PackageReferencePackCoordinate(
            new PlatformFamilyTarget(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net10.0"),
                PlatformVersion.Parse("10.0.10")));
        WorkspaceDeclarationContext reference =
            await WorkspaceReferenceDeclarationLoader.LoadAsync(
                workspace,
                referenceSource,
                coordinate,
                new PackageReferencePopulationDemand.Assembly(
                    packageMember.AssemblyIdentity),
                new PackageReferenceWorkBudget(
                    maxAssemblies: 1,
                    maxBytes: 1024 * 1024),
                root.IssueOperationLease(
                    TestContext.Current.CancellationToken,
                    requestTimeout: TimeSpan.FromMinutes(2),
                    operationTimeout: TimeSpan.FromMinutes(2)));
        Assert.True(reference.Receipt.IsRealized);
        Assert.Single(reference.Receipt.Members);

        int afterReferenceLoad =
            packageNetwork.RequestCount + referenceNetwork.RequestCount;
        await locator.Maintenance.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            afterReferenceLoad,
            packageNetwork.RequestCount + referenceNetwork.RequestCount);
        Assert.Equal(2, locator.InventoryReadCount);

        TypeDeclarationLocatorResult.Evaluated current =
            await ResidentFind(
                locator,
                "System.Text.Json.JsonSerializer");
        Assert.Equal(
            afterReferenceLoad,
            packageNetwork.RequestCount + referenceNetwork.RequestCount);
        Assert.Equal(2, current.Answers[0].Candidates.Length);
        Assert.IsType<ExactLibrarySourceCoordinate.Package>(
            current.Answers[0].Candidates[0].Coordinate);
        Assert.IsType<WorkspaceDeclarationOrigin.ContextLoad>(
            current.Answers[0].Candidates[0].Observation.Origin);
        Assert.IsType<ExactLibrarySourceCoordinate.Platform>(
            current.Answers[0].Candidates[1].Coordinate);
        WorkspaceDeclarationOrigin.PlatformReference referenceOrigin =
            Assert.IsType<WorkspaceDeclarationOrigin.PlatformReference>(
                current.Answers[0].Candidates[1].Observation.Origin);
        Assert.Equal(
            "ref/net10.0/System.Text.Json.dll",
            referenceOrigin.Path);
        Assert.Equal(coordinate, referenceOrigin.Source.Coordinate);
        Assert.Equal(
            PackageSourceDisplay.ForDiagnostics(
                PackageSource.NuGetOrg).ToString(),
            referenceOrigin.Source.Authority.ToString());
        Assert.Single(first.Answers[0].Candidates);
        Assert.Single(first.Population.Contexts);
    }

    private static PackageReferencePackCoordinate Coordinate() =>
        new(
            new PlatformFamilyTarget(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse(
                    WorkspaceReferenceSourceFixture.TargetFramework),
                PlatformVersion.Parse(
                    WorkspaceReferenceSourceFixture.Version)));

    private static PackageReferencePopulationDemand ExactAssembly(
        byte[] image) =>
        new PackageReferencePopulationDemand.Assembly(
            WorkspaceReferenceTestData.Identity(image));

    private static PackageReferenceWorkBudget Work() =>
        new(maxAssemblies: 1, maxBytes: 1024 * 1024);

    private static WorkspaceDeclarationPopulation Capture(
        InspectionWorkspace workspace,
        params WorkspaceDeclarationContext[] contexts) =>
        Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
            workspace.CaptureDeclarationPopulation([.. contexts])).Population;

    private static WorkspaceDeclarationOrigin.PlatformReference ReferenceOrigin(
        WorkspaceDeclarationMember member) =>
        Assert.IsType<WorkspaceDeclarationOrigin.PlatformReference>(
            member.Origin);

    private static AssemblyTypeDeclarationInventory ReadDeclarations(
        WorkspaceDeclarationPopulation population,
        WorkspaceDeclarationMember member) =>
        Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(
            Assert.IsType<WorkspaceDeclarationInventoryOutcome.Inspected>(
                population.ReadDeclarations(
                    member.Occurrence,
                    TestContext.Current.CancellationToken)).Outcome).Inventory;

    private static TypeDeclarationLocatorResult.Evaluated Locate(
        WorkspaceDeclarationPopulation population,
        string pattern = "N.Widget") =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            TypeDeclarationLocatorQuery.Execute(
                population,
                [new TypeDeclarationLocatorRequest.Pattern(pattern)],
                cancellationToken:
                    TestContext.Current.CancellationToken));

    private static async Task<TypeDeclarationLocatorResult.Evaluated>
        ResidentFind(
            WorkspaceDeclarationLocator locator,
            string pattern = "N.Widget") =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.ExecuteAsync(
                [new TypeDeclarationLocatorRequest.Pattern(pattern)],
                cancellationToken:
                    TestContext.Current.CancellationToken));

    private static void AssertReferenceSelection(
        WorkspaceDeclarationMember member)
    {
        Assert.IsType<ExactLibrarySourceCoordinate.Platform>(
            member.Coordinate);
        var provenance =
            Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(
                member.Selection);
        Assert.Equal("Microsoft.NETCore.App", provenance.Framework);
        Assert.Equal(
            WorkspaceReferenceSourceFixture.Version,
            provenance.FrameworkVersion);
        Assert.Equal("NuGet reference pack", provenance.ResolverSource);
    }

    private static AssemblyContextGroup CreateUnadmittedGroup(
        InspectionWorkspace workspace,
        byte[] image)
    {
        var assembly = ResolvedAssemblyReference.Create(
            WorkspaceReferenceTestData.Identity(image),
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local(
                "unadmitted reference fixture"));
        return workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    assembly,
                    NoResolverAssemblyBindingPolicy.Instance),
            ]);
    }

    private static void AssertEquivalent(
        TypeDeclarationLocatorResult.Evaluated expected,
        TypeDeclarationLocatorResult.Evaluated actual)
    {
        Assert.Equal(
            expected.Population.Contexts,
            actual.Population.Contexts);
        Assert.Equal(expected.IncludeAll, actual.IncludeAll);
        Assert.Equal(expected.Members, actual.Members);
        Assert.Equal(expected.Answers.Length, actual.Answers.Length);
        foreach ((TypeDeclarationLocatorAnswer left,
            TypeDeclarationLocatorAnswer right) in
            expected.Answers.Zip(actual.Answers))
        {
            Assert.Equal(left.Request, right.Request);
            Assert.Equal(
                left.IsRealizationComplete,
                right.IsRealizationComplete);
            Assert.Equal(
                left.IsEvaluationComplete,
                right.IsEvaluationComplete);
            Assert.Equal(
                left.Candidates.Select(candidate =>
                    (
                        candidate.Coordinate,
                        candidate.Name,
                        candidate.Kind,
                        candidate.Observation)),
                right.Candidates.Select(candidate =>
                    (
                        candidate.Coordinate,
                        candidate.Name,
                        candidate.Kind,
                        candidate.Observation)));
        }
    }
}
