using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task LiveTypeLocator_InvalidRequestsDoNotActivateMaintenance()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceTypeDeclarationLocator locator =
            workspace.GetTypeDeclarationLocator();
        WorkspaceDeclarationContext context = await LocatorContext(
            workspace,
            LocatorImage(
                "Dormant",
                metadata => LocatorDefinition(metadata, "N", "Widget")));
        WorkspaceDeclarationOccurrence occurrence =
            Assert.Single(context.Receipt.Members).Occurrence;

        TypeDeclarationLocatorResult result = await locator.LocateAsync(
            [],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            TypeDeclarationLocatorRejectionKind.EmptyRequests,
            Assert.IsType<TypeDeclarationLocatorResult.Rejected>(result).Kind);
        await locator.WaitForCurrentMaintenanceAsync();
        Assert.Equal(0, locator.GetInventoryReadCount(occurrence));
    }

    [Fact]
    public async Task LiveTypeLocator_FirstDemandReusesSuccessfulInventory()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(
            workspace,
            LocatorImage(
                "Resident",
                metadata => LocatorDefinition(metadata, "N", "Widget")));
        WorkspaceDeclarationOccurrence occurrence =
            Assert.Single(context.Receipt.Members).Occurrence;
        WorkspaceTypeDeclarationLocator locator =
            workspace.GetTypeDeclarationLocator();

        TypeDeclarationLocatorResult.Evaluated first =
            await LocateLive(locator, "Widget");
        TypeDeclarationLocatorResult.Evaluated second =
            await LocateLive(locator, "Widget");

        Assert.Single(first.Answers[0].Candidates);
        Assert.Single(second.Answers[0].Candidates);
        Assert.Equal(1, locator.GetInventoryReadCount(occurrence));
    }

    [Fact]
    public async Task LiveTypeLocator_ActivationRecoversEarlierPendingContext()
    {
        await using var workspace = new InspectionWorkspace();
        using var paused = new PausedPopulationHandler(
            Archive(
                ("lib/net10.0/First.dll",
                    LocatorImage(
                        "First",
                        metadata =>
                            LocatorDefinition(metadata, "N", "Widget")))));
        using var firstClient = new HttpClient(paused);
        Task<WorkspaceDeclarationContext> firstLoad =
            WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                new()
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(
                    firstClient,
                    new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);
        await paused.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        WorkspaceDeclarationContext second = await LocatorContext(
            workspace,
            LocatorImage(
                "Second",
                metadata => LocatorDefinition(metadata, "N", "Widget")));
        WorkspaceTypeDeclarationLocator locator =
            workspace.GetTypeDeclarationLocator();
        Task<TypeDeclarationLocatorResult> p1Task = locator.LocateAsync(
            [new TypeDeclarationLocatorRequest.Pattern("Widget")],
            cancellationToken: TestContext.Current.CancellationToken);

        paused.Resume.TrySetResult();
        WorkspaceDeclarationContext first = await firstLoad;
        TypeDeclarationLocatorResult.Evaluated p1 =
            Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
                await p1Task);
        await locator.WaitForCurrentMaintenanceAsync();
        Assert.Equal(
            1,
            locator.GetInventoryReadCount(
                Assert.Single(first.Receipt.Members).Occurrence));
        TypeDeclarationLocatorResult.Evaluated p2 =
            await LocateLive(locator, "Widget");

        Assert.Single(p1.Population.Contexts);
        Assert.Same(second.Receipt, p1.Population.Contexts[0]);
        Assert.Equal(2, p2.Population.Contexts.Length);
        Assert.Same(first.Receipt, p2.Population.Contexts[0]);
        Assert.Same(second.Receipt, p2.Population.Contexts[1]);
        Assert.Single(p1.Answers[0].Candidates);
        Assert.Equal(2, p2.Answers[0].Candidates.Length);
        Assert.Equal(
            1,
            locator.GetInventoryReadCount(
                Assert.Single(first.Receipt.Members).Occurrence));
        Assert.Equal(
            1,
            locator.GetInventoryReadCount(
                Assert.Single(second.Receipt.Members).Occurrence));
    }

    [Fact]
    public async Task LiveTypeLocator_CallerCancellationDoesNotCancelSharedWork()
    {
        await using var workspace = new InspectionWorkspace();
        var paused = PausedLocatorContext(
            workspace,
            "Shared",
            LocatorImage(
                "Shared",
                metadata => LocatorDefinition(metadata, "N", "Widget")));
        using var entered = paused.Entered;
        using var resume = paused.Resume;
        WorkspaceDeclarationOccurrence occurrence =
            Assert.Single(paused.Context.Receipt.Members).Occurrence;
        WorkspaceTypeDeclarationLocator locator =
            workspace.GetTypeDeclarationLocator();
        using var cancellation = new CancellationTokenSource();

        Task<TypeDeclarationLocatorResult> cancelled = locator.LocateAsync(
            [new TypeDeclarationLocatorRequest.Pattern("Widget")],
            cancellationToken: cancellation.Token);
        Task<TypeDeclarationLocatorResult> survivor = locator.LocateAsync(
            [new TypeDeclarationLocatorRequest.Pattern("Widget")],
            cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            Assert.True(
                entered.Wait(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken));
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => cancelled);
            Assert.False(survivor.IsCompleted);
        }
        finally
        {
            resume.Set();
        }
        TypeDeclarationLocatorResult.Evaluated result =
            Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
                await survivor);
        Assert.Single(result.Answers[0].Candidates);
        Assert.Equal(1, locator.GetInventoryReadCount(occurrence));
        Assert.Equal(1, paused.OpenCount());
    }

    [Fact]
    public async Task LiveTypeLocator_BoundGrowthInventoriesOnlyNewlyAdmittedPrefix()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(
            workspace,
            LocatorImage(
                "First",
                metadata => LocatorDefinition(metadata, "N", "Widget")),
            LocatorImage(
                "Second",
                metadata => LocatorDefinition(metadata, "N", "Widget")));
        WorkspaceDeclarationMember[] members =
            [.. context.Receipt.Members];
        WorkspaceTypeDeclarationLocator locator =
            workspace.GetTypeDeclarationLocator();

        TypeDeclarationLocatorResult.Evaluated first =
            await LocateLive(locator, "Widget", maxInventoryReads: 1);
        Assert.Single(first.Answers[0].Candidates);
        Assert.Equal(1, locator.GetInventoryReadCount(members[0].Occurrence));
        Assert.Equal(0, locator.GetInventoryReadCount(members[1].Occurrence));

        TypeDeclarationLocatorResult.Evaluated second =
            await LocateLive(locator, "Widget", maxInventoryReads: 2);
        Assert.Equal(2, second.Answers[0].Candidates.Length);
        Assert.Equal(1, locator.GetInventoryReadCount(members[0].Occurrence));
        Assert.Equal(1, locator.GetInventoryReadCount(members[1].Occurrence));
    }

    [Fact]
    public async Task LiveTypeLocator_CachesMetadataRejectionAndCloseStopsAdmission()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Duplicate.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Duplicate"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            0);
        foreach (string name in new[] { "<Module>", "Duplicate", "Duplicate" })
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString(name),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder());
        var image = new BlobBuilder();
        builder.Serialize(image);

        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(
            workspace,
            image.ToArray());
        WorkspaceDeclarationOccurrence occurrence =
            Assert.Single(context.Receipt.Members).Occurrence;
        WorkspaceTypeDeclarationLocator locator =
            workspace.GetTypeDeclarationLocator();

        TypeDeclarationLocatorResult.Evaluated first =
            await LocateLive(locator, "*");
        TypeDeclarationLocatorResult.Evaluated second =
            await LocateLive(locator, "*");
        Assert.Single(
            first.Members.OfType<
                TypeDeclarationLocatorMemberOutcome.InventoryRejected>());
        Assert.Single(
            second.Members.OfType<
                TypeDeclarationLocatorMemberOutcome.InventoryRejected>());
        Assert.Equal(1, locator.GetInventoryReadCount(occurrence));

        await workspace.CloseAsync();
        TypeDeclarationLocatorResult closed = await locator.LocateAsync(
            [new TypeDeclarationLocatorRequest.Pattern("*")],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            WorkspaceDeclarationPopulationFailure.WorkspaceClosed,
            Assert.IsType<TypeDeclarationLocatorResult.Rejected>(closed)
                .PopulationFailure);
    }

    [Fact]
    public async Task LiveTypeLocator_CloseDrainsBorrowedInventoryWork()
    {
        var workspace = new InspectionWorkspace();
        var paused = PausedLocatorContext(
            workspace,
            "Closing",
            LocatorImage(
                "Closing",
                metadata => LocatorDefinition(metadata, "N", "Widget")));
        using var entered = paused.Entered;
        using var resume = paused.Resume;
        WorkspaceTypeDeclarationLocator locator =
            workspace.GetTypeDeclarationLocator();

        Task<TypeDeclarationLocatorResult> query = locator.LocateAsync(
            [new TypeDeclarationLocatorRequest.Pattern("Widget")],
            cancellationToken: TestContext.Current.CancellationToken);
        Task<InspectionWorkspaceCloseReport>? close = null;
        try
        {
            Assert.True(
                entered.Wait(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken));
            close = workspace.CloseAsync();
            Assert.False(close.IsCompleted);
            Assert.Throws<ObjectDisposedException>(
                () => paused.Group.UseAssemblyImage(
                    paused.Assembly,
                    static content => content.Content.Length));
        }
        finally
        {
            resume.Set();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.Empty((await close).ArtifactSessionCleanupFailures);
        Assert.Equal(1, paused.OpenCount());
    }

    static async Task<TypeDeclarationLocatorResult.Evaluated> LocateLive(
        WorkspaceTypeDeclarationLocator locator,
        string pattern,
        int? maxInventoryReads = null) =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.LocateAsync(
                [new TypeDeclarationLocatorRequest.Pattern(pattern)],
                maxInventoryReads: maxInventoryReads,
                cancellationToken: TestContext.Current.CancellationToken));

    static (
        WorkspaceDeclarationContext Context,
        AssemblyContextGroup Group,
        ResolvedAssemblyReference Assembly,
        ManualResetEventSlim Entered,
        ManualResetEventSlim Resume,
        Func<int> OpenCount) PausedLocatorContext(
            InspectionWorkspace workspace,
            string assemblyName,
            byte[] image)
    {
        var entered = new ManualResetEventSlim();
        var resume = new ManualResetEventSlim();
        int opens = 0;
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                assemblyName,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () =>
            {
                Interlocked.Increment(ref opens);
                entered.Set();
                resume.Wait(TestContext.Current.CancellationToken);
                return new MemoryStream(image, writable: false);
            },
            AssemblyResolutionProvenance.Local(
                "live locator paused inventory"));
        var participant = new AssemblyContextParticipant(
            assembly,
            RejectingLocatorBindingPolicy.Instance);
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        WorkspaceMemberCoordinate declared =
            PackageMember(Version);
        var realized = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            Producer(NuGetOrg),
            Framework,
            runtimeIdentifier: null);
        int order = workspace.BeginDeclarationContext();
        WorkspaceDeclarationContext context =
            workspace.CompleteDeclarationContext(
                order,
                new()
                {
                    Framework = Framework,
                    Members = [declared],
                },
                new WorkspaceContextLoadOutcome.Loaded(
                    group,
                    [new(declared, realized, participant)],
                    availablePlatformAssemblies: [],
                    Framework,
                    runtimeIdentifier: null));
        return (
            context,
            group,
            assembly,
            entered,
            resume,
            () => Volatile.Read(ref opens));
    }

    sealed class RejectingLocatorBindingPolicy : IAssemblyBindingPolicy
    {
        internal static RejectingLocatorBindingPolicy Instance { get; } =
            new();

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
