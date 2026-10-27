using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Package endpoint scope contract gates 1 to 3
/// (<c>docs/design/package-endpoint-scope.md#pathological-cases-and-gates</c>)
/// over the real <c>Avalonia</c> 12.1.2 archive, which ships
/// <c>ref/net10.0</c> and <c>lib/net10.0</c>, read by range.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    [Fact]
    public async Task
        EndpointScope_ExactAllocationMapsSurfaceMethodToImplementation()
    {
        const string package = "System.Text.Json";
        const string version = "10.0.0";
        var server = new RangeFeed(
            package,
            version,
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "PackageAdmission",
                    "System.Text.Json.10.0.0.nupkg")));
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        await using PackageEndpointScope scope = AssertOpened(
            await OpenEndpointAsync(
                environment,
                new InMemoryPackageStore(),
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(package, version),
                    "net9.0",
                    PackageAssetDemand.SurfaceAndImplementation)));
        ILInspector.Metadata.ApiSurface surface =
            await PackageEndpointApiSurfaceQuery.ExecuteAsync(
                scope,
                package,
                ApiSurfaceScope.IncludeAll,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        ILInspector.Metadata.ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.Schema.JsonSchemaExporter");
        ILInspector.Metadata.ResolvedMemberTarget target =
            Assert.IsType<ILInspector.Metadata.ResolvedMemberTarget>(
                ILInspector.Metadata.MemberTargetResolver.Resolve(
                    type,
                    ILInspector.Metadata.MemberTargetSelector.Parse(
                        "MapJsonSchemaCore")).Target);
        int surfaceToken =
            Assert.IsType<int>(target.Body!.MetadataToken);

        PackageEndpointMethodAllocationResult result =
            await PackageEndpointMethodAllocationQuery.ExecuteAsync(
                scope,
                Assert.IsType<string>(type.SourceAssemblyPath),
                surfaceToken,
                TestContext.Current.CancellationToken);

        var analyzed = Assert.IsType<
            PackageEndpointMethodAllocationResult.Analyzed>(result);
        AssemblyMethodAllocationAnalysis analysis = Assert.IsType<
            AssemblyContextEntry<AssemblyMethodAllocationAnalysis>.Available>(
                analyzed.Entry).Value;
        Assert.Equal(
            analysis.RequestedMethodToken,
            analysis.Method.MetadataToken);
        Assert.Empty(analysis.Diagnostics);
        Assert.NotEmpty(analysis.Allocations);
    }

    /// <summary>
    /// Step 4 part 2: an endpoint's merged API surface holds every surface
    /// participant's Types, ordered by name, with the participants' counts
    /// summed and each Type naming the package asset it came from.
    /// </summary>
    [Fact]
    public async Task EndpointScope_MergedApiSurface_HoldsEveryParticipant()
    {
        var server = new RangeFeed(Avalonia, AvaloniaVersion, ReadAvalonia());
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        await using PackageEndpointScope scope = AssertOpened(
            await OpenEndpointAsync(
                environment,
                new InMemoryPackageStore(),
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(Avalonia, AvaloniaVersion),
                    "net10.0",
                    PackageAssetDemand.Surface)));
        var log = new List<string>();

        ILInspector.Metadata.ApiSurface surface = await PackageEndpointApiSurfaceQuery.ExecuteAsync(
            scope,
            Avalonia,
            ApiSurfaceScope.Public,
            log.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal(Avalonia, surface.Name);
        Assert.Equal("net10.0", surface.Tfm);
        Assert.Equal(scope.SurfaceParticipants.Length, log.Count(static line => line.StartsWith("  + ", StringComparison.Ordinal)));
        Assert.Equal(surface.Types.Count, surface.PublicTypeCount);
        Assert.Equal(
            surface.Types.Select(static type => type.FullName).Order(),
            surface.Types.Select(static type => type.FullName));
        Assert.Contains(surface.Types, static type => type.FullName == "Avalonia.Controls.Button");
        Assert.Contains(surface.Types, static type => type.FullName == "Avalonia.AvaloniaObject");
        Assert.All(surface.Types, type =>
            Assert.Contains(
                scope.SurfaceParticipants,
                participant => participant.Asset.Path == type.SourceAssemblyPath));
        Assert.Empty(surface.InspectionFailures);
    }

    /// <summary>
    /// Gate 1: a <c>Surface</c> endpoint exposes one surface participant per
    /// selected compile asset, with package provenance, and no
    /// implementation role; its ranged read materializes only the surface
    /// folder.
    /// </summary>
    [Fact]
    public async Task EndpointScope_Surface_ExposesSurfaceParticipantsOnly()
    {
        byte[] archive = ReadAvalonia();
        ArchiveOracle oracle = ArchiveOracle.Read(archive);
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        await using PackageEndpointScope scope = AssertOpened(
            await OpenEndpointAsync(
                environment,
                new InMemoryPackageStore(),
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(Avalonia, AvaloniaVersion),
                    "net10.0",
                    PackageAssetDemand.Surface)));

        Assert.Equal(11, scope.SurfaceParticipants.Length);
        Assert.Empty(scope.ImplementationParticipants);
        Assert.All(scope.SurfaceParticipants, participant =>
        {
            Assert.Equal(PackageEndpointRole.Surface, participant.Role);
            Assert.StartsWith(AvaloniaSurface, participant.Asset.Path, StringComparison.Ordinal);
            Assert.Equal(Avalonia, participant.Provenance.PackageId, ignoreCase: true);
            Assert.Equal(AvaloniaVersion, participant.Provenance.PackageVersion);
            Assert.Equal("net10.0", participant.Provenance.TargetFramework);
            Assert.Equal(participant.Asset.Path, participant.Provenance.AssetPath);
        });
        Assert.Equal(
            [.. Enumerable.Range(0, scope.SurfaceParticipants.Length)],
            scope.SurfaceParticipants.Select(static participant => participant.Index));
        var content = Assert.IsType<RangedPackageContent>(scope.Payload.Content);
        Assert.Equal(
            oracle.Folder(AvaloniaSurface).Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));

        PackageEndpointParticipant controls = scope.SurfaceParticipants.Single(
            static participant => participant.Asset.Path.EndsWith("/Avalonia.Controls.dll", StringComparison.Ordinal));
        ArtifactRootResult<string> name = await scope.UseSurfaceParticipantAsync(
            controls,
            static (_, participant, _) =>
                ValueTask.FromResult(participant.Assembly.Identity.Name),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "Avalonia.Controls",
            Assert.IsType<ArtifactRootResult<string>.Available>(name).Value);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scope.UseImplementationAsync(
                static (_, _, _) => ValueTask.FromResult(0),
                TestContext.Current.CancellationToken));

        await scope.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await scope.UseSurfaceAsync(
                static (_, participants, _) => ValueTask.FromResult(participants.Length),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EndpointScope_ExactLibraryDemandNarrowsTransferAndParticipants()
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        await using PackageEndpointScope scope = AssertOpened(
            await OpenEndpointAsync(
                environment,
                new InMemoryPackageStore(),
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(
                        Avalonia,
                        AvaloniaVersion),
                    "net10.0",
                    PackageAssetDemand.Surface,
                    librarySelector: new PackageLibrarySelector(
                        "Avalonia.Controls.dll"))));

        PackageEndpointParticipant selected =
            Assert.Single(scope.SurfaceParticipants);
        Assert.EndsWith(
            "/Avalonia.Controls.dll",
            selected.Asset.Path,
            StringComparison.Ordinal);
        Assert.Empty(scope.ImplementationParticipants);
        Assert.NotNull(scope.Root.Root.AssetSelection);
        Assert.Equal(11, scope.Root.Root.AssetSelection.Assets.Count);
        var content =
            Assert.IsType<RangedPackageContent>(scope.Payload.Content);
        Assert.True(content.IsMaterialized(selected.Asset.Path));
        Assert.False(
            content.IsMaterialized(
                $"{AvaloniaSurface}Avalonia.Dialogs.dll"));
    }

    [Fact]
    public async Task
        EndpointScope_CompileLibrariesDemandMaterializesExactAggregateEntries()
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(
            Avalonia,
            AvaloniaVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();
        PackageEndpointScopeRequest request =
            PackageEndpointScopeRequest.ForCompileLibraries(
                PackageSourceCoordinate.Create(
                    Avalonia,
                    AvaloniaVersion),
                "net10.0",
                PackageAssetDemand.Surface);

        await using PackageEndpointScope cold =
            AssertOpened(
                await OpenSemanticEndpointAsync(
                    environment,
                    store,
                    request));
        string[] expected =
        [
            .. cold.SurfaceParticipants.Select(
                static participant =>
                    participant.Asset.Path),
        ];
        Assert.Equal(11, expected.Length);
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            cold.Payload.Content
                .EnumerateEntries()
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            cold.Payload.Content.EnumerateEntries(),
            static path => path.EndsWith(
                ".xml",
                StringComparison.OrdinalIgnoreCase));
        PackageTransferReceipt coldTransfer =
            cold.Contribution.Result.Evidence.Acquisition!.Transfer;
        Assert.Equal(
            PackagePayloadOrigin.Ranged,
            cold.Payload.Origin);
        Assert.NotEmpty(coldTransfer.Requests);

        await using PackageEndpointScope warm =
            AssertOpened(
                await OpenSemanticEndpointAsync(
                    environment,
                    store,
                    request));
        PackageTransferReceipt warmTransfer =
            warm.Contribution.Result.Evidence.Acquisition!.Transfer;
        Assert.Equal(
            PackagePayloadOrigin.Cache,
            warm.Payload.Origin);
        Assert.Empty(warmTransfer.Requests);
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            warm.Payload.Content
                .EnumerateEntries()
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task
        EndpointScope_CompileLibrariesDemandReadsOnlyNamedImplementation()
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(
            Avalonia,
            AvaloniaVersion,
            archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();
        PackageEndpointScopeRequest request =
            PackageEndpointScopeRequest.ForCompileLibraries(
                PackageSourceCoordinate.Create(
                    Avalonia,
                    AvaloniaVersion),
                "net10.0",
                PackageAssetDemand.SurfaceAndImplementation,
                ["Avalonia.Dialogs.dll"]);

        await using PackageEndpointScope cold =
            AssertOpened(
                await OpenSemanticEndpointAsync(
                    environment,
                    store,
                    request));

        Assert.Equal(11, cold.SurfaceParticipants.Length);
        PackageEndpointParticipant implementation =
            Assert.Single(cold.ImplementationParticipants);
        Assert.EndsWith(
            "/Avalonia.Dialogs.dll",
            implementation.Asset.Path,
            StringComparison.Ordinal);
        Assert.NotNull(implementation.SurfaceCorrespondence);
        IReadOnlySet<string> entries =
            cold.Payload.Content
                .EnumerateEntries()
                .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(implementation.Asset.Path, entries);
        Assert.DoesNotContain(
            $"{AvaloniaImplementation}Avalonia.Controls.dll",
            entries);
        Assert.All(
            cold.SurfaceParticipants,
            participant =>
                Assert.Contains(participant.Asset.Path, entries));
        PackageTransferReceipt coldTransfer =
            cold.Contribution.Result.Evidence.Acquisition!.Transfer;
        Assert.Equal(
            PackagePayloadOrigin.Ranged,
            cold.Payload.Origin);
        Assert.NotEmpty(coldTransfer.Requests);

        await using PackageEndpointScope warm =
            AssertOpened(
                await OpenSemanticEndpointAsync(
                    environment,
                    store,
                    request));
        Assert.Single(warm.ImplementationParticipants);
        Assert.Empty(
            warm.Contribution.Result.Evidence.Acquisition!.Transfer.Requests);

        await using PackageEndpointScope complete =
            AssertOpened(
                await OpenSemanticEndpointAsync(
                    environment,
                    new InMemoryPackageStore(),
                    request,
                    rangedSizeCut: long.MaxValue));
        Assert.Equal(PackagePayloadOrigin.Download, complete.Payload.Origin);
        Assert.Single(complete.ImplementationParticipants);
        Assert.DoesNotContain(
            $"{AvaloniaImplementation}Avalonia.Controls.dll",
            complete.Payload.Content.EnumerateEntries());
    }

    /// <summary>
    /// Gate 2: a <c>SurfaceAndImplementation</c> endpoint also exposes one
    /// implementation participant per realized implementation asset, each
    /// with its surface correspondence, readable through its group.
    /// </summary>
    [Fact]
    public async Task EndpointScope_SurfaceAndImplementation_CorrespondsImplementationToSurface()
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        await using PackageEndpointScope scope = AssertOpened(
            await OpenEndpointAsync(
                environment,
                new InMemoryPackageStore(),
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(Avalonia, AvaloniaVersion),
                    "net10.0",
                    PackageAssetDemand.SurfaceAndImplementation)));

        Assert.Equal(11, scope.SurfaceParticipants.Length);
        Assert.Equal(11, scope.ImplementationParticipants.Length);
        Assert.All(scope.ImplementationParticipants, implementation =>
        {
            Assert.Equal(PackageEndpointRole.Implementation, implementation.Role);
            Assert.StartsWith(
                AvaloniaImplementation, implementation.Asset.Path, StringComparison.Ordinal);
            PackageEndpointParticipant surface =
                Assert.IsType<PackageEndpointParticipant>(implementation.SurfaceCorrespondence);
            Assert.Contains(surface, scope.SurfaceParticipants);
            Assert.Equal(surface.Asset.AssemblyName, implementation.Asset.AssemblyName);
        });

        PackageEndpointParticipant dialogs = scope.ImplementationParticipants.Single(
            static participant => participant.Asset.Path.EndsWith("/Avalonia.Dialogs.dll", StringComparison.Ordinal));
        ArtifactRootResult<string> name = await scope.UseImplementationParticipantAsync(
            dialogs,
            static (_, participant, _) =>
                ValueTask.FromResult(participant.Assembly.Identity.Name),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "Avalonia.Dialogs",
            Assert.IsType<ArtifactRootResult<string>.Available>(name).Value);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await scope.UseSurfaceParticipantAsync(
                dialogs,
                static (_, _, _) => ValueTask.FromResult(0),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Step 4 part 3: the body query hands each endpoint's implementation
    /// participants to one comparison, each with its image, a descriptor
    /// that reads that image, and a resolver that binds references within
    /// the endpoint's implementation group. A <c>Surface</c> endpoint has no
    /// implementation role.
    /// </summary>
    [Fact]
    public async Task EndpointScope_BodyQuery_HandsImplementationParticipantsTheirImagesAndResolvers()
    {
        var server = new RangeFeed(Avalonia, AvaloniaVersion, ReadAvalonia());
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();
        var request = new PackageEndpointScopeRequest(
            PackageSourceCoordinate.Create(Avalonia, AvaloniaVersion),
            "net10.0",
            PackageAssetDemand.SurfaceAndImplementation);
        await using PackageEndpointScope before = AssertOpened(
            await OpenEndpointAsync(environment, store, request));
        await using PackageEndpointScope after = AssertOpened(
            await OpenEndpointAsync(environment, store, request));

        (int Before, int After, string? Bound) seen = await PackageEndpointBodyQuery.ExecuteAsync(
            before,
            after,
            (beforeParticipants, afterParticipants, _) =>
            {
                Assert.Equal(
                    before.ImplementationParticipants,
                    beforeParticipants.Select(static participant => participant.Descriptor));
                Assert.Equal(
                    after.ImplementationParticipants,
                    afterParticipants.Select(static participant => participant.Descriptor));
                Assert.All(afterParticipants, participant =>
                {
                    Assert.StartsWith(
                        AvaloniaImplementation,
                        participant.Descriptor.Asset.Path,
                        StringComparison.Ordinal);
                    Assert.Equal(
                        Path.GetFileNameWithoutExtension(participant.Descriptor.Asset.Path),
                        participant.Assembly.Identity.Name);
                    using Stream content = participant.Assembly.OpenRead();
                    Assert.Equal(participant.Image.Length, content.Length);

                    // SourceHouse reads the participant in the group that
                    // holds it, with the package provenance its PDB
                    // settlement keys on.
                    Assert.Contains(participant.GroupParticipant, participant.Group.Participants);
                    var provenance = Assert.IsType<ILInspector.Metadata.AssemblyResolutionProvenance.PackageAsset>(
                        participant.GroupParticipant.Assembly.Provenance);
                    Assert.Equal(
                        (
                            participant.Descriptor.Provenance.PackageId,
                            participant.Descriptor.Provenance.PackageVersion,
                            participant.Descriptor.Provenance.TargetFramework,
                            participant.Descriptor.Provenance.AssetPath),
                        (provenance.PackageId, provenance.PackageVersion, provenance.Tfm, provenance.AssetPath));
                });

                PackageEndpointBodyParticipant controls = afterParticipants.Single(
                    static participant => participant.Assembly.Identity.Name == "Avalonia.Controls");
                using var pe = new System.Reflection.PortableExecutable.PEReader(
                    controls.Image);
                System.Reflection.Metadata.MetadataReader reader =
                    System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
                ILInspector.Metadata.AssemblyReferenceIdentity baseReference = reader.AssemblyReferences
                    .Select(handle => ILInspector.Metadata.AssemblyReferenceIdentity.From(reader, handle))
                    .Single(static identity => identity.Name == "Avalonia.Base");
                return ValueTask.FromResult((
                    beforeParticipants.Length,
                    afterParticipants.Length,
                    controls.Resolver.Resolve(
                        baseReference,
                        ILInspector.Metadata.AssemblyResolutionScope.Any)?.Identity.Name));
            },
            TestContext.Current.CancellationToken);

        Assert.Equal((11, 11, "Avalonia.Base"), seen);

        // Step 4 part 5: one endpoint's participants, as a forwarded
        // target's scope reads them, bind within that endpoint alone.
        string? single = await PackageEndpointBodyQuery.ExecuteAsync(
            after,
            (participants, _) =>
            {
                Assert.Equal(
                    after.ImplementationParticipants,
                    participants.Select(static participant => participant.Descriptor));
                PackageEndpointBodyParticipant controls = participants.Single(
                    static participant => participant.Assembly.Identity.Name == "Avalonia.Controls");
                Assert.Contains(controls.GroupParticipant, controls.Group.Participants);
                using var pe = new System.Reflection.PortableExecutable.PEReader(
                    controls.Image);
                System.Reflection.Metadata.MetadataReader reader =
                    System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
                ILInspector.Metadata.AssemblyReferenceIdentity baseReference = reader.AssemblyReferences
                    .Select(handle => ILInspector.Metadata.AssemblyReferenceIdentity.From(reader, handle))
                    .Single(static identity => identity.Name == "Avalonia.Base");
                return ValueTask.FromResult(
                    controls.Resolver.Resolve(
                        baseReference,
                        ILInspector.Metadata.AssemblyResolutionScope.Any)?.Identity.Name);
            },
            TestContext.Current.CancellationToken);
        Assert.Equal("Avalonia.Base", single);

        await using PackageEndpointScope surface = AssertOpened(
            await OpenEndpointAsync(
                environment,
                store,
                new PackageEndpointScopeRequest(
                    PackageSourceCoordinate.Create(Avalonia, AvaloniaVersion),
                    "net10.0",
                    PackageAssetDemand.Surface)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PackageEndpointBodyQuery.ExecuteAsync(
                surface,
                after,
                static (_, _, _) => ValueTask.FromResult(0),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Gate 3: a coordinate the source does not have, and a framework the
    /// package has no compatible compile assets for, are typed failures that
    /// keep the House evidence, never an empty scope.
    /// </summary>
    [Theory]
    [InlineData("9.9.9", "net10.0", PackageEndpointScopeFailureKind.NotFound)]
    [InlineData(AvaloniaVersion, "net6.0", PackageEndpointScopeFailureKind.NoCompatibleFramework)]
    public async Task EndpointScope_MissingCoordinateOrFramework_IsTypedFailure(
        string version,
        string framework,
        PackageEndpointScopeFailureKind expected)
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        PackageEndpointScopeOutcome outcome = await OpenEndpointAsync(
            environment,
            new InMemoryPackageStore(),
            new PackageEndpointScopeRequest(
                PackageSourceCoordinate.Create(Avalonia, version),
                framework,
                PackageAssetDemand.Surface));

        var failed = Assert.IsType<PackageEndpointScopeOutcome.Failed>(outcome);
        Assert.Equal(expected, failed.Kind);
        Assert.NotNull(failed.HouseResult);
        Assert.False(string.IsNullOrWhiteSpace(failed.Message));
        if (expected == PackageEndpointScopeFailureKind.NoCompatibleFramework)
        {
            PackageCompileAssetSelection selection =
                Assert.IsType<PackageCompileAssetSelection>(failed.Selection);
            Assert.False(selection.IsSelected);
        }
    }

    /// <summary>
    /// Step 4 part 1: the scope takes the House's package demand and target
    /// context, as <c>package</c> does. A <c>*</c> pattern settles to the
    /// version the feed lists, and the owner default selects the highest
    /// compile slice, the same participants an explicit <c>net10.0</c> opens.
    /// </summary>
    [Fact]
    public async Task EndpointScope_SelectingDemandAndOwnerDefault_SettleInTheHouse()
    {
        byte[] archive = ReadAvalonia();
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        Assert.True(PackageHouseDemand.TryCreate(
            Avalonia, "12.1.*", includePrerelease: false, out PackageHouseDemand? demand, out string? error), error);
        Assert.IsType<PackageHouseDemand.Selecting>(demand);

        await using PackageEndpointScope scope = AssertOpened(await OpenEndpointAsync(
            environment,
            new InMemoryPackageStore(),
            new PackageEndpointScopeRequest(
                demand,
                PackageHouseTargetContext.OwnerDefault(),
                PackageAssetDemand.Surface)));

        Assert.Equal(AvaloniaVersion, scope.PackageVersion);
        Assert.Equal("net10.0", scope.TargetFramework);
        Assert.Equal(11, scope.SurfaceParticipants.Length);
        Assert.All(
            scope.SurfaceParticipants,
            participant => Assert.Equal("net10.0", participant.Provenance.TargetFramework));
    }

    /// <summary>Gate 3 for a selecting demand: a pattern no listed version matches is not found.</summary>
    [Fact]
    public async Task EndpointScope_SelectingDemandWithNoMatch_IsNotFound()
    {
        var server = new RangeFeed(Avalonia, AvaloniaVersion, ReadAvalonia());
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        Assert.True(PackageHouseDemand.TryCreate(
            Avalonia, "13.*", includePrerelease: false, out PackageHouseDemand? demand, out _));

        PackageEndpointScopeOutcome outcome = await OpenEndpointAsync(
            environment,
            new InMemoryPackageStore(),
            new PackageEndpointScopeRequest(
                demand,
                PackageHouseTargetContext.OwnerDefault(),
                PackageAssetDemand.Surface));

        var failed = Assert.IsType<PackageEndpointScopeOutcome.Failed>(outcome);
        Assert.Equal(PackageEndpointScopeFailureKind.NotFound, failed.Kind);
        Assert.NotNull(failed.HouseResult);
        Assert.StartsWith($"{Avalonia} 13.* was not found", failed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndpointScopeRequest_TakesExactOrSelectingDemandsOnly()
    {
        Assert.True(PackageHouseDemand.TryCreate(
            Avalonia, AvaloniaVersion, includePrerelease: false, out PackageHouseDemand? exact, out _));
        var request = new PackageEndpointScopeRequest(
            exact,
            PackageHouseTargetContext.OwnerDefault(),
            PackageAssetDemand.Surface);
        Assert.Same(exact, request.Demand);
        Assert.Equal(PackageHouseTargetSelectionMode.OwnerDefault, request.Target.Mode);

        Assert.True(PackageHouseDemand.TryCreate(
            Avalonia, "LATEST", includePrerelease: false, out PackageHouseDemand? latest, out _));
        Assert.IsType<PackageVersionSelectionRequest.AlwaysLatest>(
            Assert.IsType<PackageHouseDemand.Selecting>(latest).Request);
        Assert.False(PackageHouseDemand.TryCreate(
            Avalonia, "1.0.0..2.0.0", includePrerelease: false, out _, out string? range));
        Assert.NotNull(range);
        Assert.False(PackageHouseDemand.TryCreate(
            Avalonia, "not-a-version", includePrerelease: false, out _, out _));
    }

    private static PackageEndpointScope AssertOpened(PackageEndpointScopeOutcome outcome)
    {
        if (outcome is PackageEndpointScopeOutcome.Failed failed)
            Assert.Fail($"{failed.Kind}: {failed.Message}");
        return Assert.IsType<PackageEndpointScopeOutcome.Opened>(outcome).Scope;
    }

    private static Task<PackageEndpointScopeOutcome> OpenEndpointAsync(
        RangedEnvironment environment,
        IPackageStore store,
        PackageEndpointScopeRequest request)
    {
        PackagePayloadAcquisitionPlan acquisition =
            request.LibrarySelector is null
                ? new PackagePayloadAcquisitionPlan(
                    (_, _) => store,
                    access: PackagePayloadAccess.Ranged,
                    log: environment.Log.Enqueue,
                    rangedSizeCut: 0)
                : PackagePayloadAcquisitionPlan.ForContentQueries(
                    (_, _) => store,
                    log: environment.Log.Enqueue,
                    rangedSizeCut: 0);
        var house = new PackageHouse(
            environment.Authorization,
            acquisition);
        var operation = PackageHouseOperation.Create(PackageHouseOperationProfile.Realize);
        return PackageEndpointScope.OpenAsync(
            house,
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operation.RequestTimeout,
                operation.OperationTimeout),
            request,
            TestContext.Current.CancellationToken);
    }

    private static Task<PackageEndpointScopeOutcome>
        OpenSemanticEndpointAsync(
            RangedEnvironment environment,
            IPackageStore store,
            PackageEndpointScopeRequest request,
            long rangedSizeCut = 0)
    {
        var house = new PackageHouse(
            environment.Authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store,
                log: environment.Log.Enqueue,
                rangedSizeCut: rangedSizeCut));
        var operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Realize);
        return PackageEndpointScope.OpenAsync(
            house,
            environment.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operation.RequestTimeout,
                operation.OperationTimeout),
            request,
            TestContext.Current.CancellationToken);
    }
}
