using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Theory]
    [InlineData("runtime", RuntimePackPackageId, PlatformFamily.DotNetRuntime)]
    [InlineData("aspnetcore", AspNetCorePackPackageId, PlatformFamily.AspNetCore)]
    public async Task DeclarationPopulation_PlatformKeepsSourceDomainAndOnlySelectedAssemblies(
        string family, string transportPackage, PlatformFamily expectedFamily)
    {
        await using var workspace = new InspectionWorkspace();
        var store = new InMemoryPackageStore();
        await store.CommitAsync(transportPackage, RuntimePackVersion, Producer(NuGetOrg),
            new MemoryStream(RuntimePack()), TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        string assemblyName = Path.GetFileNameWithoutExtension(TargetPath);
        WorkspaceDeclarationContext context = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new()
            {
                Framework = Framework,
                Members = [WorkspaceMemberCoordinate.Platform(family, assemblyName, RuntimePackVersion)],
            },
            Options(client, store), TestContext.Current.CancellationToken);
        Assert.Equal(2, ContextLoaded(context).AvailablePlatformAssemblies.Length);
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, context);
        WorkspaceDeclarationMember member = Assert.Single(population.Receipt.Members);
        var coordinate = Assert.IsType<ExactLibrarySourceCoordinate.Platform>(member.Coordinate);
        Assert.Equal(expectedFamily, coordinate.Population.Family);
        Assert.Equal(assemblyName, member.AssemblyIdentity.Name);
        Assert.Equal(Producer(NuGetOrg),
            Assert.IsType<RealizedMemberCoordinate.Platform>(ContextOrigin(member).Realized).Producer);
        Assert.IsType<AssemblyResolutionProvenance.PlatformAsset>(member.Selection);
        Assert.NotEmpty(ReadDeclarations(population, member).Declarations);
    }

    [Fact]
    public async Task DeclarationPopulation_RequestOrderAndSnapshotPrecedeAsyncRealization()
    {
        await using var workspace = new InspectionWorkspace();
        using var paused = new PausedPopulationHandler(TargetPackage());
        using var firstClient = new HttpClient(paused);
        using var secondClient = new HttpClient(new FailingHandler());
        var requested = new List<WorkspaceMemberCoordinate> { PackageMember(Version) };
        Task<WorkspaceDeclarationContext> firstLoad =
            WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace, new() { Framework = Framework, Members = requested },
                Options(firstClient, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);
        await paused.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        WorkspaceDeclarationContext second;
        try
        {
            requested.Clear();
            second = await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace, new() { Framework = Framework, Members = [PackageMember(Version)] },
                Options(secondClient, await CachedStoreAsync(Version, TargetPackage())),
                TestContext.Current.CancellationToken);
            Assert.False(firstLoad.IsCompleted);
        }
        finally
        {
            paused.Resume.TrySetResult();
        }

        WorkspaceDeclarationContext first = await firstLoad;
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, second, first);
        Assert.True(population.Receipt.IsRealizationComplete);
        Assert.Single(ContextRequest(first).Members);
        Assert.Same(first.Receipt, population.Receipt.Contexts[0]);
        Assert.Same(second.Receipt, population.Receipt.Contexts[1]);
    }

    [Fact]
    public async Task DeclarationPopulation_PreservesOriginsOccurrencesAndEarlierReceipts()
    {
        await using var workspace = new InspectionWorkspace();
        var store = new InMemoryPackageStore();
        foreach (PackageSource source in new[] { NuGetOrg, FeedB })
        {
            await store.CommitAsync(PackageId, Version, Producer(source),
                new MemoryStream(TargetPackage()), TestContext.Current.CancellationToken);
        }
        using var client = new HttpClient(new FailingHandler());
        var request = new WorkspaceContextInput
        {
            Framework = Framework,
            Members = [PackageMember(Version)],
        };
        WorkspaceDeclarationContext first = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, request, Options(client, store), TestContext.Current.CancellationToken);
        WorkspaceDeclarationPopulation p1 = CaptureDeclarations(workspace, first);
        WorkspaceDeclarationContext second = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, request,
            Options(client, store, sourceAuthorization: new UniformPackageSourceAuthorization([FeedB])),
            TestContext.Current.CancellationToken);
        WorkspaceDeclarationPopulation p2 = CaptureDeclarations(workspace, second, first);
        WorkspaceDeclarationPopulation permuted = CaptureDeclarations(workspace, first, second);

        WorkspaceDeclarationMember earlier = Assert.Single(p1.Receipt.Members);
        Assert.Equal(2, p2.Receipt.Members.Length);
        Assert.Same(earlier.Occurrence, p2.Receipt.Members[0].Occurrence);
        Assert.Equal(p2.Receipt.Members.Select(member => member.Occurrence),
            permuted.Receipt.Members.Select(member => member.Occurrence));
        Assert.Equal(earlier.Coordinate, p2.Receipt.Members[1].Coordinate);
        Assert.NotSame(earlier.Occurrence, p2.Receipt.Members[1].Occurrence);
        Assert.NotEqual(
            Assert.IsType<RealizedMemberCoordinate.Package>(ContextOrigin(earlier).Realized).Producer,
            Assert.IsType<RealizedMemberCoordinate.Package>(ContextOrigin(p2.Receipt.Members[1]).Realized).Producer);
        Assert.NotSame(p1.Receipt.Identity, p2.Receipt.Identity);
        Assert.True(p1.Receipt.IsRealizationComplete);
        var selection =
            Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
                earlier.Selection);
        Assert.Equal(Framework, selection.Tfm);
        Assert.Equal(
            $"lib/{Framework}/{Path.GetFileName(TargetPath)}",
            selection.AssetPath);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.OccurrenceNotSelected,
            Assert.IsType<WorkspaceDeclarationInventoryOutcome.Unavailable>(
                p1.ReadDeclarations(p2.Receipt.Members[1].Occurrence,
                    TestContext.Current.CancellationToken)).Failure);

        foreach (WorkspaceDeclarationMember member in p2.Receipt.Members)
        {
            AssemblyTypeDeclarationInventory inventory = ReadDeclarations(p2, member);
            Assert.Equal(member.AssemblyIdentity, inventory.Identity);
            Assert.NotEmpty(inventory.Declarations);
        }
    }

    [Fact]
    public async Task DeclarationPopulation_PreservesWholeFailedRequestAlongsideHealthyMembers()
    {
        await using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(Version, TargetPackage());
        using var client = new HttpClient(new NotFoundHandler());
        var requested = new List<WorkspaceMemberCoordinate>
        {
            PackageMember(Version),
            WorkspaceMemberCoordinate.Package("missing.package", Version),
        };
        WorkspaceDeclarationContext failed = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new() { Framework = Framework, Members = requested },
            Options(client, store), TestContext.Current.CancellationToken);
        requested.Clear();
        WorkspaceDeclarationContext healthy = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new() { Framework = Framework, Members = [PackageMember(Version)] },
            Options(client, store), TestContext.Current.CancellationToken);

        WorkspaceDeclarationPopulation failedOnly = CaptureDeclarations(workspace, failed);
        Assert.False(failedOnly.Receipt.IsRealizationComplete);
        Assert.Empty(failedOnly.Receipt.Members);
        Assert.Equal(2, ContextRequest(failed).Members.Count);
        Assert.NotEmpty(failed.Receipt.Failures);
        Assert.False(failed.Receipt.IsRealized);

        WorkspaceDeclarationPopulation mixed = CaptureDeclarations(workspace, healthy, failed);
        Assert.False(mixed.Receipt.IsRealizationComplete);
        Assert.Single(mixed.Receipt.Members);
        Assert.Same(failed.Receipt, mixed.Receipt.Contexts[0]);
        AssemblyTypeDeclarationInventory mixedInventory =
            ReadDeclarations(mixed, mixed.Receipt.Members[0]);
        Assert.NotEmpty(mixedInventory.Declarations);
        var unresolvedFocus =
            Assert.IsType<WorkspaceExactTypeFocusOutcome.Unavailable>(
                WorkspaceExactTypeFocusQuery.Execute(
                    mixed,
                    mixedInventory.Definitions[0].ToMetadataFullName(),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        Assert.Contains(
            "incomplete",
            unresolvedFocus.Detail,
            StringComparison.OrdinalIgnoreCase);

        WorkspaceDeclarationPopulation empty = CaptureDeclarations(workspace);
        Assert.True(empty.Receipt.IsRealizationComplete);
        Assert.Empty(empty.Receipt.Members);
    }

    [Fact]
    public async Task DeclarationPopulation_EmbeddedOriginIsNotInventedAndReadsUseRetainedContent()
    {
        await using var workspace = new InspectionWorkspace();
        byte[] image = File.ReadAllBytes(EmbeddedPath);
        var provider = new StubEmbeddedContent(image);
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext context = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Members = [EmbeddedMember(image)] },
            Options(client, new InMemoryPackageStore(), provider),
            TestContext.Current.CancellationToken);
        int opens = provider.OpenCount;
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, context);
        WorkspaceDeclarationMember member = Assert.Single(population.Receipt.Members);
        Assert.Equal(WorkspaceDeclarationCoordinateStatus.CoordinateUnavailable, member.CoordinateStatus);
        Assert.Null(member.Coordinate);
        Assert.IsType<RealizedMemberCoordinate.Embedded>(ContextOrigin(member).Realized);
        AssemblyTypeDeclarationInventory inventory = ReadDeclarations(population, member);
        _ = ReadDeclarations(population, member);
        Assert.Equal(opens, provider.OpenCount);

        await workspace.CloseAsync();
        Assert.NotEmpty(inventory.Declarations);
        Assert.Single(population.Receipt.Members);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.WorkspaceClosed,
            Assert.IsType<WorkspaceDeclarationInventoryOutcome.Unavailable>(
                population.ReadDeclarations(member.Occurrence,
                    TestContext.Current.CancellationToken)).Failure);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.WorkspaceClosed,
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Rejected>(
                workspace.CaptureDeclarationPopulation([context])).Failure);
    }

    [Fact]
    public async Task DeclarationPopulation_RejectsForeignDuplicateAndReleasedContexts()
    {
        await using var workspace = new InspectionWorkspace();
        await using var foreign = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext context = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Framework = Framework, Members = [PackageMember(Version)] },
            Options(client, await CachedStoreAsync(Version, TargetPackage())),
            TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.ForeignWorkspace,
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Rejected>(
                foreign.CaptureDeclarationPopulation([context])).Failure);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.DuplicateContext,
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Rejected>(
                workspace.CaptureDeclarationPopulation([context, context])).Failure);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.MalformedSelection,
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Rejected>(
                workspace.CaptureDeclarationPopulation(default)).Failure);

        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, context);
        ContextLoaded(context).Group.Dispose();
        Assert.Equal(WorkspaceDeclarationPopulationFailure.ContextUnavailable,
            Assert.IsType<WorkspaceDeclarationPopulationCapture.Rejected>(
                workspace.CaptureDeclarationPopulation([context])).Failure);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.ContextUnavailable,
            Assert.IsType<WorkspaceDeclarationInventoryOutcome.Unavailable>(
                population.ReadDeclarations(population.Receipt.Members[0].Occurrence,
                    TestContext.Current.CancellationToken)).Failure);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.Throws<OperationCanceledException>(() =>
            population.ReadDeclarations(population.Receipt.Members[0].Occurrence, cancellation.Token));
    }

    [Fact]
    public async Task DeclarationPopulation_MetadataRejectionRemainsAttributedAfterRealization()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Duplicate.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Duplicate"), new Version(1, 0, 0, 0),
            default, default, 0, 0);
        foreach (string name in new[] { "<Module>", "Duplicate", "Duplicate" })
        {
            metadata.AddTypeDefinition(TypeAttributes.Public, default, metadata.GetOrAddString(name),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        }
        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true), new BlobBuilder());
        var image = new BlobBuilder();
        builder.Serialize(image);
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext context = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Framework = Framework, Members = [PackageMember(Version)] },
            Options(client, await CachedStoreAsync(
                Version, Archive(($"lib/{Framework}/Duplicate.dll", image.ToArray())))),
            TestContext.Current.CancellationToken);
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, context);
        Assert.True(population.Receipt.IsRealizationComplete);
        WorkspaceDeclarationMember member = Assert.Single(population.Receipt.Members);
        var inspected = Assert.IsType<WorkspaceDeclarationInventoryOutcome.Inspected>(
            population.ReadDeclarations(member.Occurrence, TestContext.Current.CancellationToken));
        Assert.Equal(CandidateOpenFailureKind.InvalidImage,
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(inspected.Outcome).Failure.Kind);
        Assert.Same(member, Assert.Single(population.Receipt.Members));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task DeclarationPopulation_RealJsonPackageAndPlatformKeepDistinctChoices()
    {
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient();
        var options = Options(client, new InMemoryPackageStore());
        WorkspaceDeclarationContext package = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new()
            {
                Framework = Framework,
                Members = [WorkspaceMemberCoordinate.Package("System.Text.Json", "10.0.0")],
            },
            options, TestContext.Current.CancellationToken);
        WorkspaceDeclarationContext platform = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new()
            {
                Framework = Framework,
                Members =
                [
                    WorkspaceMemberCoordinate.Platform(
                        "runtime", assembly: "System.Text.Json", version: "10.0.10"),
                ],
            },
            options, TestContext.Current.CancellationToken);
        _ = ContextLoaded(package);
        _ = ContextLoaded(platform);
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, package, platform);
        Assert.True(population.Receipt.IsRealizationComplete);
        Assert.Equal(2, population.Receipt.Members.Length);
        Assert.IsType<ExactLibrarySourceCoordinate.Package>(population.Receipt.Members[0].Coordinate);
        Assert.IsType<ExactLibrarySourceCoordinate.Platform>(population.Receipt.Members[1].Coordinate);
        foreach (WorkspaceDeclarationMember member in population.Receipt.Members)
        {
            Assert.Contains(ReadDeclarations(population, member).GetDeclarations(),
                declaration => declaration.Name.Namespace == "System.Text.Json"
                    && declaration.Name.Segments.SequenceEqual(["JsonSerializer"]));
        }
    }

    static WorkspaceDeclarationPopulation CaptureDeclarations(
        InspectionWorkspace workspace, params WorkspaceDeclarationContext[] contexts) =>
        Assert.IsType<WorkspaceDeclarationPopulationCapture.Captured>(
            workspace.CaptureDeclarationPopulation([.. contexts])).Population;

    static WorkspaceContextLoadOutcome.Loaded ContextLoaded(WorkspaceDeclarationContext context) =>
        Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(context.ContextLoadOutcome);

    static WorkspaceContextInput ContextRequest(WorkspaceDeclarationContext context) =>
        Assert.IsType<WorkspaceDeclarationRequest.ContextLoad>(context.Receipt.Request).Input;

    static WorkspaceDeclarationOrigin.ContextLoad ContextOrigin(WorkspaceDeclarationMember member) =>
        Assert.IsType<WorkspaceDeclarationOrigin.ContextLoad>(member.Origin);

    static AssemblyTypeDeclarationInventory ReadDeclarations(
        WorkspaceDeclarationPopulation population, WorkspaceDeclarationMember member) =>
        Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(
            Assert.IsType<WorkspaceDeclarationInventoryOutcome.Inspected>(
                population.ReadDeclarations(member.Occurrence,
                    TestContext.Current.CancellationToken)).Outcome).Inventory;

    sealed class PausedPopulationHandler(byte[] payload)
        : DelegatingHandler(new PayloadHandler(payload, Version))
    {
        internal TaskCompletionSource Started { get; } = new();
        internal TaskCompletionSource Resume { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
