using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

using ActivationResult =
    DotnetInspect.Web.BrowserSpotlightCurrentPlatformActivationResult<
        DotnetInspect.Web.TestPackageAction,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent,
        DotnetInspect.Web.Tests.BrowserSpotlightPlatformActivationTests
            .TestPlatformResult>;
using Descriptor = DotnetInspect.Web.BrowserSpotlightDestinationDescriptor<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Destination = DotnetInspect.Web.BrowserSpotlightDestination<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Plan = DotnetInspect.Web.BrowserSpotlightDestinationActivationPlan<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Result = DotnetInspect.Web.BrowserSpotlightDestinationProjectionResult<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;

namespace DotnetInspect.Web.Tests;

public sealed class BrowserSpotlightPlatformActivationTests
{
    private const string PackageVersion = "11.0.0-preview.7.26381.103";

    [Fact]
    public async Task ExecuteInvokesExactPlatformActionWithoutScopeMutation()
    {
        await using InspectionWorkspace workspace = PlatformWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        var ownerAction = new TestPlatformAction("platform-library");
        BrowserSpotlightPlatformAction<TestPlatformAction> action =
            PlatformAction(basis, ownerAction);
        Descriptor descriptor = Projected(
            basis,
            new Destination.PlatformLibrary(PlatformLibrary(), action));
        WorkspaceScopeSnapshot before = basis.Scope;
        var ownerResult = new TestPlatformResult.Applied();
        BrowserSpotlightPlatformAction<TestPlatformAction>? received = null;

        var settled = Assert.IsType<ActivationResult.Settled>(
            await BrowserSpotlightCurrentPlatformActivation
                .ExecuteAsync<
                    TestPackageAction,
                    TestNavigationAction,
                    TestPlatformAction,
                    TestLibraryIntent,
                    TestPlatformResult>(
                    workspace,
                    descriptor,
                    (candidate, _) =>
                    {
                        received = candidate;
                        return ValueTask.FromResult<TestPlatformResult>(
                            ownerResult);
                    },
                    TestContext.Current.CancellationToken));
        WorkspaceScopeSnapshot after = await Scope(workspace);

        Assert.Same(action, received);
        Assert.Same(action, settled.Action);
        Assert.Same(ownerResult, settled.Result);
        Assert.Equal(
            BrowserSpotlightDestinationHome.Platform,
            descriptor.Presentation.SourceHome);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.RegistrationCovered,
            descriptor.Presentation.WorkspaceRelationship);
        Assert.Equal(
            BrowserSpotlightWorkspaceDisposition.PreserveCurrent,
            descriptor.Presentation.WorkspaceDisposition);
        Assert.IsType<BrowserSpotlightDestinationAvailability.Available>(
            descriptor.Presentation.Availability);
        Assert.Same(before.Revision, after.Revision);
        Assert.Same(before.PublicationBase, after.PublicationBase);
        Assert.Empty(after.Revision.Packages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutePreservesOpaqueOwnerSettlement(bool superseded)
    {
        await using InspectionWorkspace workspace = PlatformWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        Descriptor descriptor = Projected(
            basis,
            new Destination.PlatformLibrary(
                PlatformLibrary(),
                PlatformAction(basis, "platform-library")));
        TestPlatformResult ownerResult = superseded
            ? new TestPlatformResult.Superseded()
            : new TestPlatformResult.Failed(
                "Platform acquisition failed.");

        var settled = Assert.IsType<ActivationResult.Settled>(
            await BrowserSpotlightCurrentPlatformActivation
                .ExecuteAsync<
                    TestPackageAction,
                    TestNavigationAction,
                    TestPlatformAction,
                    TestLibraryIntent,
                    TestPlatformResult>(
                    workspace,
                    descriptor,
                    (_, _) => ValueTask.FromResult(ownerResult),
                    TestContext.Current.CancellationToken));

        Assert.Same(ownerResult, settled.Result);
    }

    [Fact]
    public async Task RealizedPlatformLibraryActivatesWithoutRegistrationCoverage()
    {
        await using var workspace = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        BrowserSpotlightPlatformAction<TestPlatformAction> action =
            PlatformAction(basis, "realized-platform-library");
        Descriptor descriptor = Projected(
            basis,
            new Destination.CurrentPlatformLibrary(
                PlatformLibrary(),
                action));
        BrowserSpotlightPlatformAction<TestPlatformAction>? received = null;

        var settled = Assert.IsType<ActivationResult.Settled>(
            await BrowserSpotlightCurrentPlatformActivation
                .ExecuteAsync<
                    TestPackageAction,
                    TestNavigationAction,
                    TestPlatformAction,
                    TestLibraryIntent,
                    TestPlatformResult>(
                    workspace,
                    descriptor,
                    (candidate, _) =>
                    {
                        received = candidate;
                        return ValueTask.FromResult<TestPlatformResult>(
                            new TestPlatformResult.Applied());
                    },
                    TestContext.Current.CancellationToken));

        Assert.Same(action, received);
        Assert.Same(action, settled.Action);
        Assert.Equal(
            BrowserSpotlightWorkspaceRelationship.Admitted,
            descriptor.Presentation.WorkspaceRelationship);
        Assert.Empty(descriptor.Coverage);
    }

    [Fact]
    public async Task RegistrationMovementBlocksBeforeOwnerInvocation()
    {
        await using InspectionWorkspace workspace = PlatformWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        Descriptor descriptor = Projected(
            basis,
            new Destination.PlatformLibrary(
                PlatformLibrary(),
                PlatformAction(basis, "platform-library")));
        WorkspaceRegistrationOperationResult replaced =
            workspace.ReplaceRegistrations(
                basis.Registrations,
                []);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            replaced);
        int invocations = 0;

        var blocked = Assert.IsType<ActivationResult.Blocked>(
            await BrowserSpotlightCurrentPlatformActivation
                .ExecuteAsync<
                    TestPackageAction,
                    TestNavigationAction,
                    TestPlatformAction,
                    TestLibraryIntent,
                    TestPlatformResult>(
                    workspace,
                    descriptor,
                    (_, _) =>
                    {
                        invocations++;
                        return ValueTask.FromResult<TestPlatformResult>(
                            new TestPlatformResult.Applied());
                    },
                    TestContext.Current.CancellationToken));

        var stale = Assert.IsType<
            BrowserSpotlightActivationBlock.Stale>(blocked.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason.RegistrationRevision,
            stale.Reason);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task ScopeMovementBlocksBeforeOwnerInvocation()
    {
        await using InspectionWorkspace workspace = PlatformWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        Descriptor descriptor = Projected(
            basis,
            new Destination.PlatformLibrary(
                PlatformLibrary(),
                PlatformAction(basis, "platform-library")));
        WorkspaceScopeOperationResult replacement =
            await workspace.ReplaceScopeAsync(
                basis.Scope.Revision,
                [
                    Binding(
                        PackageSourceCoordinate.Create(
                            "System.Text.Json",
                            PackageVersion),
                        PackageAssemblyPath()),
                ],
                DateTimeOffset.UtcNow.AddSeconds(30),
                TestContext.Current.CancellationToken);
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(replacement);
        int invocations = 0;

        var blocked = Assert.IsType<ActivationResult.Blocked>(
            await BrowserSpotlightCurrentPlatformActivation
                .ExecuteAsync<
                    TestPackageAction,
                    TestNavigationAction,
                    TestPlatformAction,
                    TestLibraryIntent,
                    TestPlatformResult>(
                    workspace,
                    descriptor,
                    (_, _) =>
                    {
                        invocations++;
                        return ValueTask.FromResult<TestPlatformResult>(
                            new TestPlatformResult.Applied());
                    },
                    TestContext.Current.CancellationToken));

        var stale = Assert.IsType<
            BrowserSpotlightActivationBlock.Stale>(blocked.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason.ScopeRevision,
            stale.Reason);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task PublicationBaseMovementBlocksBeforeOwnerInvocation()
    {
        await using InspectionWorkspace workspace = PlatformWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        Descriptor descriptor = Projected(
            basis,
            new Destination.PlatformLibrary(
                PlatformLibrary(),
                PlatformAction(basis, "platform-library")));
        ActivationResult? activation = null;
        int invocations = 0;
        PackageRootBinding binding = Binding(
            PackageSourceCoordinate.Create(
                "System.Text.Json",
                PackageVersion),
            PackageAssemblyPath(),
            onOpen: () =>
            {
                WorkspaceScopeSnapshot preparing =
                    Scope(workspace).GetAwaiter().GetResult();
                Assert.Same(basis.Scope.Revision, preparing.Revision);
                Assert.NotSame(
                    basis.Scope.PublicationBase,
                    preparing.PublicationBase);
                activation = BrowserSpotlightCurrentPlatformActivation
                    .ExecuteAsync<
                        TestPackageAction,
                        TestNavigationAction,
                        TestPlatformAction,
                        TestLibraryIntent,
                        TestPlatformResult>(
                        workspace,
                        descriptor,
                        (_, _) =>
                        {
                            invocations++;
                            return ValueTask.FromResult<TestPlatformResult>(
                                new TestPlatformResult.Applied());
                        },
                        TestContext.Current.CancellationToken)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            });

        WorkspaceScopeOperationResult replacement =
            await workspace.ReplaceScopeAsync(
                basis.Scope.Revision,
                [binding],
                DateTimeOffset.UtcNow.AddSeconds(30),
                TestContext.Current.CancellationToken);
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(replacement);
        var blocked = Assert.IsType<ActivationResult.Blocked>(activation);
        var stale = Assert.IsType<
            BrowserSpotlightActivationBlock.Stale>(blocked.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason.ScopePublicationBase,
            stale.Reason);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task ExecuteRejectsNonPlatformPlan()
    {
        await using var workspace = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        var request = new BrowserSpotlightPackageRequest<TestPackageAction>(
            PackageSourceCoordinate.Create("Example", "1.0.0"),
            new TestPackageAction("nuget-org"));
        var descriptor = new Descriptor(
            basis,
            new Destination.Package(request),
            ImmutableArray<
                BrowserSpotlightCoverageWitness<TestPackageAction>>.Empty,
            new Plan.RestoreExternalPackageWorkspace(request));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await BrowserSpotlightCurrentPlatformActivation
                .ExecuteAsync<
                    TestPackageAction,
                    TestNavigationAction,
                    TestPlatformAction,
                    TestLibraryIntent,
                    TestPlatformResult>(
                    workspace,
                    descriptor,
                    (_, _) => ValueTask.FromResult<TestPlatformResult>(
                        new TestPlatformResult.Applied()),
                    TestContext.Current.CancellationToken));
    }

    private static InspectionWorkspace PlatformWorkspace() =>
        new(
        [
            new WorkspaceRegistration.Ecosystem(
                new WorkspaceEcosystemRegistrationDeclaration(
                    WorkspaceEcosystemRegistrationId.Create(
                        "ecosystem.runtime"),
                    namespaceRoots: [],
                    corePackages: [],
                    populations:
                    [
                        new WorkspaceEcosystemPopulationDeclaration.Platform(
                            DotNetRuntimePopulation()),
                    ])),
        ]);

    private static Descriptor Projected(
        BrowserSpotlightActivationBasis basis,
        Destination destination) =>
        Assert.IsType<Result.Projected>(
            BrowserSpotlightDestinationProjection.Project(
                basis,
                destination)).Descriptor;

    private static async Task<BrowserSpotlightActivationBasis> Basis(
        InspectionWorkspace workspace) =>
        new(
            resultGeneration: 1,
            await Scope(workspace),
            Registrations(workspace));

    private static async Task<WorkspaceScopeSnapshot> Scope(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;

    private static WorkspaceRegistrationRevision Registrations(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    private static BrowserSpotlightPlatformAction<TestPlatformAction>
        PlatformAction(
            BrowserSpotlightActivationBasis basis,
            string value) =>
        PlatformAction(basis, new TestPlatformAction(value));

    private static BrowserSpotlightPlatformAction<TestPlatformAction>
        PlatformAction(
            BrowserSpotlightActivationBasis basis,
            TestPlatformAction action) =>
        new(basis.Scope.Revision.Workspace, action);

    private static ExactLibrarySourceCoordinate.Platform PlatformLibrary() =>
        new(
            DotNetRuntimePopulation(),
            Assembly(PlatformAssemblyPath()));

    private static PlatformLibraryPopulationDeclaration
        DotNetRuntimePopulation() =>
        new(PlatformFamily.DotNetRuntime);

    private static PackageRootBinding Binding(
        PackageSourceCoordinate package,
        string assemblyPath,
        Action? onOpen = null)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                "lib/net11.0/System.Text.Json.dll");
            using Stream destination = entry.Open();
            using Stream source = File.OpenRead(assemblyPath);
            source.CopyTo(destination);
        }

        IPackageContent content = new InMemoryPackageContent(
            bytes.ToArray(),
            fromCache: false,
            producerKey: "nuget-org");
        if (onOpen is not null)
        {
            content = new CallbackPackageContent(content, onOpen);
        }
        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                package,
                content,
                producerKey: "nuget-org",
                PackagePayloadOrigin.Download),
            selectionTargetFramework: "net11.0",
            runtimeIdentifier: null);
    }

    private static ManagedMetadataIdentity.Assembly Assembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    private static string PackageAssemblyPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "Spotlight",
            "package",
            "System.Text.Json.dll");

    private static string PlatformAssemblyPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "Spotlight",
            "platform",
            "System.Text.Json.dll");

    internal abstract record TestPlatformResult
    {
        internal sealed record Applied : TestPlatformResult;

        internal sealed record Failed(string Message) : TestPlatformResult;

        internal sealed record Superseded : TestPlatformResult;
    }

    private sealed class CallbackPackageContent(
        IPackageContent inner,
        Action onOpen) : IPackageContent
    {
        private Action? _onOpen = onOpen;

        public string? RootPath => inner.RootPath;

        public string? NupkgPath => inner.NupkgPath;

        public bool FromCache => inner.FromCache;

        public string ProducerKey => inner.ProducerKey;

        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(relativePath, out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();
    }
}
