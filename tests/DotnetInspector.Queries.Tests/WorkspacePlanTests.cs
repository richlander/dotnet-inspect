using System.Collections.Immutable;
using System.Net;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspacePlanTests
{
    [Fact]
    public async Task EmptyPlanIsReusableWithoutSharingLiveIdentity()
    {
        WorkspacePlan plan = new();
        Assert.Empty(plan.Registrations);
        Assert.Empty(plan.Contexts);
        Assert.Empty(WorkspacePlan.Empty.Registrations);
        Assert.Empty(WorkspacePlan.Empty.Contexts);
        await using InspectionWorkspace first = WorkspaceRegistrationConsumer.Create(plan);
        await using InspectionWorkspace second = WorkspaceRegistrationConsumer.Create(plan);

        WorkspaceRegistrationRevision firstRevision = Current(first);
        WorkspaceRegistrationRevision secondRevision = Current(second);
        WorkspaceRegistrationObservation firstObservation =
            WorkspaceRegistrationConsumer.Observe(first);
        Assert.Same(plan, firstRevision.Plan);
        Assert.Same(plan, secondRevision.Plan);
        Assert.Equal(plan.Contexts, firstObservation.Contexts);
        Assert.NotSame(firstRevision.Workspace, secondRevision.Workspace);
        Assert.NotSame(firstRevision.Identity, secondRevision.Identity);
    }

    [Fact]
    public async Task PublicConsumerRetainsExactOrderedOwnerValues()
    {
        var library = new WorkspaceRegistration.ExactLibrary(
            WorkspaceRegistrationTestData.RealPackageSystemTextJson());
        var prefix = new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."));
        var ecosystem = new WorkspaceRegistration.Ecosystem(Platform());
        ImmutableArray<WorkspaceRegistration> registrations = [library, prefix, ecosystem];
        WorkspacePlan plan = WorkspaceRegistrationConsumer.CreatePlan(registrations);

        Assert.Equal(registrations, plan.Registrations);
        await using InspectionWorkspace workspace = WorkspaceRegistrationConsumer.Create(plan);
        WorkspaceRegistrationObservation observed = WorkspaceRegistrationConsumer.Observe(workspace);
        Assert.Same(plan, observed.Revision.Plan);
        Assert.Equal(plan.Registrations, observed.Revision.Registrations);
        Assert.Same(library.Coordinate, Assert.Single(observed.ExactLibraries));
        Assert.Same(prefix.Prefix, Assert.Single(observed.PackagePrefixes));
        Assert.Same(ecosystem.Declaration, Assert.Single(observed.Ecosystems));
        Assert.Equal("System.Text.Json", library.Coordinate.LibraryIdentity.Identity.Name);

        var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync());
        Assert.Empty(scope.Snapshot.Packages);
    }

    [Fact]
    public async Task ContextBearingPlanIsReusableAcrossIndependentOwnersAndAfterClose()
    {
        WorkspacePlan plan = WorkspaceRegistrationConsumer.CreatePlan(
            [],
            RealContexts());
        var first = WorkspaceRegistrationConsumer.Create(plan);
        await using InspectionWorkspace second =
            WorkspaceRegistrationConsumer.Create(plan);

        WorkspaceRegistrationRevision firstRevision = Current(first);
        WorkspaceRegistrationRevision secondRevision = Current(second);
        Assert.Same(plan, firstRevision.Plan);
        Assert.Same(plan, secondRevision.Plan);
        Assert.NotSame(firstRevision.Workspace, secondRevision.Workspace);
        Assert.NotSame(firstRevision.Identity, secondRevision.Identity);

        await first.DisposeAsync();
        await using InspectionWorkspace third =
            WorkspaceRegistrationConsumer.Create(plan);
        WorkspaceRegistrationRevision thirdRevision = Current(third);
        Assert.Same(plan, thirdRevision.Plan);
        Assert.NotSame(firstRevision.Workspace, thirdRevision.Workspace);
        Assert.Equal(2, thirdRevision.Plan.Contexts.Length);
    }

    [Fact]
    public void ContextOrderTargetsAndCallerCollectionsAreSnapshotted()
    {
        List<WorkspaceMemberCoordinate> packageMembers =
        [
            WorkspaceMemberCoordinate.Package(
                "System.Text.Json",
                "11.0.0-preview.7.26381.103",
                "net10.0"),
        ];
        List<WorkspaceMemberCoordinate> platformMembers =
        [
            WorkspaceMemberCoordinate.Platform(
                "runtime",
                "System.Text.Json",
                "11.0.0-preview.7.26381.103",
                "net11.0"),
        ];
        List<WorkspaceContextInput> contexts =
        [
            new()
            {
                Framework = "net10.0",
                RuntimeIdentifier = "linux-x64",
                Members = packageMembers,
            },
            new()
            {
                Framework = "net11.0",
                Members = platformMembers,
            },
        ];

        WorkspacePlan plan = WorkspaceRegistrationConsumer.CreatePlan([], contexts);
        contexts.Clear();
        packageMembers.Clear();
        platformMembers[0] = WorkspaceMemberCoordinate.Platform("aspnetcore");

        Assert.Equal(2, plan.Contexts.Length);
        Assert.Equal("net10.0", plan.Contexts[0].Framework);
        Assert.Equal("linux-x64", plan.Contexts[0].RuntimeIdentifier);
        var package = Assert.IsType<WorkspaceMemberCoordinate.PackageMember>(
            Assert.Single(plan.Contexts[0].Members));
        Assert.Equal("System.Text.Json", package.PackageId);
        Assert.Equal("11.0.0-preview.7.26381.103", package.Version);
        Assert.Equal("net10.0", package.Framework);

        Assert.Equal("net11.0", plan.Contexts[1].Framework);
        Assert.Null(plan.Contexts[1].RuntimeIdentifier);
        var platform = Assert.IsType<WorkspaceMemberCoordinate.PlatformMember>(
            Assert.Single(plan.Contexts[1].Members));
        Assert.Equal("runtime", platform.Family);
        Assert.Equal("System.Text.Json", platform.Assembly);
        Assert.Equal("11.0.0-preview.7.26381.103", platform.Version);
        Assert.Equal("net11.0", platform.Framework);
    }

    [Fact]
    public async Task ConstructionDoesNotAcquireAndLoaderValidationRemainsAtInvocation()
    {
        var declaration = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.no-construction-work"),
            [],
            [],
            [],
            EcosystemIntegrationScannerBinding.Create(UnexpectedScanner));
        WorkspaceContextInput invalid = RealContexts()[0] with
        {
            Framework = "not a framework",
        };
        WorkspacePlan plan = WorkspaceRegistrationConsumer.CreatePlan(
            [new WorkspaceRegistration.Ecosystem(declaration)],
            [invalid]);
        await using InspectionWorkspace workspace =
            WorkspaceRegistrationConsumer.Create(plan);
        var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync());
        Assert.Empty(scope.Snapshot.Packages);

        var authorization = new RecordingAuthorization();
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var failed = Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Failed>(
            await WorkspaceContextLoader.AcquirePackageRootAsync(
                plan.Contexts[0],
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization = authorization,
                    PackageStore = new InMemoryPackageStore(),
                },
                TestContext.Current.CancellationToken));

        Assert.Contains(
            failed.Failures,
            failure => failure.Kind == WorkspaceContextLoadFailureKind.InvalidCoordinate);
        Assert.Equal(0, authorization.Requests);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public void PlanConstructionRejectsTheWholeInvalidSet()
    {
        var library = new WorkspaceRegistration.ExactLibrary(
            WorkspaceRegistrationTestData.RealPackageSystemTextJson());
        var prefix = new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."));
        ImmutableArray<WorkspaceRegistration>[] invalid =
        [
            default,
            [null!],
            [prefix, library, new WorkspaceRegistration.ExactLibrary(
                WorkspaceRegistrationTestData.RealPackageSystemTextJson())],
            [prefix, library, new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."))],
            [prefix, new WorkspaceRegistration.Ecosystem(Platform()),
                new WorkspaceRegistration.Ecosystem(Platform())],
        ];
        foreach (ImmutableArray<WorkspaceRegistration> registrations in invalid)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(
                () => WorkspaceRegistrationConsumer.CreatePlan(registrations));
            Assert.Equal("registrations", error.ParamName);
        }
        Assert.Throws<ArgumentNullException>(
            () => WorkspaceRegistrationConsumer.Create((WorkspacePlan)null!));
        Assert.Throws<ArgumentNullException>(
            () => WorkspaceRegistrationConsumer.CreatePlan([], null!));
        Assert.Throws<ArgumentException>(
            () => WorkspaceRegistrationConsumer.CreatePlan([], [null!]));
        Assert.Throws<ArgumentException>(
            () => WorkspaceRegistrationConsumer.CreatePlan(
                [],
                [new WorkspaceContextInput { Members = null! }]));
    }

    [Fact]
    public async Task ReplacementChangesOneLivePlanWithoutMutatingSharedData()
    {
        var original = new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."));
        var replacement = new WorkspaceRegistration.PackagePrefix(new("Aspire."));
        WorkspacePlan plan = new([original], RealContexts());
        await using var first = new InspectionWorkspace(plan);
        await using var second = new InspectionWorkspace(plan);
        WorkspaceRegistrationRevision initial = Current(first);
        var unchanged = Assert.IsType<WorkspaceRegistrationOperationResult.NoEffect>(
            first.ReplaceRegistrations(initial,
                [new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."))]));
        Assert.Same(initial, unchanged.Revision);
        Assert.Same(plan, unchanged.Revision.Plan);

        var changed = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            first.ReplaceRegistrations(initial, [replacement]));
        Assert.NotSame(plan, changed.Revision.Plan);
        Assert.Equal([replacement], changed.Revision.Plan.Registrations);
        Assert.Equal(2, changed.Revision.Plan.Contexts.Length);
        Assert.Same(plan.Contexts[0], changed.Revision.Plan.Contexts[0]);
        Assert.Same(plan.Contexts[1], changed.Revision.Plan.Contexts[1]);
        Assert.Equal([original], plan.Registrations);
        Assert.Same(plan, initial.Plan);
        Assert.Same(plan, Current(second).Plan);
        Assert.Same(changed.Revision, Current(first));
    }

    [Fact]
    public async Task HistoricalPlanCanCreateAnotherOwnerButCannotTransferRevisionAuthority()
    {
        WorkspacePlan plan = new([new WorkspaceRegistration.Ecosystem(Platform())]);
        var first = new InspectionWorkspace(plan);
        WorkspaceRegistrationRevision initial = Current(first);
        await first.DisposeAsync();
        var historical = Assert.IsType<WorkspaceRegistrationReadResult.Unavailable>(
            first.GetRegistrationSnapshot());
        Assert.Same(plan, historical.LastRevision.Plan);

        await using var second = new InspectionWorkspace(historical.LastRevision.Plan);
        WorkspaceRegistrationRevision current = Current(second);
        Assert.Same(plan, current.Plan);
        Assert.NotSame(initial.Workspace, current.Workspace);
        var rejected = Assert.IsType<WorkspaceRegistrationOperationResult.Rejected>(
            second.ReplaceRegistrations(initial, plan.Registrations));
        Assert.Equal(WorkspaceRegistrationRejection.ForeignWorkspace, rejected.Reason);
        Assert.Same(current, rejected.Revision);
    }

    [Fact]
    public async Task NewEcosystemCorrespondencePublishesANewPlan()
    {
        WorkspaceEcosystemRegistrationDeclaration first = Platform();
        WorkspaceEcosystemRegistrationDeclaration replacement = Platform();
        WorkspacePlan plan = new([new WorkspaceRegistration.Ecosystem(first)]);
        await using var workspace = new InspectionWorkspace(plan);
        var changed = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(Current(workspace),
                [new WorkspaceRegistration.Ecosystem(replacement)]));

        Assert.Same(first, Assert.IsType<WorkspaceRegistration.Ecosystem>(
            Assert.Single(plan.Registrations)).Declaration);
        Assert.Same(replacement, Assert.IsType<WorkspaceRegistration.Ecosystem>(
            Assert.Single(changed.Revision.Plan.Registrations)).Declaration);
        Assert.NotSame(plan, changed.Revision.Plan);
    }

    [Fact]
    public async Task ConvenienceConstructionUsesTheSamePlanBoundary()
    {
        var registration = new WorkspaceRegistration.PackagePrefix(new("Aspire."));
        await using var empty = new InspectionWorkspace();
        await using var explicitWorkspace = new InspectionWorkspace([registration]);

        Assert.Same(WorkspacePlan.Empty, Current(empty).Plan);
        Assert.Equal([registration], Current(explicitWorkspace).Plan.Registrations);
    }

    static WorkspaceRegistrationRevision Current(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    static WorkspaceEcosystemRegistrationDeclaration Platform() =>
        new(WorkspaceEcosystemRegistrationId.Create("ecosystem.platform"),
            ["System"], [],
            [new WorkspaceEcosystemPopulationDeclaration.Platform(
                new(PlatformFamily.DotNetRuntime))]);

    static WorkspaceContextInput[] RealContexts() =>
    [
        new()
        {
            Framework = "net10.0",
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    "System.Text.Json",
                    "11.0.0-preview.7.26381.103",
                    "net10.0"),
            ],
        },
        new()
        {
            Framework = "net11.0",
            Members =
            [
                WorkspaceMemberCoordinate.Platform(
                    "runtime",
                    "System.Text.Json",
                    "11.0.0-preview.7.26381.103",
                    "net11.0"),
            ],
        },
    ];

    sealed class RecordingAuthorization : IPackageSourceAuthorization
    {
        public int Requests { get; private set; }

        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
        {
            Requests++;
            return PackageSourceAuthorization.Deny("No source is available.");
        }
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            });
        }
    }

    static ImmutableArray<EcosystemIntegrationClassification> UnexpectedScanner(
        EcosystemIntegrationObservationContext context) =>
        throw new InvalidOperationException(
            "Plan and Workspace construction must not invoke scanners.");
}
