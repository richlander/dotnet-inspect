using System.Text;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageHouseDependencyPruningQueryTests
{
    [Fact]
    public void CandidateFreeApplicabilityPreservesEarlyStates()
    {
        PackageDependencyEvidenceRoot root = WithAuthorship(
            PackageRoot(),
            PackageDependencyEvidenceAuthorship.ApplicationAuthored);
        PackageDependencyEvidenceDeclaration declaration =
            SelectedDeclaration(root);

        PackageHouseDependencyPruningApplicability applicability =
            PackageHouseDependencyPruningApplicabilityQuery.Execute(
                root,
                declaration,
                PackageHouseTargetContext.Exact("net11.0"));

        Assert.Equal(
            PackageHouseDependencyPruningApplicabilityState
                .ApplicationAuthoredExemption,
            applicability.State);
        Assert.Same(root, applicability.Root);
        Assert.Same(declaration, applicability.Declaration);
    }

    [Fact]
    public void CandidateFreeApplicabilityStopsBeforePlatformInventory()
    {
        PackageDependencyEvidenceRoot root = PackageRoot();

        PackageHouseDependencyPruningApplicability applicability =
            PackageHouseDependencyPruningApplicabilityQuery.Execute(
                root,
                SelectedDeclaration(root),
                PackageHouseTargetContext.Exact("net11.0"));

        Assert.Equal(
            PackageHouseDependencyPruningApplicabilityState
                .CandidateRequired,
            applicability.State);
        Assert.Null(applicability.TargetUnavailableReason);
    }

    [Fact]
    public void SelectedLibraryDeclarationEvaluatesExactPolicyReceipt()
    {
        PackageHouseDependencyInput input =
            Input(PackageRoot());
        PlatformPruneInventory inventory =
            Inventory("Example.Dependency", "11.0.0");

        PackageHouseDependencyPruningResult.Evaluated evaluated =
            Assert.IsType<
                PackageHouseDependencyPruningResult.Evaluated>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    inventory));

        Assert.Same(input, evaluated.Input);
        Assert.Same(input.Request, evaluated.Pruning.Request);
        Assert.Same(inventory, evaluated.Pruning.Policy.Inventory);
        Assert.True(evaluated.Pruning.Supply.DelegatesToPlatform);
    }

    [Fact]
    public void CompatibleFallbackGroupMayBeEvaluated()
    {
        PackageHouseDependencyInput input =
            Input(PackageRoot(targetFramework: null));

        Assert.IsType<PackageHouseDependencyPruningResult.Evaluated>(
            PackageHouseDependencyPruningQuery.Execute(
                input,
                Inventory("Example.Dependency", "11.0.0")));
        Assert.Equal(
            "net11.0",
            input.Root.Selection.RequestedFramework?.ToString());
    }

    [Fact]
    public void ApplicationAuthorshipPrecedesIncompleteProcessing()
    {
        PackageDependencyEvidenceRoot root = WithAuthorship(
            PackageRoot(),
            PackageDependencyEvidenceAuthorship.ApplicationAuthored);
        root = root with
        {
            Processing = IncompleteProcessing(
                PackageDependencyEvidenceProcessingObservation
                    .RuntimeDependencyProjection),
        };
        PackageHouseDependencyInput input = Input(root);

        Assert.IsType<
            PackageHouseDependencyPruningResult
                .ApplicationAuthoredExemption>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    Inventory("Example.Dependency", "11.0.0")));
    }

    [Fact]
    public void UnattributedAuthorshipIsNotGuessed()
    {
        PackageHouseDependencyInput input = Input(
            WithAuthorship(
                PackageRoot(),
                PackageDependencyEvidenceAuthorship.Unattributed));

        Assert.IsType<
            PackageHouseDependencyPruningResult.UnattributedAuthorship>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    Inventory("Example.Dependency", "11.0.0")));
    }

    [Fact]
    public void IncompleteProcessingPrecedesItsObservations()
    {
        PackageDependencyEvidenceRoot root = PackageRoot() with
        {
            Processing = IncompleteProcessing(
                PackageDependencyEvidenceProcessingObservation
                    .RuntimeDependencyProjection),
        };
        PackageHouseDependencyInput input = Input(root);

        Assert.IsType<
            PackageHouseDependencyPruningResult.ProcessingIncomplete>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    Inventory("Example.Dependency", "11.0.0")));
    }

    [Fact]
    public void RuntimeProjectionPrecedesPriorPruningObservation()
    {
        PackageDependencyEvidenceRoot root = PackageRoot() with
        {
            Processing =
                new PackageDependencyEvidenceProcessingResult.Available(
                    [
                        PackageDependencyEvidenceProcessingObservation
                            .PackagePruningEvaluation,
                        PackageDependencyEvidenceProcessingObservation
                            .RuntimeDependencyProjection,
                    ],
                    [],
                    PackageDependencyEvidencePhaseCompletion.Complete),
        };
        PackageHouseDependencyInput input = Input(root);

        Assert.IsType<
            PackageHouseDependencyPruningResult.RuntimeProjected>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    Inventory("Example.Dependency", "11.0.0")));
    }

    [Fact]
    public void CompleteProcessingStatesRemainDistinct()
    {
        PackageDependencyEvidenceRoot initial = PackageRoot();
        PackageDependencyEvidenceProcessingResult.Available prior =
            new(
                [
                    PackageDependencyEvidenceProcessingObservation
                        .RestoreResolution,
                    PackageDependencyEvidenceProcessingObservation
                        .PackagePruningEvaluation,
                ],
                [],
                PackageDependencyEvidencePhaseCompletion.Complete);
        PackageDependencyEvidenceProcessingResult.Available notEvidenced =
            new(
                [
                    PackageDependencyEvidenceProcessingObservation
                        .RestoreResolution,
                ],
                [],
                PackageDependencyEvidencePhaseCompletion.Complete);

        Assert.IsType<
            PackageHouseDependencyPruningResult.PreviouslyEvaluated>(
                PackageHouseDependencyPruningQuery.Execute(
                    Input(initial with { Processing = prior }),
                    Inventory("Example.Dependency", "11.0.0")));
        Assert.IsType<
            PackageHouseDependencyPruningResult.ProcessingNotEvidenced>(
                PackageHouseDependencyPruningQuery.Execute(
                    Input(initial with { Processing = notEvidenced }),
                    Inventory("Example.Dependency", "11.0.0")));
    }

    [Fact]
    public void RestoredRelationshipIsNotEvaluatedAgain()
    {
        PackageHouseDependencyInput input =
            RelationshipInput(RestoredRoot());

        Assert.IsType<
            PackageHouseDependencyPruningResult.PreviouslyEvaluated>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    Inventory(
                        input.Subject.Candidate.Coordinate.PackageId,
                        "11.0.0")));
    }

    [Fact]
    public void RelationshipAuthorshipStillPrecedesProcessing()
    {
        PackageDependencyEvidenceRoot root =
            WithRelationshipAuthorship(
                RestoredRoot() with
                {
                    Processing =
                        new PackageDependencyEvidenceProcessingResult
                            .NotApplicable(),
                },
                PackageDependencyEvidenceAuthorship.ApplicationAuthored);
        PackageHouseDependencyInput input = RelationshipInput(
            root,
            PackageDependencyEvidenceAuthorship.ApplicationAuthored);

        Assert.IsType<
            PackageHouseDependencyPruningResult
                .ApplicationAuthoredExemption>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    Inventory(
                        input.Subject.Candidate.Coordinate.PackageId,
                        "11.0.0")));
    }

    [Fact]
    public void RelationshipWithoutProcessingTargetRemainsUnavailable()
    {
        PackageDependencyEvidenceRoot root = RestoredRoot() with
        {
            Processing =
                new PackageDependencyEvidenceProcessingResult
                    .NotApplicable(),
        };
        PackageHouseDependencyInput input = RelationshipInput(root);

        PackageHouseDependencyPruningResult.TargetUnavailable result =
            Assert.IsType<
                PackageHouseDependencyPruningResult.TargetUnavailable>(
                    PackageHouseDependencyPruningQuery.Execute(
                        input,
                        Inventory(
                            input.Subject.Candidate.Coordinate.PackageId,
                            "11.0.0")));
        Assert.Equal(
            PackageHouseDependencyPruningTargetUnavailableReason
                .RelationshipTargetCorrespondenceUnavailable,
            result.Reason);
    }

    [Fact]
    public void UnavailableAndFailedProcessingRemainDistinct()
    {
        PackageDependencyEvidenceRoot initial = PackageRoot();
        PackageDependencyEvidenceProcessingResult.Failed failed =
            new(
                new PackageDependencyEvidenceProcessingFailure.Association(
                    PackageDependencyEvidenceProcessingObservation
                        .PackagePruningEvaluation));

        Assert.IsType<
            PackageHouseDependencyPruningResult.ProcessingUnavailable>(
                PackageHouseDependencyPruningQuery.Execute(
                    Input(
                        initial with
                        {
                            Processing =
                                new PackageDependencyEvidenceProcessingResult
                                    .Unavailable(),
                        }),
                    Inventory("Example.Dependency", "11.0.0")));
        Assert.IsType<
            PackageHouseDependencyPruningResult.ProcessingFailed>(
                PackageHouseDependencyPruningQuery.Execute(
                    Input(initial with { Processing = failed }),
                    Inventory("Example.Dependency", "11.0.0")));
    }

    [Fact]
    public void DeclarationMustBelongToSelectedGroup()
    {
        PackageDependencyEvidenceRoot root =
            PackageRoot(includeNonSelectedGroup: true);
        PackageDependencyEvidenceDeclarationResult.Available available =
            Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                    root.Declaration);
        PackageDependencyEvidenceDeclaration nonSelected =
            available.Groups
                .Single(group =>
                    group.Identity != root.Selection.SelectedGroup)
                .Declarations.Single();
        PackageHouseDependencyInput input =
            Input(root, nonSelected);

        PackageHouseDependencyPruningResult.TargetUnavailable result =
            Assert.IsType<
                PackageHouseDependencyPruningResult.TargetUnavailable>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    Inventory("Example.Dependency", "11.0.0")));
        Assert.Equal(
            PackageHouseDependencyPruningTargetUnavailableReason
                .DeclarationNotSelected,
            result.Reason);
    }

    [Fact]
    public void TargetFailuresRemainTypedAndNonEvaluating()
    {
        PackageDependencyEvidenceRoot root = PackageRoot();
        PackageHouseDependencyInput input = Input(root);

        PackageHouseDependencyPruningResult.TargetUnavailable unavailable =
            Assert.IsType<
                PackageHouseDependencyPruningResult.TargetUnavailable>(
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    inventory: null));
        Assert.Equal(
            PackageHouseDependencyPruningTargetUnavailableReason
                .InventoryUnavailable,
            unavailable.Reason);

        PackageHouseDependencyInput mismatch = Input(
            root,
            targetFramework: "net12.0");
        PackageHouseDependencyPruningResult.TargetUnavailable
            frameworkMismatch =
                Assert.IsType<
                    PackageHouseDependencyPruningResult.TargetUnavailable>(
                        PackageHouseDependencyPruningQuery.Execute(
                            mismatch,
                            Inventory(
                                "Example.Dependency",
                                "11.0.0")));
        Assert.Equal(
            PackageHouseDependencyPruningTargetUnavailableReason
                .RequestedFrameworkMismatch,
            frameworkMismatch.Reason);
    }

    private static PackageHouseDependencyInput Input(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration? declaration = null,
        string targetFramework = "net11.0")
    {
        declaration ??=
            Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                    root.Declaration)
                .Groups.SelectMany(group => group.Declarations)
                .First(dependency =>
                    dependency.Identity.Group
                    == root.Selection.SelectedGroup);
        PackageAcquisitionCandidate candidate =
            Candidate(
                PackageSourceCoordinate.Create(
                    declaration.CanonicalPackageId,
                    "2.0.0"));
        var resolution =
            new PackageDependencyCandidateResult.Resolved(
                declaration,
                candidate,
                []);
        return PackageHouseDependencyInputAdapter.Create(
            root,
            resolution,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire),
            PackageHouseTargetContext.Exact(
                targetFramework,
                platformTarget: PlatformTarget(targetFramework)));
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

    private static PackageDependencyEvidenceRoot PackageRoot(
        string? targetFramework = "net11.0",
        bool includeNonSelectedGroup = false)
    {
        string otherGroup = includeNonSelectedGroup
            ? """
                  <group targetFramework="net10.0">
                    <dependency id="Example.Dependency"
                                version="[2.0.0]" />
                  </group>
              """
            : "";
        string selectedGroup = targetFramework is null
            ? """
                  <dependency id="Example.Dependency"
                              version="[2.0.0]" />
              """
            : $$"""
                  <group targetFramework="{{targetFramework}}">
                    <dependency id="Example.Dependency"
                                version="[2.0.0]" />
                  </group>
              """;
        byte[] bytes = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>Example.Package</id>
                <version>1.0.0</version>
                <authors>Example</authors>
                <description>Example</description>
                <dependencies>
            {{otherGroup}}
            {{selectedGroup}}
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

    private static PackageDependencyEvidenceRoot WithAuthorship(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceAuthorship authorship)
    {
        PackageDependencyEvidenceDeclarationResult.Available available =
            Assert.IsType<
                PackageDependencyEvidenceDeclarationResult.Available>(
                    root.Declaration);
        PackageDependencyEvidenceGroup[] groups =
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
        ];
        return root with
        {
            Declaration =
                new PackageDependencyEvidenceDeclarationResult.Available(
                    [.. groups],
                    available.Failures,
                    available.Completion),
        };
    }

    private static PackageHouseDependencyInput RelationshipInput(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceAuthorship authorship =
            PackageDependencyEvidenceAuthorship.LibraryDeclared)
    {
        PackageDependencyEvidenceRelationship relationship =
            Assert.IsType<
                PackageDependencyEvidenceRelationshipResult.Available>(
                    root.Relationships)
                .Relationships.First(candidate =>
                    candidate.Authorship
                    == authorship);
        PackageAcquisitionCandidate candidate =
            Candidate(relationship.ResolvedCoordinate);
        return PackageHouseDependencyInputAdapter.Create(
            root,
            relationship,
            candidate,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire),
            PackageHouseTargetContext.Exact(
                "net11.0",
                platformTarget: PlatformTarget("net11.0")));
    }

    private static PackageDependencyEvidenceRoot
        WithRelationshipAuthorship(
            PackageDependencyEvidenceRoot root,
            PackageDependencyEvidenceAuthorship authorship)
    {
        PackageDependencyEvidenceRelationshipResult.Available available =
            Assert.IsType<
                PackageDependencyEvidenceRelationshipResult.Available>(
                    root.Relationships);
        return root with
        {
            Relationships =
                new PackageDependencyEvidenceRelationshipResult.Available(
                    available.Packages,
                    [
                        .. available.Relationships.Select(
                            relationship =>
                                relationship with
                                {
                                    Authorship = authorship,
                                }),
                    ],
                    available.Failures,
                    available.Completion),
        };
    }

    private static PackageDependencyEvidenceRoot RestoredRoot()
    {
        RestoredProjectDependencyFacts facts = Assert.IsType<
            RestoredProjectDependencyFactsResult.Available>(
                RestoredProjectDependencyFactsQuery.Execute(
                    File.ReadAllBytes(
                        FixtureCatalog.RestoredProjectDependencyFacts
                            .AssetPath("project.assets.json")),
                    new RestoredProjectTargetRequest("net11.0"))).Value;
        return Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateRestoredProjectInput(
                                facts,
                                PackageDependencyEvidenceAcquisitionForm
                                    .ProjectAssets),
                    ])).Roots);
    }

    private static PackageDependencyEvidenceProcessingResult.Available
        IncompleteProcessing(
            PackageDependencyEvidenceProcessingObservation observation) =>
        new(
            [observation],
            [
                new PackageDependencyEvidenceProcessingFailure.Association(
                    PackageDependencyEvidenceProcessingObservation
                        .PackagePruningEvaluation),
            ],
            PackageDependencyEvidencePhaseCompletion.Incomplete);

    private static PackageAcquisitionCandidate Candidate(
        PackageSourceCoordinate coordinate)
    {
        var authority = new ConfiguredPackageAuthority(
            new PackageSource(
                "test",
                "https://test.example/v3/index.json"));
        return PackageAcquisitionCandidate.CreatePinned(
            new object(),
            coordinate,
            [authority]);
    }

    private static PlatformPruneInventory Inventory(
        string packageId,
        string suppliedVersion) =>
        PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(
                "Microsoft.NETCore.App",
                "net11.0",
                NuGetVersion.Parse("11.0.0")),
            [$"{packageId}|{suppliedVersion}"]);

    private static PlatformFamilyTarget PlatformTarget(
        string framework) =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse(framework),
            PlatformVersion.Parse(
                framework switch
                {
                    "net10.0" => "10.0.0",
                    "net12.0" => "12.0.0",
                    _ => "11.0.0",
                }));
}
