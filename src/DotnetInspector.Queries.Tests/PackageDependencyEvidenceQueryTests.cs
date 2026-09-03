using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using DotnetInspector.Fixtures;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageDependencyEvidenceQueryTests
{
    [Fact]
    public void Execute_PackageAndDirectNuspecRetainDistinctProvenanceAndCompareEqual()
    {
        PackageManifestFacts selfAttested = Manifest(
            """
            <group targetFramework="net8.0">
              <dependency id="Example.Dependency" version="[2.0.0]" />
            </group>
            """);
        PackageManifestFacts expected = selfAttested with
        {
            IdentityProvenance =
                PackageManifestIdentityProvenance.ExpectedCoordinate,
        };

        PackageDependencyEvidenceRoot package = NormalizePackage(
            expected,
            PackageDependencyEvidenceSourceKind.PackageArchive,
            "net8.0");
        PackageDependencyEvidenceRoot nuspec = NormalizePackage(
            selfAttested,
            PackageDependencyEvidenceSourceKind.DirectNuspec,
            "net8.0");
        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(package, nuspec);

        Assert.Equal(
            PackageManifestIdentityProvenance.ExpectedCoordinate,
            Assert.IsType<PackageDependencyEvidenceRootProvenance.Package>(
                package.Provenance).IdentityProvenance);
        Assert.Equal(
            PackageManifestIdentityProvenance.SelfAttested,
            Assert.IsType<PackageDependencyEvidenceRootProvenance.Package>(
                nuspec.Provenance).IdentityProvenance);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.SelectedCore);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.SelectedScoped);
    }

    [Fact]
    public void Compare_FixtureManifestAndRestoredFacts_HaveEqualDeclarations()
    {
        PackageManifestFacts manifest = Available(
            PackageManifestFactsQuery.ExecuteSelfAttested(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "manifest.nuspec"))));
        PackageDependencyEvidenceRoot package = NormalizePackage(
            manifest,
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        RestoredProjectDependencyFacts restoredFacts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "project.assets.json"))));
        PackageDependencyEvidenceRoot restored =
            NormalizeRestored(restoredFacts);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(package, restored);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
        AssertSelectionUnavailable(comparison.SelectedCore);
        AssertSelectionUnavailable(comparison.SelectedScoped);
    }

    [Fact]
    public void Compare_FrameworkOnlyMutation_LeavesCoreEqualAndScopedUnequal()
    {
        PackageManifestFacts net8 = Manifest(
            """
            <group targetFramework="net8.0">
              <dependency id="Example.Dependency" version="[2.0.0]" />
            </group>
            """);
        PackageManifestFacts net9 = Manifest(
            """
            <group targetFramework="net9.0">
              <dependency id="Example.Dependency" version="[2.0.0]" />
            </group>
            """);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(
                NormalizePackage(
                    net8,
                    PackageDependencyEvidenceSourceKind.DirectNuspec,
                    "net8.0"),
                NormalizePackage(
                    net9,
                    PackageDependencyEvidenceSourceKind.DirectNuspec,
                    "net9.0"));

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.Scoped);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.SelectedCore);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.SelectedScoped);
    }

    [Fact]
    public void Compare_VersionConstraintMutation_IsUnequal()
    {
        PackageManifestFacts version1 = Manifest(
            """
            <group targetFramework="net8.0">
              <dependency id="Example.Dependency" version="[1.0.0]" />
            </group>
            """);
        PackageManifestFacts version2 = Manifest(
            """
            <group targetFramework="net8.0">
              <dependency id="Example.Dependency" version="[2.0.0]" />
            </group>
            """);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(
                NormalizePackage(
                    version1,
                    PackageDependencyEvidenceSourceKind.DirectNuspec),
                NormalizePackage(
                    version2,
                    PackageDependencyEvidenceSourceKind.DirectNuspec));

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.Scoped);
    }

    [Fact]
    public void Execute_CoalescesInterleavedImplicitGroupsButKeepsExplicitAnyGroup()
    {
        PackageManifestFacts facts = Manifest(
            """
            <dependency id="Before" version="[1.0.0]" />
            <group targetFramework="any">
              <dependency id="Middle" version="[2.0.0]" />
            </group>
            <dependency id="After" version="[3.0.0]" />
            """);

        PackageDependencyEvidenceRoot root = NormalizePackage(
            facts,
            PackageDependencyEvidenceSourceKind.DirectNuspec,
            "net8.0");
        PackageDependencyEvidenceDeclarationResult.Available declaration =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration);

        Assert.Equal(2, declaration.Groups.Length);
        PackageDependencyEvidenceGroup implicitGroup =
            Assert.Single(
                declaration.Groups,
                group =>
                    Assert.IsType<
                        PackageDependencyEvidenceGroupIdentity.Package>(
                        group.Identity).IsImplicitManifestGroup);
        Assert.Equal(
            [0, 2],
            implicitGroup.SourceOccurrences.Select(occurrence =>
                Assert.IsType<
                    PackageDependencyEvidenceGroupOccurrence.Package>(
                    occurrence).SourceIndex));
        Assert.Equal(
            ["after", "before"],
            implicitGroup.Declarations.Select(
                declaration => declaration.CanonicalPackageId));
        PackageDependencyEvidenceGroup explicitGroup =
            Assert.Single(
                declaration.Groups,
                group =>
                    !Assert.IsType<
                        PackageDependencyEvidenceGroupIdentity.Package>(
                        group.Identity).IsImplicitManifestGroup);
        Assert.Equal(
            PackageDependencyFrameworkScopeKind.AnyFramework,
            explicitGroup.FrameworkScope.Kind);
        Assert.Equal(
            implicitGroup.Identity,
            root.Selection.SelectedGroup);
        Assert.Equal(
            0,
            Assert.IsType<PackageDependencyEvidenceGroupOccurrence.Package>(
                root.Selection.SelectedSourceOccurrence).SourceIndex);
    }

    [Fact]
    public void Execute_ConflictingImplicitAndExplicitAnyDeclarationsStaySeparate()
    {
        PackageManifestFacts facts = Facts(
            [
                Group("any", ("Shared", "[1.0.0]")) with
                {
                    IsImplicitManifestGroup = true,
                },
                Group("any", ("Shared", "[2.0.0]")),
                Group("any", ("Other", "[3.0.0]")) with
                {
                    IsImplicitManifestGroup = true,
                },
            ]);

        PackageDependencyEvidenceRoot root = NormalizePackage(
            facts,
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceDeclarationResult.Available declaration =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration);

        Assert.True(declaration.IsComplete);
        Assert.Empty(declaration.Failures);
        Assert.Equal(2, declaration.Groups.Length);
        Assert.All(
            declaration.Groups,
            group => Assert.Equal(
                PackageDependencyFrameworkScopeKind.AnyFramework,
                group.FrameworkScope.Kind));
        string[] sharedConstraints =
        [
            .. declaration.Groups
                .SelectMany(group => group.Declarations)
                .Where(dependency =>
                    dependency.CanonicalPackageId == "shared")
                .Select(dependency =>
                    dependency.CanonicalVersionConstraint)
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(2, sharedConstraints.Length);
        Assert.NotEqual(sharedConstraints[0], sharedConstraints[1]);
    }

    [Fact]
    public void Compare_AdjacentAndInterleavedImplicitRunsNormalizeEqually()
    {
        PackageManifestFacts interleaved = Facts(
            [
                Group("any", ("Before", "[1.0.0]")) with
                {
                    IsImplicitManifestGroup = true,
                },
                Group("net9.0", ("Middle", "[2.0.0]")),
                Group("any", ("After", "[3.0.0]")) with
                {
                    IsImplicitManifestGroup = true,
                },
            ]);
        PackageManifestFacts adjacent = Facts(
            [
                Group(
                    "any",
                    ("Before", "[1.0.0]"),
                    ("After", "[3.0.0]")) with
                {
                    IsImplicitManifestGroup = true,
                },
                Group("net9.0", ("Middle", "[2.0.0]")),
            ]);
        PackageDependencyEvidenceRoot left = NormalizePackage(
            interleaved,
            PackageDependencyEvidenceSourceKind.DirectNuspec,
            "net8.0");
        PackageDependencyEvidenceRoot right = NormalizePackage(
            adjacent,
            PackageDependencyEvidenceSourceKind.PackageArchive,
            "net8.0");

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(left, right);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.SelectedCore);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.SelectedScoped);
    }

    [Fact]
    public void Execute_DuplicateDeclarationsCoalesceAndConflictsStayVisible()
    {
        PackageManifestFacts duplicateFacts = Facts(
            [
                Group(
                    "net8.0",
                    ("B", "[2.0.0]"),
                    ("A", "[1.0]"),
                    ("a", "[1.0.0]")),
            ]);
        PackageDependencyEvidenceRoot duplicateRoot = NormalizePackage(
            duplicateFacts,
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceDeclarationResult.Available duplicateDeclaration =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                duplicateRoot.Declaration);
        PackageDependencyEvidenceGroup duplicateGroup =
            Assert.Single(duplicateDeclaration.Groups);

        Assert.True(duplicateDeclaration.IsComplete);
        Assert.Equal(
            ["a", "b"],
            duplicateGroup.Declarations.Select(
                declaration => declaration.CanonicalPackageId));
        Assert.Equal(2, duplicateGroup.Declarations[0].SourceOccurrenceCount);

        PackageManifestFacts conflictFacts = Facts(
            [
                Group(
                    "net8.0",
                    ("A", "[1.0.0]"),
                    ("a", "[2.0.0]")),
            ]);
        PackageDependencyEvidenceRoot conflictRoot = NormalizePackage(
            conflictFacts,
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceDeclarationResult.Available conflictDeclaration =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                conflictRoot.Declaration);

        Assert.False(conflictDeclaration.IsComplete);
        Assert.Empty(Assert.Single(conflictDeclaration.Groups).Declarations);
        Assert.IsType<
            PackageDependencyEvidenceDeclarationFailure
                .ConflictingPackageDeclaration>(
                Assert.Single(conflictDeclaration.Failures));
        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(conflictRoot, duplicateRoot);
        AssertDeclarationIncomplete(comparison.Core);
        AssertDeclarationIncomplete(comparison.Scoped);
        AssertDeclarationIncomplete(comparison.SelectedCore);
        AssertDeclarationIncomplete(comparison.SelectedScoped);
    }

    [Fact]
    public void Compare_EmptyLogicalGroupMultiplicityParticipatesInEquality()
    {
        PackageDependencyEvidenceRoot twoGroups = NormalizePackage(
            Facts([Group("net8.0"), Group("net8.0")]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceRoot oneGroup = NormalizePackage(
            Facts([Group("net8.0")]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(twoGroups, oneGroup);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.Scoped);
    }

    [Fact]
    public void Compare_UnrecognizedScopesUseSameOwnerParityOnly()
    {
        PackageDependencyEvidenceRoot first = NormalizePackage(
            Facts([Group("future-one", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceRoot same = NormalizePackage(
            Facts([Group("future-one", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceSourceKind.PackageArchive);
        PackageDependencyEvidenceRoot different = NormalizePackage(
            Facts([Group("future-two", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            PackageDependencyEvidenceQuery.Compare(first, same).Scoped);
        AssertFrameworkScopeNotComparable(
            PackageDependencyEvidenceQuery.Compare(first, different).Scoped);

        RestoredProjectDependencyFacts restoredFacts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "project.assets.json"))));
        AssertFrameworkScopeNotComparable(
            PackageDependencyEvidenceQuery.Compare(
                first,
                NormalizeRestored(restoredFacts)).Scoped);
    }

    [Fact]
    public void Compare_RepeatedMatchingOpaqueScopesRemainComparable()
    {
        PackageDependencyEvidenceRoot repeated = NormalizePackage(
            Facts([Group("future-one"), Group("future-one")]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceRoot single = NormalizePackage(
            Facts([Group("future-one")]),
            PackageDependencyEvidenceSourceKind.PackageArchive);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(repeated, single);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            comparison.Scoped);
    }

    [Fact]
    public void Compare_MixedExactAndUnrecognizedGroupsAreOrderIndependent()
    {
        PackageDependencyEvidenceRoot left = NormalizePackage(
            Facts(
                [
                    Group("net8.0", ("A", "[1.0.0]")),
                    Group("future-one", ("B", "[2.0.0]")),
                ]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceRoot reordered = NormalizePackage(
            Facts(
                [
                    Group("future-one", ("B", "[2.0.0]")),
                    Group("net8.0", ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceSourceKind.PackageArchive);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(left, reordered);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
    }

    [Fact]
    public void Compare_RepeatedMixedScopeSignaturesAreNotComparableAcrossOwners()
    {
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Facts(
                [
                    Group("net8.0", ("A", "[1.0.0]")),
                    Group("future-one", ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        byte[] assets = Encoding.UTF8.GetBytes(
            """
            {
              "version": 3,
              "targets": {
                ".NETCoreApp,Version=v8.0": {}
              },
              "project": {
                "frameworks": {
                  "future-one": {
                    "dependencies": {
                      "A": {
                        "target": "Package",
                        "version": "[1.0.0]"
                      }
                    }
                  },
                  "net8.0": {
                    "dependencies": {
                      "A": {
                        "target": "Package",
                        "version": "[1.0.0]"
                      }
                    }
                  }
                }
              },
              "projectFileDependencyGroups": {
                ".NETCoreApp,Version=v8.0": []
              }
            }
            """);
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(RestoredProjectDependencyFactsQuery.Execute(assets)));

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(package, restored);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        AssertFrameworkScopeNotComparable(comparison.Scoped);
    }

    [Fact]
    public void Execute_PreservesRestoredDiamondEdgesWithoutAffectingDeclarationComparison()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "project.assets.json")),
                new RestoredProjectTargetRequest("net11.0")));
        RestoredProjectGraphResult.Available sourceGraph =
            Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        PackageDependencyEvidenceRoot root = NormalizeRestored(facts);
        PackageDependencyEvidenceGraphResult.Available graph =
            Assert.IsType<PackageDependencyEvidenceGraphResult.Available>(
                root.Graph);

        Assert.True(graph.IsComplete);
        Assert.Equal(sourceGraph.Packages, graph.Packages);
        Assert.Equal(sourceGraph.Edges, graph.Edges);
        RestoredProjectGraphEdge[] diamondEdges =
        [
            .. graph.Edges.Where(edge =>
                edge.Dependency.Coordinate.PackageId == "nuget.versioning"),
        ];
        Assert.True(diamondEdges.Length >= 2);
        Assert.Contains(
            diamondEdges,
            edge => edge.Parent is RestoredProjectGraphParentIdentity.Package);
        Assert.Contains(
            diamondEdges,
            edge => edge.Parent is RestoredProjectGraphParentIdentity.Project);
    }

    [Fact]
    public void Execute_PreservesSelectedRestoredTargetWhenGraphIsUnavailable()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "project.assets.json")),
                new RestoredProjectTargetRequest("net11.0")));
        facts = facts with
        {
            Graph = new RestoredProjectGraphResult.Unavailable(),
        };

        PackageDependencyEvidenceRoot root = NormalizeRestored(facts);

        Assert.Equal(facts.SelectedTarget, root.RestoredTarget);
        Assert.IsType<PackageDependencyEvidenceGraphResult.Unavailable>(
            root.Graph);
    }

    [Fact]
    public void Execute_PreservesEveryRestoredGraphStateIndependently()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "project.assets.json")),
                new RestoredProjectTargetRequest("net11.0")));
        var graphFailure = new RestoredProjectGraphFailure(
            RestoredProjectGraphFailureReason.UnresolvedDependency);
        RestoredProjectDependencyFacts[] variants =
        [
            facts with
            {
                Graph = new RestoredProjectGraphResult.Available(
                    [],
                    [],
                    [],
                    RestoredProjectPhaseCompletion.Complete),
            },
            facts with
            {
                Graph = new RestoredProjectGraphResult.Available(
                    [],
                    [],
                    [graphFailure],
                    RestoredProjectPhaseCompletion.Incomplete),
            },
            facts with
            {
                Graph = new RestoredProjectGraphResult.Unavailable(),
            },
            facts with
            {
                Graph = new RestoredProjectGraphResult.Failed(graphFailure),
            },
        ];
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        .. variants.Select(variant =>
                            new PackageDependencyEvidenceInput.RestoredProject(
                                variant,
                                PackageDependencyEvidenceSourceKind.ProjectAssets)),
                    ]));

        Assert.Equal(1, outcome.Phases.CompleteGraphs);
        Assert.Equal(1, outcome.Phases.IncompleteGraphs);
        Assert.Equal(1, outcome.Phases.UnavailableGraphs);
        Assert.Equal(1, outcome.Phases.FailedGraphs);
        Assert.All(outcome.Roots, root => Assert.NotNull(root.RestoredTarget));
        PackageDependencyEvidenceRoot baseline = outcome.Roots[0];
        Assert.All(
            outcome.Roots.Skip(1),
            root =>
            {
                PackageDependencyEvidenceComparison comparison =
                    PackageDependencyEvidenceQuery.Compare(baseline, root);
                Assert.IsType<
                    PackageDependencyEvidenceComparisonResult.Equal>(
                        comparison.Core);
                Assert.IsType<
                    PackageDependencyEvidenceComparisonResult.Equal>(
                        comparison.Scoped);
            });
    }

    [Fact]
    public void Execute_PreservesIncompleteRestoredDeclarationFailures()
    {
        RestoredProjectDependencyFacts completeFacts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                File.ReadAllBytes(
                    FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                        "project.assets.json"))));
        RestoredProjectDeclarationResult.Available completeDeclaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                completeFacts.Declaration);
        var sourceFailure = new RestoredProjectDeclarationFailure(
            RestoredProjectDeclarationFailureReason.InvalidPackageDeclaration);
        RestoredProjectDependencyFacts incompleteFacts = completeFacts with
        {
            Declaration = new RestoredProjectDeclarationResult.Available(
                completeDeclaration.Groups,
                [sourceFailure],
                RestoredProjectPhaseCompletion.Incomplete),
        };

        PackageDependencyEvidenceRoot incomplete =
            NormalizeRestored(incompleteFacts);
        PackageDependencyEvidenceDeclarationResult.Available declaration =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                incomplete.Declaration);

        Assert.False(declaration.IsComplete);
        Assert.Equal(
            sourceFailure,
            Assert.IsType<
                PackageDependencyEvidenceDeclarationFailure.RestoredProject>(
                Assert.Single(declaration.Failures)).Failure);
        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(
                incomplete,
                NormalizeRestored(completeFacts));
        AssertDeclarationIncomplete(comparison.Core);
        AssertDeclarationIncomplete(comparison.Scoped);
    }

    [Fact]
    public void Execute_RootSetIncompletenessDoesNotDowngradeAdmittedRoot()
    {
        PackageManifestFacts facts = Manifest(
            """
            <group targetFramework="net8.0">
              <dependency id="A" version="[1.0.0]" />
            </group>
            """);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            facts,
                            PackageDependencyEvidenceSourceKind.DirectNuspec),
                    ],
                    [
                        new PackageDependencyEvidenceRootFailure.Package(
                            PackageDependencyEvidenceSourceKind.DirectNuspec,
                            facts.Coordinate,
                            new PackageManifestFailure(
                                PackageManifestFailureReason.MalformedXml)),
                    ],
                    rejectedRootCount: 2,
                    isTruncated: true));

        Assert.Equal(
            PackageDependencyEvidenceRootSetCompletion.Incomplete,
            outcome.RootSet.Completion);
        Assert.Equal(1, outcome.RootSet.AdmittedRootCount);
        Assert.Equal(2, outcome.RootSet.RejectedRootCount);
        Assert.Equal(1, outcome.RootSet.FailedRootCount);
        Assert.True(outcome.RootSet.IsTruncated);
        Assert.Equal(1, outcome.Phases.CompleteDeclarations);
        Assert.True(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                Assert.Single(outcome.Roots).Declaration).IsComplete);
    }

    [Fact]
    public void Execute_BlankExplicitFrameworkGroupHasAnyFrameworkSemantics()
    {
        PackageDependencyEvidenceRoot root = NormalizePackage(
            Facts([Group("", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceGroup group = Assert.Single(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration).Groups);

        Assert.Equal(
            PackageDependencyFrameworkScopeKind.AnyFramework,
            group.FrameworkScope.Kind);
        Assert.Equal("", group.FrameworkScope.SourceSpelling.ToString());
    }

    [Fact]
    public void Execute_InvalidPackageDeclarationsProduceTypedIncompleteEvidence()
    {
        PackageDependencyEvidenceRoot root = NormalizePackage(
            Facts(
                [
                    Group(
                        "net8.0",
                        ("Valid", "[1.0.0]"),
                        ("", "[2.0.0]"),
                        ("Broken.Range", "not-a-range")),
                ]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceDeclarationResult.Available declaration =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration);
        PackageDependencyEvidenceGroup group =
            Assert.Single(declaration.Groups);
        PackageDependencyEvidenceDeclarationFailure.InvalidPackageDeclaration
            failure = Assert.IsType<
                PackageDependencyEvidenceDeclarationFailure
                    .InvalidPackageDeclaration>(
                Assert.Single(declaration.Failures));

        Assert.False(declaration.IsComplete);
        Assert.Equal(2, failure.SourceOccurrenceCount);
        Assert.Equal(group.Identity, failure.Group);
        Assert.Equal(
            "valid",
            Assert.Single(group.Declarations).CanonicalPackageId);
    }

    [Fact]
    public void Execute_PreservesContainedPackageProfileAndAcquisitionFailures()
    {
        PackageManifestFacts facts = Facts(
            [Group("net8.0", ("A", "[1.0.0]"))]);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var match = new PackageProfileMatch(
            facts.Coordinate.PackageId,
            facts.Coordinate.Version,
            [],
            0,
            false,
            source.Source,
            facts);
        PackageDependencyEvidenceRootFailure.PackageProfile profileFailure =
            PackageDependencyEvidenceQuery.CreatePackageProfileFailure(
                new PackageProfileFailure(
                    "Bad\u202EPackage",
                    "1.0.0",
                    source.Source,
                    PackageProfileFailureKind.ManifestAcquisition,
                    "Source\u202Efailure"));
        var acquisitionFailure =
            new PackageDependencyEvidenceRootFailure.Acquisition(
                PackageDependencyEvidenceSourceKind.ProjectLocator,
                PackageDependencyEvidenceAcquisitionFailureReason.NotRestored,
                SourceLabel:
                    new InertString(TextPolicy.Field, "Example.csproj"));

        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [PackageDependencyEvidenceQuery.CreatePackageInput(match)],
                    [profileFailure, acquisitionFailure]));
        PackageDependencyEvidenceRoot root = Assert.Single(outcome.Roots);
        var provenance =
            Assert.IsType<PackageDependencyEvidenceRootProvenance.Package>(
                root.Provenance);

        Assert.Equal(
            PackageDependencyEvidenceSourceKind.PackageSourceManifest,
            provenance.SourceKind);
        Assert.Same(source.Source, provenance.Source);
        Assert.Equal(
            PackageDependencyEvidenceRootSetCompletion.Incomplete,
            outcome.RootSet.Completion);
        Assert.Equal(2, outcome.RootSet.FailedRootCount);
        Assert.True(profileFailure.PackageId!.Value.WasEncoded);
        Assert.True(profileFailure.Message.WasEncoded);
        Assert.DoesNotContain(
            "\u202E",
            profileFailure.PackageId.Value.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\u202E",
            profileFailure.Message.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionFailureReason.NotRestored,
            acquisitionFailure.Reason);
    }

    [Fact]
    public void CreatePackageProfileFailure_DoesNotPromoteDisputedSearchIdentity()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageDependencyEvidenceRootFailure.PackageProfile failure =
            PackageDependencyEvidenceQuery.CreatePackageProfileFailure(
                new PackageProfileFailure(
                    "Disputed.Package",
                    "1.0.0",
                    source.Source,
                    PackageProfileFailureKind.SearchContract,
                    "The source returned inconsistent identity."));

        Assert.Null(failure.Coordinate);
        Assert.Equal("Disputed.Package", failure.PackageId!.Value.ToString());
        Assert.Equal("1.0.0", failure.Version!.Value.ToString());

        var invalidFailure =
            new PackageDependencyEvidenceRootFailure.PackageProfile(
                source.Source,
                PackageProfileFailureKind.SearchContract,
                ManifestFailureReason: null,
                Coordinate:
                    PackageSourceCoordinate.Create(
                        "Disputed.Package",
                        "1.0.0"),
                PackageId: null,
                Version: null,
                Message:
                    new InertString(
                        TextPolicy.Prose,
                        "Disputed identity."));
        Assert.Throws<ArgumentException>(() =>
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [],
                    [invalidFailure])));
    }

    [Fact]
    public void Execute_PackagePrefixAdapterPreservesTerminalCompletion()
    {
        PackageManifestFacts facts = Facts(
            [Group("net8.0", ("A", "[1.0.0]"))]);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var match = new PackageProfileMatch(
            facts.Coordinate.PackageId,
            facts.Coordinate.Version,
            [],
            0,
            false,
            source.Source,
            facts);
        var summary = new PackageProfileSummary(
            "Example.",
            source.Source,
            Candidates: 1,
            Matches: 1,
            Failures: 0,
            PackageSearchTruncationReason.SourcePageLimit);
        PackageDependencyEvidenceRequest request =
            PackageDependencyEvidenceQuery.CreatePackagePrefixRequest(
                [match],
                [],
                summary);

        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(request);
        PackageDependencyEvidencePackagePrefixCompletion completion =
            Assert.IsType<PackageDependencyEvidencePackagePrefixCompletion>(
                outcome.RootSet.PackagePrefixCompletion);

        Assert.Equal(
            PackageDependencyEvidenceRootSetCompletion.Incomplete,
            outcome.RootSet.Completion);
        Assert.True(outcome.RootSet.IsTruncated);
        Assert.Equal(
            PackageSearchTruncationReason.SourcePageLimit,
            completion.TruncationReason);
        Assert.Same(source.Source, completion.Source);
        Assert.Equal("Example.", completion.Prefix.ToString());
        Assert.Equal(1, completion.Candidates);
        Assert.Equal(1, completion.Matches);
        Assert.Equal(0, completion.Failures);
    }

    [Fact]
    public void Execute_RejectsSelectedIndexOnANonSelectedPackageOutcome()
    {
        PackageManifestFacts facts = Facts(
            [Group("net8.0", ("A", "[1.0.0]"))]);
        PackageDependencyGroups groups =
            PackageDependencyGroupsQuery.ProjectDependencyGroups(
                facts,
                "net9.0") with
            {
                SelectedGroupIndex = 0,
            };

        Assert.Throws<ArgumentException>(() =>
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        new PackageDependencyEvidenceInput.Package(
                            facts,
                            groups,
                            PackageDependencyEvidenceSourceKind.DirectNuspec),
                    ])));
    }

    [Fact]
    public void Compare_CompletePairIsIsolatedFromUnrelatedRootsAndTruncation()
    {
        PackageManifestFacts left = Facts(
            [Group("net8.0", ("A", "[1.0.0]"))],
            "Left.Package");
        PackageManifestFacts unrelated = Facts(
            [
                Group(
                    "future-one",
                    ("Broken", "[1.0.0]"),
                    ("broken", "[2.0.0]")),
            ],
            "Unrelated.Package");
        PackageManifestFacts right = Facts(
            [Group("net8.0", ("A", "[1.0.0]"))],
            "Right.Package");
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            left,
                            PackageDependencyEvidenceSourceKind.DirectNuspec),
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            unrelated,
                            PackageDependencyEvidenceSourceKind.DirectNuspec),
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            right,
                            PackageDependencyEvidenceSourceKind.PackageArchive),
                    ],
                    isTruncated: true));

        Assert.False(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                outcome.Roots[1].Declaration).IsComplete);
        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(
                outcome.Roots[0],
                outcome.Roots[2]);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
    }

    [Fact]
    public void Compare_PackageAndRestoredEmptyGroupsAreEqual()
    {
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Facts([Group("net8.0")]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        byte[] assets = Encoding.UTF8.GetBytes(
            """
            {
              "version": 3,
              "targets": {
                ".NETCoreApp,Version=v8.0": {}
              },
              "project": {
                "frameworks": {
                  "net8.0": {
                    "dependencies": {}
                  }
                }
              },
              "projectFileDependencyGroups": {
                ".NETCoreApp,Version=v8.0": []
              }
            }
            """);
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(RestoredProjectDependencyFactsQuery.Execute(assets)));

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(package, restored);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
    }

    [Fact]
    public void Execute_ArtifactTextCrossesTheResultAsInertString()
    {
        const string hostileFramework = "net8.0\u202Eevil";
        PackageDependencyEvidenceRoot root = NormalizePackage(
            Facts([Group(hostileFramework, ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceGroup group = Assert.Single(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration).Groups);

        Assert.Equal(
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework,
            group.FrameworkScope.Kind);
        Assert.True(group.FrameworkScope.SourceSpelling.WasEncoded);
        Assert.True(group.FrameworkScope.SourceSpelling.RequiredContainment);
        Assert.DoesNotContain(
            "\u202E",
            group.FrameworkScope.SourceSpelling.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            @"\u202E",
            group.FrameworkScope.SourceSpelling.ToString(),
            StringComparison.Ordinal);
        Assert.Null(
            typeof(PackageDependencyFrameworkScopeIdentity).GetProperty(
                "OpaqueIdentity",
                BindingFlags.Instance | BindingFlags.Public));
        Assert.DoesNotContain(
            "sha256:",
            group.FrameworkScope.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_CanonicalizesLongAndPlatformFrameworkSpellings()
    {
        PackageDependencyEvidenceRoot longForm = NormalizePackage(
            Facts(
                [
                    Group(
                        ".NETCoreApp,Version=v8.0,Platform=windows,PlatformVersion=10.0.19041.0",
                        ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        PackageDependencyEvidenceRoot shortForm = NormalizePackage(
            Facts(
                [
                    Group(
                        "net8.0-windows10.0.19041.0",
                        ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceSourceKind.PackageArchive);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(longForm, shortForm);
        PackageDependencyEvidenceGroup group = Assert.Single(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                longForm.Declaration).Groups);

        Assert.Equal(
            "net8.0-windows10.0.19041",
            group.FrameworkScope.CanonicalFramework);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);

        PackageDependencyEvidenceRoot newerPlatform = NormalizePackage(
            Facts(
                [
                    Group(
                        "net8.0-windows10.0.22621.0",
                        ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            PackageDependencyEvidenceQuery.Compare(
                shortForm,
                newerPlatform).Scoped);

        PackageDependencyEvidenceRoot targetWithRuntime = NormalizePackage(
            Facts([Group("net8.0/linux-x64", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        Assert.Equal(
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework,
            Assert.Single(
                Assert.IsType<
                    PackageDependencyEvidenceDeclarationResult.Available>(
                    targetWithRuntime.Declaration).Groups).FrameworkScope.Kind);

        PackageDependencyEvidenceRoot uap = NormalizePackage(
            Facts([Group("UAP,Version=v10.0", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        Assert.Equal(
            "uap10.0",
            Assert.Single(
                Assert.IsType<
                    PackageDependencyEvidenceDeclarationResult.Available>(
                    uap.Declaration).Groups).FrameworkScope.CanonicalFramework);

        PackageDependencyEvidenceRoot malformed = NormalizePackage(
            Facts(
                [
                    Group(
                        ".NETCoreApp,Version=v99.0,Unknown=value",
                        ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceSourceKind.DirectNuspec);
        Assert.Equal(
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework,
            Assert.Single(
                Assert.IsType<
                    PackageDependencyEvidenceDeclarationResult.Available>(
                    malformed.Declaration).Groups).FrameworkScope.Kind);
    }

    private static PackageDependencyEvidenceRoot NormalizePackage(
        PackageManifestFacts facts,
        PackageDependencyEvidenceSourceKind sourceKind,
        string? requestedFramework = null)
    {
        return Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            facts,
                            sourceKind,
                            requestedFramework),
                    ])).Roots);
    }

    private static PackageDependencyEvidenceRoot NormalizeRestored(
        RestoredProjectDependencyFacts facts) =>
        Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreateRestoredProjectInput(
                            facts,
                            PackageDependencyEvidenceSourceKind.ProjectAssets),
                    ])).Roots);

    private static PackageManifestFacts Manifest(string dependencies)
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
                  {{dependencies}}
                </dependencies>
              </metadata>
            </package>
            """);
        return Available(PackageManifestFactsQuery.ExecuteSelfAttested(bytes));
    }

    private static PackageManifestFacts Facts(
        ImmutableArray<DeclaredPackageDependencyGroup> groups,
        string packageId = "Example.Package") =>
        new(
            PackageSourceCoordinate.Create(packageId, "1.0.0"),
            "",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            false,
            null,
            groups)
        {
            IdentityProvenance =
                PackageManifestIdentityProvenance.SelfAttested,
        };

    private static DeclaredPackageDependencyGroup Group(
        string targetFramework,
        params (string Id, string Constraint)[] dependencies) =>
        new(
            targetFramework,
            [
                .. dependencies.Select(dependency =>
                    new DeclaredPackageDependency(
                        dependency.Id,
                        dependency.Constraint)),
            ]);

    private static PackageManifestFacts Available(
        PackageManifestFactsResult result) =>
        Assert.IsType<PackageManifestFactsResult.Available>(result).Value;

    private static RestoredProjectDependencyFacts Available(
        RestoredProjectDependencyFactsResult result) =>
        Assert.IsType<RestoredProjectDependencyFactsResult.Available>(result).Value;

    private static void AssertDeclarationIncomplete(
        PackageDependencyEvidenceComparisonResult result)
    {
        PackageDependencyEvidenceComparisonResult.NotComparable notComparable =
            Assert.IsType<
                PackageDependencyEvidenceComparisonResult.NotComparable>(
                result);
        Assert.Equal(
            PackageDependencyEvidenceNotComparableReason
                .DeclarationProjectionIncomplete,
            notComparable.Reason);
    }

    private static void AssertFrameworkScopeNotComparable(
        PackageDependencyEvidenceComparisonResult result)
    {
        PackageDependencyEvidenceComparisonResult.NotComparable notComparable =
            Assert.IsType<
                PackageDependencyEvidenceComparisonResult.NotComparable>(
                result);
        Assert.Equal(
            PackageDependencyEvidenceNotComparableReason.FrameworkScope,
            notComparable.Reason);
    }

    private static void AssertSelectionUnavailable(
        PackageDependencyEvidenceComparisonResult result)
    {
        PackageDependencyEvidenceComparisonResult.NotComparable notComparable =
            Assert.IsType<
                PackageDependencyEvidenceComparisonResult.NotComparable>(
                result);
        Assert.Equal(
            PackageDependencyEvidenceNotComparableReason
                .SelectionStatusUnavailable,
            notComparable.Reason);
    }
}
