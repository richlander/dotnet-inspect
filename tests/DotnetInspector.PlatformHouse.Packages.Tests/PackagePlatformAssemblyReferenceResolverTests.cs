using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformAssemblyReferenceResolverTests
{
    [Fact]
    public async Task
        AdapterProducesExactBindingContributionAfterPackageSettlement()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(image);
        PlatformHouseRequest request =
            Request(adapter, identity, cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        await environment.AssertSettledAsync();

        var contribution =
            Assert.IsType<PlatformSourceContribution.Realization>(
                reference.Contribution);
        var population =
            Assert.IsType<PlatformPopulationDemand.Library>(
                contribution.Population);
        var assembly = Assert.IsType<PlatformLibraryDemand.Assembly>(
            population.Value);
        PackageReferenceLibrary library =
            Assert.Single(reference.Value.Libraries);
        Assert.True(identity.IsEquivalentTo(assembly.Identity));
        Assert.True(identity.IsEquivalentTo(library.Identity));
        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Same(((PlatformTargetDemand.Exact)request.Target).Target,
            contribution.Target);
        Assert.Same(
            adapter.ReferenceRealization,
            contribution.Capability);
        Assert.Equal(
            PlatformSourceContributionCompleteness.Authoritative,
            contribution.RealizationCompleteness);
        Assert.Equal(
            image,
            await PackagePlatformTestData.ReadAllAsync(library));
    }

    [Fact]
    public async Task
        ResolveAsync_PreservesPackageProvenanceAndSettlesContribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
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
        await environment.AssertSettledAsync();
        PlatformHouseConsumedWork consumed = Consumed(reference.Value);

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        consumed));

        var decision = Assert.IsType<AssemblyBindingDecision.Resolved>(
            completed.Value);
        var platformProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                decision.Candidate.Registration
                    .ArtifactRegistration!
                    .Provenance);
        Assert.Same(
            reference.Contribution,
            platformProvenance.Contribution);
        var packageProvenance =
            Assert.IsType<PackageReferenceArtifactProvenance>(
                platformProvenance.SourceProvenance);
        PackageReferenceLibrary library =
            Assert.Single(reference.Value.Libraries);
        Assert.Same(
            reference.Value.Generation,
            packageProvenance.SourceGeneration);
        Assert.Equal(
            reference.Contribution.Generation.Name,
            packageProvenance.SourceGeneration.Name);
        Assert.Same(
            reference.Value.Coordinate,
            packageProvenance.Coordinate);
        Assert.Equal(library.Path, packageProvenance.Path);
        Assert.Same(
            reference.Value.Candidate,
            packageProvenance.Candidate);
        Assert.Same(
            reference.Value.Authority,
            packageProvenance.Authority);
        Assert.Same(reference.Value.Source, packageProvenance.Source);
        Assert.Same(
            reference.Value.ContentGeneration,
            packageProvenance.ContentGeneration);
        Assert.Equal(reference.Value.Origin, packageProvenance.Origin);
        Assert.True(
            library.Identity.IsEquivalentTo(
                packageProvenance.Identity));
        Assert.Same(
            reference.Contribution,
            Assert.Single(completed.Receipt.SourceSettlements)
                .Contribution);
        Assert.Equal(
            PlatformHouseSettlementKind.Completed,
            completed.Receipt.SettlementKind);
        Assert.Equal(consumed, completed.Receipt.ConsumedWork);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsSuccessfulResultForDifferentRequest()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(image);
        PlatformHouseRequest sourceRequest =
            Request(adapter, identity, cancellationToken);
        PlatformHouseRequest executionRequest =
            Request(adapter, identity, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));
        await environment.AssertSettledAsync();

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PackagePlatformAssemblyReferenceResolver.ResolveAsync(
                executionRequest,
                reference,
                Consumed(reference.Value));

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                outcome);
        Assert.Equal(
            PlatformHouseRejectionKind.InvalidRequest,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.Empty(rejected.Receipt.SourceSettlements);
    }

    static byte[] Image() =>
        PackagePlatformTestData.Assembly(
            "System.Text.Json",
            new Version(11, 0, 0, 0));

    static PackagePlatformTestEnvironment Environment(byte[] image) =>
        PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Text.Json.dll",
                            image),
                    ]),
            ]);

    static PackagePlatformHouseAdapter Adapter(
        PackagePlatformTestEnvironment environment) =>
        new(environment.CreateSource(), "package-binding");

    static PlatformHouseRequest Request(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken)
    {
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(
                PackagePlatformTestEnvironment.Version));
        var origin = new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create(
                "package-binding-test"));
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("package-binding-plan"),
            PlatformSourcePolicyGeneration.Create(
                "package-binding-policy"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ReferenceRealization]),
            ]);
        var metadataRequest =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Platform),
                "package-binding-metadata-request");
        var route = new PlatformAssemblyReferenceRoute(
            metadataRequest.Identity,
            target,
            origin,
            sources.Identity,
            sources.Generation);
        var operation =
            new PlatformHouseOperation.ResolveAssemblyReference
                .WithPrerequisites<PlatformAssemblyReferenceRoute>(
                    metadataRequest,
                    new PlatformRoutePrerequisitesEvidence<
                        PlatformAssemblyReferenceRoute>(
                            route,
                            "package-binding-route"),
                    PlatformViewDemand.Reference);
        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "package-binding-request"),
            new PlatformTargetDemand.Exact(target),
            origin,
            operation,
            sources,
            new PlatformHouseWorkBudget(
                maxSourceOperations: 1,
                maxTargetCandidates: 0,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 16 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    static PlatformHouseConsumedWork Consumed(
        PackageReferenceRealization reference)
    {
        PackageReferenceLibrary library =
            Assert.Single(reference.Libraries);
        return new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: 1,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: library.ContentLength,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);
    }
}
