using System.Text;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageDependencyPruningInspectionTests
{
    [Fact]
    public async Task ExecuteAsync_PreservesOrderedApplicabilityAndHouseResults()
    {
        PackageDependencyEvidenceRoot packageRoot = PackageRoot();
        PackageDependencyEvidenceRoot applicationRoot = WithAuthorship(
            PackageRoot(),
            PackageDependencyEvidenceAuthorship.ApplicationAuthored);
        PackageDependencyEvidenceDeclaration applicationDeclaration =
            SelectedDeclaration(applicationRoot);
        PackageDependencyEvidenceDeclaration packageDeclaration =
            SelectedDeclaration(packageRoot);
        PlatformPruneInventory inventory =
            Inventory("Example.Dependency", "11.0.0");
        var source = new StubCandidateSource(
            PackageAcquisitionCandidateResultState.Resolved);

        InspectionEnvelope<PackageDependencyPruningInspectionResult> envelope =
            await PackageDependencyPruningInspection.ExecuteAsync(
                new PackageDependencyPruningInspectionRequest(
                    [
                        new(
                            applicationRoot,
                            applicationDeclaration),
                        new(
                            packageRoot,
                            packageDeclaration),
                    ],
                    Target(),
                    inventory),
                source,
                TestContext.Current.CancellationToken);

        Assert.Equal(2, envelope.Content.Outcomes.Length);
        PackageDependencyPruningInspectionOutcome.NotEvaluated notEvaluated =
            Assert.IsType<
                PackageDependencyPruningInspectionOutcome.NotEvaluated>(
                    envelope.Content.Outcomes[0]);
        Assert.Same(applicationRoot, notEvaluated.Subject.Root);
        Assert.Same(
            applicationDeclaration,
            notEvaluated.Subject.Declaration);
        Assert.Equal(
            PackageHouseDependencyPruningApplicabilityState
                .ApplicationAuthoredExemption,
            notEvaluated.Applicability.State);

        PackageDependencyPruningInspectionOutcome.Evaluated evaluated =
            Assert.IsType<
                PackageDependencyPruningInspectionOutcome.Evaluated>(
                    envelope.Content.Outcomes[1]);
        Assert.Same(packageRoot, evaluated.Subject.Root);
        Assert.Same(packageDeclaration, evaluated.Subject.Declaration);
        Assert.Same(inventory, evaluated.Result.Pruning.Policy.Inventory);
        Assert.True(
            evaluated.Result.Pruning.Supply.DelegatesToPlatform);
        Assert.Equal(1, source.PinnedCalls);

        InspectionPortableProjection.NonProjectable share =
            Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
        Assert.Equal(
            "package-dependency-pruning",
            envelope.ResourcePath.Value);
        Assert.Null(share.Location);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsCandidateIncompletionTyped()
    {
        PackageDependencyEvidenceRoot root = PackageRoot();
        PackageDependencyEvidenceDeclaration declaration =
            SelectedDeclaration(root);
        var source = new StubCandidateSource(
            PackageAcquisitionCandidateResultState.Incomplete);

        InspectionEnvelope<PackageDependencyPruningInspectionResult> envelope =
            await PackageDependencyPruningInspection.ExecuteAsync(
                new PackageDependencyPruningInspectionRequest(
                    [new(root, declaration)],
                    Target(),
                    Inventory("Example.Dependency", "11.0.0")),
                source,
                TestContext.Current.CancellationToken);

        PackageDependencyPruningInspectionOutcome.CandidateUnavailable
            unavailable =
                Assert.IsType<
                    PackageDependencyPruningInspectionOutcome
                        .CandidateUnavailable>(
                            Assert.Single(envelope.Content.Outcomes));
        PackageDependencyCandidateResult.Incomplete incomplete =
            Assert.IsType<PackageDependencyCandidateResult.Incomplete>(
                unavailable.Candidate);
        Assert.IsType<
            PackageDependencyCandidateIncomplete.PinnedAuthorization>(
                incomplete.Evidence);
        Assert.Equal(1, source.PinnedCalls);
    }

    private static PackageDependencyEvidenceRoot PackageRoot()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            """
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <authors>Example</authors>
                <description>Example</description>
                <dependencies>
                  <group targetFramework="net11.0">
                    <dependency id="Example.Dependency"
                                version="[2.0.0]" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        PackageManifestFacts facts = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.ExecuteSelfAttested(bytes)).Value;
        return Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            facts,
                            PackageDependencyEvidenceAcquisitionForm
                                .DirectNuspec,
                            "net11.0"),
                    ])).Roots);
    }

    private static PackageDependencyEvidenceDeclaration SelectedDeclaration(
        PackageDependencyEvidenceRoot root) =>
        Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration)
            .Groups.SelectMany(group => group.Declarations)
            .Single(declaration =>
                declaration.Identity.Group
                == root.Selection.SelectedGroup);

    private static PackageDependencyEvidenceRoot WithAuthorship(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceAuthorship authorship)
    {
        PackageDependencyEvidenceDeclarationResult.Available available =
            Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                    root.Declaration);
        return root with
        {
            Declaration =
                new PackageDependencyEvidenceDeclarationResult.Available(
                    [
                        .. available.Groups.Select(group =>
                            group with
                            {
                                Declarations =
                                [
                                    .. group.Declarations.Select(
                                        declaration =>
                                            declaration with
                                            {
                                                Authorship = authorship,
                                            }),
                                ],
                            }),
                    ],
                    available.Failures,
                    available.Completion),
        };
    }

    private static PackageHouseTargetContext Target() =>
        PackageHouseTargetContext.Exact(
            "net11.0",
            platformTarget: new PlatformFamilyTarget(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net11.0"),
                PlatformVersion.Parse("11.0.0")));

    private static PlatformPruneInventory Inventory(
        string packageId,
        string suppliedVersion) =>
        PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(
                "Microsoft.NETCore.App",
                "net11.0",
                NuGetVersion.Parse("11.0.0")),
            [$"{packageId}|{suppliedVersion}"]);

    private sealed class StubCandidateSource(
        PackageAcquisitionCandidateResultState state) :
        IPackageDependencyCandidateSource
    {
        private readonly object _issuer = new();
        private readonly ConfiguredPackageAuthority _authority = new(
            new PackageSource(
                "test",
                "https://test.example/v3/index.json"));

        public int PinnedCalls { get; private set; }

        public ValueTask<PackageAcquisitionCandidateResult>
            ResolvePinnedCandidateAsync(
                PackageSourceCoordinate coordinate,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PinnedCalls++;
            return ValueTask.FromResult(
                new PackageAcquisitionCandidateResult(
                    state,
                    state == PackageAcquisitionCandidateResultState.Resolved
                        ? PackageAcquisitionCandidate.CreatePinned(
                            _issuer,
                            coordinate,
                            [_authority])
                        : null,
                    []));
        }

        public Task<PackageVersionDiscoveryResult>
            DiscoverDependencyVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new InvalidOperationException(
                "The exact dependency should use pinned candidate resolution.");
    }
}
