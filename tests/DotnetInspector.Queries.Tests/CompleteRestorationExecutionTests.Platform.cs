using System.Text.Json;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries.Tests;

public sealed partial class CompleteRestorationExecutionTests
{
    [Fact]
    public async Task OriginalPlatformPacket_DirectRestoreNeverConstructs()
    {
        var authority = new TestIntentAuthority();
        var preparation = Assert.IsType<CompleteRestorationPreparationResult.Failed>(
            CompleteRestorationPreparation.FromPacket(
                CompleteRestorationPreparationTests.OriginalPlatformPacket,
                authority, TestContext.Current.CancellationToken));
        using var client = new HttpClient(new RejectingHandler());
        var failed = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Failed>(
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation, authority, new NeverConstructHost(),
                Options(client, []), TestContext.Current.CancellationToken));
        Assert.Equal(1,
            Assert.IsType<CompleteRestorationFailure.UnsupportedVersion>(
                failed.Failure).Version);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 3)]
    public async Task SourceNativePlatform_RestoresAndBorrowsExactLibrary(
        bool selectType, bool legacyUpgrade, int expectedFailure)
    {
        const string rc1 = "11.0.0-rc.1.26425.128";
        const string rc3 = "11.0.0-rc.3.26522.148";
        string version = legacyUpgrade ? rc1 : rc3;
        string path = legacyUpgrade
            ? Path.Combine(
                Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                    "..", "..", "..")),
                "packs", "Microsoft.NETCore.App.Ref", rc1, "ref", "net11.0",
                "System.Text.Json.dll")
            : typeof(JsonElement).Assembly.Location;
        if (!File.Exists(path))
            Assert.Skip($"The exact RC1 reference asset is not installed: {path}");
        var source = ResolvedAssemblyReference.CreateFromPath(
            path, AssemblyResolutionProvenance.Platform(
                "Microsoft.NETCore.App", version,
                "installed runtime"));
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(version));
        byte[] image = await File.ReadAllBytesAsync(
            path, TestContext.Current.CancellationToken);
        PortableLibraryIdentity library = PortableIdentityProjection
            .FromAssembly(source.Identity);
        PortableLibraryIdentity selectedLibrary = expectedFailure == 2
            ? new("Unavailable.Assembly", library.Version, library.Culture,
                library.PublicKeyToken)
            : library;
        MetadataTypeDefinitionName name =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System.Text.Json", ["JsonElement"])).Name;
        WorkspaceSharePacket packet = WorkspaceSharePacket.CreateV6(
            [new(WorkspaceShareSourceKind.Group, ":Platform",
                version, "net11.0", null)],
            [new([0])], [], 0, 0,
            [
                new(null, new PortableSubjectRequest.Workspace(), null, null),
                selectType
                    ? new WorkspaceShareViewState(0,
                        new PortableSubjectRequest.Type(),
                        new PortableRetainedSubjectContext.Type(selectedLibrary, name),
                        "type.overview")
                    : new WorkspaceShareViewState(0,
                        new PortableSubjectRequest.Library(),
                        new PortableRetainedSubjectContext.Library(selectedLibrary),
                        "library.types"),
            ]);
        string encoded = legacyUpgrade
            ? selectType
                ? CompleteRestorationPreparationTests.OriginalPlatformPacket
                : WorkspaceSharePacketCodec.Encode(
                    WorkspaceSharePacketCodec.ParseJson(
                        WorkspaceSharePacketCodec.SerializeJson(
                            WorkspaceSharePacketCodec.Decode(
                                CompleteRestorationPreparationTests
                                    .OriginalPlatformPacket,
                                TestContext.Current.CancellationToken))
                            .Replace(
                                "\"y\":\"System.Text.Json:System.Text.Json.JsonElement\",",
                                "", StringComparison.Ordinal),
                        TestContext.Current.CancellationToken))
            : WorkspaceSharePacketCodec.Encode(packet);
        if (expectedFailure == 3)
            encoded = WorkspaceSharePacketCodec.Encode(
                WorkspaceSharePacketCodec.ParseJson(
                    WorkspaceSharePacketCodec.SerializeJson(
                        WorkspaceSharePacketCodec.Decode(
                            encoded, TestContext.Current.CancellationToken))
                        .Replace("System.Text.Json.dll",
                            "Unavailable.dll", StringComparison.Ordinal),
                    TestContext.Current.CancellationToken));
        var authority = new TestIntentAuthority();
        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            legacyUpgrade
                ? CompleteRestorationPreparation.UpgradeLegacyPlatformPacket(
                    encoded, authority, TestContext.Current.CancellationToken)
                : CompleteRestorationPreparation.FromPacket(
                    encoded, authority, TestContext.Current.CancellationToken));
        using var client = new HttpClient(new RejectingHandler());
        LibraryContentOwner? directOwner = null;
        LibraryContentOwner? admittedOwner = null;
        ArtifactSetSession? artifacts = null;
        var host = new TestHost
        {
            BeforeClose = async () =>
            {
                if (directOwner is not null)
                    await directOwner.DisposeAsync();
                if (admittedOwner is not null)
                    await admittedOwner.DisposeAsync();
            },
        };
        var nameBounds = new LibraryTypeDeclarationInventoryInspectionBounds(
            image.Length, expectedFailure == 1 ? 0 : 100_000,
            1_000_000, 10_000_000);
        CompleteRestorationExecutionOptions options =
            Options(client, image) with
            {
                CaptureSelectedSubject = true,
                NativeNameBounds = nameBounds,
                AdmitPlatform = async (workspace, requested, cancellationToken) =>
                {
                    Assert.Equal(target, requested);
                    using AssemblyContextGroup group =
                        workspace.CreateAssemblyContextGroup(
                            [new AssemblyContextParticipant(
                                source, new NativeBindingPolicy())]);
                    var materialized = Assert.IsType<
                        AssemblyContextLibraryAdapterResult.Completed>(
                        await AssemblyContextLibraryAdapter.MaterializeAsync(
                            group, Assert.Single(group.Participants),
                            AssemblyContextLibraryRole.ApiOnly,
                            new(image.Length, image.Length),
                            cancellationToken));
                    LibraryReference reference = materialized.Owner.Reference;
                    directOwner = materialized.Owner;
                    artifacts = materialized.Artifacts;
                    ManagedMetadataIdentity.Assembly identity =
                        reference.ApiAssembly.AssemblyIdentity!;
                    LibraryReference platform =
                        LibraryReference.CreateFromSource(
                            new ExactLibrarySourceCoordinate.Platform(
                                new PlatformLibraryPopulationDeclaration(
                                    PlatformFamily.DotNetRuntime),
                                identity),
                            new LibraryAssemblyCorrespondence(
                                reference.ApiAssembly.ArtifactReference, identity,
                                reference.ApiAssembly.ArtifactReference, identity));
                    using ArtifactQueryLease lease =
                        materialized.Artifacts.IssueLease(
                            materialized.Artifacts.CreateQueryAuthorization());
                    var owner = new LibraryContentOwner(
                        platform,
                        [materialized.Artifacts.IssueContentLease(
                            reference.ApiAssembly.ArtifactReference, lease)]);
                    admittedOwner = owner;
                    WorkspaceLibraryAdmissionReceipt admission =
                        Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                            await workspace.AdmitLibraryBatchAsync(
                                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                                    workspace.GetRegistrationSnapshot()).Revision,
                                materialized.Artifacts, [owner])).Receipt;
                    var contextOutcome =
                        WorkspaceLibraryDeclarationContextAdmission.Admit(
                                workspace, admission,
                                new WorkspaceDeclarationRequest.PlatformPopulation(
                                    target),
                                [new WorkspaceLibraryDeclarationContextMember(
                                    platform.SourceCoordinate!, identity.Identity,
                                    new WorkspaceDeclarationOrigin.PlatformPopulation(
                                        target,
                                        WorkspacePlatformPopulationMemberRole.Focus,
                                        "runtime test", identity.Identity.Name),
                                    AssemblyResolutionProvenance.Platform(
                                        "Microsoft.NETCore.App",
                                        target.Version.Value,
                                        "runtime test"),
                                    packageRequest: null)],
                                nameBounds);
                    var context =
                        Assert.IsType<WorkspaceLibraryDeclarationContextAdmissionOutcome
                            .Admitted>(contextOutcome).Context;
                    return new(admission, context,
                        [new(Assert.Single(admission.Occurrences), target, path)]);
                },
            };
        var result = await WorkspaceDefinitionConsumer.RestoreAsync(
            ready, authority, host, options,
            TestContext.Current.CancellationToken);
        if (expectedFailure != 0)
        {
            var failedOutcome =
                Assert.IsType<CompleteRestorationResult<InspectionWorkspace>
                    .Failed>(result);
            var selector =
                Assert.IsType<CompleteRestorationFailure.SelectorResolutionFailed>(
                    failedOutcome.Failure);
            Assert.Equal(expectedFailure == 1
                    ? CommittedSelectorResolutionFailureKind.TypeInventoryIncomplete
                    : CommittedSelectorResolutionFailureKind.LibraryMissing,
                selector.Failure.Kind);
            Assert.True(host.CloseReport!.Succeeded);
            await artifacts!.DisposeAsync();
            return;
        }
        if (result is CompleteRestorationResult<InspectionWorkspace>.Failed failure)
            Assert.Fail($"{failure.Failure.GetType().Name}: {failure.Failure.Message}");
        var activated =
            Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Activated>(
                result);
        await using InspectionWorkspace workspace = activated.Activation;
        Assert.NotNull(activated.Workspace.SelectedContext);
        Assert.Empty(activated.Workspace.Snapshot.Scope.Packages);
        Assert.Equal(selectType ? StructuralSubjectKind.Type
            : StructuralSubjectKind.Library,
            activated.Workspace.Snapshot.Navigation.State.CurrentSnapshot
                .ActiveSubject.Kind);
        if (selectType)
            Assert.IsType<LibraryTypeDeclarationInventoryInspectionOutcome.Completed>(
                activated.Workspace.Snapshot.Navigation.State.CurrentSnapshot
                    .AdmittedInventory);
        else
            Assert.Null(activated.Workspace.Snapshot.Navigation.State.CurrentSnapshot
                .AdmittedInventory);
        Assert.IsType<NavigationLensActivationResult.Applied>(
            Assert.IsType<CompleteRestorationResolvedState.Version6>(
                activated.Workspace.Snapshot.Resolved).States[1].LensResolution);
        string canonical =
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection).CanonicalPacket;
        Assert.Equal(
            WorkspaceSharePacketCodec.Format6Version,
            WorkspaceSharePacketCodec.Decode(
                canonical, TestContext.Current.CancellationToken).FormatVersion);
        if (!legacyUpgrade)
            Assert.Equal(encoded, canonical);
        else
        {
            var upgraded =
                WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                    WorkspaceSharePacketCodec.Decode(
                        canonical, TestContext.Current.CancellationToken),
                    TestContext.Current.CancellationToken);
            var identity = upgraded.View!.States[1].Context switch
            {
                PortableRetainedSubjectContext.Type type =>
                    type.LibraryIdentity,
                PortableRetainedSubjectContext.Library libraryContext =>
                    libraryContext.LibraryIdentity,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(library, identity);
            Assert.Equal(WorkspaceSharePacketCodec.Encode(packet), canonical);
        }
        bool legacyCallbackInvoked = false;
        var legacy = await CompleteRestorationSelectedSubjectOperation.ExecuteAsync(
            workspace, activated.Workspace,
            (subject, _) =>
            {
                legacyCallbackInvoked = true;
                return ValueTask.FromResult(subject.Subject.Kind);
            }, TestContext.Current.CancellationToken);
        Assert.False(legacyCallbackInvoked);
        Assert.Equal(CompleteRestorationSubjectUnavailableReason.UnsupportedSubject,
            Assert.IsType<CompleteRestorationSubjectResult<StructuralSubjectKind>
                .Unavailable>(legacy).Failure.Reason);
        var borrowed =
            Assert.IsType<CompleteRestorationSubjectResult<string>.Completed>(
                await CompleteRestorationSelectedSubjectOperation
                    .ExecuteAdmittedAsync(
                        workspace, activated.Workspace,
                        (subject, lease, token) =>
                        {
                            Assert.Same(subject.Occurrence,
                                subject.Evaluation.Occurrence);
                            var inspected =
                                Assert.IsType<LibraryTypeDeclarationInventoryInspectionOutcome
                                    .Completed>(
                                    LibraryTypeDeclarationInventoryInspection.Execute(
                                        new(subject.Occurrence.Library,
                                            options.NativeNameBounds),
                                        lease, token));
                            Assert.Contains(
                                inspected.Correspondence.Inventory.Declarations,
                                row => row.Kind ==
                                    AssemblyTypeDeclarationKind.Definition
                                    && row.Name.Equals(name));
                            return ValueTask.FromResult(
                                subject.Occurrence.Library.ApiAssembly
                                    .AssemblyIdentity!.Identity.Name);
                        }, TestContext.Current.CancellationToken));
        Assert.Equal("System.Text.Json", borrowed.Content);
        await directOwner!.DisposeAsync();
        await admittedOwner!.DisposeAsync();
        Assert.True((await workspace.CloseAsync()).Succeeded);
        bool borrowedAfterClose = false;
        var afterClose =
            await CompleteRestorationSelectedSubjectOperation.ExecuteAdmittedAsync(
                workspace, activated.Workspace,
                (subject, lease, token) =>
                {
                    borrowedAfterClose = true;
                    return ValueTask.FromResult(subject.Subject.Kind);
                }, TestContext.Current.CancellationToken);
        Assert.False(borrowedAfterClose);
        Assert.Equal(CompleteRestorationSubjectUnavailableReason.ArtifactUnavailable,
            Assert.IsType<CompleteRestorationSubjectResult<StructuralSubjectKind>
                .Unavailable>(afterClose).Failure.Reason);
        await artifacts!.DisposeAsync();
        if (legacyUpgrade)
        {
            var replayAuthority = new TestIntentAuthority();
            var replay = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                CompleteRestorationPreparation.FromPacket(
                    canonical, replayAuthority,
                    TestContext.Current.CancellationToken));
            var replayed =
                Assert.IsType<CompleteRestorationResult<InspectionWorkspace>
                    .Activated>(
                    await WorkspaceDefinitionConsumer.RestoreAsync(
                        replay, replayAuthority, new TestHost(), options,
                        TestContext.Current.CancellationToken));
            await using InspectionWorkspace restored = replayed.Activation;
            Assert.Equal(selectType ? StructuralSubjectKind.Type
                    : StructuralSubjectKind.Library,
                replayed.Workspace.Snapshot.Navigation.State.CurrentSnapshot
                    .ActiveSubject.Kind);
            Assert.Equal(
                Assert.Single(activated.Workspace.Snapshot.Contexts).Members.Length,
                Assert.Single(replayed.Workspace.Snapshot.Contexts).Members.Length);
            Assert.Equal(canonical,
                Assert.IsType<CompleteRestorationProjection.Projectable>(
                    replayed.Workspace.Projection).CanonicalPacket);
            await directOwner!.DisposeAsync();
            await admittedOwner!.DisposeAsync();
            Assert.True((await restored.CloseAsync()).Succeeded);
            await artifacts!.DisposeAsync();
        }
    }

    private sealed class NativeBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(Version, AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinnedPlatformSharedContext_RestoresExactPackageAssociation(
        bool reverseContexts)
    {
        PackageFixture package = await SystemTextJsonAndPlatformPackagesAsync();

        int mixedContextIndex = reverseContexts ? 1 : 0;
        string contexts = reverseContexts ? "[[2],[0,1]]" : "[[0,1],[2]]";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            $$$"""
            {"f":3,"t":[[":Platform","10.0.14","net10.0","linux-x64"],["System.Text.Json","9.0.4","net10.0","linux-x64"],["System.Text.Json","9.0.4","net9.0",null]],"g":{{{contexts}}},"r":[],"a":1,"x":{{{mixedContextIndex}}},"v":[{"t":null,"u":{"k":"workspace"}},{"t":0},{"t":1,"r":{"k":"package"},"u":{"k":"package"}},{"t":2,"r":{"k":"package"},"u":{"k":"package"}}]}
            """,
            TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        var authority = new TestIntentAuthority();
        var preparation =
            Assert.IsType<CompleteRestorationPreparationResult.Ready>(
                WorkspaceDefinitionConsumer.PrepareRestoration(
                    encoded,
                    authority));
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationResult<InspectionWorkspace> result =
            await WorkspaceDefinitionConsumer.RestoreAsync(
                preparation,
                authority,
                new TestHost(),
                Options(client, package.Store),
                TestContext.Current.CancellationToken);

        if (result is CompleteRestorationResult<InspectionWorkspace>.Failed failed)
            Assert.Fail($"{failed.Failure.GetType().Name}: {failed.Failure.Message}");
        var activated = Assert.IsType<
            CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
        await using InspectionWorkspace workspace = activated.Activation;
        NavigationConsumerSnapshot navigation =
            activated.Workspace.Snapshot.Navigation.State.Snapshot;
        Assert.Equal(StructuralSubjectKind.Package, navigation.ActiveSubject.Kind);
        NavigationConsumerPackageDescriptor active = Assert.Single(
            navigation.Packages,
            package => package.Subject.Id == navigation.ActiveSubject.Id);
        Assert.Equal("System.Text.Json", active.PackageId, ignoreCase: true);
        Assert.Equal("9.0.4", active.Version);
        Assert.Equal("net9.0", active.Framework);
        Assert.True(active.IsCurrent);
        var resolved = Assert.IsType<CompleteRestorationResolvedState.Version3>(
            activated.Workspace.Snapshot.Resolved);
        var activeSubject =
            Assert.IsType<StructuralSubjectIdentity.PackageSubject>(
                resolved.States[resolved.ActiveStateIndex!.Value]
                    .Initialization!.Subject);
        Assert.Equal("net10.0", activeSubject.Descriptor.Coordinate.Framework);
        Assert.Equal(
            "linux-x64",
            activeSubject.Descriptor.Coordinate.RuntimeIdentifier);

        var mixed = Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(
            activated.Workspace.Contexts[mixedContextIndex].ContextLoadOutcome);
        RealizedMemberCoordinate.Platform[] platforms =
        [
            .. mixed.Members.Select(member => member.Realized)
                .OfType<RealizedMemberCoordinate.Platform>()
                .Distinct(),
        ];
        Assert.NotEmpty(platforms);
        Assert.All(platforms, platform =>
        {
            Assert.Equal("runtime", platform.Family);
            Assert.Equal("10.0.14", platform.Version);
            Assert.Equal("net10.0", platform.Framework);
        });
        Assert.Equal(
            "System.Text.Json",
            Assert.Single(mixed.PackageRoots).Root.PackageId,
            ignoreCase: true);
        var neighbor = Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(
            activated.Workspace.Contexts[1 - mixedContextIndex]
                .ContextLoadOutcome);
        Assert.Equal("net9.0", neighbor.Framework);
        Assert.DoesNotContain(
            neighbor.Members,
            member => member.Realized is RealizedMemberCoordinate.Platform);
        Assert.Equal(
            "System.Text.Json",
            Assert.Single(neighbor.PackageRoots).Root.PackageId,
            ignoreCase: true);
        var neighborSubject =
            Assert.IsType<StructuralSubjectIdentity.PackageSubject>(
                resolved.States[3].Initialization!.Subject);
        Assert.Equal("net9.0", neighborSubject.Descriptor.Coordinate.Framework);
        Assert.Equal(
            encoded,
            Assert.IsType<CompleteRestorationProjection.Projectable>(
                activated.Workspace.Projection).CanonicalPacket);
    }

    private static async Task<PackageFixture>
        SystemTextJsonAndPlatformPackagesAsync()
    {
        PackageFixture package = await SystemTextJsonPackageAsync();
        foreach (string packageId in new[]
        {
            "Microsoft.NETCore.App.Ref",
            "Microsoft.NETCore.App.Runtime.linux-x64",
        })
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "CompleteRestoration",
                $"{packageId.ToLowerInvariant()}.10.0.14.nupkg");
            await using FileStream content = File.OpenRead(path);
            await package.Store.CommitAsync(
                packageId,
                "10.0.14",
                NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json"),
                content,
                TestContext.Current.CancellationToken);
        }
        return package;
    }
}
