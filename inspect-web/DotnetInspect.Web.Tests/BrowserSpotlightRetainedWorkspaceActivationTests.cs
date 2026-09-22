using System.IO.Compression;
using System.Runtime.Versioning;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

using ActivationResult =
    DotnetInspect.Web.BrowserSpotlightExternalPackageActivationResult<
        DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent,
        DotnetInspect.Web.BrowserRetainedWorkspaceActivationRequest,
        DotnetInspect.Web.BrowserPreparedWorkspaceActivation,
        DotnetInspector.Queries.Definitions.CompleteRestorationFailure,
        DotnetInspect.Web.BrowserRetainedWorkspacePosting,
        DotnetInspect.Web.BrowserRetainedWorkspaceActivationRejection,
        DotnetInspect.Web.BrowserRetainedWorkspaceNonPostingResult>;
using Descriptor = DotnetInspect.Web.BrowserSpotlightDestinationDescriptor<
    DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Destination = DotnetInspect.Web.BrowserSpotlightDestination<
    DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using ProjectionResult =
    DotnetInspect.Web.BrowserSpotlightDestinationProjectionResult<
        DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent>;

namespace DotnetInspect.Web.Tests;

[Collection("Retained Workspace activation")]
[SupportedOSPlatform("browser")]
public sealed partial class BrowserSpotlightRetainedWorkspaceActivationTests
{
    const string ExternalPackageId = "Example.External";
    const string ExternalPackageVersion = "1.0.0";

    [Fact]
    public async Task UncoveredPackagePublishesExactCuratedDefinition()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateSourceAsync(owner);
        WorkspacePlan curated =
            EcosystemPackCatalog.CreatePlatformWorkspacePlan();
        BrowserSpotlightExternalPackageWorkspaceRequest external =
            ExternalRequest(curated);
        Descriptor descriptor = await DescriptorAsync(
            owner,
            external,
            generation: 7);

        ActivationResult result =
            await BrowserSpotlightRetainedWorkspaceActivation.ExecuteAsync(
                owner,
                "source",
                descriptor,
                TestContext.Current.CancellationToken);
        if (result is ActivationResult.RestorationFailed failed)
            Assert.Fail(failed.Result.Message);
        var published = Assert.IsType<ActivationResult.Published>(result);

