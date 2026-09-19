using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

using ActivationResult =
    DotnetInspect.Web.BrowserSpotlightExternalPackageActivationResult<
        DotnetInspect.Web.TestPackageAction,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestDefinitionsRequest,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestCompleteActivation,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestDefinitionsFailure,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestHostPublication,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestHostRejection,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestNonPostingResult>;
using AdmissionResult =
    DotnetInspect.Web.BrowserSpotlightRetainedWorkspaceAdmissionResult<
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestHostAuthority,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestHostRejection>;
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
using ProjectionResult =
    DotnetInspect.Web.BrowserSpotlightDestinationProjectionResult<
        DotnetInspect.Web.TestPackageAction,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent>;
using PublicationResult =
    DotnetInspect.Web.BrowserSpotlightRetainedWorkspacePublicationResult<
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestHostPublication,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestHostRejection>;
using RestorationResult =
    DotnetInspect.Web.BrowserSpotlightWorkspaceRestorationResult<
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestCompleteActivation,
        DotnetInspect.Web.Tests.BrowserSpotlightExternalPackageActivationTests
            .TestDefinitionsFailure>;

namespace DotnetInspect.Web.Tests;

public sealed class BrowserSpotlightExternalPackageActivationTests
{
    private const string PackageVersion = "11.0.0-preview.7.26381.103";

    [Fact]
    public async Task ExecutePublishesExactCompleteActivationWithCuratedPlan()
    {
        await using var source = new InspectionWorkspace();
        await using var fresh = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(source, generation: 7);
        BrowserSpotlightPackageRequest<TestPackageAction> package =
            PackageRequest("Humanizer.Core", "2.14.1");
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(package));
        var host = new TestRetainedHost(source.Identity, generation: 7);
        var complete = new TestCompleteActivation(fresh.Identity);
        TestDefinitionsRequest? received = null;
        int nonPostingCount = 0;

        var published = Assert.IsType<ActivationResult.Published>(
            await Execute(
                source,
                descriptor,
                host,
                (candidate, curated, authority) =>
                {
                    received = new(candidate, curated, authority);
                    return received;
                },
                (request, _) =>
                    ValueTask.FromResult<RestorationResult>(
                        new RestorationResult.Complete(complete)),
                activation =>
                {
                    Assert.Same(complete, activation);
                    nonPostingCount++;
                    return ValueTask.FromResult(
                        new TestNonPostingResult());
                }));

