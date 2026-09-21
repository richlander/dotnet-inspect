using System.Reflection.Metadata;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

public sealed class InstalledPlatformHouseAdapterTests
{
    [Fact]
    public void DiscoverTargets_ProducesAuthorizedResourceFreeContribution()
    {
        using var hive = new TestHive();
        hive.CreateReferencePack();
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            cancellationToken: TestContext.Current.CancellationToken);

        InstalledPlatformHouseResult<InstalledReferenceTargetInventory>
            result = adapter.DiscoverTargets(request);

        var succeeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceTargetInventory>.Succeeded>(result);
        var contribution =
            Assert.IsType<PlatformSourceContribution.TargetDiscovery>(
                succeeded.Contribution);
        PlatformFamilyTarget candidate =
            Assert.Single(contribution.Candidates);
        Assert.Equal(PlatformFamily.DotNetRuntime, candidate.Family);
        Assert.Equal("net11.0", candidate.TargetFramework.ToString());
        Assert.Equal("11.0.0", candidate.Version.Value);
        Assert.Same(
            adapter.Capabilities.TargetDiscovery,
            contribution.Capability);
        Assert.Same(request.Snapshot, contribution.Request);
    }

    [Fact]
    public void
        DiscoverTargets_FamilyDefaultUsesAllFrameworksAndPreparesAssociations()
    {
        using var hive = new TestHive();
        hive.CreateReferencePack("10.0.12", "net10.0");
        hive.CreateReferencePack("11.0.0-rc.1", "net11.0");
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = FamilyDefaultRequest(
            adapter,
            TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceTargetInventory>.Succeeded>(
                    adapter.DiscoverTargets(request));
        var attempt =
            Assert.IsType<PlatformTargetDiscoveryAttempt.Succeeded>(
                InstalledPlatformTargetDiscovery.PrepareAttempt(
                    succeeded));

        Assert.Equal(
            ["10.0.12", "11.0.0-rc.1"],
            attempt.Candidates.Select(
                candidate => candidate.Target.Version.Value));
        Assert.All(
            attempt.Candidates,
            candidate => Assert.IsType<
                PlatformTargetDiscoveryCandidate<
                    InstalledReferenceTarget>>(candidate));
        Assert.Same(
            adapter.Capabilities.TargetDiscovery,
            InstalledPlatformTargetDiscovery.CreateSource(adapter).Capability);
    }

    [Fact]
    public void DiscoverTargets_RejectsUnauthorizedCapabilityBeforeSourceWork()
    {
        using var hive = new TestHive();
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            authorizeCapability: false,
            cancellationToken: TestContext.Current.CancellationToken);

        InstalledPlatformHouseResult<InstalledReferenceTargetInventory>
            result = adapter.DiscoverTargets(request);

        var notSucceeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceTargetInventory>.NotSucceeded>(result);
        Assert.Equal(
            PlatformSourceContributionKind.Rejected,
            notSucceeded.Contribution.Kind);
        Assert.Equal(
            PlatformSourceFacet.TargetDiscovery,
            notSucceeded.Contribution.Facet);
    }

    [Fact]
    public void
        DiscoverTargets_DemandMismatchReturnsTypedRejection()
    {
        using var hive = new TestHive();
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformSourceCapabilityIdentity demanded =
            PlatformSourceCapabilityIdentity.Create("other");
        PlatformHouseRequest request = SelectingRequest(
            adapter,
            demandedCapability: demanded,
            sourceCapabilities:
            [
                adapter.Capabilities.TargetDiscovery,
                demanded,
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PlatformHouseRequestValidation.Accepted>(
            PlatformHouseRequestValidation.Validate(request));
        InstalledPlatformHouseResult<InstalledReferenceTargetInventory>
            result = adapter.DiscoverTargets(request);

        var notSucceeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceTargetInventory>.NotSucceeded>(result);
        Assert.Equal(
            PlatformSourceContributionKind.Rejected,
            notSucceeded.Contribution.Kind);
        Assert.Same(
            adapter.Capabilities.TargetDiscovery,
            notSucceeded.Contribution.Capability);
    }

    [Fact]
    public async Task RealizeReference_ProducesAuthoritativeExactTargetContribution()
    {
        using var hive = new TestHive();
        string directory = hive.CreateReferencePack();
        hive.CopyAssembly(
            directory,
            typeof(InstalledPlatformHouseAdapterTests).Assembly.Location);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformFamilyTarget target = Target();
        PlatformHouseRequest request = ExactRequest(
            adapter,
            target,
            new PlatformPopulationDemand.CompletePopulation(),
            cancellationToken: TestContext.Current.CancellationToken);

        InstalledPlatformHouseResult<InstalledReferenceRealization>
            result = await adapter.RealizeReferenceAsync(request);

        var succeeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(result);
        var contribution =
            Assert.IsType<PlatformSourceContribution.Realization>(
                succeeded.Contribution);
        Assert.Equal(PlatformSourceFacet.Reference, contribution.Facet);
        Assert.Equal(
            PlatformSourceContributionCompleteness.Authoritative,
            contribution.RealizationCompleteness);
        Assert.Same(target, contribution.Target);
        Assert.Single(succeeded.Value.Libraries);
    }

    [Fact]
    public async Task
        RealizeReference_BindingProducesExactAssemblyContribution()
    {
        using var hive = new TestHive();
        string source =
            typeof(InstalledPlatformHouseAdapterTests).Assembly.Location;
        string directory = hive.CreateReferencePack();
        hive.CopyAssembly(directory, source);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformFamilyTarget target = Target();
        AssemblyReferenceIdentity identity = ReadIdentity(source);
        PlatformHouseRequest request = BindingRequest(
            adapter,
            target,
            identity,
            TestContext.Current.CancellationToken);

        InstalledPlatformHouseResult<InstalledReferenceRealization>
            result = await adapter.RealizeReferenceAsync(request);

        var succeeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(result);
        var contribution =
            Assert.IsType<PlatformSourceContribution.Realization>(
                succeeded.Contribution);
        var population =
            Assert.IsType<PlatformPopulationDemand.Library>(
                contribution.Population);
        var assembly = Assert.IsType<PlatformLibraryDemand.Assembly>(
            population.Value);
        Assert.True(identity.IsEquivalentTo(assembly.Identity));
        Assert.True(
            identity.IsEquivalentTo(
                Assert.Single(succeeded.Value.Libraries).Identity));
        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Same(target, contribution.Target);
        Assert.Same(
            adapter.Capabilities.ReferenceRealization,
            contribution.Capability);
        Assert.Equal(
            PlatformSourceContributionCompleteness.Authoritative,
            contribution.RealizationCompleteness);
    }

    [Fact]
    public async Task RealizeReference_RejectsOpaquePlatformLibraryIdentity()
    {
        using var hive = new TestHive();
        hive.CreateReferencePack();
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformLibraryIdentity identity =
            PlatformLibraryIdentityAuthority.Create("test").Issue("library");
        PlatformHouseRequest request = ExactRequest(
            adapter,
            Target(),
            new PlatformPopulationDemand.Library(
                new PlatformLibraryDemand.PlatformLibrary(identity)),
            cancellationToken: TestContext.Current.CancellationToken);

        InstalledPlatformHouseResult<InstalledReferenceRealization>
            result = await adapter.RealizeReferenceAsync(request);

        var notSucceeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded>(result);
        Assert.Equal(
            PlatformSourceContributionKind.Rejected,
            notSucceeded.Contribution.Kind);
    }

    [Fact]
    public async Task EntryPoints_PreserveCancellationBeforeZeroBudgetOutcome()
    {
        using var hive = new TestHive();
        hive.CreateReferencePack();
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        PlatformHouseWorkBudget noWork = Work(
            maxSourceOperations: 0,
            maxDuration: TimeSpan.Zero);

        PlatformHouseRequest discovery = SelectingRequest(
            adapter,
            work: noWork,
            cancellationToken: cancellation.Token);
        Assert.Throws<OperationCanceledException>(
            () => adapter.DiscoverTargets(discovery));

        PlatformHouseRequest realization = ExactRequest(
            adapter,
            Target(),
            new PlatformPopulationDemand.CompletePopulation(),
            work: noWork,
            cancellationToken: cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await adapter.RealizeReferenceAsync(realization));
    }

    [Fact]
    public async Task RealizeImplementation_ProducesAuthoritativeExactTargetContribution()
    {
        using var hive = new TestHive();
        string directory = hive.CreateImplementationFramework();
        hive.CopyAssembly(
            directory,
            typeof(InstalledPlatformHouseAdapterTests).Assembly.Location);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformFamilyTarget target = Target();
        PlatformHouseRequest request = ImplementationRequest(
            adapter,
            target,
            new PlatformPopulationDemand.CompletePopulation(),
            TestContext.Current.CancellationToken);

        InstalledPlatformHouseResult<InstalledImplementationRealization>
            result = await adapter.RealizeImplementationAsync(request);

        var succeeded = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(result);
        var contribution =
            Assert.IsType<PlatformSourceContribution.Realization>(
                succeeded.Contribution);
        Assert.Equal(
            PlatformSourceFacet.Implementation,
            contribution.Facet);
        Assert.Equal(
            PlatformSourceContributionCompleteness.Authoritative,
            contribution.RealizationCompleteness);
        Assert.Same(target, contribution.Target);
        Assert.Single(succeeded.Value.Frameworks);
        Assert.Single(succeeded.Value.Libraries);
    }

    [Fact]
    public async Task MissingImplementationMemberRetainsClosureWork()
    {
        using var hive = new TestHive();
        string directory = hive.CreateImplementationFramework();
        hive.CopyAssembly(
            directory,
            typeof(InstalledPlatformHouseAdapterTests).Assembly.Location);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = ImplementationRequest(
            adapter,
            Target(),
            new PlatformPopulationDemand.Library(
                new PlatformLibraryDemand.Assembly(
                    new AssemblyReferenceIdentity(
                        "Missing",
                        new Version(1, 0, 0, 0),
                        null,
                        null))),
            TestContext.Current.CancellationToken);

        var terminal = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.NotSucceeded>(
                    await adapter.RealizeImplementationAsync(request));

        Assert.NotNull(terminal.SourceWork);
        Assert.Equal(1, terminal.SourceWork.Assemblies);
        Assert.True(terminal.SourceWork.Bytes > 0);
    }

    static PlatformHouseRequest SelectingRequest(
        InstalledPlatformHouseAdapter adapter,
        bool authorizeCapability = true,
        PlatformSourceCapabilityIdentity? demandedCapability = null,
        IReadOnlyList<PlatformSourceCapabilityIdentity>? sourceCapabilities =
            null,
        PlatformHouseWorkBudget? work = null,
        CancellationToken cancellationToken = default)
    {
        PlatformSourceCapabilityIdentity authorized =
            authorizeCapability
                ? adapter.Capabilities.TargetDiscovery
                : PlatformSourceCapabilityIdentity.Create("other");
        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("discover"),
            new PlatformTargetDemand.Selecting(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net11.0"),
                new PlatformVersionSelectionDemand.Requirement(
                    PlatformVersionRequirementIdentity.Create("net11")),
                [
                    demandedCapability
                        ?? adapter.Capabilities.TargetDiscovery,
                ],
                new PlatformTargetDiscoveryBudget(8, 16)),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            Plan(
                PlatformSourceFacet.TargetDiscovery,
                sourceCapabilities is null
                    ? [authorized]
                    : [.. sourceCapabilities]),
            work ?? Work(),
            cancellationToken);
    }

    static PlatformHouseRequest FamilyDefaultRequest(
        InstalledPlatformHouseAdapter adapter,
        CancellationToken cancellationToken)
    {
        PlatformSourceCapabilityIdentity fallback =
            PlatformSourceCapabilityIdentity.Create("package-fallback");
        var preferred = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.AllFrameworks(),
            [adapter.Capabilities.TargetDiscovery]);
        var fallbackStage = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.ExactFramework(
                PlatformTargetFramework.Parse("net10.0")),
            [fallback]);
        return new(
            PlatformHouseRequestIdentity.Create("family-default-discover"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred,
                    fallbackStage),
                new PlatformTargetDiscoveryBudget(8, 16)),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            Plan(
                PlatformSourceFacet.TargetDiscovery,
                adapter.Capabilities.TargetDiscovery,
                fallback),
            Work(),
            cancellationToken);
    }

    static PlatformHouseRequest ExactRequest(
        InstalledPlatformHouseAdapter adapter,
        PlatformFamilyTarget target,
        PlatformPopulationDemand population,
        PlatformHouseWorkBudget? work = null,
        CancellationToken cancellationToken = default) =>
        new(
            PlatformHouseRequestIdentity.Create("realize"),
            new PlatformTargetDemand.Exact(target),
            Origin(),
            new PlatformHouseOperation.Realize(
                population,
                PlatformViewDemand.Reference),
            Plan(
                PlatformSourceFacet.Reference,
                adapter.Capabilities.ReferenceRealization),
            work ?? Work(),
            cancellationToken);

    static PlatformHouseRequest ImplementationRequest(
        InstalledPlatformHouseAdapter adapter,
        PlatformFamilyTarget target,
        PlatformPopulationDemand population,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create("implementation"),
            new PlatformTargetDemand.Exact(target),
            Origin(),
            new PlatformHouseOperation.Realize(
                population,
                PlatformViewDemand.Implementation),
            Plan(
                PlatformSourceFacet.Implementation,
                adapter.Capabilities.ImplementationRealization),
            Work(),
            cancellationToken);

    static PlatformHouseRequest BindingRequest(
        InstalledPlatformHouseAdapter adapter,
        PlatformFamilyTarget target,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken)
    {
        var origin = new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create("binding-test"));
        PlatformSourcePlan sources = Plan(
            PlatformSourceFacet.Reference,
            adapter.Capabilities.ReferenceRealization);
        var metadataRequest =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Platform),
                "installed-binding-request");
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
                            "installed-binding-route"),
                    PlatformViewDemand.Reference);
        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("installed-binding"),
            new PlatformTargetDemand.Exact(target),
            origin,
            operation,
            sources,
            Work(),
            cancellationToken);
    }

    static PlatformSourcePlan Plan(
        PlatformSourceFacet facet,
        params PlatformSourceCapabilityIdentity[] capabilities) =>
        new(
            PlatformSourcePlanIdentity.Create("installed-plan"),
            PlatformSourcePolicyGeneration.Create("installed-policy"),
            [
                new PlatformSourceSelection(
                    facet,
                    PlatformSourceSelectionMode.Precedence,
                    capabilities),
            ]);

    static PlatformHouseRequestOrigin Origin() =>
        new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create("test"));

    static PlatformHouseWorkBudget Work(
        int maxSourceOperations = 2,
        TimeSpan? maxDuration = null) =>
        new(
            maxSourceOperations,
            maxTargetCandidates: 8,
            maxAssemblies: 8,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 32 * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: maxDuration ?? TimeSpan.FromSeconds(30));

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader =
            new System.Reflection.PortableExecutable.PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

}
