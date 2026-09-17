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
    public async Task A_B_A_RestoresWholeWorkspaceAndSelectedPackage()
    {
        const string foo = "FooPackage";
        const string bar = "BarPackage";
        const string baz = "BazPackage";
        CompleteRestorationExecutionOptions options =
            await MultiPackageOptionsAsync();
        string firstPacket = Packet([foo, bar], selectedIndex: 1);
        string secondPacket = Packet([baz, bar], selectedIndex: 0);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspaceInstallation first =
            await ActivateAsync(owner, "workspace-1", firstPacket);
        using WorkspaceRealizationOperationLease firstOperation =
            await EnterAsync(owner, "workspace-1");
        AssertWorkspace(
            first,
            firstOperation,
            [foo, bar],
            selectedIndex: 1);
        BrowserRetainedWorkspaceInstallation second =
            await ActivateAsync(owner, "workspace-2", secondPacket);
        using (WorkspaceRealizationOperationLease secondOperation =
            await EnterAsync(owner, "workspace-2"))
        {
            AssertWorkspace(
                second,
                secondOperation,
                [baz, bar],
                selectedIndex: 0);
        }

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
            await ActivateAsync(owner, "workspace-1", firstPacket);
        using WorkspaceRealizationOperationLease thirdOperation =
            await EnterAsync(owner, "workspace-1");
        AssertWorkspace(
            third,
            thirdOperation,
            [foo, bar],
            selectedIndex: 1);
        Assert.NotEqual(first.RealizationId, third.RealizationId);
        Assert.NotEqual(second.RealizationId, third.RealizationId);
        Assert.True(
            first.PublicationOrdinal < second.PublicationOrdinal
            && second.PublicationOrdinal < third.PublicationOrdinal);
        Assert.Equal(
            "workspace-1",
            owner.Active!.RetainedDefinitionId);
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
        return activated.Installation;
    }

    static void AssertWorkspace(
        BrowserRetainedWorkspaceInstallation installation,
        WorkspaceRealizationOperationLease operation,
        IReadOnlyList<string> expectedPackages,
        int selectedIndex)
    {
        Assert.Equal(
            [.. expectedPackages.Order(StringComparer.Ordinal)],
            [.. operation.Scope.Packages
                .Select(package => package.Occurrence.Package.PackageId)
                .Order(StringComparer.Ordinal)]);
        Assert.Equal(
            selectedIndex + 1,
            installation.Navigation.ActiveStateIndex);
        Assert.Equal(
            expectedPackages[selectedIndex],
            operation.Scope.Packages[selectedIndex]
                .Occurrence.Package.PackageId);
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
        int schemaVersion = InspectionDefinitionSchema.Version3) =>
        Packet(
            ["System.Text.Json"],
            selectedIndex: 0,
            packageVersion: "9.0.4",
            framework: "net9.0",
            schemaVersion);

    static string Packet(
        IReadOnlyList<string> packageIds,
        int selectedIndex,
        string packageVersion = "11.0.0-preview.7.26381.103",
        string framework = "net10.0",
        int schemaVersion = InspectionDefinitionSchema.Version3)
    {
        if ((uint)selectedIndex >= (uint)packageIds.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        var registry = new InspectionDefinitionRegistry();
        var packages = packageIds
            .Select(packageId =>
                new DefinitionMemberCoordinate.PackageCoordinate(
                    packageId,
                    packageVersion,
                    framework))
            .ToArray();
        string[] tabIds = packages
            .Select((_, index) => $"package-{index}")
            .ToArray();
        registry.Add(new WorkspaceDefinition(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework,
                    members: packages),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            schemaVersion,
            "navigation",
            [.. packages.Select(
                (package, index) => new NavigationTabDefinition(
                    tabIds[index],
                    coordinate: package))],
            focus: tabIds[selectedIndex]));
        registry.Add(new CommittedViewDefinition(
            schemaVersion,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                .. tabIds.Select(tabId =>
                    new CommittedViewStateDefinition(
                        tabId,
                        new PortableSubjectRequest.Package(),
                        new PortableRetainedSubjectContext.Package())),
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

    static async Task<CompleteRestorationExecutionOptions>
        MultiPackageOptionsAsync()
    {
        const string sourceUrl = "https://api.nuget.org/v3/index.json";
        PackageFixture[] fixtures =
        [
            new(
                "FooPackage",
                "System.Text.Json",
                "Spotlight/package"),
            new(
                "BarPackage",
                "Microsoft.Extensions.Logging",
                "PlatformDemo"),
            new(
                "BazPackage",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                "PlatformDemo"),
        ];
        var store = new InMemoryPackageStore();
        foreach (PackageFixture fixture in fixtures)
        {
            byte[] assembly = await File.ReadAllBytesAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    fixture.AssetDirectory,
                    $"{fixture.AssemblyName}.dll"),
                TestContext.Current.CancellationToken);
            byte[] package = Archive(
                ($"lib/net10.0/{fixture.AssemblyName}.dll", assembly));
            await store.CommitAsync(
                fixture.PackageId,
                "11.0.0-preview.7.26381.103",
                NuGetCache.GetSourceKey(sourceUrl),
                new MemoryStream(package, writable: false),
                TestContext.Current.CancellationToken);
        }

        CompleteRestorationExecutionOptions baseline = await OptionsAsync();
        return baseline with
        {
            ContextLoad = baseline.ContextLoad with
            {
                PackageStore = store,
            },
        };
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

    static byte[] Archive(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
            buffer,
            System.IO.Compression.ZipArchiveMode.Create,
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

    sealed record PackageFixture(
        string PackageId,
        string AssemblyName,
        string AssetDirectory);

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
