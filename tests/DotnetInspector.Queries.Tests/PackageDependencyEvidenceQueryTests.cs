using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
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
            PackageDependencyEvidenceAcquisitionForm.PackageArchive,
            "net8.0");
        PackageDependencyEvidenceRoot nuspec = NormalizePackage(
            selfAttested,
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
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
    public void Execute_CurrentInputKindsAndDeclarationBasesAreExplicit()
    {
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Manifest(
                """
                <group targetFramework="net8.0">
                  <dependency id="Example.Dependency" version="[2.0.0]" />
                </group>
                """),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
            "net8.0");
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(
                RestoredProjectDependencyFactsQuery.Execute(
                    File.ReadAllBytes(
                        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                            "project.assets.json")),
                    new RestoredProjectTargetRequest("net11.0"))));

        Assert.Equal(
            PackageDependencyEvidenceInputKind.PackageManifest,
            package.InputKind);
        Assert.Equal(
            PackageDependencyEvidenceDeclarationBasis.PackageManifest,
            package.DeclarationBasis);
        Assert.Equal(
            PackageDependencyEvidenceInputKind.RestoredProject,
            restored.InputKind);
        Assert.Equal(
            PackageDependencyEvidenceDeclarationBasis.RestoredProject,
            restored.DeclarationBasis);
    }

    [Fact]
    public void Execute_CurrentRuntimeManifestRetainsProviderIdentityTargetAndProvenance()
    {
        string assemblyName =
            typeof(PackageDependencyEvidenceQueryTests).Assembly.GetName().Name!;
        RuntimeDependencyFacts facts = RuntimeFacts(
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    $"{assemblyName}.deps.json")));
        var sourceLabel = new InertString(
            TextPolicy.Field,
            "test-host.deps.json");
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateRuntimeDependencyManifestInput(
                                new RuntimeDependencyFactsResult.Available(facts),
                                sourceLabel: sourceLabel),
                    ]));
        PackageDependencyEvidenceRoot root = Assert.Single(outcome.Roots);
        var identity = Assert.IsType<
            PackageDependencyEvidenceRootIdentity.RuntimeDependencyManifest>(
                root.Identity);
        var provenance = Assert.IsType<
            PackageDependencyEvidenceRootProvenance.RuntimeDependencyManifest>(
                root.Provenance);
        PackageDependencyEvidenceRelationshipResult.Available relationships =
            Assert.IsType<PackageDependencyEvidenceRelationshipResult.Available>(
                root.Relationships);

        Assert.Equal(facts.Root, identity.Identity);
        Assert.Equal(facts.ContentProvenance, provenance.ContentProvenance);
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionForm
                .RuntimeDependencyManifest,
            provenance.AcquisitionForm);
        Assert.Equal("test-host.deps.json", provenance.SourceLabel?.ToString());
        Assert.Equal(
            PackageDependencyEvidenceInputKind.RuntimeDependencyManifest,
            root.InputKind);
        Assert.Equal(
            PackageDependencyEvidenceDeclarationBasis.NotApplicable,
            root.DeclarationBasis);
        Assert.Equal(facts.Target, root.RuntimeTarget);
        Assert.Null(root.RestoredTarget);
        Assert.IsType<PackageDependencyEvidenceDeclarationResult.NotApplicable>(
            root.Declaration);
        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus.Unavailable,
            root.Selection.Status);
        Assert.Equal(facts.Graph.Packages.Length, relationships.Packages.Length);
        Assert.Equal(facts.Graph.Edges.Length, relationships.Relationships.Length);
        Assert.True(relationships.IsComplete);
        Assert.All(
            relationships.Packages,
            package => Assert.IsType<
                PackageDependencyEvidencePackageIdentity
                    .RuntimeDependencyManifest>(package.Identity));
        Assert.All(
            relationships.Relationships,
            relationship => Assert.IsType<
                PackageDependencyEvidenceRelationshipIdentity
                    .RuntimeDependencyManifest>(relationship.Identity));
        PackageDependencyEvidenceProcessingResult.Available processing =
            Assert.IsType<PackageDependencyEvidenceProcessingResult.Available>(
                root.Processing);
        Assert.Equal(
            [
                PackageDependencyEvidenceProcessingObservation
                    .RuntimeDependencyProjection,
            ],
            processing.Observations);
        Assert.True(processing.IsComplete);
        Assert.Equal(1, outcome.Phases.Declarations.NotApplicable);
        Assert.Equal(1, outcome.Phases.Relationships.Complete);
        Assert.Equal(1, outcome.Phases.Processing.Complete);
    }

    [Fact]
    public void PackageInput_RuntimeRelationshipsDoNotInventConstraintsRolesOrPruning()
    {
        PackageDependencyEvidenceRoot root = NormalizeRuntime(
            new RuntimeDependencyFactsResult.Available(
                RuntimeFacts(
                    """
                    {
                      "runtimeTarget": { "name": "net8.0" },
                      "targets": {
                        "net8.0": {
                          "Example.App/1.0.0": {
                            "dependencies": {
                              "Example.Package": "1.0.0"
                            }
                          },
                          "Example.Package/1.0.0": {
                            "dependencies": {
                              "Example.Transitive": "2.0.0"
                            }
                          },
                          "Example.Transitive/2.0.0": {}
                        }
                      },
                      "libraries": {
                        "Example.App/1.0.0": { "type": "project" },
                        "Example.Package/1.0.0": { "type": "package" },
                        "Example.Transitive/2.0.0": { "type": "package" }
                      }
                    }
                    """)));
        PackageDependencyEvidenceRelationshipResult.Available relationships =
            Assert.IsType<PackageDependencyEvidenceRelationshipResult.Available>(
                root.Relationships);
        PackageDependencyEvidenceRelationship applicationRelationship =
            relationships.Relationships.Single(relationship =>
                relationship.ResolvedCoordinate.PackageId == "example.package");
        PackageDependencyEvidenceRelationship packageRelationship =
            relationships.Relationships.Single(relationship =>
                relationship.ResolvedCoordinate.PackageId
                    == "example.transitive");

        Assert.IsType<
            PackageDependencyEvidenceRelationshipParentIdentity
                .RuntimeDependencyLibrary>(
                applicationRelationship.Parent);
        Assert.Equal(
            PackageDependencyEvidenceAuthorship.Unattributed,
            applicationRelationship.Authorship);
        Assert.IsType<
            PackageDependencyEvidenceRelationshipParentIdentity.Package>(
                packageRelationship.Parent);
        Assert.Equal(
            PackageDependencyEvidenceAuthorship.LibraryDeclared,
            packageRelationship.Authorship);
        Assert.All(
            relationships.Relationships,
            relationship =>
            {
                Assert.Null(relationship.CanonicalRequestedConstraint);
                Assert.Null(relationship.SourceRequestedConstraintSpelling);
                Assert.Null(relationship.Role);
                Assert.Null(relationship.DeclarationAssociation);
            });

        PackageDependencyEvidenceProcessingResult.Available processing =
            Assert.IsType<PackageDependencyEvidenceProcessingResult.Available>(
                root.Processing);
        Assert.Equal(
            [
                PackageDependencyEvidenceProcessingObservation
                    .RuntimeDependencyProjection,
            ],
            processing.Observations);
        Assert.DoesNotContain(
            PackageDependencyEvidenceProcessingObservation
                .PackagePruningEvaluation,
            processing.Observations);
    }

    [Fact]
    public void Execute_RuntimeIncompleteFactsRetainUsableGraphAndFailures()
    {
        PackageDependencyEvidenceRoot root = NormalizeRuntime(
            new RuntimeDependencyFactsResult.Available(
                RuntimeFacts(
                    """
                    {
                      "runtimeTarget": { "name": "net8.0" },
                      "targets": {
                        "net8.0": {
                          "Example.App/1.0.0": {
                            "dependencies": {
                              "Example.Valid": "1.0.0",
                              "Example.Missing": "2.0.0"
                            }
                          },
                          "Example.Valid/1.0.0": {}
                        }
                      },
                      "libraries": {
                        "Example.App/1.0.0": { "type": "project" },
                        "Example.Valid/1.0.0": { "type": "package" }
                      }
                    }
                    """)));
        PackageDependencyEvidenceRelationshipResult.Available relationships =
            Assert.IsType<PackageDependencyEvidenceRelationshipResult.Available>(
                root.Relationships);

        Assert.False(relationships.IsComplete);
        Assert.Single(relationships.Packages);
        Assert.Single(relationships.Relationships);
        var failure = Assert.IsType<
            PackageDependencyEvidenceRelationshipFailure
                .RuntimeDependencyManifest>(
                Assert.Single(relationships.Failures));
        Assert.Equal(
            RuntimeDependencyGraphFailureReason.UnresolvedDependency,
            failure.Failure.Reason);
        Assert.Equal(1, failure.Failure.Count);
        Assert.True(
            Assert.IsType<PackageDependencyEvidenceProcessingResult.Available>(
                root.Processing).IsComplete);
    }

    [Fact]
    public void Execute_RuntimeCompleteEmptyGraphIsNotUnavailable()
    {
        PackageDependencyEvidenceRoot root = NormalizeRuntime(
            new RuntimeDependencyFactsResult.Available(
                RuntimeFacts(
                    """
                    {
                      "runtimeTarget": { "name": "net8.0" },
                      "targets": {
                        "net8.0": {}
                      },
                      "libraries": {}
                    }
                    """)));
        PackageDependencyEvidenceRelationshipResult.Available relationships =
            Assert.IsType<PackageDependencyEvidenceRelationshipResult.Available>(
                root.Relationships);

        Assert.True(relationships.IsComplete);
        Assert.Empty(relationships.Packages);
        Assert.Empty(relationships.Relationships);
        Assert.Empty(relationships.Failures);
    }

    [Fact]
    public void Compare_RuntimeDeclarationNotApplicableIsDistinctFromIncomplete()
    {
        PackageDependencyEvidenceRoot runtime = NormalizeRuntime(
            new RuntimeDependencyFactsResult.Available(
                RuntimeFacts(
                    """
                    {
                      "runtimeTarget": { "name": "net8.0" },
                      "targets": {
                        "net8.0": {}
                      },
                      "libraries": {}
                    }
                    """)));
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Manifest("""<group targetFramework="net8.0" />"""),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(runtime, package);

        Assert.All(
            [
                comparison.Core,
                comparison.Scoped,
                comparison.SelectedCore,
                comparison.SelectedScoped,
            ],
            result => Assert.Equal(
                PackageDependencyEvidenceNotComparableReason
                    .DeclarationNotApplicable,
                Assert.IsType<
                    PackageDependencyEvidenceComparisonResult.NotComparable>(
                        result).Reason));
    }

    [Fact]
    public void Execute_RuntimeProviderFailureBecomesFailedRoot()
    {
        RuntimeDependencyFactsResult result =
            RuntimeDependencyFactsQuery.Execute(
                Encoding.UTF8.GetBytes("{"));
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateRuntimeDependencyManifestInput(result),
                    ]));

        Assert.Empty(outcome.Roots);
        var failure = Assert.IsType<
            PackageDependencyEvidenceRootFailure.RuntimeDependencyManifest>(
                Assert.Single(outcome.FailedRoots));
        Assert.Equal(
            RuntimeDependencyFailureReason.MalformedOrDuplicateBearingJson,
            failure.Failure.Reason);
        Assert.Equal(
            PackageDependencyEvidenceRootSetCompletion.Incomplete,
            outcome.RootSet.Completion);
        Assert.Equal(0, outcome.RootSet.AdmittedRootCount);
        Assert.Equal(1, outcome.RootSet.FailedRootCount);
    }

    [Fact]
    public void CreateRuntimeDependencyManifestInput_RequiresRuntimeManifestAcquisition()
    {
        RuntimeDependencyFactsResult result =
            new RuntimeDependencyFactsResult.Available(
                RuntimeFacts(
                    """
                    {
                      "runtimeTarget": { "name": "net8.0" },
                      "targets": {
                        "net8.0": {}
                      },
                      "libraries": {}
                    }
                    """));

        Assert.Throws<ArgumentException>(() =>
            PackageDependencyEvidenceQuery.CreateRuntimeDependencyManifestInput(
                result,
                PackageDependencyEvidenceAcquisitionForm.ProjectAssets));
    }

    [Fact]
    public void RuntimeRoot_RequiresExclusiveMatchingTargetEvidence()
    {
        RuntimeDependencyFacts facts = RuntimeFacts(
            """
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": {
                "net8.0": {}
              },
              "libraries": {}
            }
            """);
        PackageDependencyEvidenceRoot runtime = NormalizeRuntime(
            new RuntimeDependencyFactsResult.Available(facts));
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(
                RestoredProjectDependencyFactsQuery.Execute(
                    File.ReadAllBytes(
                        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                            "project.assets.json")),
                    new RestoredProjectTargetRequest("net11.0"))));

        Assert.Throws<ArgumentException>(() =>
            new PackageDependencyEvidenceRoot(
                runtime.Identity,
                runtime.Provenance,
                runtime.Display,
                runtime.Declaration,
                runtime.Selection,
                restored.RestoredTarget,
                runtime.Relationships,
                runtime.Processing,
                runtime.RuntimeTarget));

        RuntimeDependencyTarget mismatched =
            facts.Target with { FrameworkIdentity = "net9.0" };
        Assert.Throws<ArgumentException>(() =>
            new PackageDependencyEvidenceRoot(
                runtime.Identity,
                runtime.Provenance,
                runtime.Display,
                runtime.Declaration,
                runtime.Selection,
                null,
                runtime.Relationships,
                runtime.Processing,
                mismatched));
    }

    [Fact]
    public void Execute_AuthoredSyntaxRetainsTargetsDeclarationsAndProvenance()
    {
        var sourceLabel = new InertString(
            TextPolicy.Field,
            "Example.csproj");
        AuthoredProjectDependencyFactsResult result = AuthoredFacts(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Example.Common" Version="[1.0, 2.0)" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
                <PackageReference Include="Example.Specific" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);

        PackageDependencyEvidenceRoot root = NormalizeAuthored(
            result,
            sourceLabel);
        var identity =
            Assert.IsType<
                PackageDependencyEvidenceRootIdentity.AuthoredProject>(
                root.Identity);
        var provenance =
            Assert.IsType<
                PackageDependencyEvidenceRootProvenance.AuthoredProject>(
                root.Provenance);
        AuthoredProjectDependencyFacts facts =
            Assert.IsType<AuthoredProjectDependencyFactsResult.Available>(
                result).Value;
        PackageDependencyEvidenceDeclarationResult.Available declarations =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration);

        Assert.Equal(facts.Identity, identity.Identity);
        Assert.Equal(facts.ContentProvenance, provenance.ContentProvenance);
        Assert.Equal(
            PackageDependencyEvidenceAcquisitionForm.ProjectXml,
            provenance.AcquisitionForm);
        Assert.Equal("Example.csproj", provenance.SourceLabel?.ToString());
        Assert.Equal(
            PackageDependencyEvidenceInputKind.AuthoredProject,
            root.InputKind);
        Assert.Equal(
            PackageDependencyEvidenceDeclarationBasis.AuthoredProjectSyntax,
            root.DeclarationBasis);
        Assert.True(declarations.IsComplete);
        Assert.Equal(3, declarations.Groups.Length);
        Assert.Equal(
            [
                PackageDependencyFrameworkScopeKind.AnyFramework,
                PackageDependencyFrameworkScopeKind.ExactFramework,
                PackageDependencyFrameworkScopeKind.ExactFramework,
            ],
            declarations.Groups.Select(group => group.FrameworkScope.Kind));
        Assert.Equal(
            [null, "net8.0", "net9.0"],
            declarations.Groups.Select(
                group => group.FrameworkScope.CanonicalFramework));
        Assert.Empty(declarations.Groups[1].Declarations);
        Assert.Equal(
            "example.common",
            Assert.Single(declarations.Groups[0].Declarations)
                .CanonicalPackageId);
        PackageDependencyEvidenceDeclaration specific =
            Assert.Single(declarations.Groups[2].Declarations);
        Assert.Equal("example.specific", specific.CanonicalPackageId);
        Assert.Equal("[3.0.0, )", specific.CanonicalVersionConstraint);
        Assert.Equal(
            PackageDependencyEvidenceAuthorship.ApplicationAuthored,
            specific.Authorship);
        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus.Unavailable,
            root.Selection.Status);
        Assert.Null(root.RestoredTarget);
        Assert.IsType<PackageDependencyEvidenceRelationshipResult.NotApplicable>(
            root.Relationships);
        Assert.IsType<PackageDependencyEvidenceProcessingResult.NotApplicable>(
            root.Processing);
    }

    [Fact]
    public void Execute_AuthoredCompleteEmptyProjectIsNotUnavailable()
    {
        PackageDependencyEvidenceRoot root = NormalizeAuthored(
            AuthoredFacts("<Project Sdk=\"Microsoft.NET.Sdk\" />"));
        PackageDependencyEvidenceDeclarationResult.Available declarations =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration);

        Assert.True(declarations.IsComplete);
        Assert.Empty(declarations.Groups);
        Assert.Empty(declarations.Failures);
    }

    [Fact]
    public void Execute_AuthoredDuplicateSyntaxRetainsSourceOccurrenceCount()
    {
        PackageDependencyEvidenceRoot root = NormalizeAuthored(
            AuthoredFacts(
                """
                <Project>
                  <ItemGroup>
                    <PackageReference Include="Example.Package" Version="1.0" />
                    <PackageReference Include="example.package" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """));
        PackageDependencyEvidenceGroup group = Assert.Single(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration).Groups);
        PackageDependencyEvidenceDeclaration declaration =
            Assert.Single(group.Declarations);
        var occurrence = Assert.IsType<
            PackageDependencyEvidenceGroupOccurrence
                .AuthoredProjectDeclaration>(
                Assert.Single(group.SourceOccurrences));

        Assert.Equal(2, declaration.SourceOccurrenceCount);
        Assert.Equal(2, occurrence.SourceOccurrenceCount);
    }

    [Fact]
    public void Execute_AuthoredIncompleteFactsRetainUsableAndOpaqueEvidence()
    {
        PackageDependencyEvidenceRoot root = NormalizeAuthored(
            AuthoredFacts(
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="Directory.Build.props" />
                  <PropertyGroup>
                    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Example.Valid" Version="1.0.0" />
                    <PackageReference Include="Example.Managed" />
                    <PackageReference Include="Example.Conditional"
                                      Version="3.0.0"
                                      Condition="'$(Configuration)' == 'Release'" />
                  </ItemGroup>
                </Project>
                """));
        PackageDependencyEvidenceDeclarationResult.Available declarations =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration);

        Assert.False(declarations.IsComplete);
        Assert.Contains(
            declarations.Failures,
            failure => failure is
                PackageDependencyEvidenceDeclarationFailure.AuthoredProject
                {
                    Limitation.Reason:
                        AuthoredProjectDependencyLimitationReason.ExplicitImport,
                });
        Assert.DoesNotContain(
            declarations.Failures,
            failure => failure is
                PackageDependencyEvidenceDeclarationFailure
                    .InvalidPackageDeclaration);
        Assert.Contains(
            declarations.Failures,
            failure => failure is
                PackageDependencyEvidenceDeclarationFailure.AuthoredProject
                {
                    Limitation.Reason:
                        AuthoredProjectDependencyLimitationReason
                            .MissingVersionConstraint,
                });
        Assert.Equal(
            2,
            declarations.Groups.Count(group =>
                group.FrameworkScope.Kind
                    == PackageDependencyFrameworkScopeKind.UnresolvedFramework));
        PackageDependencyEvidenceGroup any = declarations.Groups.Single(group =>
            group.FrameworkScope.Kind
                == PackageDependencyFrameworkScopeKind.AnyFramework);
        Assert.Equal(
            ["example.valid"],
            any.Declarations.Select(declaration =>
                declaration.CanonicalPackageId));
        Assert.DoesNotContain(
            declarations.Groups.Where(group =>
                group.FrameworkScope.Kind
                    != PackageDependencyFrameworkScopeKind.AnyFramework),
            group => group.Declarations.Any(declaration =>
                declaration.CanonicalPackageId == "example.valid"));
    }

    [Fact]
    public void Execute_AuthoredUnprojectedSyntaxRetainsOpaqueIdentity()
    {
        AuthoredProjectDependencyFactsResult.Incomplete provider =
            Assert.IsType<AuthoredProjectDependencyFactsResult.Incomplete>(
                AuthoredFacts(
                    """
                    <Project>
                      <ItemGroup>
                        <PackageReference Version="1.0.0" />
                      </ItemGroup>
                    </Project>
                    """));
        PackageDependencyEvidenceRoot root = NormalizeAuthored(provider);
        PackageDependencyEvidenceDeclarationResult.Available declarations =
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                root.Declaration);
        var failure = Assert.IsType<
            PackageDependencyEvidenceDeclarationFailure
                .AuthoredProjectUnresolvedSyntax>(
                declarations.Failures.Single(candidate =>
                    candidate is
                        PackageDependencyEvidenceDeclarationFailure
                            .AuthoredProjectUnresolvedSyntax));

        Assert.Equal(
            Assert.Single(provider.Value.UnresolvedDependencySyntax),
            failure.Syntax);
    }

    [Fact]
    public void Execute_AuthoredProviderFailureBecomesFailedRoot()
    {
        AuthoredProjectDependencyFactsResult result =
            AuthoredFacts("<Project>");
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateAuthoredProjectInput(result),
                    ]));

        Assert.Empty(outcome.Roots);
        var failure =
            Assert.IsType<
                PackageDependencyEvidenceRootFailure.AuthoredProject>(
                Assert.Single(outcome.FailedRoots));
        Assert.Equal(
            AuthoredProjectDependencyFactsFailureReason.MalformedXml,
            failure.Failure.Reason);
        Assert.Equal(
            PackageDependencyEvidenceRootSetCompletion.Incomplete,
            outcome.RootSet.Completion);
        Assert.Equal(0, outcome.RootSet.AdmittedRootCount);
        Assert.Equal(1, outcome.RootSet.FailedRootCount);
    }

    [Fact]
    public void Compare_AuthoredExactScopeMatchesEquivalentPackageManifest()
    {
        PackageDependencyEvidenceRoot authored = NormalizeAuthored(
            AuthoredFacts(
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <PackageReference Include="Example.Dependency"
                                      Version="[2.0.0]" />
                  </ItemGroup>
                </Project>
                """));
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Manifest(
                """
                <group targetFramework="net8.0">
                  <dependency id="Example.Dependency" version="[2.0.0]" />
                </group>
                """),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(authored, package);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
        AssertSelectionUnavailable(comparison.SelectedCore);
        AssertSelectionUnavailable(comparison.SelectedScoped);
    }

    [Fact]
    public void Compare_AuthoredPrereleaseConstraintUsesNuGetEquivalence()
    {
        PackageDependencyEvidenceRoot authored = NormalizeAuthored(
            AuthoredFacts(
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <PackageReference Include="Example.Dependency"
                                      Version="[1.0.0-BETA]" />
                  </ItemGroup>
                </Project>
                """));
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Manifest(
                """
                <group targetFramework="net8.0">
                  <dependency id="Example.Dependency"
                              version="[1.0.0-BETA]" />
                </group>
                """),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);

        PackageDependencyEvidenceComparison comparison =
            PackageDependencyEvidenceQuery.Compare(authored, package);

        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Core);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Equal>(
            comparison.Scoped);
    }

    [Fact]
    public void CreateAuthoredProjectInput_RequiresProjectXmlAcquisition()
    {
        AuthoredProjectDependencyFactsResult result =
            AuthoredFacts("<Project />");

        Assert.Throws<ArgumentException>(() =>
            PackageDependencyEvidenceQuery.CreateAuthoredProjectInput(
                result,
                PackageDependencyEvidenceAcquisitionForm.ProjectLocator));
    }

    [Fact]
    public void PackageInput_InputKindAndBasisRequireMatchingIdentityAndProvenance()
    {
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Manifest("""<group targetFramework="net8.0" />"""),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(
                RestoredProjectDependencyFactsQuery.Execute(
                    File.ReadAllBytes(
                        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                            "project.assets.json")),
                    new RestoredProjectTargetRequest("net11.0"))));

        Assert.Throws<ArgumentException>(() =>
            new PackageDependencyEvidenceRoot(
                restored.Identity,
                package.Provenance,
                restored.Display,
                restored.Declaration,
                restored.Selection,
                restored.RestoredTarget,
                restored.Relationships,
                restored.Processing));
    }

    [Fact]
    public void PackageInput_PackageManifestDeclarationsAreLibraryDeclared()
    {
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Manifest(
                """
                <group targetFramework="net8.0">
                  <dependency id="Example.Dependency" version="[2.0.0]" />
                </group>
                """),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
            "net8.0");

        Assert.All(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                package.Declaration).Groups.SelectMany(group => group.Declarations),
            declaration => Assert.Equal(
                PackageDependencyEvidenceAuthorship.LibraryDeclared,
                declaration.Authorship));
    }

    [Fact]
    public void PackageInput_NotApplicableIsNotUnavailableOrCompleteEmpty()
    {
        PackageDependencyEvidenceRoot package = NormalizePackage(
            Manifest(
                """
                <group targetFramework="net8.0">
                  <dependency id="Example.Dependency" version="[2.0.0]" />
                </group>
                """),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
            "net8.0");

        Assert.IsType<PackageDependencyEvidenceRelationshipResult.NotApplicable>(
            package.Relationships);
        Assert.IsNotType<PackageDependencyEvidenceRelationshipResult.Unavailable>(
            package.Relationships);
        Assert.IsNotType<PackageDependencyEvidenceRelationshipResult.Available>(
            package.Relationships);
        Assert.IsType<PackageDependencyEvidenceProcessingResult.NotApplicable>(
            package.Processing);
        Assert.IsNotType<PackageDependencyEvidenceProcessingResult.Unavailable>(
            package.Processing);
        Assert.IsNotType<PackageDependencyEvidenceProcessingResult.Available>(
            package.Processing);
        Assert.Equal(1, PackageDependencyEvidenceQuery.Execute(
            new PackageDependencyEvidenceRequest(
                [
                    PackageDependencyEvidenceQuery.CreatePackageInput(
                        Manifest(
                            """
                            <group targetFramework="net8.0" />
                            """),
                        PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
                        "net8.0"),
                ])).Phases.Relationships.NotApplicable);
    }

    [Fact]
    public void PackageInput_AssetsPruningObservationRequiresTypedPackagesToPruneEvidence()
    {
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(
                RestoredProjectDependencyFactsQuery.Execute(
                    File.ReadAllBytes(
                        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                            "project.assets.json")),
                    new RestoredProjectTargetRequest("net11.0"))));

        var processing =
            Assert.IsType<PackageDependencyEvidenceProcessingResult.Available>(
                restored.Processing);
        Assert.True(processing.IsComplete);
        Assert.Equal(
            [
                PackageDependencyEvidenceProcessingObservation.RestoreResolution,
                PackageDependencyEvidenceProcessingObservation
                    .PackagePruningEvaluation,
            ],
            processing.Observations);
    }

    [Fact]
    public void PackageInput_AssetsWithoutPruneEvidenceRemainProcessingUnknown()
    {
        byte[] assetsBytes = MutateRestoredAssets(
            root => root["project"]!["frameworks"]!["net11.0"]!
                .AsObject()
                .Remove("packagesToPrune"));
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(
                RestoredProjectDependencyFactsQuery.Execute(
                    assetsBytes,
                    new RestoredProjectTargetRequest("net11.0"))));

        var processing =
            Assert.IsType<PackageDependencyEvidenceProcessingResult.Available>(
                restored.Processing);
        Assert.True(processing.IsComplete);
        Assert.Equal(
            [PackageDependencyEvidenceProcessingObservation.RestoreResolution],
            processing.Observations);
        Assert.DoesNotContain(
            PackageDependencyEvidenceProcessingObservation
                .PackagePruningEvaluation,
            processing.Observations);
    }

    [Fact]
    public void PackageInput_InvalidPruneEvidencePreservesRestoreAsIncompleteProcessing()
    {
        byte[] assetsBytes = MutateRestoredAssets(
            root => root["project"]!["frameworks"]!["net11.0"]!
                .AsObject()["packagesToPrune"] = "invalid");
        PackageDependencyEvidenceRoot restored = NormalizeRestored(
            Available(
                RestoredProjectDependencyFactsQuery.Execute(
                    assetsBytes,
                    new RestoredProjectTargetRequest("net11.0"))));

        var processing =
            Assert.IsType<PackageDependencyEvidenceProcessingResult.Available>(
                restored.Processing);
        var failure = Assert.IsType<
            PackageDependencyEvidenceProcessingFailure
                .RestoredProjectPackagePruning>(
            Assert.Single(processing.Failures));

        Assert.False(processing.IsComplete);
        Assert.Equal(
            [PackageDependencyEvidenceProcessingObservation.RestoreResolution],
            processing.Observations);
        Assert.Equal(
            RestoredProjectPackagePruningFailureReason.InvalidShape,
            failure.Failure.Reason);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
                    PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
                    "net8.0"),
                NormalizePackage(
                    net9,
                    PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
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
                    PackageDependencyEvidenceAcquisitionForm.DirectNuspec),
                NormalizePackage(
                    version2,
                    PackageDependencyEvidenceAcquisitionForm.DirectNuspec));

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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
            "net8.0");
        PackageDependencyEvidenceRoot right = NormalizePackage(
            adjacent,
            PackageDependencyEvidenceAcquisitionForm.PackageArchive,
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        PackageDependencyEvidenceRoot oneGroup = NormalizePackage(
            Facts([Group("net8.0")]),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);

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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        PackageDependencyEvidenceRoot same = NormalizePackage(
            Facts([Group("future-one", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceAcquisitionForm.PackageArchive);
        PackageDependencyEvidenceRoot different = NormalizePackage(
            Facts([Group("future-two", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);

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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        PackageDependencyEvidenceRoot single = NormalizePackage(
            Facts([Group("future-one")]),
            PackageDependencyEvidenceAcquisitionForm.PackageArchive);

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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        PackageDependencyEvidenceRoot reordered = NormalizePackage(
            Facts(
                [
                    Group("future-one", ("B", "[2.0.0]")),
                    Group("net8.0", ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceAcquisitionForm.PackageArchive);

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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
        PackageDependencyEvidenceRelationshipResult.Available relationships =
            Assert.IsType<PackageDependencyEvidenceRelationshipResult.Available>(
                root.Relationships);

        Assert.True(relationships.IsComplete);
        Assert.Equal(sourceGraph.Packages.Length, relationships.Packages.Length);
        Assert.Equal(sourceGraph.Edges.Length, relationships.Relationships.Length);
        PackageDependencyEvidenceRelationship[] diamondRelationships =
        [
            .. relationships.Relationships.Where(relationship =>
                relationship.ResolvedCoordinate.PackageId == "nuget.versioning"),
        ];
        Assert.True(diamondRelationships.Length >= 2);
        Assert.Contains(
            diamondRelationships,
            relationship =>
                relationship.Parent
                    is PackageDependencyEvidenceRelationshipParentIdentity.Package);
        Assert.Contains(
            diamondRelationships,
            relationship =>
                relationship.Parent
                    is PackageDependencyEvidenceRelationshipParentIdentity.Project);
    }

    [Fact]
    public void PackageInput_RequestedConstraintAndResolvedCoordinateRemainIndependent()
    {
        PackageDependencyEvidenceRelationship relationship =
            RestoredRelationships().Relationships.First(relationship =>
                relationship.CanonicalRequestedConstraint is not null);

        Assert.NotNull(relationship.CanonicalRequestedConstraint);
        Assert.NotNull(relationship.SourceRequestedConstraintSpelling);
        Assert.NotEqual(
            relationship.CanonicalRequestedConstraint,
            relationship.ResolvedCoordinate.Version);
    }

    [Fact]
    public void PackageInput_RestoredRelationshipOriginRequiresOwnerAssociation()
    {
        ImmutableArray<PackageDependencyEvidenceRelationship> relationships =
            RestoredRelationships().Relationships;
        PackageDependencyEvidenceRelationship rootRelationship =
            relationships.First(relationship =>
                relationship.Parent
                    is PackageDependencyEvidenceRelationshipParentIdentity.Root);
        PackageDependencyEvidenceRelationship packageRelationship =
            relationships.First(relationship =>
                relationship.Parent
                    is PackageDependencyEvidenceRelationshipParentIdentity.Package);

        Assert.Equal(
            PackageDependencyEvidenceAuthorship.ApplicationAuthored,
            rootRelationship.Authorship);
        PackageDependencyEvidenceDeclarationIdentity declarationAssociation =
            Assert.IsType<PackageDependencyEvidenceDeclarationIdentity>(
                rootRelationship.DeclarationAssociation);
        PackageDependencyEvidenceGroupIdentity.RestoredProject groupAssociation =
            Assert.IsType<PackageDependencyEvidenceGroupIdentity.RestoredProject>(
                declarationAssociation.Group);
        Assert.Equal(
            rootRelationship.ResolvedCoordinate.PackageId,
            declarationAssociation.CanonicalPackageId);
        Assert.Equal(
            Assert.IsType<PackageDependencyEvidenceRootIdentity.RestoredProject>(
                Assert.IsType<
                    PackageDependencyEvidenceRelationshipParentIdentity.Root>(
                        rootRelationship.Parent).Identity).Identity.Selection,
            groupAssociation.Identity.Selection);
        Assert.Equal(
            PackageDependencyEvidenceAuthorship.LibraryDeclared,
            packageRelationship.Authorship);
        Assert.Null(packageRelationship.DeclarationAssociation);
    }

    [Fact]
    public void PackageInput_ProjectNodeRelationshipRemainsUnattributedWithoutAssociation()
    {
        PackageDependencyEvidenceRelationship projectRelationship =
            RestoredRelationships().Relationships.First(relationship =>
                relationship.Parent
                    is PackageDependencyEvidenceRelationshipParentIdentity.Project);

        Assert.Equal(
            PackageDependencyEvidenceAuthorship.Unattributed,
            projectRelationship.Authorship);
        Assert.Null(projectRelationship.DeclarationAssociation);
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
        Assert.IsType<PackageDependencyEvidenceRelationshipResult.Unavailable>(
            root.Relationships);
        PackageDependencyEvidenceProcessingResult.Available processing =
            Assert.IsType<PackageDependencyEvidenceProcessingResult.Available>(
                root.Processing);
        Assert.Equal(
            [
                PackageDependencyEvidenceProcessingObservation.RestoreResolution,
                PackageDependencyEvidenceProcessingObservation
                    .PackagePruningEvaluation,
            ],
            processing.Observations);
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
                                PackageDependencyEvidenceAcquisitionForm.ProjectAssets)),
                    ]));

        Assert.Equal(1, outcome.Phases.Relationships.Complete);
        Assert.Equal(1, outcome.Phases.Relationships.Incomplete);
        Assert.Equal(1, outcome.Phases.Relationships.Unavailable);
        Assert.Equal(1, outcome.Phases.Relationships.Failed);
        Assert.Equal(4, outcome.Phases.Processing.Complete);
        Assert.Equal(0, outcome.Phases.Processing.Unavailable);
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
                            PackageDependencyEvidenceAcquisitionForm.DirectNuspec),
                    ],
                    [
                        new PackageDependencyEvidenceRootFailure.Package(
                            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
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
        Assert.Equal(1, outcome.Phases.Declarations.Complete);
        Assert.True(
            Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
                Assert.Single(outcome.Roots).Declaration).IsComplete);
    }

    [Fact]
    public void Execute_BlankExplicitFrameworkGroupHasAnyFrameworkSemantics()
    {
        PackageDependencyEvidenceRoot root = NormalizePackage(
            Facts([Group("", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
                PackageDependencyEvidenceAcquisitionForm.ProjectLocator,
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
            PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
            provenance.AcquisitionForm);
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
    public void Execute_PackagePrefixAdapterPreservesContractFailureSource()
    {
        using IPackageSourceClient queriedSource =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        using IPackageSourceClient failedCandidateSource =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var failure = new PackageProfileFailure(
            "Disputed.Package",
            "1.0.0",
            failedCandidateSource.Source,
            PackageProfileFailureKind.SearchContract,
            "The package source returned inconsistent provenance.");
        var summary = new PackageProfileSummary(
            "Disputed.",
            queriedSource.Source,
            Candidates: 1,
            Matches: 0,
            Failures: 1,
            PackageSearchTruncationReason.None);

        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                PackageDependencyEvidenceQuery.CreatePackagePrefixRequest(
                    [],
                    [failure],
                    summary));
        var normalizedFailure =
            Assert.IsType<
                PackageDependencyEvidenceRootFailure.PackageProfile>(
                Assert.Single(outcome.FailedRoots));

        Assert.Same(failedCandidateSource.Source, normalizedFailure.Source);
        Assert.Same(
            queriedSource.Source,
            outcome.RootSet.PackagePrefixCompletion!.Source);
        Assert.Equal(
            PackageDependencyEvidenceRootSetCompletion.Incomplete,
            outcome.RootSet.Completion);
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
                            PackageDependencyEvidenceAcquisitionForm.DirectNuspec),
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
                            PackageDependencyEvidenceAcquisitionForm.DirectNuspec),
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            unrelated,
                            PackageDependencyEvidenceAcquisitionForm.DirectNuspec),
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            right,
                            PackageDependencyEvidenceAcquisitionForm.PackageArchive),
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        PackageDependencyEvidenceRoot shortForm = NormalizePackage(
            Facts(
                [
                    Group(
                        "net8.0-windows10.0.19041.0",
                        ("A", "[1.0.0]")),
                ]),
            PackageDependencyEvidenceAcquisitionForm.PackageArchive);

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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        Assert.IsType<PackageDependencyEvidenceComparisonResult.Unequal>(
            PackageDependencyEvidenceQuery.Compare(
                shortForm,
                newerPlatform).Scoped);

        PackageDependencyEvidenceRoot targetWithRuntime = NormalizePackage(
            Facts([Group("net8.0/linux-x64", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        Assert.Equal(
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework,
            Assert.Single(
                Assert.IsType<
                    PackageDependencyEvidenceDeclarationResult.Available>(
                    targetWithRuntime.Declaration).Groups).FrameworkScope.Kind);

        PackageDependencyEvidenceRoot uap = NormalizePackage(
            Facts([Group("UAP,Version=v10.0", ("A", "[1.0.0]"))]),
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
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
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec);
        Assert.Equal(
            PackageDependencyFrameworkScopeKind.UnrecognizedFramework,
            Assert.Single(
                Assert.IsType<
                    PackageDependencyEvidenceDeclarationResult.Available>(
                    malformed.Declaration).Groups).FrameworkScope.Kind);
    }

    private static PackageDependencyEvidenceRoot NormalizePackage(
        PackageManifestFacts facts,
        PackageDependencyEvidenceAcquisitionForm sourceKind,
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
                            PackageDependencyEvidenceAcquisitionForm.ProjectAssets),
                    ])).Roots);

    private static PackageDependencyEvidenceRoot NormalizeAuthored(
        AuthoredProjectDependencyFactsResult result,
        InertString? sourceLabel = null) =>
        Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateAuthoredProjectInput(
                                result,
                                sourceLabel: sourceLabel),
                    ])).Roots);

    private static PackageDependencyEvidenceRoot NormalizeRuntime(
        RuntimeDependencyFactsResult result) =>
        Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateRuntimeDependencyManifestInput(result),
                    ])).Roots);

    private static RuntimeDependencyFacts RuntimeFacts(byte[] bytes) =>
        Assert.IsType<RuntimeDependencyFactsResult.Available>(
            RuntimeDependencyFactsQuery.Execute(bytes)).Value;

    private static RuntimeDependencyFacts RuntimeFacts(string json) =>
        RuntimeFacts(Encoding.UTF8.GetBytes(json));

    private static AuthoredProjectDependencyFactsResult AuthoredFacts(
        string projectXml) =>
        AuthoredProjectDependencyFactsQuery.Execute(
            Encoding.UTF8.GetBytes(projectXml));

    private static PackageDependencyEvidenceRelationshipResult.Available
        RestoredRelationships() =>
        Assert.IsType<PackageDependencyEvidenceRelationshipResult.Available>(
            NormalizeRestored(
                Available(
                    RestoredProjectDependencyFactsQuery.Execute(
                        File.ReadAllBytes(
                            FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                                "project.assets.json")),
                        new RestoredProjectTargetRequest("net11.0"))))
                .Relationships);

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

    private static byte[] MutateRestoredAssets(Action<JsonNode> mutate)
    {
        JsonNode root = JsonNode.Parse(
            File.ReadAllBytes(
                FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                    "project.assets.json")))!;
        mutate(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

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
