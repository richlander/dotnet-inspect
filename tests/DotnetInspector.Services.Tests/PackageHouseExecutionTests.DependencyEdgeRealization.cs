using System.Text;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task
        DependencyEdgeRealizationSelectsCompatibleDestinationWithoutChangingSource()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.Create(
                new SourceBehavior(
                    [Version],
                    PayloadEntries:
                    [
                        $"lib/net10.0/{PackageId}.dll",
                        $"lib/net11.0/{PackageId}.dll",
                    ]));
        PackageDependencyTraversalOutcome traversal =
            await TraversalAsync(
                environment,
                PackageId,
                Version,
                sourceFramework: "netstandard2.0",
                traversalFramework: "net12.0");
        var request = new PackageDependencyEdgeRealizationRequest(
            traversal,
            rootOccurrenceIndex: 0,
            edgeIndex: 0,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net12.0"));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(request);

        PackageDependencyEdgeRealizationEvidence evidence =
            await execution.ExecuteAsync(
                environment.CreateHouse(
                    (_, _) => new InMemoryPackageStore()),
                environment.IssueOperation(
                    execution.Request,
                    TestContext.Current.CancellationToken));

        PackageHouseResult.Settled settled =
            Assert.IsType<PackageHouseResult.Settled>(evidence.Result);
        PackageHouseRootContribution contribution =
            Assert.IsType<PackageHouseRootContributionOutcome.Contributed>(
                evidence.RootContribution).Contribution;
        Assert.Same(settled, contribution.Result);
        Assert.Equal(
            "netstandard2.0",
            traversal.Projections[0].Evidence!
                .Selection.RequestedFramework?.ToString());
        Assert.Equal(
            "net12.0",
            contribution.Binding.CreateReacquisitionRequest()
                .CompileTargetFramework);
        Assert.Equal(
            "net11.0",
            contribution.Binding.CreateReacquisitionRequest()
                .SelectionTargetFramework);
        Assert.Equal(
            "net11.0",
            contribution.Realization.Selection.TargetFramework);
        Assert.Null(evidence.PlatformDelegation);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyEdgeRealizationDelegatesSubsumedDestinationBeforeAcquisition()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForPackage(
                PrunablePackageId,
                new SourceBehavior(
                    ["10.0.0"],
                    PayloadEntries:
                    [
                        $"lib/net12.0/{PrunablePackageId}.dll",
                    ]));
        PackageDependencyTraversalOutcome traversal =
            await TraversalAsync(
                environment,
                PrunablePackageId,
                "10.0.0",
                sourceFramework: "netstandard2.0",
                traversalFramework: "net12.0");
        var platformTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net12.0"),
            PlatformVersion.Parse("12.0.0"));
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net12.0",
                    NuGetVersion.Parse("12.0.0")),
                [$"{PrunablePackageId}|12.0.0"]);
        var request = new PackageDependencyEdgeRealizationRequest(
            traversal,
            rootOccurrenceIndex: 0,
            edgeIndex: 0,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(
                "net12.0",
                platformTarget: platformTarget),
            inventory);
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(request);

        PackageDependencyEdgeRealizationEvidence evidence =
            await execution.ExecuteAsync(
                environment.CreateHouse(),
                environment.IssueOperation(
                    execution.Request,
                    TestContext.Current.CancellationToken));

        PackageHouseResult.Delegated delegated =
            Assert.IsType<PackageHouseResult.Delegated>(evidence.Result);
        Assert.Same(
            execution.Pruning,
            delegated.Delegation.Pruning);
        Assert.Same(
            delegated.Delegation,
            evidence.PlatformDelegation);
        Assert.Equal(
            PackageHouseRootNoContributionReason.ResourceFreeSettlement,
            Assert.IsType<PackageHouseRootContributionOutcome.NoContribution>(
                evidence.RootContribution).Reason);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyEdgeRealizationPreservesHouseRejectionBeforePruning()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForPackage(
                PrunablePackageId,
                new SourceBehavior(["10.0.0"]));
        PackageDependencyTraversalOutcome traversal =
            await TraversalAsync(
                environment,
                PrunablePackageId,
                "10.0.0",
                sourceFramework: "netstandard2.0",
                traversalFramework: "net12.0");
        var platformTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net12.0"),
            PlatformVersion.Parse("12.0.0"));
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net12.0",
                    NuGetVersion.Parse("12.0.0")),
                [$"{PrunablePackageId}|12.0.0"]);
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact(
                        "net12.0",
                        platformTarget: platformTarget),
                    inventory));
        PackageSourceAuthorization otherAuthorization =
            PackageSourceAuthorization.Authorize(
                [
                    new PackageSource(
                        "other",
                        "https://other.example/v3/index.json"),
                ]);
        var house = new PackageHouse(
            new FixedAuthorization(
                otherAuthorization,
                PrunablePackageId));

        PackageDependencyEdgeRealizationEvidence evidence =
            await execution.ExecuteAsync(
                house,
                environment.IssueOperation(
                    execution.Request,
                    TestContext.Current.CancellationToken));

        PackageHouseResult.Rejected rejected =
            Assert.IsType<PackageHouseResult.Rejected>(evidence.Result);
        Assert.Same(execution.Request, rejected.Request);
        Assert.Null(rejected.Decision!.Pruning);
        Assert.Same(execution.Pruning, evidence.Execution.Pruning);
        Assert.Equal(0, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    private static async Task<PackageDependencyTraversalOutcome>
        TraversalAsync(
            HouseEnvironment environment,
            string dependencyPackageId,
            string dependencyVersion,
            string sourceFramework,
            string traversalFramework)
    {
        PackageDependencyEvidenceRoot root =
            DependencyRoot(
                dependencyPackageId,
                dependencyVersion,
                sourceFramework);
        var candidateSource =
            new AuthorizedPackageDependencyCandidateSource(
                environment.Authorization,
                environment.Root);
        return await PackageDependencyTraversalQuery.ExecuteAsync(
            new PackageDependencyTraversalRequest(
                [
                    new PackageDependencyTraversalRootOccurrence(
                        root,
                        PackageDependencyTraversalExpansionAuthority
                            .RecursiveSources),
                ],
                new TraversalTargetFrameworkPolicy(traversalFramework),
                new PackageDependencyTraversalCandidateAdapter(
                    candidateSource),
                new UnexpectedManifestAcquirer(),
                new PackageDependencyTraversalWorkBudget(
                    maxManifestProjections: 1,
                    maxDeclarationResolutions: 1),
                maxDepth: 1),
            TestContext.Current.CancellationToken);
    }

    private static PackageDependencyEvidenceRoot DependencyRoot(
        string dependencyPackageId,
        string dependencyVersion,
        string sourceFramework)
    {
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>contoso.root</id>
                <version>1.0.0</version>
                <authors>Contoso</authors>
                <description>Dependency edge realization fixture.</description>
                <dependencies>
                  <group targetFramework="{{sourceFramework}}">
                    <dependency id="{{dependencyPackageId}}"
                                version="[{{dependencyVersion}}]" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        PackageManifestFacts facts =
            Assert.IsType<PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.ExecuteSelfAttested(manifest)).Value;
        return Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            facts,
                            PackageDependencyEvidenceAcquisitionForm
                                .PackageArchive,
                            sourceFramework),
                    ])).Roots);
    }

    private sealed class UnexpectedManifestAcquirer
        : IPackageDependencyTraversalManifestAcquirer
    {
        public Task<PackageDependencyTraversalManifestResult> AcquireAsync(
            PackageAcquisitionCandidate candidate,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new InvalidOperationException(
                "A depth-one traversal must not acquire the destination manifest.");
    }
}
