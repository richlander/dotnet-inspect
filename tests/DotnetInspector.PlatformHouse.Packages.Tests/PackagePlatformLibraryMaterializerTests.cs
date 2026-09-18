using System.Reflection;
using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Resources;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformLibraryMaterializerTests
{
    [Fact]
    public async Task
        PackageReferencePopulation_TransfersOrderedLibraryAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        Assert.Equal(
            ["System.Runtime", "System.Text.Json"],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(
            reference.Value.Libraries.Length,
            completed.Population.Owners.Count);
        Assert.Same(
            reference.Contribution,
            Assert.Single(
                    completed.Population.Receipt.HouseReceipt
                        .SourceSettlements)
                .Contribution);
        for (int index = 0;
            index < reference.Value.Libraries.Length;
            index++)
        {
            PackageReferenceLibrary source =
                reference.Value.Libraries[index];
            LibraryReference library =
                completed.Population.Value.Libraries[index];
            Assert.Same(
                library,
                completed.Population.Owners[index].Reference);
            Assert.Null(library.ImplementationAssembly);
            Assert.Single(library.Contents);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    library.ApiAssembly.AssemblyIdentity!.Identity));
            var provenance =
                Assert.IsType<PackageReferenceArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            library.ApiAssembly.ArtifactReference
                                .Provenance)
                        .SourceProvenance);
            Assert.Same(
                reference.Value.Generation,
                provenance.SourceGeneration);
            Assert.Same(reference.Value.Coordinate, provenance.Coordinate);
            Assert.Equal(source.Path, provenance.Path);
            Assert.Same(reference.Value.Candidate, provenance.Candidate);
            Assert.Same(reference.Value.Authority, provenance.Authority);
            Assert.Same(reference.Value.Source, provenance.Source);
            Assert.Same(
                reference.Value.ContentGeneration,
                provenance.ContentGeneration);
            Assert.Equal(reference.Value.Origin, provenance.Origin);
            using LibraryOperationLease operation = Issued(
                completed.Population.Owners[index],
                library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        await environment.AssertSettledAsync();
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
        PackageReferencePopulation_RejectsForeignContribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest sourceRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        PlatformHouseRequest materializationRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        materializationRequest,
                        reference,
                        Consumed(reference.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageReferencePopulation_IncompleteWorkTransfersNoAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseConsumedWork consumed = Consumed(reference.Value);

        var terminal = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        new PlatformHouseConsumedWork(
                            consumed.SourceOperations,
                            consumed.TargetCandidates,
                            request.Work.MaxAssemblies + 1,
                            consumed.XmlDocuments,
                            consumed.PortablePdbs,
                            consumed.SourceDocuments,
                            consumed.Bytes,
                            consumed.ForwardingHops,
                            consumed.TargetComparisons,
                            consumed.Elapsed)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageReferencePopulation_CancellationTransfersNoAuthority()
    {
        using var cancellation = new CancellationTokenSource();
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellation.Token,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageImplementationPopulation_TransfersOrderedLibraryAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        Consumed(implementation.Value)));

        Assert.Equal(
            ["System.Net.Http", "System.Text.Json"],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(
            implementation.Value.Libraries.Select(
                static library => library.Identity),
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity),
            AssemblyReferenceIdentity.EquivalentComparer);
        Assert.Equal(
            implementation.Value.Libraries.Length,
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
            index < implementation.Value.Libraries.Length;
            index++)
        {
            PackageImplementationLibrary source =
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
                    library.ApiAssembly.AssemblyIdentity!.Identity));
            var provenance =
                Assert.IsType<PackageImplementationArtifactProvenance>(
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
            Assert.Equal(source.Framework.Name, provenance.FrameworkName);
            Assert.Equal(
                source.Framework.Family,
                provenance.FrameworkFamily);
            Assert.Equal(
                source.Framework.Version,
                provenance.FrameworkVersion);
            Assert.Equal(source.Framework.PackageId, provenance.PackageId);
            Assert.Equal(
                source.Framework.RuntimeIdentifier,
                provenance.RuntimeIdentifier);
            Assert.Equal(
                source.ManifestCoordinate,
                provenance.ManifestCoordinate);
            Assert.Same(source.Framework.Candidate, provenance.Candidate);
            Assert.Same(source.Framework.Authority, provenance.Authority);
            Assert.Same(source.Framework.Source, provenance.Source);
            Assert.Same(
                source.Framework.ContentGeneration,
                provenance.ContentGeneration);
            Assert.Equal(source.Framework.Origin, provenance.Origin);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    provenance.Identity));
            Assert.Equal(source.ContentDigest, provenance.ContentDigest);
            using LibraryOperationLease operation = Issued(
                completed.Population.Owners[index],
                library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        await environment.AssertSettledAsync();
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
        PackageImplementationPopulation_RejectsForeignContribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest sourceRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        PlatformHouseRequest materializationRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        sourceRequest,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        materializationRequest,
                        implementation,
                        Consumed(implementation.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageImplementationPopulation_IncompleteWorkTransfersNoAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseConsumedWork consumed =
            Consumed(implementation.Value);

        var terminal = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        new PlatformHouseConsumedWork(
                            consumed.SourceOperations,
                            consumed.TargetCandidates,
                            request.Work.MaxAssemblies + 1,
                            consumed.XmlDocuments,
                            consumed.PortablePdbs,
                            consumed.SourceDocuments,
                            consumed.Bytes,
                            consumed.ForwardingHops,
                            consumed.TargetComparisons,
                            consumed.Elapsed)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageImplementationPopulation_CancellationTransfersNoAuthority()
    {
        using var cancellation = new CancellationTokenSource();
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellation.Token,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        Consumed(implementation.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackagePairedPopulation_TransfersLosslessUnionAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(
                            reference.Value,
                            implementation.Value)));

        Assert.Equal(
            [
                "System.Runtime",
                "System.Text.Json",
                "System.Net.Http",
            ],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(3, completed.Population.Owners.Count);
        Assert.Equal(
            [reference.Contribution, implementation.Contribution],
            completed.Population.Receipt.HouseReceipt.SourceSettlements
                .Select(static settlement => settlement.Contribution));

        LibraryReference referenceOnly =
            completed.Population.Value.Libraries[0];
        Assert.Null(referenceOnly.ImplementationAssembly);
        Assert.Single(referenceOnly.Contents);
        var referenceProvenance =
            Assert.IsType<PackageReferenceArtifactProvenance>(
                Assert.IsType<PlatformLibraryArtifactProvenance>(
                        referenceOnly.ApiAssembly.ArtifactReference.Provenance)
                    .SourceProvenance);
        Assert.Same(
            reference.Value.Candidate,
            referenceProvenance.Candidate);
        Assert.Same(
            reference.Value.Authority,
            referenceProvenance.Authority);
        Assert.Same(
            reference.Value.ContentGeneration,
            referenceProvenance.ContentGeneration);

        LibraryReference paired =
            completed.Population.Value.Libraries[1];
        Assert.Equal(2, paired.Contents.Count);
        Assert.NotSame(
            paired.ApiAssembly,
            paired.ImplementationAssembly);

        LibraryReference implementationOnly =
            completed.Population.Value.Libraries[2];
        Assert.Same(
            implementationOnly.ApiAssembly,
            implementationOnly.ImplementationAssembly);
        Assert.Equal(2, implementationOnly.ApiAssembly.Roles.Count);
        PackageImplementationLibrary sourceImplementation =
            Assert.Single(
                implementation.Value.Libraries,
                static library =>
                    library.Identity.Name == "System.Net.Http");
        var implementationProvenance =
            Assert.IsType<PackageImplementationArtifactProvenance>(
                Assert.IsType<PlatformLibraryArtifactProvenance>(
                        implementationOnly.ApiAssembly.ArtifactReference
                            .Provenance)
                    .SourceProvenance);
        Assert.Same(
            sourceImplementation.Framework.Candidate,
            implementationProvenance.Candidate);
        Assert.Same(
            sourceImplementation.Framework.Authority,
            implementationProvenance.Authority);
        Assert.Same(
            sourceImplementation.Framework.ContentGeneration,
            implementationProvenance.ContentGeneration);
        Assert.Equal(
            sourceImplementation.ContentDigest,
            implementationProvenance.ContentDigest);

        await environment.AssertSettledAsync();
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        for (int index = 0;
            index < completed.Population.Owners.Count;
            index++)
        {
            Assert.Same(
                completed.Population.Value.Libraries[index],
                completed.Population.Owners[index].Reference);
            await completed.Population.Owners[index].DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        PackagePairedPopulation_RejectsForeignImplementation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellationToken);
        PlatformHouseRequest foreign =
            PopulationRequest(adapter, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        foreign,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                foreign.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(
                            reference.Value,
                            implementation.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackagePairedPopulation_IncompleteWorkTransfersNoAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseConsumedWork consumed =
            Consumed(reference.Value, implementation.Value);

        var terminal = Assert.IsType<
            PackagePlatformPopulationMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        new PlatformHouseConsumedWork(
                            consumed.SourceOperations,
                            consumed.TargetCandidates,
                            request.Work.MaxAssemblies + 1,
                            consumed.XmlDocuments,
                            consumed.PortablePdbs,
                            consumed.SourceDocuments,
                            consumed.Bytes,
                            consumed.ForwardingHops,
                            consumed.TargetComparisons,
                            consumed.Elapsed)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackagePairedPopulation_CancellationTransfersNoAuthority()
    {
        using var cancellation = new CancellationTokenSource();
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellation.Token);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(
                            reference.Value,
                            implementation.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageSystemTextJson_TransfersLibraryAndArtifactAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] referenceImage =
            PackagePlatformTestData.Assembly(
                "System.Text.Json",
                new Version(11, 0, 0, 0));
        byte[] implementationImage =
            PackagePlatformTestData.Assembly(
                "System.Text.Json",
                new Version(11, 0, 0, 0));
        await using PackagePlatformTestEnvironment environment =
            Environment(referenceImage, implementationImage);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(referenceImage);
        PlatformHouseRequest request = Request(
            adapter,
            identity,
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(reference.Value, implementation.Value)));

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
        var packageReference =
            Assert.IsType<PackageReferenceArtifactProvenance>(
                referenceProvenance.SourceProvenance);
        Assert.Same(
            reference.Value.Generation,
            packageReference.SourceGeneration);
        Assert.Same(
            reference.Value.Candidate,
            packageReference.Candidate);
        Assert.Same(
            reference.Value.Authority,
            packageReference.Authority);
        Assert.Same(
            reference.Value.Source,
            packageReference.Source);
        Assert.Same(
            reference.Value.ContentGeneration,
            packageReference.ContentGeneration);

        var implementationProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                library.ImplementationAssembly.ArtifactReference
                    .Provenance);
        Assert.Same(
            implementation.Contribution,
            implementationProvenance.Contribution);
        var packageImplementation =
            Assert.IsType<PackageImplementationArtifactProvenance>(
                implementationProvenance.SourceProvenance);
        PackageImplementationLibrary implementationLibrary =
            Assert.Single(implementation.Value.Libraries);
        Assert.Same(
            implementation.Value.Generation,
            packageImplementation.SourceGeneration);
        Assert.Same(
            implementationLibrary.Framework.Candidate,
            packageImplementation.Candidate);
        Assert.Same(
            implementationLibrary.Framework.ContentGeneration,
            packageImplementation.ContentGeneration);
        Assert.Equal(
            implementationLibrary.ContentDigest,
            packageImplementation.ContentDigest);

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
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task PackageReferenceOnly_ClosesOneApiRole()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Reference,
            cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        Assert.Null(
            completed.Library.Value.Reference
                .ImplementationAssembly);
        Assert.Single(
            completed.Library.Value.Reference.Contents);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task PackageImplementationOnly_AssignsBothRoles()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(referenceImage: null, implementationImage: image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Implementation,
            cancellationToken);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationAsync(
                        request,
                        implementation,
                        Consumed(implementation.Value)));

        LibraryReference library = completed.Library.Value.Reference;
        Assert.Same(
            library.ApiAssembly,
            library.ImplementationAssembly);
        Assert.Equal(2, library.ApiAssembly.Roles.Count);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ForeignSuccessfulResult_IsRejectedBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(image);
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
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        materializationRequest,
                        reference,
                        Consumed(reference.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task MissingPriorSourceEvidence_ReturnsTerminalAfterCleanup()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Reference,
            cancellationToken,
            packageFirst: false);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task CancellationPrecedesArtifactOwnership()
    {
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        using var cancellation = new CancellationTokenSource();
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Reference,
            cancellation.Token);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public void PackageArtifactProvenance_IsResourceFree()
    {
        Type[] types =
        [
            typeof(PackageReferenceArtifactProvenance),
            typeof(PackageImplementationArtifactProvenance),
            typeof(PackagePlatformLibraryMaterializationResult.Terminal),
            typeof(PackagePlatformPopulationMaterializationResult.Terminal),
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
            typeof(PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded),
            typeof(PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded),
        ];

        foreach (Type result in succeededResults)
        {
            Assert.Empty(
                result.GetConstructors(
                    BindingFlags.Instance
                    | BindingFlags.Public));
        }
    }

    static PackagePlatformTestEnvironment Environment(
        byte[]? referenceImage,
        byte[]? implementationImage = null)
    {
        var packages = new List<(
            string PackageId,
            string Version,
            IReadOnlyList<KeyValuePair<string, byte[]>> Entries)>();
        if (referenceImage is not null)
        {
            packages.Add(
                (
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    PackagePlatformTestEnvironment.Version,
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Text.Json.dll",
                            referenceImage),
                    ]));
        }
        if (implementationImage is not null)
        {
            packages.Add(
                (
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    PackagePlatformTestEnvironment.Version,
                    PackagePlatformTestData.RuntimePackEntries(
                        "Microsoft.NETCore.App",
                        PackagePlatformTestData.RuntimeConfiguration(),
                        PackagePlatformTestData.DependencyManifest(
                            "System.Text.Json.dll"),
                        ("System.Text.Json.dll", implementationImage))));
        }
        return PackagePlatformTestEnvironment.Create(
            [TestSourceBehavior.CreatePackages([.. packages])]);
    }

    static PackagePlatformTestEnvironment PopulationEnvironment()
    {
        byte[] json =
            PackagePlatformTestData.Assembly("System.Text.Json");
        byte[] runtime =
            PackagePlatformTestData.Assembly("System.Runtime");
        byte[] http =
            PackagePlatformTestData.Assembly("System.Net.Http");
        var referenceEntries =
            new List<KeyValuePair<string, byte[]>>
            {
                PackagePlatformTestData.Entry(
                    "ref/net11.0/System.Runtime.dll",
                    runtime),
                PackagePlatformTestData.Entry(
                    "ref/net11.0/System.Text.Json.dll",
                    json),
            };
        return PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        PackagePlatformTestEnvironment.Version,
                        referenceEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        PackagePlatformTestData.RuntimePackEntries(
                            "Microsoft.NETCore.App",
                            PackagePlatformTestData.RuntimeConfiguration(),
                            PackagePlatformTestData.DependencyManifest(
                                "System.Text.Json.dll",
                                "System.Net.Http.dll"),
                            ("System.Text.Json.dll", json),
                            ("System.Net.Http.dll", http)))),
            ]);
    }

    static PackagePlatformHouseAdapter Adapter(
        PackagePlatformTestEnvironment environment) =>
        new(environment.CreateSource(), "package-materializer");

    static PlatformHouseRequest Request(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        PlatformViewDemand view,
        CancellationToken cancellationToken,
        bool packageFirst = true)
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
                        adapter.ReferenceRealization,
                        packageFirst)));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    Capabilities(
                        adapter.ImplementationRealization,
                        packageFirst)));
        }

        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("package-library"),
            new PlatformTargetDemand.Exact(Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("package-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "package-policy"),
                selections),
            Work(),
            cancellationToken);
    }

    static PlatformHouseRequest PopulationRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken,
        PlatformViewDemand view =
            PlatformViewDemand.ReferenceAndImplementation)
    {
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ReferenceRealization]));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ImplementationRealization]));
        }

        return
        new(
            PlatformHouseRequestIdentity.Create("package-population"),
            new PlatformTargetDemand.Exact(Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("package-population-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "package-population-policy"),
                selections),
            Work(),
            cancellationToken);
    }

    static IReadOnlyList<PlatformSourceCapabilityIdentity> Capabilities(
        PlatformSourceCapabilityIdentity package,
        bool packageFirst) =>
        packageFirst
            ? [package]
            :
            [
                PlatformSourceCapabilityIdentity.Create("prior"),
                package,
            ];

    static PlatformHouseWorkBudget Work() =>
        new(
            maxSourceOperations: 8,
            maxTargetCandidates: 0,
            maxAssemblies: 512,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 64 * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromSeconds(30));

    static PlatformHouseConsumedWork Consumed(
        PackageReferenceRealization reference) =>
        Consumed(
            sourceOperations: 1,
            assemblies: reference.Libraries.Length,
            bytes: reference.Libraries.Sum(
                static library => library.ContentLength));

    static PlatformHouseConsumedWork Consumed(
        PackageImplementationRealization implementation) =>
        Consumed(
            sourceOperations: implementation.Frameworks.Length,
            assemblies: implementation.Libraries.Length,
            bytes: implementation.Libraries.Sum(
                static library => library.ContentLength));

    static PlatformHouseConsumedWork Consumed(
        PackageReferenceRealization reference,
        PackageImplementationRealization implementation) =>
        Consumed(
            sourceOperations: 1 + implementation.Frameworks.Length,
            assemblies:
                reference.Libraries.Length
                + implementation.Libraries.Length,
            bytes:
                reference.Libraries.Sum(
                    static library => library.ContentLength)
                + implementation.Libraries.Sum(
                    static library => library.ContentLength));

    static PlatformHouseConsumedWork Consumed(
        int sourceOperations,
        int assemblies,
        long bytes) =>
        new(
            sourceOperations,
            targetCandidates: 0,
            assemblies,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(
                PackagePlatformTestEnvironment.Version));

    static LibraryOperationLease Issued(
        LibraryContentOwner owner,
        LibraryReference reference) =>
        Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                owner.IssueOperationLease(reference))
            .Lease;
}
