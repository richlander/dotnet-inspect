using System.IO.Compression;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition(
    "Retained Workspace activation",
    DisableParallelization = true)]
public sealed class BrowserRetainedWorkspaceActivationCollection;

[Collection("Retained Workspace activation")]
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
    public async Task CompleteSavedWorkspace_InstallsWorkspaceRootAndFocusedPackage()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = CompleteWorkspaceRootPacket();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspaceInstallation installation =
            await ActivateAsync(owner, "saved", packet);

        Assert.Equal(0, installation.Navigation.ActiveStateIndex);
        Assert.Equal("t0", installation.Definition.ActiveTabId);
        Assert.Equal("g0", installation.Definition.SelectedContextId);
        Assert.Equal(
            WorkspaceSharePacketCodec.CurrentFormatVersion,
            WorkspaceSharePacketCodec.Decode(
                packet,
                TestContext.Current.CancellationToken).FormatVersion);
    }

    [Fact]
    public async Task ProductionOptions_PackageOnlyActivationHasNoPlatformBuilderFailure()
    {
        await SeedProductionPackageAsync(
            "System.Text.Json",
            "9.0.4",
            Path.Combine(
                FindRepositoryRoot(),
                "fixtures",
                "services",
                "signatures",
                "system.text.json.9.0.4.nupkg"));
        await using (
            var owner = new BrowserRetainedWorkspaceActivationOwner(
                BrowserCompleteRestorationOptions.Create))
        {
            BrowserRetainedWorkspaceInstallation installation =
                await ActivateAsync(owner, "package-only", Packet());

            Assert.Single(installation.Packages);
            Assert.Equal("package", installation.Packages[0].Kind);
        }
        await DrainProductionCacheAsync();
    }

    [Fact]
    public async Task ProductionOptions_PlatformActivationProjectsExactSurface()
    {
        string assets = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "FrameworkActivation");
        await SeedProductionPackageAsync(
            "Microsoft.NETCore.App.Ref",
            "10.0.10",
            Path.Combine(
                assets,
                "microsoft.netcore.app.ref.10.0.10.nupkg"));
        await SeedProductionPackageAsync(
            "Microsoft.NETCore.App.Runtime.linux-x64",
            "10.0.10",
            Path.Combine(
                assets,
                "microsoft.netcore.app.runtime.linux-x64.10.0.10.nupkg"));
        await using (
            var owner = new BrowserRetainedWorkspaceActivationOwner(
                BrowserCompleteRestorationOptions.Create))
        {
            BrowserRetainedWorkspaceActivationResult result =
                await owner.ActivateAsync(
                    Request("platform", PlatformPacket()),
                    TestContext.Current.CancellationToken);
            var activated = Assert.IsType<
                BrowserRetainedWorkspaceActivationResult.Activated>(result);
            BrowserRetainedWorkspaceInstallation installation =
                activated.Installation;

            BrowserRetainedWorkspacePackage platform =
                Assert.Single(installation.Packages);
            Assert.Equal("platform", platform.Kind);
            Assert.Equal("Microsoft.NETCore.App", platform.Surface.Package);
            Assert.Equal("net10.0", platform.Surface.ActiveFramework);
        }
        await DrainProductionCacheAsync();
    }

    [Fact]
    public async Task RegistrationOnlyPacket_ActivatesWorkspaceOnlyPresentation()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await owner.ActivateAsync(
                    Request(
                        "registration-only",
                        RegistrationOnlyPacket(),
                        presentationActiveTabIndex: null),
                    TestContext.Current.CancellationToken));

        Assert.Empty(activated.Installation.Definition.Tabs);
        Assert.Empty(activated.Installation.Definition.Contexts);
        Assert.Null(activated.Installation.Definition.ActiveTabId);
        Assert.Null(activated.Installation.Definition.SelectedContextId);
        Assert.Empty(activated.Installation.Packages);
    }

    [Fact]
    public async Task PackagePresentation_PreservesFrameworksAndDocuments()
    {
        CompleteRestorationExecutionOptions options =
            await OptionsAsync(includeReadme: true);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspaceInstallation installation =
            await ActivateAsync(owner, "package-presentation", Packet());

        BrowserPackageSurfaceInfo surface =
            Assert.Single(installation.Packages).Surface;
        Assert.Contains("net9.0", surface.Frameworks);
        Assert.True(surface.Frameworks.Length > 1);
        BrowserPackageDocumentEntry document =
            Assert.Single(
                surface.Documents,
                static document => document.Kind == "readme");
        Assert.Equal("readme", document.Kind);
        Assert.Equal("README.md", document.Path);
    }

    [Fact]
    public async Task PackageActionRequiresExactActiveRealizationAdmission()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceInstallation first =
            await ActivateAsync(owner, "a", packet);

        Assert.IsType<
            BrowserRetainedWorkspacePackageActivationResult.Activated>(
                await owner.ActivatePackageAsync(
                    first.RetainedDefinitionId,
                    first.RealizationId,
                    "t0",
                    TestContext.Current.CancellationToken));

        _ = await ActivateAsync(owner, "b", packet);
        Assert.IsType<
            BrowserRetainedWorkspacePackageActivationResult.Superseded>(
                await owner.ActivatePackageAsync(
                    first.RetainedDefinitionId,
                    first.RealizationId,
                    "t0",
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreparedCandidateRequiresConsumerCommitBeforeCutover()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceActivationSession rejected =
            owner.BeginActivation(
                Request("rejected", Packet()),
                TestContext.Current.CancellationToken);

        Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(
            await rejected.Preparation);
        Assert.Null(owner.Active);
        rejected.Supersede();
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            await rejected.Completion);
        Assert.Null(owner.Active);

        BrowserRetainedWorkspaceActivationSession accepted =
            owner.BeginActivation(
                Request("accepted", Packet()),
                TestContext.Current.CancellationToken);
        Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(
            await accepted.Preparation);
        Assert.True(accepted.Commit());
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await accepted.Completion);
        Assert.Equal(
            activated.Installation.RealizationId,
            owner.Active?.RealizationId);
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
    public async Task CleanupFailedDeactivation_RemovesActiveAuthority()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateAsync(owner, "a", packet);
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, "a"))
        {
            RegisterThrowingResource(operation.Workspace);
        }

        var failed = Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.CleanupFailed>(
                await owner.DeactivateAsync(
                    "a",
                    TestContext.Current.CancellationToken));

        Assert.False(failed.Settlement.Succeeded);
        Assert.Null(owner.Active);
        Assert.Single(owner.Capacity.FailedSettlements);
        Assert.IsType<
            WorkspaceRealizationOperationAdmission.Unavailable>(
                await owner.EnterOperationAsync(
                    "a",
                    TestContext.Current.CancellationToken));
        AggregateException cleanup = await Assert.ThrowsAsync<
            AggregateException>(
                () => owner.DisposeAsync().AsTask());
        Assert.IsType<WorkspaceRealizationSettlementException>(
            Assert.Single(cleanup.InnerExceptions));
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
        string packet,
        int? presentationActiveTabIndex = 0) =>
        new(
            $"activation-{id}-{Guid.NewGuid():N}",
            id,
            $"Workspace {id}",
            $"/workspace/{id}",
            packet,
            presentationActiveTabIndex);

    static async Task<BrowserRetainedWorkspaceInstallation> ActivateAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string id,
        string packet)
    {
        BrowserRetainedWorkspaceActivationResult result =
            await owner.ActivateAsync(
                Request(id, packet),
                TestContext.Current.CancellationToken);
        Assert.True(
            result
                is BrowserRetainedWorkspaceActivationResult.Activated,
            (result as BrowserRetainedWorkspaceActivationResult.Failed)
                ?.Failure.Message);
        var activated =
            (BrowserRetainedWorkspaceActivationResult.Activated)result;
        Assert.Equal(packet, activated.Installation.CanonicalPacket);
        Assert.Equal(2, activated.Installation.Navigation.States.Length);
        BrowserRetainedWorkspacePackage package =
            Assert.Single(activated.Installation.Packages);
        Assert.Equal("package", package.Kind);
        Assert.Equal("t0", package.NavigationId);
        Assert.Equal("System.Text.Json", package.Surface.Package);
        Assert.Equal("9.0.4", package.Surface.Version);
        Assert.Equal("t0", activated.Installation.Definition.ActiveTabId);
        Assert.Equal("g0", activated.Installation.Definition.SelectedContextId);
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

    static string CompleteWorkspaceRootPacket()
    {
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json",
            "9.0.4",
            "net9.0");
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket(
                new WorkspaceSharePacketDefinitionSet(
                    new WorkspaceDefinition(
                        InspectionDefinitionSchema.Version1,
                        WorkspaceSharePacketTransposer.WorkspaceId,
                        [
                            new WorkspaceContextDefinition(
                                "g0",
                                "net9.0",
                                members: [package]),
                        ]),
                    new NavigationDefinition(
                        InspectionDefinitionSchema.Version1,
                        WorkspaceSharePacketTransposer.NavigationId,
                        [
                            new NavigationTabDefinition(
                                "t0",
                                coordinate: package),
                        ],
                        "t0"),
                    new ViewDefinition(
                        InspectionDefinitionSchema.Version1,
                        WorkspaceSharePacketTransposer.ViewId),
                    new ScenarioDefinition(
                        InspectionDefinitionSchema.Version1,
                        WorkspaceSharePacketTransposer.ScenarioId,
                        workspace: WorkspaceSharePacketTransposer.WorkspaceId,
                        context: "g0",
                        view: WorkspaceSharePacketTransposer.ViewId,
                        navigation:
                            WorkspaceSharePacketTransposer.NavigationId)),
                TestContext.Current.CancellationToken);
        Assert.True(
            projection.Succeeded,
            projection.Failure?.Message);
        return WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
    }

    static async Task<CompleteRestorationExecutionOptions> OptionsAsync(
        bool includeReadme = false)
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
        if (includeReadme)
            package = AddReadme(package);
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

    static byte[] AddReadme(byte[] package)
    {
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using var input = new ZipArchive(
                new MemoryStream(package, writable: false),
                ZipArchiveMode.Read);
            foreach (ZipArchiveEntry source in input.Entries)
            {
                ZipArchiveEntry target =
                    destination.CreateEntry(source.FullName);
                using Stream sourceStream = source.Open();
                using Stream targetStream = target.Open();
                sourceStream.CopyTo(targetStream);
            }
            ZipArchiveEntry readme =
                destination.CreateEntry("README.md");
            using var writer = new StreamWriter(readme.Open());
            writer.Write("# System.Text.Json");
        }
        return output.ToArray();
    }

    static async Task SeedProductionPackageAsync(
        string packageId,
        string version,
        string path)
    {
        byte[] package = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        using BrowserPackageWorkspace.PackageDownloadReservation reservation =
            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                BrowserPackageWorkspace.PackageKey(packageId, version),
                package.LongLength);
        await BrowserPackageWorkspace.SessionPackageStore.CommitAsync(
            packageId,
            version,
            NuGetCache.GetSourceKey(
                "https://api.nuget.org/v3/index.json"),
            new MemoryStream(package, writable: false),
            TestContext.Current.CancellationToken);
        reservation.Complete();
    }

    static async Task DrainProductionCacheAsync()
    {
        using BrowserPackageWorkspace.PackageDownloadReservation reservation =
            await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"retained-activation-drain-{Guid.NewGuid():N}",
                128L * 1024 * 1024);
    }

    static string RegistrationOnlyPacket() =>
        WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                """
                {"f":3,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}
                """,
                TestContext.Current.CancellationToken));

    static string PlatformPacket()
        => WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                """
                {"f":3,"t":[[":Platform","10.0.10","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
                """,
                TestContext.Current.CancellationToken));

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

    static void RegisterThrowingResource(InspectionWorkspace workspace)
    {
        System.Reflection.FieldInfo? groupsField =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(groupsField);
        var groups = Assert.IsType<List<AssemblyContextGroup>>(
            groupsField.GetValue(workspace));
        AssemblyContextGroup group = Assert.Single(groups);
        System.Reflection.MethodInfo? register =
            typeof(AssemblyContextGroup).GetMethod(
                "RegisterOwnedResource",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(register);
        _ = register.Invoke(group, [new ThrowingResource()]);
    }

    sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }

    sealed class ThrowingResource : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException("Injected cleanup failure.");
    }
}
