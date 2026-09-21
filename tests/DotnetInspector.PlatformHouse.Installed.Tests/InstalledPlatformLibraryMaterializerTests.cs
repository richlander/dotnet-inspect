using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Platform;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Resources;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

public sealed class InstalledPlatformLibraryMaterializerTests
{
    private static readonly ApiSurfaceExtractionBounds
        s_documentationApiSurfaceBounds =
            new(
                maxTypes: 5_000,
                maxMembers: 100_000,
                maxInspectionFailures: 1_000,
                maxTypeForwarders: 10_000,
                maxMetadataRows: 1_000_000,
                maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public async Task
        SelectedInstalledSystemTextJsonCompletesWithoutFallback()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referenceAssembly =
            FindReferenceAssembly("System.Text.Json.dll");
        string implementationAssembly =
            typeof(JsonSerializer).Assembly.Location;
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referenceAssembly);
        hive.CopyAssembly(
            hive.CreateImplementationFramework(
                implementationAssembly),
            implementationAssembly);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformSourceCapabilityIdentity fallbackDiscovery =
            PlatformSourceCapabilityIdentity.Create(
                "package-target-discovery");
        PlatformHouseRequest request = SelectedLibraryRequest(
            adapter,
            fallbackDiscovery,
            ReadIdentity(referenceAssembly),
            cancellationToken);
        int fallbackInvocations = 0;
        var fallback = new PlatformTargetDiscoverySource(
            fallbackDiscovery,
            (operation, _) =>
            {
                fallbackInvocations++;
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.NotSucceeded(
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.TargetDiscovery,
                            fallbackDiscovery,
                            operation.Snapshot,
                            PlatformSourceGeneration.Create(
                                "package-generation"),
                            exactTarget: null,
                            PlatformSourceUnavailabilityKind.Absent));
                return ValueTask.FromResult(attempt);
            });

        var completed = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Completed>(
                await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                    request,
                    [
                        InstalledPlatformTargetDiscovery.CreateSource(
                            adapter),
                        fallback,
                    ],
                    [
                        InstalledPlatformSelectedLibraryRealization
                            .CreateReferenceSource(
                                adapter,
                                PlatformHouseCandidateIdentity.Create(
                                    "installed-reference-candidate")),
                        InstalledPlatformSelectedLibraryRealization
                            .CreateImplementationSource(
                                adapter,
                                PlatformHouseCandidateIdentity.Create(
                                    "installed-implementation-candidate")),
                    ],
                    "installed-selected-library"));

        Assert.Equal(0, fallbackInvocations);
        Assert.Equal(
            "11.0.0",
            completed.Library.Outcome.Receipt.TargetSettlement
                .SettledTarget!.Version.Value);
        LibraryReference library = completed.Library.Value.Reference;
        Assert.NotNull(library.ImplementationAssembly);
        using (LibraryOperationLease operation =
            Issued(completed.Library.Owner, library))
        {
            Assert.Equal(
                ((byte)'M', (byte)'M'),
                operation.SnapshotPair(
                    library.ApiAssembly,
                    library.ImplementationAssembly!,
                    static (view, _) =>
                        (view.First.Content[0],
                            view.Second.Content[0]),
                    cancellationToken));
        }

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        await completed.Library.Owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        SelectedInstalledReferencePopulationCompletesWithoutFallback()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePack = hive.CreateReferencePack();
        hive.CopyAssembly(
            referencePack,
            FindReferenceAssembly("System.Runtime.dll"));
        hive.CopyAssembly(
            referencePack,
            FindReferenceAssembly("System.Text.Json.dll"));
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformSourceCapabilityIdentity fallbackDiscovery =
            PlatformSourceCapabilityIdentity.Create(
                "package-target-discovery");
        PlatformHouseRequest request =
            SelectedReferencePopulationRequest(
                adapter,
                fallbackDiscovery,
                cancellationToken);
        int fallbackInvocations = 0;
        var fallback = new PlatformTargetDiscoverySource(
            fallbackDiscovery,
            (operation, _) =>
            {
                fallbackInvocations++;
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.NotSucceeded(
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.TargetDiscovery,
                            fallbackDiscovery,
                            operation.Snapshot,
                            PlatformSourceGeneration.Create(
                                "package-generation"),
                            exactTarget: null,
                            PlatformSourceUnavailabilityKind.Absent));
                return ValueTask.FromResult(attempt);
            });

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PlatformHouseSelectedReferencePopulationExecutor
                    .ExecuteAsync(
                        request,
                        [
                            InstalledPlatformTargetDiscovery.CreateSource(
                                adapter),
                            fallback,
                        ],
                        [
                            InstalledPlatformSelectedReferencePopulationRealization
                                .CreateSource(
                                    adapter,
                                    PlatformHouseCandidateIdentity.Create(
                                        "installed-reference-population-candidate")),
                        ],
                        "installed-selected-reference-population"));

        Assert.Equal(0, fallbackInvocations);
        Assert.Equal(
            ["System.Runtime", "System.Text.Json"],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(
            "11.0.0",
            completed.Population.Outcome.Receipt.TargetSettlement
                .SettledTarget!.Version.Value);
        Assert.Equal(
            2,
            completed.Population.Outcome.Receipt.ConsumedWork.Assemblies);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        foreach (LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        UnmeasuredSelectedPopulationIncompleteReservesDelegatedWork()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string source =
            typeof(InstalledPlatformLibraryMaterializerTests)
                .Assembly.Location;
        string referencePack = hive.CreateReferencePack();
        hive.CopyAssembly(referencePack, source);
        string secondSource =
            FindReferenceAssembly("System.Runtime.dll");
        hive.CopyAssembly(referencePack, secondSource);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformSourceCapabilityIdentity fallbackDiscovery =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-target-discovery");
        PlatformSourceCapabilityIdentity fallbackReference =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-reference");
        long maximumBytes =
            new FileInfo(source).Length
            + new FileInfo(secondSource).Length;
        PlatformHouseRequest request =
            SelectedReferencePopulationFailureRequest(
                adapter,
                fallbackDiscovery,
                fallbackReference,
                maximumBytes,
                cancellationToken);
        int fallbackInvocations = 0;
        var discoveryFallback = new PlatformTargetDiscoverySource(
            fallbackDiscovery,
            (operation, _) =>
            {
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.NotSucceeded(
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.TargetDiscovery,
                            fallbackDiscovery,
                            operation.Snapshot,
                            PlatformSourceGeneration.Create(
                                "fallback-discovery-generation"),
                            exactTarget: null,
                            PlatformSourceUnavailabilityKind.Absent));
                return ValueTask.FromResult(attempt);
            });
        var realizationFallback =
            new PlatformReferencePopulationRealizationSource(
                fallbackReference,
                (operation, target, _, _) =>
                {
                    fallbackInvocations++;
                    PlatformReferencePopulationRealizationSourceAttempt
                        attempt =
                            new PlatformReferencePopulationRealizationSourceAttempt
                                .NotSucceeded(
                                    new PlatformSourceContribution
                                        .Unavailable(
                                            PlatformSourceFacet.Reference,
                                            fallbackReference,
                                            operation.Snapshot,
                                            PlatformSourceGeneration.Create(
                                                "fallback-reference-generation"),
                                            target,
                                            PlatformSourceUnavailabilityKind
                                                .Absent));
                    return ValueTask.FromResult(attempt);
                });

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseSelectedReferencePopulationExecutor
                    .ExecuteAsync(
                        request,
                        [
                            InstalledPlatformTargetDiscovery.CreateSource(
                                adapter),
                            discoveryFallback,
                        ],
                        [
                            InstalledPlatformSelectedReferencePopulationRealization
                                .CreateSource(
                                    adapter,
                                    PlatformHouseCandidateIdentity.Create(
                                        "installed-reference-population-candidate")),
                            realizationFallback,
                        ],
                        "installed-selected-reference-population"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, fallbackInvocations);
        PlatformHouseConsumedWork consumed =
            terminal.TerminalRealization.Outcome.Receipt.ConsumedWork;
        Assert.Equal(1, consumed.Assemblies);
        Assert.Equal(0, consumed.XmlDocuments);
        Assert.Equal(maximumBytes, consumed.Bytes);
    }

    [Fact]
    public async Task
        UnmeasuredInstalledFailureConsumesDelegatedWorkBeforeFallback()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string source =
            typeof(InstalledPlatformLibraryMaterializerTests)
                .Assembly.Location;
        string referencePack = hive.CreateReferencePack();
        hive.CopyAssembly(referencePack, source);
        Directory.CreateDirectory(
            Path.ChangeExtension(
                Path.Combine(referencePack, Path.GetFileName(source)),
                ".xml"));
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformSourceCapabilityIdentity fallbackDiscovery =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-target-discovery");
        PlatformSourceCapabilityIdentity fallbackReference =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-reference");
        long maximumBytes = new FileInfo(source).Length;
        PlatformHouseRequest request =
            SelectedReferenceFailureRequest(
                adapter,
                fallbackDiscovery,
                fallbackReference,
                ReadIdentity(source),
                maximumBytes,
                cancellationToken);
        int fallbackInvocations = 0;
        var discoveryFallback = new PlatformTargetDiscoverySource(
            fallbackDiscovery,
            (operation, _) =>
            {
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.NotSucceeded(
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.TargetDiscovery,
                            fallbackDiscovery,
                            operation.Snapshot,
                            PlatformSourceGeneration.Create(
                                "fallback-discovery-generation"),
                            exactTarget: null,
                            PlatformSourceUnavailabilityKind.Absent));
                return ValueTask.FromResult(attempt);
            });
        var realizationFallback =
            new PlatformLibraryRealizationSource(
                fallbackReference,
                PlatformSourceFacet.Reference,
                (operation, target, _, _) =>
                {
                    fallbackInvocations++;
                    PlatformLibraryRealizationSourceAttempt attempt =
                        new PlatformLibraryRealizationSourceAttempt
                            .NotSucceeded(
                                new PlatformSourceContribution
                                    .Unavailable(
                                        PlatformSourceFacet.Reference,
                                        fallbackReference,
                                        operation.Snapshot,
                                        PlatformSourceGeneration.Create(
                                            "fallback-reference-generation"),
                                        target,
                                        PlatformSourceUnavailabilityKind
                                            .Absent));
                    return ValueTask.FromResult(attempt);
                });

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                    request,
                    [
                        InstalledPlatformTargetDiscovery.CreateSource(
                            adapter),
                        discoveryFallback,
                    ],
                    [
                        InstalledPlatformSelectedLibraryRealization
                            .CreateReferenceSource(
                                adapter,
                                PlatformHouseCandidateIdentity.Create(
                                    "installed-reference-candidate")),
                        realizationFallback,
                    ],
                    "installed-selected-library"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, fallbackInvocations);
        PlatformHouseConsumedWork consumed =
            terminal.TerminalRealization.Outcome.Receipt.ConsumedWork;
        Assert.Equal(1, consumed.Assemblies);
        Assert.Equal(1, consumed.XmlDocuments);
        Assert.Equal(maximumBytes, consumed.Bytes);
    }

    [Fact]
    public async Task
        UnmeasuredInstalledIncompleteConsumesDelegatedWorkBeforeAggregation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string source =
            typeof(InstalledPlatformLibraryMaterializerTests)
                .Assembly.Location;
        string referencePack = hive.CreateReferencePack();
        hive.CopyAssembly(referencePack, source);
        File.WriteAllBytes(
            Path.ChangeExtension(
                Path.Combine(referencePack, Path.GetFileName(source)),
                ".xml"),
            [1]);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformSourceCapabilityIdentity fallbackDiscovery =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-target-discovery");
        PlatformSourceCapabilityIdentity fallbackReference =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-reference");
        long maximumBytes = new FileInfo(source).Length;
        PlatformHouseRequest request =
            SelectedReferenceFailureRequest(
                adapter,
                fallbackDiscovery,
                fallbackReference,
                ReadIdentity(source),
                maximumBytes,
                cancellationToken,
                PlatformSourceSelectionMode.Aggregation);
        var discoveryFallback = new PlatformTargetDiscoverySource(
            fallbackDiscovery,
            (operation, _) =>
            {
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.NotSucceeded(
                        new PlatformSourceContribution.Unavailable(
                            PlatformSourceFacet.TargetDiscovery,
                            fallbackDiscovery,
                            operation.Snapshot,
                            PlatformSourceGeneration.Create(
                                "fallback-discovery-generation"),
                            exactTarget: null,
                            PlatformSourceUnavailabilityKind.Absent));
                return ValueTask.FromResult(attempt);
            });
        int fallbackInvocations = 0;
        var realizationFallback =
            new PlatformLibraryRealizationSource(
                fallbackReference,
                PlatformSourceFacet.Reference,
                (operation, target, _, _) =>
                {
                    fallbackInvocations++;
                    PlatformLibraryRealizationSourceAttempt attempt =
                        new PlatformLibraryRealizationSourceAttempt
                            .NotSucceeded(
                                new PlatformSourceContribution
                                    .Unavailable(
                                        PlatformSourceFacet.Reference,
                                        fallbackReference,
                                        operation.Snapshot,
                                        PlatformSourceGeneration.Create(
                                            "fallback-reference-generation"),
                                        target,
                                        PlatformSourceUnavailabilityKind
                                            .Absent));
                    return ValueTask.FromResult(attempt);
                });

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                    request,
                    [
                        InstalledPlatformTargetDiscovery.CreateSource(
                            adapter),
                        discoveryFallback,
                    ],
                    [
                        InstalledPlatformSelectedLibraryRealization
                            .CreateReferenceSource(
                                adapter,
                                PlatformHouseCandidateIdentity.Create(
                                    "installed-reference-candidate")),
                        realizationFallback,
                    ],
                    "installed-selected-library"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, fallbackInvocations);
        PlatformHouseConsumedWork consumed =
            terminal.TerminalRealization.Outcome.Receipt.ConsumedWork;
        Assert.Equal(1, consumed.Assemblies);
        Assert.Equal(1, consumed.XmlDocuments);
        Assert.Equal(maximumBytes, consumed.Bytes);
    }

    [Fact]
    public async Task
        InstalledReferencePopulation_TransfersOrderedLibraryAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePack = hive.CreateReferencePack();
        string systemRuntime =
            FindReferenceAssembly("System.Runtime.dll");
        string systemTextJson =
            FindReferenceAssembly("System.Text.Json.dll");
        hive.CopyAssembly(referencePack, systemRuntime);
        hive.CopyAssembly(referencePack, systemTextJson);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellationToken);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(
            sourceOperations: 1,
            assemblies: reference.Value.Libraries.Count,
            bytes: reference.Value.Libraries.Sum(
                static library => library.ContentLength));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        consumed));

        Assert.Equal(2, completed.Population.Value.Libraries.Count);
        Assert.Equal(2, completed.Population.Owners.Count);
        Assert.Same(
            reference.Contribution,
            Assert.Single(
                    completed.Population.Receipt.HouseReceipt
                        .SourceSettlements)
                .Contribution);
        for (int index = 0;
            index < reference.Value.Libraries.Count;
            index++)
        {
            InstalledReferenceLibrary source =
                reference.Value.Libraries[index];
            LibraryReference library =
                completed.Population.Value.Libraries[index];
            Assert.Same(
                library,
                completed.Population.Owners[index].Reference);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    Assert.IsType<ManagedMetadataIdentity.Assembly>(
                            library.ApiAssembly.AssemblyIdentity)
                        .Identity));
            var provenance =
                Assert.IsType<InstalledReferenceArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            library.ApiAssembly.ArtifactReference
                                .Provenance)
                        .SourceProvenance);
            Assert.Same(
                reference.Value.Generation,
                provenance.SourceGeneration);
            Assert.Same(
                reference.Value.Coordinate,
                provenance.Coordinate);
            Assert.Equal(source.FileName, provenance.FileName);
            using LibraryOperationLease operation =
                Issued(completed.Population.Owners[index], library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Population.Owners[0].DisposeAsync();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Population.Owners[1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        InstalledImplementationPopulation_TransfersOrderedLibraryAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string systemTextJson =
            typeof(JsonSerializer).Assembly.Location;
        string systemNetHttp =
            typeof(System.Net.Http.HttpClient).Assembly.Location;
        string framework = hive.CreateImplementationFramework(
            systemTextJson,
            systemNetHttp);
        hive.CopyAssembly(framework, systemTextJson);
        hive.CopyAssembly(framework, systemNetHttp);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(
            sourceOperations: 1,
            assemblies: implementation.Value.Libraries.Count,
            bytes: implementation.Value.Libraries.Sum(
                static library => library.ContentLength));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        consumed));

        Assert.Equal(
            implementation.Value.Libraries.Count,
            completed.Population.Value.Libraries.Count);
        Assert.Equal(
            implementation.Value.Libraries.Count,
            completed.Population.Owners.Count);
        Assert.Same(
            implementation.Contribution,
            Assert.Single(
                    completed.Population.Receipt.HouseReceipt
                        .SourceSettlements)
                .Contribution);
        Assert.NotNull(
            Assert.IsType<PlatformHouseCompletion.Realization>(
                    completed.Population.Receipt.HouseReceipt.Completion)
                .ViewCorrespondence);
        for (int index = 0;
            index < implementation.Value.Libraries.Count;
            index++)
        {
            InstalledImplementationLibrary source =
                implementation.Value.Libraries[index];
            LibraryReference library =
                completed.Population.Value.Libraries[index];
            Assert.Same(
                library,
                completed.Population.Owners[index].Reference);
            Assert.Same(
                library.ApiAssembly,
                library.ImplementationAssembly);
            Assert.Equal(2, library.ApiAssembly.Roles.Count);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    Assert.IsType<ManagedMetadataIdentity.Assembly>(
                            library.ApiAssembly.AssemblyIdentity)
                        .Identity));
            var provenance =
                Assert.IsType<InstalledImplementationArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            library.ApiAssembly.ArtifactReference
                                .Provenance)
                        .SourceProvenance);
            Assert.Same(
                implementation.Value.Generation,
                provenance.SourceGeneration);
            Assert.Same(
                implementation.Value.Coordinate,
                provenance.Coordinate);
            Assert.Equal(
                source.ManifestCoordinate,
                provenance.ManifestCoordinate);
            Assert.Equal(
                source.ContentDigest,
                provenance.ContentDigest);
            using LibraryOperationLease operation =
                Issued(completed.Population.Owners[index], library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        for (int index = 0;
            index < completed.Population.Owners.Count - 1;
            index++)
        {
            await completed.Population.Owners[index].DisposeAsync();
            Assert.False(artifactRetirement.IsCompleted);
        }
        await completed.Population.Owners[^1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        InstalledAspNetPopulation_PreservesRuntimeBindingSupport()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string runtimeAssembly =
            typeof(JsonSerializer).Assembly.Location;
        string aspNetAssembly =
            typeof(InstalledPlatformLibraryMaterializerTests)
                .Assembly.Location;
        (string runtime, string aspNetCore) =
            hive.CreateAspNetImplementationFrameworks(
                runtimeAssembly,
                aspNetAssembly);
        hive.CopyAssembly(runtime, runtimeAssembly);
        hive.CopyAssembly(aspNetCore, aspNetAssembly);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformFamilyTarget target = new(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation,
            target);
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(
            sourceOperations: 1,
            assemblies: implementation.Value.Libraries.Count,
            bytes: implementation.Value.Libraries.Sum(
                static library => library.ContentLength));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        consumed));

        Assert.Equal(
            [
                PlatformPopulationMemberRole.Focus,
                PlatformPopulationMemberRole.BindingSupport,
            ],
            completed.Population.Value.Members.Select(
                static member => member.Role));
        Assert.Equal(
            [
                PlatformFamily.AspNetCore,
                PlatformFamily.DotNetRuntime,
            ],
            completed.Population.Value.Members.Select(
                static member => member.Target.Family));

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        foreach (LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        InstalledAspNetPopulation_PreservesRolledRuntimeSupportTarget()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string runtimeAssembly =
            typeof(JsonSerializer).Assembly.Location;
        string aspNetAssembly =
            typeof(InstalledPlatformLibraryMaterializerTests)
                .Assembly.Location;
        (string runtime, string aspNetCore) =
            hive.CreateAspNetImplementationFrameworks(
                runtimeAssembly,
                aspNetAssembly,
                runtimeVersion: "12.0.0",
                runtimeReferenceVersion: "11.0.0",
                rollForward: "Major");
        hive.CopyAssembly(runtime, runtimeAssembly);
        hive.CopyAssembly(aspNetCore, aspNetAssembly);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformFamilyTarget target = new(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation,
            target);
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(
            sourceOperations: 1,
            assemblies: implementation.Value.Libraries.Count,
            bytes: implementation.Value.Libraries.Sum(
                static library => library.ContentLength));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        consumed));

        PlatformPopulationMember support = Assert.Single(
            completed.Population.Value.Members,
            static member =>
                member.Role
                    == PlatformPopulationMemberRole.BindingSupport);
        Assert.Equal(
            PlatformTargetFramework.Parse("net12.0"),
            support.Target.TargetFramework);
        Assert.Equal(
            PlatformVersion.Parse("12.0.0"),
            support.Target.Version);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        foreach (LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        InstalledPairedPopulation_TransfersLosslessUnionAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referenceJson =
            FindReferenceAssembly("System.Text.Json.dll");
        string referenceRuntime =
            FindReferenceAssembly("System.Runtime.dll");
        string implementationJson =
            typeof(JsonSerializer).Assembly.Location;
        string implementationHttp =
            typeof(System.Net.Http.HttpClient).Assembly.Location;
        string referencePack = hive.CreateReferencePack();
        hive.CopyAssembly(referencePack, referenceJson);
        hive.CopyAssembly(referencePack, referenceRuntime);
        string framework = hive.CreateImplementationFramework(
            implementationJson,
            implementationHttp);
        hive.CopyAssembly(framework, implementationJson);
        hive.CopyAssembly(framework, implementationHttp);

        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.ReferenceAndImplementation);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(
            sourceOperations: 2,
            assemblies:
                reference.Value.Libraries.Count
                + implementation.Value.Libraries.Count,
            bytes:
                reference.Value.Libraries.Sum(
                    static library => library.ContentLength)
                + implementation.Value.Libraries.Sum(
                    static library => library.ContentLength));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        consumed));

        Assert.Equal(3, completed.Population.Value.Libraries.Count);
        Assert.Equal(3, completed.Population.Owners.Count);
        Assert.Equal(
            [reference.Contribution, implementation.Contribution],
            completed.Population.Receipt.HouseReceipt.SourceSettlements
                .Select(static settlement => settlement.Contribution));
        Assert.NotNull(
            Assert.IsType<PlatformHouseCompletion.Realization>(
                    completed.Population.Receipt.HouseReceipt.Completion)
                .ViewCorrespondence);

        for (int index = 0;
            index < reference.Value.Libraries.Count;
            index++)
        {
            InstalledReferenceLibrary source =
                reference.Value.Libraries[index];
            LibraryReference library =
                completed.Population.Value.Libraries[index];
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    Assert.IsType<ManagedMetadataIdentity.Assembly>(
                            library.ApiAssembly.AssemblyIdentity)
                        .Identity));
            bool paired = implementation.Value.Libraries.Any(
                candidate =>
                    AssemblyReferenceIdentity.EquivalentComparer.Equals(
                        source.Identity,
                        candidate.Identity));
            Assert.Equal(
                paired,
                library.ImplementationAssembly is not null);
        }

        LibraryReference implementationOnly =
            completed.Population.Value.Libraries[^1];
        Assert.Equal(
            typeof(System.Net.Http.HttpClient).Assembly.GetName().Name,
            Assert.IsType<ManagedMetadataIdentity.Assembly>(
                    implementationOnly.ApiAssembly.AssemblyIdentity)
                .Identity.Name);
        Assert.Same(
            implementationOnly.ApiAssembly,
            implementationOnly.ImplementationAssembly);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        for (int index = 0;
            index < completed.Population.Owners.Count - 1;
            index++)
        {
            await completed.Population.Owners[index].DisposeAsync();
            Assert.False(artifactRetirement.IsCompleted);
        }
        await completed.Population.Owners[^1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        ForeignImplementationPopulation_ReturnsTerminal()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string implementationPath =
            typeof(JsonSerializer).Assembly.Location;
        using var requestedHive = new TestHive();
        InstalledPlatformHouseAdapter requestedAdapter =
            requestedHive.CreateAdapter();
        PlatformHouseRequest request = PopulationRequest(
            requestedAdapter,
            cancellationToken,
            PlatformViewDemand.Implementation);

        using var foreignHive = new TestHive();
        string foreignFramework =
            foreignHive.CreateImplementationFramework(
                implementationPath);
        foreignHive.CopyAssembly(
            foreignFramework,
            implementationPath);
        InstalledPlatformHouseAdapter foreignAdapter =
            foreignHive.CreateAdapter();
        PlatformHouseRequest foreignRequest = PopulationRequest(
            foreignAdapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var foreign = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await foreignAdapter.RealizeImplementationAsync(
                        foreignRequest));

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        foreign,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: foreign.Value.Libraries.Count,
                            bytes: foreign.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
    }

    [Fact]
    public async Task
        ImplementationPopulationCancellationPrecedesArtifactOwnership()
    {
        using var cancellation = new CancellationTokenSource();
        using var hive = new TestHive();
        string implementationPath =
            typeof(JsonSerializer).Assembly.Location;
        string framework =
            hive.CreateImplementationFramework(implementationPath);
        hive.CopyAssembly(framework, implementationPath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellation.Token,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        Consumed(
                            sourceOperations: 1,
                            assemblies:
                                implementation.Value.Libraries.Count,
                            bytes: implementation.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));
    }

    [Fact]
    public async Task
        InstalledSystemTextJson_TransfersLibraryAndArtifactAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        string implementationPath =
            typeof(JsonSerializer).Assembly.Location;
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        hive.CopyAssembly(
            hive.CreateImplementationFramework(implementationPath),
            implementationPath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        AssemblyReferenceIdentity identity =
            ReadIdentity(referencePath);
        PlatformHouseRequest request = Request(
            adapter,
            identity,
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken);

        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(
            sourceOperations: 2,
            assemblies:
                reference.Value.Libraries.Count
                + implementation.Value.Libraries.Count,
            bytes:
                reference.Value.Libraries.Sum(
                    static library => library.ContentLength)
                + implementation.Value.Libraries.Sum(
                    static library => library.ContentLength));

        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationAsync(
                        request,
                        reference,
                        implementation,
                        consumed));

        LibraryReference library = completed.Library.Value.Reference;
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.True(
            library.ImplementationAssembly!.HasRole(
                LibraryContentRole.ImplementationAssembly));
        Assert.True(
            AssemblyReferenceIdentity.EquivalentComparer.Equals(
                identity,
                Assert.IsType<ManagedMetadataIdentity.Assembly>(
                        library.ApiAssembly.AssemblyIdentity)
                    .Identity));
        var referenceProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                library.ApiAssembly.ArtifactReference.Provenance);
        Assert.Same(
            reference.Contribution,
            referenceProvenance.Contribution);
        var installedReference =
            Assert.IsType<InstalledReferenceArtifactProvenance>(
                referenceProvenance.SourceProvenance);
        Assert.Same(
            reference.Value.Generation,
            installedReference.SourceGeneration);
        Assert.Same(
            reference.Value.Coordinate,
            installedReference.Coordinate);
        Assert.True(
            AssemblyReferenceIdentity.EquivalentComparer.Equals(
                reference.Value.Libraries.Single().Identity,
                installedReference.Identity));
        var implementationProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                library.ImplementationAssembly.ArtifactReference
                    .Provenance);
        Assert.Same(
            implementation.Contribution,
            implementationProvenance.Contribution);
        var installedImplementation =
            Assert.IsType<InstalledImplementationArtifactProvenance>(
                implementationProvenance.SourceProvenance);
        Assert.Same(
            implementation.Value.Generation,
            installedImplementation.SourceGeneration);
        Assert.Same(
            implementation.Value.Coordinate,
            installedImplementation.Coordinate);
        Assert.Equal(
            implementation.Value.Libraries.Single().ContentDigest,
            installedImplementation.ContentDigest);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        using LibraryOperationLease operation = Issued(
            completed.Library.Owner,
            library);
        Assert.Equal(
            ((byte)'M', (byte)'M'),
            operation.SnapshotPair(
                library.ApiAssembly,
                library.ImplementationAssembly,
                static (view, _) =>
                    (view.First.Content[0],
                        view.Second.Content[0]),
                cancellationToken));
        operation.Dispose();
        await completed.Library.Owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task InstalledReferenceOnly_ClosesOneApiRole()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellationToken);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));

        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: reference.Value.Libraries.Count,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        Assert.Null(
            completed.Library.Value.Reference
                .ImplementationAssembly);
        Assert.Single(
            completed.Library.Value.Reference.Contents);
        CompiledXmlContribution contribution =
            PlatformDocumentationHouseAdapter
                .CreateCompiledXmlContribution(
                    completed.Library.Receipt,
                    Subject(completed.Library));
        Assert.Equal(
            CompiledXmlContributionKind.Unavailable,
            contribution.Kind);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
    }

    [Fact]
    public async Task
        InstalledReferenceDocumentation_BecomesExactPlatformCandidate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        string documentationPath = FindReferenceAssembly(
            "System.Text.Json.xml");
        string implementationPath =
            typeof(JsonSerializer).Assembly.Location;
        string referencePack = hive.CreateReferencePack();
        hive.CopyAssembly(referencePack, referencePath);
        hive.CopyFile(referencePack, documentationPath);
        hive.CopyAssembly(
            hive.CreateImplementationFramework(
                implementationPath),
            implementationPath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken,
            includeCompiledXmlDocumentation: true);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request));
        InstalledReferenceLibrary sourceLibrary =
            Assert.Single(reference.Value.Libraries);
        InstalledImplementationLibrary implementationLibrary =
            Assert.Single(
                implementation.Value.Libraries);
        Assert.NotNull(sourceLibrary.Documentation);

        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(
                            sourceOperations: 2,
                            assemblies: 2,
                            bytes:
                                sourceLibrary
                                    .TotalContentLength
                                + implementationLibrary
                                    .ContentLength,
                            xmlDocuments: 1)));
        try
        {
            LibraryReference library =
                completed.Library.Value.Reference;
            DocumentationSubjectReference subject =
                Subject(completed.Library);
            CompiledXmlContribution contribution =
                PlatformDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        completed.Library.Receipt,
                        subject);

            Assert.Equal(
                CompiledXmlContributionKind.Candidate,
                contribution.Kind);
            Assert.Equal(
                DocumentationSourceKind.Platform,
                contribution.Source.Kind);
            Assert.Equal(
                "platform:DotNetRuntime/net11.0/11.0.0",
                contribution.Source.Name);
            Assert.Same(library, contribution.Library);
            Assert.Same(
                library.ApiAssembly,
                contribution.CompiledXmlContent!
                    .AssociatedAssembly);
            Assert.NotSame(
                library.ImplementationAssembly,
                contribution.CompiledXmlContent
                    .AssociatedAssembly);
            var provenance =
                Assert.IsType<
                    PlatformLibraryArtifactProvenance>(
                        contribution.CompiledXmlContent
                            .Provenance);
            var installed =
                Assert.IsType<
                    InstalledReferenceDocumentationArtifactProvenance>(
                        provenance.SourceProvenance);
            Assert.Equal(
                "System.Text.Json.xml",
                installed.FileName);
        }
        finally
        {
            await completed.Library.Owner.DisposeAsync();
            await completed.Artifacts.DisposeAsync();
        }
    }

    [Fact]
    public async Task
        InstalledEmptyReferenceDocumentation_RemainsExactPlatformCandidate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        string referencePack = hive.CreateReferencePack();
        hive.CopyAssembly(referencePack, referencePath);
        File.WriteAllBytes(
            Path.Combine(
                referencePack,
                "System.Text.Json.xml"),
            []);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellationToken,
            includeCompiledXmlDocumentation: true);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        InstalledReferenceLibrary sourceLibrary =
            Assert.Single(reference.Value.Libraries);
        Assert.Equal(
            0,
            Assert.IsType<InstalledReferenceDocumentation>(
                    sourceLibrary.Documentation)
                .ContentLength);
        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes:
                                sourceLibrary
                                    .TotalContentLength,
                            xmlDocuments: 1)));
        try
        {
            LibraryReference library =
                completed.Library.Value.Reference;
            CompiledXmlContribution contribution =
                PlatformDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        completed.Library.Receipt,
                        Subject(completed.Library));

            Assert.Equal(
                CompiledXmlContributionKind.Candidate,
                contribution.Kind);
            using LibraryOperationLease operation = Issued(
                completed.Library.Owner,
                library);
            Assert.Equal(
                0,
                operation.Snapshot(
                    contribution.CompiledXmlContent!,
                    static (view, _) => view.Content.Length,
                    cancellationToken));
        }
        finally
        {
            await completed.Library.Owner.DisposeAsync();
            await completed.Artifacts.DisposeAsync();
        }
    }

    [Fact]
    public async Task
        RequestedMissingReferenceDocumentation_IsAuthoritativelyAbsent()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellationToken,
            includeCompiledXmlDocumentation: true);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        InstalledReferenceLibrary sourceLibrary =
            Assert.Single(reference.Value.Libraries);
        Assert.Null(sourceLibrary.Documentation);
        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes:
                                sourceLibrary
                                    .TotalContentLength)));
        try
        {
            CompiledXmlContribution contribution =
                PlatformDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        completed.Library.Receipt,
                        Subject(completed.Library));

            Assert.Equal(
                CompiledXmlContributionKind.Absent,
                contribution.Kind);
            Assert.Null(contribution.CompiledXmlContent);
        }
        finally
        {
            await completed.Library.Owner.DisposeAsync();
            await completed.Artifacts.DisposeAsync();
        }
    }

    [Fact]
    public async Task InstalledImplementationOnly_AssignsBothRoles()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string implementationPath =
            typeof(JsonSerializer).Assembly.Location;
        hive.CopyAssembly(
            hive.CreateImplementationFramework(implementationPath),
            implementationPath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(implementationPath),
            PlatformViewDemand.Implementation,
            cancellationToken);
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));

        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationAsync(
                        request,
                        implementation,
                        Consumed(
                            sourceOperations: 1,
                            assemblies:
                                implementation.Value.Libraries.Count,
                            bytes: implementation.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        LibraryReference library = completed.Library.Value.Reference;
        Assert.Same(
            library.ApiAssembly,
            library.ImplementationAssembly);
        Assert.Equal(2, library.ApiAssembly.Roles.Count);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
    }

    [Fact]
    public async Task ForeignSuccessfulResult_IsRejectedBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        AssemblyReferenceIdentity identity =
            ReadIdentity(referencePath);
        PlatformHouseRequest sourceRequest = Request(
            adapter,
            identity,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformHouseRequest materializationRequest = Request(
            adapter,
            identity,
            PlatformViewDemand.Reference,
            cancellationToken);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest));

        var terminal = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Terminal>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        materializationRequest,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
    }

    [Fact]
    public async Task MissingPriorSourceEvidence_ReturnsTerminalAfterCleanup()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellationToken,
            installedFirst: false);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));

        var terminal = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Terminal>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
    }

    [Fact]
    public async Task CancellationPrecedesArtifactOwnership()
    {
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        using var cancellation = new CancellationTokenSource();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellation.Token);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));
    }

    [Fact]
    public void InstalledArtifactProvenance_IsResourceFree()
    {
        Type[] types =
        [
            typeof(InstalledReferenceArtifactProvenance),
            typeof(InstalledImplementationArtifactProvenance),
            typeof(InstalledPlatformLibraryMaterializationResult.Terminal),
            typeof(PlatformPopulationArtifactMaterializationOutcome.Terminal),
        ];

        foreach (Type type in types)
        {
            Assert.Null(
                type.GetCustomAttribute<ResourceOwnershipAttribute>());
            Assert.All(
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field =>
                {
                    Assert.False(
                        typeof(IDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(IAsyncDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Stream).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Delegate).IsAssignableFrom(
                            field.FieldType));
                });
        }
    }

    [Fact]
    public void SuccessfulSourcePairing_IsAdapterIssued()
    {
        Type[] succeededResults =
        [
            typeof(InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded),
            typeof(InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded),
        ];

        foreach (Type result in succeededResults)
        {
            Assert.Empty(
                result.GetConstructors(
                    BindingFlags.Instance
                    | BindingFlags.Public));
        }
    }

    static PlatformHouseRequest SelectedLibraryRequest(
        InstalledPlatformHouseAdapter adapter,
        PlatformSourceCapabilityIdentity fallbackDiscovery,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-installed-library"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.AllFrameworks(),
                        [adapter.Capabilities.TargetDiscovery]),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [fallbackDiscovery])),
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 32,
                    maxComparisons: 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-installed-library")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformViewDemand.ReferenceAndImplementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-installed-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            adapter.Capabilities.TargetDiscovery,
                            fallbackDiscovery,
                        ]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.Capabilities.ReferenceRealization]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.Capabilities.ImplementationRealization]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 8,
                maxTargetCandidates: 32,
                maxAssemblies: 16,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 256L * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest SelectedReferencePopulationRequest(
        InstalledPlatformHouseAdapter adapter,
        PlatformSourceCapabilityIdentity fallbackDiscovery,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-installed-reference-population"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.AllFrameworks(),
                        [adapter.Capabilities.TargetDiscovery]),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [fallbackDiscovery])),
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 32,
                    maxComparisons: 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-installed-reference-population")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-installed-reference-population-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            adapter.Capabilities.TargetDiscovery,
                            fallbackDiscovery,
                        ]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.Capabilities.ReferenceRealization]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 4,
                maxTargetCandidates: 32,
                maxAssemblies: 16,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 256L * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest SelectedReferencePopulationFailureRequest(
        InstalledPlatformHouseAdapter adapter,
        PlatformSourceCapabilityIdentity fallbackDiscovery,
        PlatformSourceCapabilityIdentity fallbackReference,
        long maximumBytes,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-installed-reference-population-failure"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.AllFrameworks(),
                        [adapter.Capabilities.TargetDiscovery]),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [fallbackDiscovery])),
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 32,
                    maxComparisons: 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-installed-reference-population-failure")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-installed-reference-population-failure-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            adapter.Capabilities.TargetDiscovery,
                            fallbackDiscovery,
                        ]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            adapter.Capabilities.ReferenceRealization,
                            fallbackReference,
                        ]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 3,
                maxTargetCandidates: 32,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: maximumBytes,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest SelectedReferenceFailureRequest(
        InstalledPlatformHouseAdapter adapter,
        PlatformSourceCapabilityIdentity fallbackDiscovery,
        PlatformSourceCapabilityIdentity fallbackReference,
        AssemblyReferenceIdentity identity,
        long maximumBytes,
        CancellationToken cancellationToken,
        PlatformSourceSelectionMode referenceMode =
            PlatformSourceSelectionMode.Fallback) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-installed-reference-failure"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.AllFrameworks(),
                        [adapter.Capabilities.TargetDiscovery]),
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [fallbackDiscovery])),
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 32,
                    maxComparisons: 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-installed-reference-failure")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformViewDemand.Reference,
                PlatformLibraryContentDemand.CompiledXmlDocumentation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-installed-reference-failure-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            adapter.Capabilities.TargetDiscovery,
                            fallbackDiscovery,
                        ]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        referenceMode,
                        [
                            adapter.Capabilities.ReferenceRealization,
                            fallbackReference,
                        ]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 3,
                maxTargetCandidates: 32,
                maxAssemblies: 1,
                maxXmlDocuments: 1,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: maximumBytes,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest Request(
        InstalledPlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        PlatformViewDemand view,
        CancellationToken cancellationToken,
        bool installedFirst = true,
        bool includeCompiledXmlDocumentation = false)
    {
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    Capabilities(
                        adapter.Capabilities.ReferenceRealization,
                        installedFirst)));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    Capabilities(
                        adapter.Capabilities.ImplementationRealization,
                        installedFirst)));
        }

        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("installed-library"),
            new PlatformTargetDemand.Exact(Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                view,
                includeCompiledXmlDocumentation
                    ? PlatformLibraryContentDemand
                        .CompiledXmlDocumentation
                    : PlatformLibraryContentDemand.None),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("installed-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "installed-policy"),
                selections),
            Work(
                includeCompiledXmlDocumentation
                    ? 1
                    : 0),
            cancellationToken);
    }

    static PlatformHouseRequest PopulationRequest(
        InstalledPlatformHouseAdapter adapter,
        CancellationToken cancellationToken,
        PlatformViewDemand view = PlatformViewDemand.Reference,
        PlatformFamilyTarget? target = null)
    {
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [
                        adapter.Capabilities.ReferenceRealization,
                    ]));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [
                        adapter.Capabilities.ImplementationRealization,
                    ]));
        }
        return
        new(
            PlatformHouseRequestIdentity.Create(
                "installed-population"),
            new PlatformTargetDemand.Exact(target ?? Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "installed-population-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "installed-population-policy"),
                selections),
            Work(),
            cancellationToken);
    }

    static IReadOnlyList<PlatformSourceCapabilityIdentity> Capabilities(
        PlatformSourceCapabilityIdentity installed,
        bool installedFirst) =>
        installedFirst
            ? [installed]
            :
            [
                PlatformSourceCapabilityIdentity.Create("prior"),
                installed,
            ];

    static PlatformHouseWorkBudget Work(
        int maxXmlDocuments = 0) =>
        new(
            maxSourceOperations: 2,
            maxTargetCandidates: 0,
            maxAssemblies: 8,
            maxXmlDocuments,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 64 * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromSeconds(30));

    static PlatformHouseConsumedWork Consumed(
        int sourceOperations,
        int assemblies,
        long bytes,
        int xmlDocuments = 0) =>
        new(
            sourceOperations,
            targetCandidates: 0,
            assemblies,
            xmlDocuments,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    static DocumentationSubjectReference Subject(
        PlatformLibraryRealizationResult.Completed materialized)
    {
        LibraryReference library = materialized.Value.Reference;
        using LibraryOperationLease operation =
            Issued(materialized.Owner, library);
        var request = new LibraryApiSurfaceInspectionRequest(
            library,
            ApiSurfaceExtractionScope.Public,
            s_documentationApiSurfaceBounds);
        LibraryApiSurfaceCorrespondence correspondence =
            Assert.IsType<
                LibraryApiSurfaceInspectionOutcome.Completed>(
                    LibraryApiSurfaceInspection.Execute(
                        request,
                        operation,
                        TestContext.Current
                            .CancellationToken))
                .Correspondence;
        ApiType type = Assert.Single(
            correspondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        return DocumentationSubjectReference.ForType(
            correspondence,
            type);
    }

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));

    static LibraryOperationLease Issued(
        LibraryContentOwner owner,
        LibraryReference reference) =>
        Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                owner.IssueOperationLease(reference))
            .Lease;

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    static string FindReferenceAssembly(string fileName)
    {
        DirectoryInfo runtimeVersion =
            new FileInfo(typeof(object).Assembly.Location).Directory
            ?? throw new InvalidOperationException(
                "The runtime assembly location has no directory.");
        DirectoryInfo dotnetRoot =
            runtimeVersion.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException(
                "The runtime assembly location is outside a dotnet root.");
        string referenceRoot = Path.Combine(
            dotnetRoot.FullName,
            "packs",
            "Microsoft.NETCore.App.Ref");
        return Directory.EnumerateFiles(
                referenceRoot,
                fileName,
                SearchOption.AllDirectories)
            .Where(
                path => string.Equals(
                    new FileInfo(path).Directory?.Name,
                    "net11.0",
                    StringComparison.Ordinal))
            .OrderByDescending(
                static path => path,
                StringComparer.Ordinal)
            .First();
    }
}
