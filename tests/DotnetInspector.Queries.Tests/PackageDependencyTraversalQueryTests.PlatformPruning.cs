using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyTraversalQueryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task PlatformSupplyAvoidsManifestAndDescendantsButKeepsDeclaration(
        int capacity)
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate supplied =
            Pinned(issuer, authorization, "System.Text.Json", "8.0.0");
        PackageAcquisitionCandidate retained =
            Pinned(issuer, authorization, "Newtonsoft.Json", "13.0.3");
        PackageDependencyTraversalRootOccurrence root = Root(
            "application-package", "1.0.0",
            Dependency("System.Text.Json", "[7.0.0, 9.0.0)")
                + Dependency("Newtonsoft.Json", "[13.0.3]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "application-package", "system.text.json", () => Resolved(supplied));
        resolver.SetResponse(
            "application-package", "newtonsoft.json", () => Resolved(retained));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(
            retained, ManifestBytes("Newtonsoft.Json", "13.0.3", ""));
        WorkspacePlan plan = PruningPlan();
        var pruning = new PackageDependencyTraversalPlatformPruning(
            plan, [PruneInventory("System.Text.Json|8.0.0")]);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root], resolver, acquirer,
            maxManifestProjections: 1,
            traversalTargetPolicy: plan.TraversalTargetPolicy,
            maximumConcurrentSteps: capacity,
            platformPruning: pruning);

        Assert.True(outcome.IsComplete);
        Assert.Equal(2, outcome.Nodes.Length);
        Assert.Equal("newtonsoft.json",
            Assert.Single(outcome.Edges).Declaration.CanonicalPackageId);
        Assert.Equal(1, acquirer.CallCount);
        Assert.Equal(0, acquirer.CallsFor(supplied.Coordinate));
        Assert.Same(root.Root, outcome.Projections[0].Evidence);
        PackageDependencyTraversalPruningOutcome result =
            Assert.IsType<PackageDependencyTraversalPruningOutcome>(
                outcome.PlatformPruning);
        Assert.Equal(1, result.OmittedRoutes);
        Assert.Equal(0, result.UnevaluatedRoutes);
        PlatformSupplyReceipt receipt = Assert.Single(
            result.Comparisons.Where(comparison =>
                comparison.Receipt.Supply.DelegatesToPlatform)).Receipt;
        Assert.Equal("8.0.0", receipt.Coordinate.Version);
        Assert.Same(pruning.Inventory, receipt.Inventory);
    }

    [Fact]
    public async Task PruningComparesResolvedVersionRatherThanRangeMinimum()
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageAcquisitionCandidate candidate =
            Pinned(issuer, Authorize("authority-a"), "System.Text.Json", "8.0.0");
        PackageDependencyTraversalRootOccurrence root = Root(
            "application-package", "1.0.0",
            Dependency("System.Text.Json", "[7.0.0, 9.0.0)"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "application-package", "system.text.json", () => Resolved(candidate));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(
            candidate, ManifestBytes("System.Text.Json", "8.0.0", ""));
        WorkspacePlan plan = PruningPlan();

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root], resolver, acquirer,
            traversalTargetPolicy: plan.TraversalTargetPolicy,
            platformPruning: new(
                plan, [PruneInventory("System.Text.Json|7.0.0")]));

        Assert.True(outcome.IsComplete);
        Assert.Single(outcome.Edges);
        Assert.Equal(1, acquirer.CallCount);
        PlatformSupplyReceipt receipt =
            Assert.Single(outcome.PlatformPruning!.Comparisons).Receipt;
        Assert.Equal(PlatformSubsumption.NotSubsumed, receipt.Supply.Subsumption);
        Assert.Equal("8.0.0", receipt.Coordinate.Version);
    }

    [Theory]
    [InlineData("missing", PackageDependencyTraversalPruningAvailability.Unavailable)]
    [InlineData("wrong-target", PackageDependencyTraversalPruningAvailability.Unavailable)]
    [InlineData("unselected-family", PackageDependencyTraversalPruningAvailability.Unavailable)]
    [InlineData("no-runtime", PackageDependencyTraversalPruningAvailability.Disabled)]
    public async Task UnavailableOrIneligibleSupplyRetainsPackageRoute(
        string scenario,
        PackageDependencyTraversalPruningAvailability availability)
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageAcquisitionCandidate candidate =
            Pinned(issuer, Authorize("authority-a"), "System.Text.Json", "8.0.0");
        PackageDependencyTraversalRootOccurrence root = Root(
            "application-package", "1.0.0",
            Dependency("System.Text.Json", "[8.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "application-package", "system.text.json", () => Resolved(candidate));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(
            candidate, ManifestBytes("System.Text.Json", "8.0.0", ""));
        WorkspacePlan plan = PruningPlan(runtime: scenario != "no-runtime");
        PlatformPruneInventory[] inventories = scenario switch
        {
            "missing" => [],
            "wrong-target" =>
                [PruneInventory("System.Text.Json|8.0.0", tfm: "net9.0")],
            "unselected-family" or "no-runtime" =>
                [PruneInventory(
                    "System.Text.Json|8.0.0",
                    family: "Microsoft.AspNetCore.App")],
            _ => throw new InvalidOperationException(),
        };

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root], resolver, acquirer,
            traversalTargetPolicy: plan.TraversalTargetPolicy,
            platformPruning: new(plan, inventories));

        Assert.Single(outcome.Edges);
        Assert.Equal(1, acquirer.CallCount);
        Assert.Equal(availability, outcome.PlatformPruning!.Input.Availability);
        Assert.Equal(0, outcome.PlatformPruning.OmittedRoutes);
        Assert.Equal(
            scenario == "no-runtime" ? 0 : 1,
            outcome.PlatformPruning.UnevaluatedRoutes);
    }

    [Fact]
    public async Task PrunedRecurrenceDoesNotRemoveExplicitSuppliedRoot()
    {
        PackageDependencyTraversalRootOccurrence application = Root(
            "application-package", "1.0.0",
            Dependency("System.Text.Json", "[8.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalRootOccurrence supplied = Root(
            "System.Text.Json", "8.0.0", "",
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        supplied = new(
            supplied.Root, supplied.Authority,
            PackageDependencyTraversalRootRecurrenceAuthority.ExactCoordinate);
        var resolver = new StubCandidateResolver();
        var acquirer = new StubManifestAcquirer();
        WorkspacePlan plan = PruningPlan();

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [application, supplied], resolver, acquirer,
            traversalTargetPolicy: plan.TraversalTargetPolicy,
            platformPruning: new(
                plan, [PruneInventory("System.Text.Json|8.0.0")]));

        Assert.True(outcome.IsComplete);
        Assert.Equal(2, outcome.Roots.Length);
        Assert.Equal(2, outcome.Nodes.Length);
        Assert.Empty(outcome.Edges);
        Assert.Equal(1, outcome.PlatformPruning!.OmittedRoutes);
        Assert.Equal(0, resolver.CallCount);
        Assert.Equal(0, acquirer.CallCount);
    }

    [Fact]
    public async Task DeclarationOnlyAuthorityDoesNotAcquirePruningEvidence()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "application-package", "1.0.0",
            Dependency("System.Text.Json", "[8.0.0]"),
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);
        var resolver = new StubCandidateResolver();
        var acquirer = new StubManifestAcquirer();
        WorkspacePlan plan = PruningPlan();

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root], resolver, acquirer,
            traversalTargetPolicy: plan.TraversalTargetPolicy,
            platformPruning: new(
                plan, [PruneInventory("System.Text.Json|8.0.0")]));

        Assert.Single(outcome.DeclarationBoundaries);
        Assert.Empty(outcome.PlatformPruning!.Comparisons);
        Assert.Equal(0, resolver.CallCount);
        Assert.Equal(0, acquirer.CallCount);
    }

    private static WorkspacePlan PruningPlan(bool runtime = true) =>
        new(new TraversalTargetFrameworkPolicy("net8.0"),
        [
            new WorkspaceRegistration.Ecosystem(new(
                WorkspaceEcosystemRegistrationId.Create(
                    runtime ? "ecosystem.runtime" : "ecosystem.aspnetcore"),
                [], [],
                [
                    new WorkspaceEcosystemPopulationDeclaration.Platform(
                        new PlatformLibraryPopulationDeclaration(
                            runtime
                                ? PlatformFamily.DotNetRuntime
                                : PlatformFamily.AspNetCore)),
                ])),
        ]);

    private static PlatformPruneInventory PruneInventory(
        string supply,
        string tfm = "net8.0",
        string family = "Microsoft.NETCore.App") =>
        PlatformPruneInventory.FromExactFamily(family, tfm, "8.0.0", [supply]);
}