        BrowserRetainedWorkspacePosting posting =
            published.Publication;
        Assert.Equal("external", posting.RetainedDefinitionId);
        Assert.Same(
            external.Activation.RestorationRequest,
            posting.RestorationRequest);
        Assert.IsType<CompleteRestorationProjection.NonProjectable>(
            posting.Projection);
        Assert.Null(posting.CanonicalPacket);
        Assert.Same(posting, owner.Active);
        Assert.NotNull(posting.Predecessor);
        var settled = Assert.IsType<
            BrowserRetainedWorkspaceSettlementResult.Settled>(
                await owner.ObserveSettlementAsync(
                    posting.Predecessor.SettlementId,
                    TestContext.Current.CancellationToken));
        Assert.True(settled.Settlement.Succeeded);

        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, "external");
        Assert.Equal(
            [ExternalPackageId.ToLowerInvariant()],
            operation.Scope.Packages
                .Select(static package =>
                    package.Occurrence.Package.PackageId)
                .ToArray());
        Assert.Equal(
            curated.TraversalTargetPolicy.TargetFramework,
            Assert.Single(operation.Scope.Packages)
                .Occurrence.Package.RequestedTargetFramework);
        Assert.Equal(
            curated.Registrations,
            operation.Definition.Registrations.Registrations);
        Assert.Equal(
            curated.TraversalTargetPolicy,
            operation.Definition.Plan.TraversalTargetPolicy);
        NavigationConsumerPackageDescriptor navigationPackage =
            Assert.Single(posting.Navigation.Snapshot.Packages);
        Assert.Equal(
            navigationPackage.Subject.Id,
            posting.Navigation.Snapshot.ActivePackage);
        Assert.Equal(
            ExternalPackageId.ToLowerInvariant(),
            navigationPackage.PackageId);
    }

    [Fact]
    public void ConfiguredTraversalTargetIsRejectedBeforeRequestCapture()
    {
        WorkspacePlan platform =
            EcosystemPackCatalog.CreatePlatformWorkspacePlan();
        var configured = new WorkspacePlan(
            new TraversalTargetFrameworkPolicy("net9.0"),
            platform.Registrations);

        Assert.Throws<ArgumentException>(() => ExternalRequest(configured));
    }

    [Fact]
    public async Task SourceRegistrationMovementSettlesCandidateWithoutCutover()
    {
        CompleteRestorationExecutionOptions baseline = await OptionsAsync();
        InspectionWorkspace? sourceWorkspace = null;
        WorkspaceRegistrationRevision? sourceRegistrations = null;
        int projectionCount = 0;
        WorkspacePlan curated =
            EcosystemPackCatalog.CreatePlatformWorkspacePlan();
        CompleteRestorationExecutionOptions options = baseline with
        {
            Projection = request =>
            {
                if (Interlocked.Increment(ref projectionCount) == 2)
                {
                    WorkspaceRegistrationOperationResult moved =
                        sourceWorkspace!.ReplaceRegistrations(
                            sourceRegistrations!,
                            curated.Registrations);
                    Assert.IsType<
                        WorkspaceRegistrationOperationResult.Committed>(
                            moved);
                }
                return CompleteRestorationProjections.Classify(request);
            },
        };
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting sourcePosting =
            await ActivateSourceAsync(owner);
        BrowserSpotlightExternalPackageWorkspaceRequest external =
            ExternalRequest(curated);
        Descriptor descriptor;
        using (WorkspaceRealizationOperationLease source =
            await EnterAsync(owner, "source"))
        {
            sourceWorkspace = source.Workspace;
            sourceRegistrations = source.Definition.Registrations;
            descriptor = Descriptor(
                Basis(source, generation: 11),
                external);
        }

        var stale = Assert.IsType<
            ActivationResult.CompleteButNotPublished>(
                await BrowserSpotlightRetainedWorkspaceActivation.ExecuteAsync(
                    owner,
                    "source",
                    descriptor,
                    TestContext.Current.CancellationToken));

        var sourceBlock = Assert.IsType<
            BrowserSpotlightFreshWorkspaceBlock<
                BrowserRetainedWorkspaceActivationRejection>.Source>(
                    stale.Reason);
        var reason = Assert.IsType<
            BrowserSpotlightActivationBlock.Stale>(sourceBlock.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason.RegistrationRevision,
            reason.Reason);
        Assert.True(stale.NonPosting.Settlement!.Succeeded);
        Assert.Null(stale.NonPosting.Failure);
        Assert.Same(sourcePosting, owner.Active);
        Assert.Equal(1, owner.Capacity.Charged);
    }

    [Fact]
    public async Task CancellationAfterPreparationSettlesCandidateWithoutCutover()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting sourcePosting =
            await ActivateSourceAsync(owner);
        BrowserSpotlightExternalPackageWorkspaceRequest external =
            ExternalRequest(EcosystemPackCatalog.CreatePlatformWorkspacePlan());
        Descriptor descriptor = await DescriptorAsync(
            owner,
            external,
            generation: 13);
        using var cancellation = new CancellationTokenSource();

        var cancelled = Assert.IsType<
            ActivationResult.CompleteButNotPublished>(
                await BrowserSpotlightRetainedWorkspaceActivation.ExecuteAsync(
                    owner,
                    "source",
                    descriptor,
                    cancellation.Token,
                    cancellation.Cancel));

        Assert.IsType<
            BrowserSpotlightFreshWorkspaceBlock<
                BrowserRetainedWorkspaceActivationRejection>.Host>(
                    cancelled.Reason);
        Assert.True(cancelled.NonPosting.Settlement!.Succeeded);
        Assert.Null(cancelled.NonPosting.Failure);
        Assert.False(cancelled.Activation.IsPending);
        Assert.Same(sourcePosting, owner.Active);
        Assert.Equal(1, owner.Capacity.Charged);
    }

    [Fact]
    public async Task ExpectedRealizationAdmissionRejectsReplacement()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var host = new BrowserWorkspaceRealizationHost();
        BrowserWorkspaceRealizationCandidate first =
            await PrepareCandidateAsync(host, WorkspacePlan.Empty);
        BrowserWorkspaceRealizationCutoverResult.Activated firstCutover =
            Assert.IsType<
                BrowserWorkspaceRealizationCutoverResult.Activated>(
                    host.CutOver(first));
        BrowserWorkspaceRealizationCandidate second =
            await PrepareCandidateAsync(host, WorkspacePlan.Empty);
        _ = Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Activated>(
                host.CutOver(second));

        WorkspaceRealizationOperationAdmission admission =
            await host.EnterOperationAsync(
                firstCutover.Realization.Identity,
                TestContext.Current.CancellationToken);

        Assert.IsType<WorkspaceRealizationOperationAdmission.Unavailable>(
            admission);
    }

    static BrowserSpotlightExternalPackageWorkspaceRequest ExternalRequest(
        WorkspacePlan curated) =>
        BrowserSpotlightExternalPackageWorkspaceRequestFactory.Create(
            "external",
            "Example External",
            "/workspace/external",
            PackageSourceCoordinate.Create(
                ExternalPackageId,
                ExternalPackageVersion),
            curated);

    static async Task<Descriptor> DescriptorAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        BrowserSpotlightExternalPackageWorkspaceRequest external,
        long generation)
    {
        using WorkspaceRealizationOperationLease source =
            await EnterAsync(owner, "source");
        return Descriptor(Basis(source, generation), external);
    }

    static Descriptor Descriptor(
        BrowserSpotlightActivationBasis basis,
        BrowserSpotlightExternalPackageWorkspaceRequest external)
    {
        var request =
            new BrowserSpotlightPackageRequest<
                BrowserSpotlightExternalPackageWorkspaceRequest>(
                    external.Package,
                    external);
        return Assert.IsType<ProjectionResult.Projected>(
            BrowserSpotlightDestinationProjection.Project(
                basis,
                new Destination.Package(request))).Descriptor;
    }

    static BrowserSpotlightActivationBasis Basis(
        WorkspaceRealizationOperationLease source,
        long generation) =>
        new(
            generation,
            source.Scope,
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                source.Workspace.GetRegistrationSnapshot()).Revision);

    static async Task<BrowserRetainedWorkspacePosting>
        ActivateSourceAsync(
            BrowserRetainedWorkspaceActivationOwner owner)
    {
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await owner.ActivateAsync(
                    new BrowserRetainedWorkspaceActivationRequest(
                        "source",
                        "Source",
                        "/workspace/source",
                        Packet()),
                    TestContext.Current.CancellationToken));
        return activated.Posting;
    }

    static async ValueTask<WorkspaceRealizationOperationLease> EnterAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string retainedDefinitionId)
    {
        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                retainedDefinitionId,
                TestContext.Current.CancellationToken);
        return Assert.IsType<
            WorkspaceRealizationOperationAdmission.Admitted>(
                admission).Lease;
    }

    static async Task<BrowserWorkspaceRealizationCandidate>
        PrepareCandidateAsync(
            BrowserWorkspaceRealizationHost host,
            WorkspacePlan plan)
    {
        var prepared = Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                await host.BeginCandidateAsync(
                    plan,
                    TestContext.Current.CancellationToken));
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
        }
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                prepared.Candidate,
                TestContext.Current.CancellationToken));
        return prepared.Candidate;
    }

    static string Packet()
    {
        const int schemaVersion = InspectionDefinitionSchema.Version3;
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json",
            "9.0.4",
            "net9.0");
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    "net9.0",
                    members: [package]),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            schemaVersion,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: package)],
            focus: "package"));
        registry.Add(new CommittedViewDefinition(
            schemaVersion,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package()),
            ]));
        registry.Add(new ScenarioDefinition(
            schemaVersion,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));
        var definitions = Assert.IsType<
            InspectionDefinitionScenarioPreparationResult.Version3>(
                registry.PrepareScenario("scenario")).Definitions;
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(definitions);
        return WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
    }

    static async Task<CompleteRestorationExecutionOptions> OptionsAsync()
    {
        const string sourceUrl = "https://api.nuget.org/v3/index.json";
        byte[] sourcePackage = await File.ReadAllBytesAsync(
            Path.Combine(
                FindRepositoryRoot(),
                "fixtures",
                "services",
                "signatures",
                "system.text.json.9.0.4.nupkg"),
            TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                "System.Text.Json",
                "9.0.4",
                sourcePackage,
                fromCache: false,
                producerKey: NuGetCache.GetSourceKey(sourceUrl)));
        byte[] assembly = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Spotlight",
                "package",
                "System.Text.Json.dll"),
            TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                ExternalPackageId,
                ExternalPackageVersion,
                Archive(("lib/net11.0/System.Text.Json.dll", assembly)),
                fromCache: false,
                producerKey: NuGetCache.GetSourceKey(sourceUrl)));

        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        var available = new ViewFacetAvailabilitySnapshot(
            facets.Descriptors.Select(
                static descriptor =>
                    new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        ViewFacetAvailability.Available.Instance)));
        return new CompleteRestorationExecutionOptions
        {
            ContextLoad = new WorkspaceContextLoadOptions
            {
                HttpClient = new HttpClient(new RejectingHandler()),
                SourceAuthorization =
                    new UniformPackageSourceAuthorization(
                        [new PackageSource("nuget.org", sourceUrl)]),
                PackageStore = BrowserPackageWorkspace.SessionPackageStore,
            },
            ScopeDeadline = DateTimeOffset.UtcNow.AddMinutes(1),
            Facets = facets,
            FacetAvailability = (_, _) => available,
        };
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    static byte[] Archive(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(path).Open();
                stream.Write(content);
            }
        }
        return buffer.ToArray();
    }

    sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
