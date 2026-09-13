using System.Text.Json;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;

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

    static PlatformHouseRequest SelectingRequest(
        InstalledPlatformHouseAdapter adapter,
        bool authorizeCapability = true,
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
                [adapter.Capabilities.TargetDiscovery],
                new PlatformTargetDiscoveryBudget(8, 16)),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            Plan(PlatformSourceFacet.TargetDiscovery, authorized),
            work ?? Work(),
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

    static PlatformSourcePlan Plan(
        PlatformSourceFacet facet,
        PlatformSourceCapabilityIdentity capability) =>
        new(
            PlatformSourcePlanIdentity.Create("installed-plan"),
            PlatformSourcePolicyGeneration.Create("installed-policy"),
            [
                new PlatformSourceSelection(
                    facet,
                    PlatformSourceSelectionMode.Precedence,
                    [capability]),
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

    private sealed class TestHive : IDisposable
    {
        internal TestHive()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "dotnet-inspect-house-installed-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Identity = InstalledDotnetHiveIdentity.Create(
                "test-hive-" + Guid.NewGuid().ToString("N"));
        }

        internal string Root { get; }
        internal InstalledDotnetHiveIdentity Identity { get; }

        internal InstalledPlatformHouseAdapter CreateAdapter() =>
            new(
                new InstalledReferencePackSource(Identity, Root),
                new InstalledImplementationPlatformSource(Identity, Root),
                "installed");

        internal string CreateReferencePack()
        {
            string directory = Path.Combine(
                Root,
                "packs",
                "Microsoft.NETCore.App.Ref",
                "11.0.0",
                "ref",
                "net11.0");
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal string CreateImplementationFramework()
        {
            const string family = "Microsoft.NETCore.App";
            const string version = "11.0.0";
            string directory = Path.Combine(
                Root,
                "shared",
                family,
                version);
            Directory.CreateDirectory(directory);
            string assemblyName = Path.GetFileName(
                typeof(InstalledPlatformHouseAdapterTests)
                    .Assembly.Location);
            File.WriteAllText(
                Path.Combine(directory, family + ".deps.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        runtimeTarget = new { name = "target" },
                        targets = new Dictionary<string, object>
                        {
                            ["target"] = new Dictionary<string, object>
                            {
                                [family + ".Runtime/" + version] = new
                                {
                                    runtime =
                                        new Dictionary<string, object>
                                        {
                                            [assemblyName] = new { },
                                        },
                                },
                            },
                        },
                    }));
            return directory;
        }

        internal void CopyAssembly(
            string directory,
            string sourcePath) =>
            File.Copy(
                sourcePath,
                Path.Combine(directory, Path.GetFileName(sourcePath)));

        public void Dispose() =>
            Directory.Delete(Root, recursive: true);
    }
}
