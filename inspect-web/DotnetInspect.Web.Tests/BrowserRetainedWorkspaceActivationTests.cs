using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserRetainedWorkspaceActivationTests
{
    [Fact]
    public async Task A_B_A_UsesFreshRealizationsAndObservableSettlement()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspaceInstallation first =
            await ActivateAsync(owner, "a", packet);
        using WorkspaceRealizationOperationLease firstOperation =
            await EnterAsync(owner, "a");
        BrowserRetainedWorkspaceInstallation second =
            await ActivateAsync(owner, "b", packet);

        Assert.NotNull(second.Predecessor);
        Task<BrowserRetainedWorkspaceSettlementResult> observation =
            owner.ObserveSettlementAsync(
                second.Predecessor.SettlementId,
                TestContext.Current.CancellationToken);
        Assert.False(observation.IsCompleted);
        firstOperation.Dispose();
        var settled = Assert.IsType<
            BrowserRetainedWorkspaceSettlementResult.Settled>(
                await observation);
        Assert.True(settled.Settlement.Succeeded);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.Replaced,
            settled.Settlement.Reason);

        BrowserRetainedWorkspaceInstallation third =
            await ActivateAsync(owner, "a", packet);
        Assert.NotEqual(first.RealizationId, third.RealizationId);
        Assert.NotEqual(second.RealizationId, third.RealizationId);
        Assert.True(
            first.PublicationOrdinal < second.PublicationOrdinal
            && second.PublicationOrdinal < third.PublicationOrdinal);
        Assert.Equal("a", owner.Active!.RetainedDefinitionId);
    }

    [Fact]
    public async Task ActiveSelection_IsTypedNoEffect()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceInstallation first =
            await ActivateAsync(owner, "a", packet);

        var noEffect = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.NoEffect>(
                await owner.ActivateAsync(
                    Request("a", packet),
                    TestContext.Current.CancellationToken));

        Assert.Equal(first.RealizationId, noEffect.Installation.RealizationId);
        Assert.Equal(
            first.PublicationOrdinal,
            noEffect.Installation.PublicationOrdinal);
        Assert.Equal(1, owner.Capacity.Charged);
    }

    [Fact]
    public async Task InvalidAndLegacyPackets_PreserveIncumbentWithoutCharge()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceInstallation incumbent =
            await ActivateAsync(owner, "a", packet);

        var invalid = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await owner.ActivateAsync(
                    Request("invalid", "not-a-packet"),
                    TestContext.Current.CancellationToken));
        Assert.IsType<CompleteRestorationFailure.InvalidPacket>(
            invalid.Failure);
        Assert.Equal(1, owner.Capacity.Charged);

        var legacy = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await owner.ActivateAsync(
                    Request("legacy", LegacyPacket()),
                    TestContext.Current.CancellationToken));
        Assert.IsType<CompleteRestorationFailure.UnsupportedVersion>(
            legacy.Failure);
        Assert.Equal(1, owner.Capacity.Charged);
        Assert.Equal(
            incumbent.RealizationId,
            owner.Active!.RealizationId);
    }

    [Fact]
    public async Task Format2Packet_RemainsActivatable()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet(InspectionDefinitionSchema.Version2);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspaceInstallation installation =
            await ActivateAsync(owner, "format-2", packet);

        Assert.Equal(
            WorkspaceSharePacketCodec.Format2Version,
            WorkspaceSharePacketCodec.Decode(
                packet,
                TestContext.Current.CancellationToken).FormatVersion);
        Assert.Equal(packet, installation.CanonicalPacket);
    }

    [Fact]
    public async Task NewSelection_SupersedesBlockedPreparation()
    {
        CompleteRestorationExecutionOptions baseline = await OptionsAsync();
        int projectionCount = 0;
        BrowserRetainedWorkspaceActivationOwner? owner = null;
        Task<BrowserRetainedWorkspaceActivationResult>? replacement = null;
        string packet = Packet();
        CompleteRestorationExecutionOptions options = baseline with
        {
            Projection = request =>
            {
                if (Interlocked.Increment(ref projectionCount) == 1)
                {
                    replacement = owner!.ActivateAsync(
                        Request("b", packet),
                        TestContext.Current.CancellationToken);
                }
                return CompleteRestorationProjections.Classify(request);
            },
        };
        await using var activationOwner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        owner = activationOwner;

        BrowserRetainedWorkspaceActivationResult first =
            await activationOwner.ActivateAsync(
                Request("a", packet),
                TestContext.Current.CancellationToken);

        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            first);
        Assert.NotNull(replacement);
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await replacement);
        Assert.Equal("b", activated.Installation.RetainedDefinitionId);
        Assert.Equal("b", activationOwner.Active!.RetainedDefinitionId);
    }

    [Fact]
    public async Task SoleActiveDeactivation_ClosesAndReopensHost()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateAsync(owner, "a", packet);

        var deactivated = Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.Deactivated>(
                await owner.DeactivateAsync(
                    "a",
                    TestContext.Current.CancellationToken));
        Assert.True(deactivated.Settlement.Succeeded);
        Assert.Null(owner.Active);
        Assert.Equal(0, owner.Capacity.Charged);

        BrowserRetainedWorkspaceInstallation replacement =
            await ActivateAsync(owner, "b", packet);
        Assert.Equal("b", replacement.RetainedDefinitionId);
    }

    [Fact]
    public async Task DeactivationDrain_BlocksReplacementHostAdmission()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateAsync(owner, "a", packet);
        WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, "a");

        Task<BrowserRetainedWorkspaceDeactivationResult> deactivation =
            owner.DeactivateAsync(
                "a",
                TestContext.Current.CancellationToken);
        await Task.Yield();
        Assert.False(deactivation.IsCompleted);
        var unavailable = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await owner.ActivateAsync(
                    Request("b", packet),
                    TestContext.Current.CancellationToken));
        Assert.IsType<CompleteRestorationFailure.HostConstructionFailed>(
            unavailable.Failure);
        Assert.Equal(1, owner.Capacity.Charged);

        operation.Dispose();
        Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.Deactivated>(
                await deactivation);
        _ = await ActivateAsync(owner, "b", packet);
    }

    static BrowserRetainedWorkspaceActivationRequest Request(
        string id,
        string packet) =>
        new(id, $"Workspace {id}", $"/workspace/{id}", packet);

    static async Task<BrowserRetainedWorkspaceInstallation> ActivateAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string id,
        string packet)
    {
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await owner.ActivateAsync(
                    Request(id, packet),
                    TestContext.Current.CancellationToken));
        Assert.Equal(packet, activated.Installation.CanonicalPacket);
        Assert.Equal(2, activated.Installation.Navigation.States.Length);
        Assert.Equal(1, activated.Installation.Navigation.ActiveStateIndex);
        return activated.Installation;
    }

    static async ValueTask<WorkspaceRealizationOperationLease> EnterAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string id)
    {
        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                id,
                TestContext.Current.CancellationToken);
        return Assert.IsType<
            WorkspaceRealizationOperationAdmission.Admitted>(
                admission).Lease;
    }

    static string Packet(
        int schemaVersion = InspectionDefinitionSchema.Version3)
    {
        var registry = new InspectionDefinitionRegistry();
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json",
            "9.0.4",
            "net9.0");
        registry.Add(new WorkspaceDefinition(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: "net9.0",
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
        CommittedScenarioDefinitionSet definitions =
            registry.PrepareScenario("scenario") switch
            {
                InspectionDefinitionScenarioPreparationResult.Version2 version2 =>
                    version2.Definitions,
                InspectionDefinitionScenarioPreparationResult.Version3 version3 =>
                    version3.Definitions,
                _ => throw new InvalidOperationException(
                    "Retained activation tests require schema version 2 or 3."),
            };
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);
        return WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
    }

    static string LegacyPacket() =>
        WorkspaceSharePacketCodec.Encode(
            new WorkspaceSharePacket(
                [
                    new WorkspaceShareTab(
                        WorkspaceShareSourceKind.Package,
                        "System.Text.Json",
                        "9.0.4",
                        "net9.0",
                        runtimeIdentifier: null),
                ],
                [new WorkspaceShareContext([0])],
                activeTabIndex: 0,
                selectedContextIndex: 0,
                lens: null,
                type: null,
                memberAnchor: null,
                memberSignature: null,
                section: null,
                libraries: []));

    static async Task<CompleteRestorationExecutionOptions> OptionsAsync()
    {
        const string sourceUrl = "https://api.nuget.org/v3/index.json";
        string packagePath = Path.Combine(
            FindRepositoryRoot(),
            "fixtures",
            "services",
            "signatures",
            "system.text.json.9.0.4.nupkg");
        byte[] package = await File.ReadAllBytesAsync(
            packagePath,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            "System.Text.Json",
            "9.0.4",
            NuGetCache.GetSourceKey(sourceUrl),
            new MemoryStream(package, writable: false),
            TestContext.Current.CancellationToken);
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
                PackageStore = store,
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

    sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