        Assert.NotNull(received);
        Assert.Same(package, received.Package);
        Assert.Same(host.Authority, received.HostAuthority);
        Assert.Equal(
            [
                EcosystemPackIds.Runtime.Value,
                EcosystemPackIds.AspNetCore.Value,
                EcosystemPackIds.MicrosoftExtensions.Value,
            ],
            received.CuratedPlan.Registrations
                .Select(registration =>
                    Assert.IsType<WorkspaceRegistration.Ecosystem>(
                        registration).Declaration.Id.Value));
        Assert.Empty(received.CuratedPlan.Contexts);
        Assert.Same(received, published.Request);
        Assert.Same(complete, published.Activation);
        Assert.Same(host.Publication, published.Publication);
        Assert.Same(fresh.Identity, host.ActiveWorkspace);
        Assert.Equal(
            [source.Identity, fresh.Identity],
            host.Workspaces);
        Assert.Equal(1, host.PublicationCount);
        Assert.Equal(0, nonPostingCount);
    }

    [Fact]
    public async Task DefinitionsFailureNeverPublishesOrRunsNonPosting()
    {
        await using var source = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(source);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest("Example", "1.0.0")));
        var host = new TestRetainedHost(source.Identity);
        var failure = new TestDefinitionsFailure("package acquisition failed");
        int nonPostingCount = 0;

        var failed = Assert.IsType<ActivationResult.RestorationFailed>(
            await Execute(
                source,
                descriptor,
                host,
                static (package, curated, authority) =>
                    new(package, curated, authority),
                (_, _) =>
                    ValueTask.FromResult<RestorationResult>(
                        new RestorationResult.Failed(failure)),
                _ =>
                {
                    nonPostingCount++;
                    return ValueTask.FromResult(
                        new TestNonPostingResult());
                }));

        Assert.Same(failure, failed.Result);
        Assert.Equal(0, host.PublicationCount);
        Assert.Equal(0, nonPostingCount);
        Assert.Same(source.Identity, host.ActiveWorkspace);
    }

    [Fact]
    public async Task ScopeMovementDuringRestorationRunsNonPosting()
    {
        await using var source = new InspectionWorkspace();
        await using var fresh = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(source);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest("Example", "1.0.0")));
        var host = new TestRetainedHost(source.Identity);
        var complete = new TestCompleteActivation(fresh.Identity);
        var cleanup = new TestNonPostingResult();
        int nonPostingCount = 0;

        var blocked = Assert.IsType<
            ActivationResult.CompleteButNotPublished>(
                await Execute(
                    source,
                    descriptor,
                    host,
                    static (package, curated, authority) =>
                        new(package, curated, authority),
                    async (_, cancellationToken) =>
                    {
                        WorkspaceScopeOperationResult changed =
                            await source.AddPackagesAsync(
                                basis.Scope.Revision,
                                [Binding()],
                                DateTimeOffset.UtcNow.AddSeconds(30),
                                cancellationToken);
                        Assert.IsType<
                            WorkspaceScopeOperationResult.Committed>(changed);
                        return new RestorationResult.Complete(complete);
                    },
                    activation =>
                    {
                        Assert.Same(complete, activation);
                        nonPostingCount++;
                        return ValueTask.FromResult(cleanup);
                    }));

        var sourceBlock = Assert.IsType<
            BrowserSpotlightFreshWorkspaceBlock<TestHostRejection>.Source>(
                blocked.Reason);
        var stale = Assert.IsType<
            BrowserSpotlightActivationBlock.Stale>(sourceBlock.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason.ScopeRevision,
            stale.Reason);
        Assert.Same(cleanup, blocked.NonPosting);
        Assert.Equal(1, nonPostingCount);
        Assert.Equal(0, host.PublicationCount);
        Assert.Same(source.Identity, host.ActiveWorkspace);
    }

    [Fact]
    public async Task ActiveWorkspaceReplacementDuringRestorationRunsNonPosting()
    {
        await using var source = new InspectionWorkspace();
        await using var replacement = new InspectionWorkspace();
        await using var fresh = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(source);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest("Example", "1.0.0")));
        var host = new TestRetainedHost(source.Identity);
        var complete = new TestCompleteActivation(fresh.Identity);
        int nonPostingCount = 0;

        var blocked = Assert.IsType<
            ActivationResult.CompleteButNotPublished>(
                await Execute(
                    source,
                    descriptor,
                    host,
                    static (package, curated, authority) =>
                        new(package, curated, authority),
                    (_, _) =>
                    {
                        host.ReplaceActive(replacement.Identity);
                        return ValueTask.FromResult<RestorationResult>(
                            new RestorationResult.Complete(complete));
                    },
                    activation =>
                    {
                        Assert.Same(complete, activation);
                        nonPostingCount++;
                        return ValueTask.FromResult(
                            new TestNonPostingResult());
                    }));

        var hostBlock = Assert.IsType<
            BrowserSpotlightFreshWorkspaceBlock<TestHostRejection>.Host>(
                blocked.Reason);
        Assert.Equal(
            "Spotlight intent is no longer current.",
            hostBlock.Result.Message);
        Assert.Equal(1, nonPostingCount);
        Assert.Equal(0, host.PublicationCount);
        Assert.Same(replacement.Identity, host.ActiveWorkspace);
    }

    [Fact]
    public async Task IntentSupersessionDuringRestorationRunsNonPosting()
    {
        await using var source = new InspectionWorkspace();
        await using var fresh = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(source);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest("Example", "1.0.0")));
        var host = new TestRetainedHost(source.Identity);
        var complete = new TestCompleteActivation(fresh.Identity);
        int nonPostingCount = 0;

        var blocked = Assert.IsType<
            ActivationResult.CompleteButNotPublished>(
                await Execute(
                    source,
                    descriptor,
                    host,
                    static (package, curated, authority) =>
                        new(package, curated, authority),
                    (_, _) =>
                    {
                        host.SupersedeIntent();
                        return ValueTask.FromResult<RestorationResult>(
                            new RestorationResult.Complete(complete));
                    },
                    activation =>
                    {
                        Assert.Same(complete, activation);
                        nonPostingCount++;
                        return ValueTask.FromResult(
                            new TestNonPostingResult());
                    }));

        var hostBlock = Assert.IsType<
            BrowserSpotlightFreshWorkspaceBlock<TestHostRejection>.Host>(
                blocked.Reason);
        Assert.Equal(
            "Spotlight intent is no longer current.",
            hostBlock.Result.Message);
        BrowserSpotlightActivationBasis current = await Basis(source);
        Assert.Same(basis.Scope.Revision, current.Scope.Revision);
        Assert.Same(
            basis.Scope.PublicationBase,
            current.Scope.PublicationBase);
        Assert.Same(
            basis.Registrations.Identity,
            current.Registrations.Identity);
        Assert.Equal(1, nonPostingCount);
        Assert.Equal(0, host.PublicationCount);
        Assert.Same(source.Identity, host.ActiveWorkspace);
    }

    [Fact]
    public async Task RejectedHostIntentStartsNoDefinitionsWork()
    {
        await using var source = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(source);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest("Example", "1.0.0")));
        var host = new TestRetainedHost(source.Identity)
        {
            AdmissionRejection = new("retained Workspace capacity reached"),
        };
        int requestCount = 0;
        int restoreCount = 0;

        var blocked = Assert.IsType<ActivationResult.Blocked>(
            await Execute(
                source,
                descriptor,
                host,
                (package, curated, authority) =>
                {
                    requestCount++;
                    return new(package, curated, authority);
                },
                (_, _) =>
                {
                    restoreCount++;
                    return ValueTask.FromResult<RestorationResult>(
                        new RestorationResult.Failed(
                            new TestDefinitionsFailure("unused")));
                },
                _ => ValueTask.FromResult(
                    new TestNonPostingResult())));

        var hostBlock = Assert.IsType<
            BrowserSpotlightFreshWorkspaceBlock<TestHostRejection>.Host>(
                blocked.Reason);
        Assert.Same(host.AdmissionRejection, hostBlock.Result);
        Assert.Equal(0, requestCount);
        Assert.Equal(0, restoreCount);
        Assert.Equal(0, host.PublicationCount);
    }

    [Fact]
    public async Task ExecuteRejectsCoveredPackageAndEveryLibraryPlan()
    {
        await using var source = new InspectionWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(source);
        BrowserSpotlightPackageRequest<TestPackageAction> package =
            PackageRequest("Example", "1.0.0");
        Descriptor coveredPackage = new(
            basis,
            new Destination.Package(package),
            ImmutableArray<
                BrowserSpotlightCoverageWitness<TestPackageAction>>.Empty,
            new Plan.AddCurrentPackage(package, LibraryIntent: null));
        Descriptor library = new(
            basis,
            new Destination.PackageLibrary(
                PackageLibrary(package.Coordinate),
                package,
                new TestLibraryIntent("library")),
            ImmutableArray<
                BrowserSpotlightCoverageWitness<TestPackageAction>>.Empty,
            new Plan.Unavailable(
                BrowserSpotlightDestinationUnavailableReason
                    .UncoveredLibrary));
        var host = new TestRetainedHost(source.Identity);

        foreach (Descriptor descriptor in new[] { coveredPackage, library })
        {
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await Execute(
                    source,
                    descriptor,
                    host,
                    static (candidate, curated, authority) =>
                        new(candidate, curated, authority),
                    static (_, _) =>
                        ValueTask.FromResult<RestorationResult>(
                            new RestorationResult.Failed(
                                new TestDefinitionsFailure("unused"))),
                    static _ => ValueTask.FromResult(
                        new TestNonPostingResult())));
        }
    }

    private static ValueTask<ActivationResult> Execute(
        InspectionWorkspace source,
        Descriptor descriptor,
        TestRetainedHost host,
        BrowserSpotlightWorkspaceRestorationRequestFactory<
            TestPackageAction,
            TestHostAuthority,
            TestDefinitionsRequest> createRequest,
        BrowserSpotlightWorkspaceRestorationOperation<
            TestDefinitionsRequest,
            TestCompleteActivation,
            TestDefinitionsFailure> restore,
        BrowserSpotlightWorkspaceNonPostingOperation<
            TestCompleteActivation,
            TestNonPostingResult> nonPosting) =>
        BrowserSpotlightExternalPackageActivation.ExecuteAsync<
            TestPackageAction,
            TestNavigationAction,
            TestPlatformAction,
            TestLibraryIntent,
            TestHostAuthority,
            TestDefinitionsRequest,
            TestCompleteActivation,
            TestDefinitionsFailure,
            TestHostPublication,
            TestHostRejection,
            TestNonPostingResult>(
                source,
                descriptor,
                EcosystemPackCatalog.CreatePlatformWorkspacePlan(),
                host.Admit,
                createRequest,
                restore,
                host.Publish,
                nonPosting,
                TestContext.Current.CancellationToken);

    private static Descriptor Projected(
        BrowserSpotlightActivationBasis basis,
        Destination destination) =>
        Assert.IsType<ProjectionResult.Projected>(
            BrowserSpotlightDestinationProjection.Project(
                basis,
                destination)).Descriptor;

    private static async Task<BrowserSpotlightActivationBasis> Basis(
        InspectionWorkspace workspace,
        long generation = 1) =>
        new(
            generation,
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot,
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot()).Revision);

    private static BrowserSpotlightPackageRequest<TestPackageAction>
        PackageRequest(string id, string version) =>
        new(
            PackageSourceCoordinate.Create(id, version),
            new TestPackageAction("nuget-org"));

    private static PackageRootBinding Binding()
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
            using Stream source = File.OpenRead(Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Spotlight",
                "package",
                "System.Text.Json.dll"));
            source.CopyTo(destination);
        }

        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(
                    "System.Text.Json",
                    PackageVersion),
                new InMemoryPackageContent(
                    bytes.ToArray(),
                    fromCache: false,
                    producerKey: "nuget-org"),
                producerKey: "nuget-org",
                PackagePayloadOrigin.Download),
            selectionTargetFramework: "net11.0",
            runtimeIdentifier: null);
    }

    private static ExactLibrarySourceCoordinate.Package PackageLibrary(
        PackageSourceCoordinate package)
    {
        using var stream = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "Spotlight",
            "package",
            "System.Text.Json.dll"));
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new(
            package,
            new ManagedMetadataIdentity.Assembly(
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader)));
    }

    internal sealed record TestHostAuthority(
        BrowserSpotlightFreshWorkspaceAuthority Authority,
        object Token);

    internal sealed record TestDefinitionsRequest(
        BrowserSpotlightPackageRequest<TestPackageAction> Package,
        WorkspacePlan CuratedPlan,
        TestHostAuthority HostAuthority);

    internal sealed record TestCompleteActivation(
        InspectionWorkspaceIdentity Workspace);

    internal sealed record TestDefinitionsFailure(string Message);

    internal sealed record TestHostPublication(
        InspectionWorkspaceIdentity Workspace);

    internal sealed record TestHostRejection(string Message);

    internal sealed record TestNonPostingResult;

    private sealed class TestRetainedHost
    {
        private readonly long _generation;
        private TestHostAuthority? _authority;

        internal TestRetainedHost(
            InspectionWorkspaceIdentity activeWorkspace,
            long generation = 1)
        {
            ActiveWorkspace = activeWorkspace;
            Workspaces = [activeWorkspace];
            _generation = generation;
        }

        internal InspectionWorkspaceIdentity ActiveWorkspace { get; private set; }

        internal List<InspectionWorkspaceIdentity> Workspaces { get; }

        internal TestHostAuthority? Authority => _authority;

        internal TestHostPublication? Publication { get; private set; }

        internal TestHostRejection? AdmissionRejection { get; init; }

        internal int PublicationCount { get; private set; }

        internal AdmissionResult Admit(
            BrowserSpotlightFreshWorkspaceAuthority authority)
        {
            if (AdmissionRejection is { } rejected)
                return new AdmissionResult.Rejected(rejected);
            if (authority.ResultGeneration != _generation
                || !ReferenceEquals(
                    authority.SourceWorkspace,
                    ActiveWorkspace))
            {
                return new AdmissionResult.Rejected(
                    new("Spotlight intent is no longer current."));
            }

            _authority = new(authority, new object());
            return new AdmissionResult.Admitted(_authority);
        }

        internal PublicationResult Publish(
            BrowserSpotlightRetainedWorkspacePublicationRequest<
                TestHostAuthority,
                TestCompleteActivation> request)
        {
            if (!ReferenceEquals(request.HostAuthority, _authority)
                || !ReferenceEquals(
                    request.HostAuthority.Authority,
                    request.Authority)
                || request.Authority.ResultGeneration != _generation
                || !ReferenceEquals(
                    request.Authority.SourceWorkspace,
                    ActiveWorkspace))
            {
                return new PublicationResult.Rejected(
                    new("Spotlight intent is no longer current."));
            }

            PublicationCount++;
            Workspaces.Add(request.Activation.Workspace);
            ActiveWorkspace = request.Activation.Workspace;
            Publication = new(request.Activation.Workspace);
            return new PublicationResult.Published(Publication);
        }

        internal void ReplaceActive(
            InspectionWorkspaceIdentity workspace)
        {
            if (!Workspaces.Contains(workspace))
                Workspaces.Add(workspace);
            ActiveWorkspace = workspace;
        }

        internal void SupersedeIntent()
        {
            TestHostAuthority authority =
                _authority
                ?? throw new InvalidOperationException(
                    "No Spotlight intent has been admitted.");
            _authority = new(authority.Authority, new object());
        }
    }
}
