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
        var authorization = new CountingAuthorization();
        await using PackageSourceSettlementLease source =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "Pinned pruning candidates do not create source clients."));
        using PackageSourceOperationLease operation =
            source.IssueOperationLease(
                TestContext.Current.CancellationToken);

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
                authorization,
                operation);

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
        Assert.Equal(1, authorization.Calls);

        InspectionShare.NonProjectable share =
            Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Equal("package-dependency-pruning/share", share.Path);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task
        ExecuteAsync_OperationDeadlineKeepsIncompletionTypedAndStopsLaterSubjects()
    {
        PackageDependencyEvidenceRoot root =
            PackageRoot("[2.0.0,3.0.0)");
        PackageDependencyEvidenceDeclaration declaration =
            SelectedDeclaration(root);
        var authorization = new CountingAuthorization(
            TimeSpan.FromMilliseconds(100));
        await using PackageSourceSettlementLease source =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "Timed-out ranged discovery does not create source clients."));
        using PackageSourceOperationLease operation =
            source.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromMilliseconds(25));

        InspectionEnvelope<PackageDependencyPruningInspectionResult> envelope =
            await PackageDependencyPruningInspection.ExecuteAsync(
                new PackageDependencyPruningInspectionRequest(
                    [
                        new(root, declaration),
                        new(root, declaration),
                    ],
                    Target(),
                    Inventory("Example.Dependency", "11.0.0")),
                authorization,
                operation);

        Assert.Equal(2, envelope.Content.Outcomes.Length);
        Assert.All(
            envelope.Content.Outcomes,
            outcome =>
            {
                PackageDependencyPruningInspectionOutcome
                    .CandidateUnavailable unavailable =
                        Assert.IsType<
                            PackageDependencyPruningInspectionOutcome
                                .CandidateUnavailable>(outcome);
                PackageDependencyCandidateResult.Incomplete incomplete =
                    Assert.IsType<
                        PackageDependencyCandidateResult.Incomplete>(
                            unavailable.Candidate);
                PackageDependencyCandidateIncomplete.VersionDiscovery
                    evidence =
                        Assert.IsType<
                            PackageDependencyCandidateIncomplete
                                .VersionDiscovery>(
                                    incomplete.Evidence);
                Assert.Contains(
                    evidence.Failures,
                    failure => failure.Timeout?.Kind
                        == PackageSourceTimeoutKind.Operation);
            });
        Assert.Equal(1, authorization.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellationDoesNotPublishOutcome()
    {
        PackageDependencyEvidenceRoot root = PackageRoot();
        PackageDependencyEvidenceDeclaration declaration =
            SelectedDeclaration(root);
        var authorization = new CountingAuthorization();
        await using PackageSourceSettlementLease source =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "Canceled pruning does not create source clients."));
        using var cancellation = CancellationTokenSource
            .CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        using PackageSourceOperationLease operation =
            source.IssueOperationLease(cancellation.Token);
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await PackageDependencyPruningInspection.ExecuteAsync(
                        new PackageDependencyPruningInspectionRequest(
                            [new(root, declaration)],
                            Target(),
                            Inventory(
                                "Example.Dependency",
                                "11.0.0")),
                        authorization,
                        operation));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(0, authorization.Calls);
    }

    private static PackageDependencyEvidenceRoot PackageRoot(
        string version = "[2.0.0]")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <authors>Example</authors>
                <description>Example</description>
                <dependencies>
                  <group targetFramework="net11.0">
                    <dependency id="Example.Dependency"
                                version="{{version}}" />
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

    private sealed class CountingAuthorization(
        TimeSpan? delay = null) : IPackageSourceAuthorization
    {
        private readonly PackageSourceAuthorization _authorization =
            PackageSourceAuthorization.Authorize(
                [
                    new PackageSource(
                        "test",
                        "https://test.example/v3/index.json"),
                ]);

        public int Calls { get; private set; }

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            Calls++;
            if (delay is { } value)
                Thread.Sleep(value);
            return _authorization;
        }
    }
}
