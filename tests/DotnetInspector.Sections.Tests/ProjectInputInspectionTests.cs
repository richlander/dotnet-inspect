using System.Text;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections.Tests;

public sealed class ProjectInputInspectionTests
{
    [Fact]
    public void AuthoredProjectPreservesConstraintsWithoutResolvingPackages()
    {
        InspectionEnvelope<ProjectInputDocument> inspection = Inspect(
            ProjectInputKind.AuthoredProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup>
            </Project>
            """);

        ProjectInputDocument document = inspection.Content;
        DependencyEvidenceDependencyRow declaration =
            Assert.Single(document.Evidence.Dependencies);
        Assert.Equal("newtonsoft.json", declaration.PackageId);
        Assert.Contains("13.0.3", declaration.VersionConstraint);
        Assert.Empty(document.Evidence.RestoredPackages);
        Assert.Null(document.RestoredTraversal);
        Assert.IsType<AuthoredProjectDependencyFactsResult.Available>(
            document.AuthoredProject);
        Assert.IsType<InspectionShare.NonProjectable>(inspection.Share);
    }

    [Fact]
    public void CentralVersionsRemainVisibleAsIncompleteDeclarations()
    {
        ProjectInputDocument document = Inspect(
            ProjectInputKind.AuthoredProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>$(DefaultTargetFramework)</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Markout" /></ItemGroup>
            </Project>
            """).Content;

        var incomplete =
            Assert.IsType<AuthoredProjectDependencyFactsResult.Incomplete>(
                document.AuthoredProject);
        AuthoredProjectPackageDeclaration declaration =
            Assert.Single(incomplete.Value.PackageDeclarations);
        Assert.Equal("markout", declaration.CanonicalPackageId);
        Assert.Null(declaration.CanonicalVersionConstraint);
        Assert.NotEmpty(document.Evidence.Failures);
        Assert.Empty(document.Evidence.RestoredPackages);
    }

    [Fact]
    public void RestoredProjectPreservesExactVersionsAndTarget()
    {
        ProjectInputDocument document = Inspect(
            ProjectInputKind.RestoredProject,
            Assets).Content;

        DependencyEvidenceRestoredPackageRow package =
            Assert.Single(document.Evidence.RestoredPackages);
        Assert.Equal("newtonsoft.json", package.PackageId);
        Assert.Equal("13.0.3", package.PackageVersion);
        DependencyEvidenceRootRow root =
            Assert.Single(document.Evidence.Roots);
        Assert.Equal(1, root.RootIndex);
        Assert.Equal("net11.0", root.RestoredTargetFrameworkIdentity);
        var traversal =
            Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
                document.RestoredTraversal);
        Assert.Equal(
            RestoredProjectTraversalCompletion.Complete,
            traversal.Value.Completion);
        Assert.Single(traversal.Value.PackageRelationships);
        Assert.Null(document.AuthoredProject);
    }

    [Fact]
    public void FailedPruningRemainsVisibleBesideACompleteGraph()
    {
        string assets = Assets.Replace(
            "\"dependencies\": {",
            "\"packagesToPrune\": \"invalid\", \"dependencies\": {",
            StringComparison.Ordinal);
        ProjectInputDocument document = Inspect(
            ProjectInputKind.RestoredProject, assets).Content;

        var traversal =
            Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
                document.RestoredTraversal);
        Assert.Equal(RestoredProjectTraversalCompletion.Complete, traversal.Value.Completion);
        var pruning = Assert.IsType<RestoredProjectPackagePruningResult.Failed>(
            traversal.Value.Facts.PackagePruning);
        Assert.Equal(RestoredProjectPackagePruningFailureReason.InvalidShape, pruning.Failure.Reason);
        DependencyEvidenceFailureRow failure = Assert.Single(document.Evidence.Failures);
        Assert.Equal(DependencyEvidenceFailurePhase.Pruning, failure.Phase);
        Assert.Equal(pruning.Failure.Reason.ToString(), failure.Reason);
        Assert.Equal(pruning.Failure.Message, failure.Message.ToString());
        Assert.Equal(pruning.Failure.Count, failure.Occurrences);
        Assert.Equal(Assert.Single(document.Evidence.Roots).Identity, failure.RootIdentity);
        Assert.Equal(1, failure.RootIndex);
    }

    [Fact]
    public void RequestedRuntimeTargetSelectsItsExactGraph()
    {
        string assets = Assets.Replace(
            """
            "targets": {
            """,
            """
            "targets": {
              "net11.0/linux-x64": {
                "Newtonsoft.Json/13.0.4": { "type": "package" }
              },
            """,
            StringComparison.Ordinal);
        ProjectInputDocument document = ProjectInputInspection.Execute(
            ProjectInputKind.RestoredProject,
            "project.assets.json",
            Encoding.UTF8.GetBytes(assets),
            new RestoredProjectTargetRequest("net11.0", "linux-x64")).Content;

        DependencyEvidenceRootRow root = Assert.Single(document.Evidence.Roots);
        Assert.Equal("net11.0", root.RestoredTargetFrameworkIdentity);
        Assert.Equal("linux-x64", root.RestoredRuntimeIdentifier);
        Assert.Equal(
            "13.0.4",
            Assert.Single(document.Evidence.RestoredPackages).PackageVersion);
    }

    [Fact]
    public void UnmatchedTargetKeepsDeclarationsWithoutInventingAGraph()
    {
        ProjectInputDocument document = ProjectInputInspection.Execute(
            ProjectInputKind.RestoredProject,
            "project.assets.json",
            Encoding.UTF8.GetBytes(Assets),
            new RestoredProjectTargetRequest("net12.0")).Content;

        DependencyEvidenceRootRow root = Assert.Single(document.Evidence.Roots);
        Assert.Equal(
            DependencyEvidenceDeclarationState.Available,
            root.DeclarationState);
        Assert.Equal(DependencyEvidenceGraphState.Unavailable, root.GraphState);
        Assert.Single(document.Evidence.Dependencies);
        Assert.Empty(document.Evidence.RestoredPackages);
        Assert.IsType<RestoredProjectDependencyTraversalResult.Unavailable>(
            document.RestoredTraversal);
    }

    [Fact]
    public void PartialGraphRetainsPackagesAndScopedFailures()
    {
        string assets = Assets.Replace(
            """{ "type": "package" }""",
            """
            {
              "type": "package",
              "dependencies": { "Missing.Package": "1.0.0" }
            }
            """,
            StringComparison.Ordinal);
        ProjectInputDocument document = Inspect(
            ProjectInputKind.RestoredProject,
            assets).Content;

        var traversal =
            Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
                document.RestoredTraversal);
        Assert.Equal(
            RestoredProjectTraversalCompletion.Partial,
            traversal.Value.Completion);
        Assert.Equal(
            RestoredProjectGraphFailureReason.UnresolvedDependency,
            Assert.Single(traversal.Value.Failures).Reason);
        Assert.Equal(
            PackageDependencyEvidencePhaseCompletion.Incomplete,
            Assert.Single(document.Evidence.Roots).GraphCompletion);
        Assert.Single(document.Evidence.RestoredPackages);
        Assert.NotEmpty(document.Evidence.Failures);
    }

    [Fact]
    public void GraphFailureDoesNotDiscardTheAdmittedDocument()
    {
        string assets = Assets.Replace(
            """
            "targets": {
            """,
            """
            "targets": {
              "NET11.0": {},
            """,
            StringComparison.Ordinal);
        ProjectInputDocument document = Inspect(
            ProjectInputKind.RestoredProject,
            assets).Content;

        var failed =
            Assert.IsType<RestoredProjectDependencyTraversalResult.Failed>(
                document.RestoredTraversal);
        var graph =
            Assert.IsType<RestoredProjectDependencyTraversalFailure.Graph>(
                failed.Failure);
        Assert.Equal(
            RestoredProjectGraphFailureReason.AmbiguousTargetIdentity,
            graph.Failure.Reason);
        DependencyEvidenceRootRow root = Assert.Single(document.Evidence.Roots);
        Assert.Equal(DependencyEvidenceGraphState.Failed, root.GraphState);
        Assert.Equal(
            DependencyEvidenceDeclarationState.Available,
            root.DeclarationState);
        Assert.Single(document.Evidence.Dependencies);
        Assert.Equal(0, document.Evidence.Summary.FailedRootCount);
        Assert.NotEmpty(document.Evidence.Failures);
    }

    [Theory]
    [InlineData(ProjectInputKind.AuthoredProject, "<Project")]
    [InlineData(ProjectInputKind.RestoredProject, "{")]
    [InlineData(ProjectInputKind.RestoredProject, """{"version":99}""")]
    public void InvalidInputRetainsFailureInsteadOfAnEmptySuccessfulProject(
        ProjectInputKind kind,
        string content)
    {
        ProjectInputDocument document = Inspect(kind, content).Content;

        Assert.Empty(document.Evidence.Roots);
        Assert.NotEmpty(document.Evidence.Failures);
        Assert.All(document.Evidence.Failures,
            failure => Assert.Equal(1, failure.RootIndex));
        Assert.Equal(1, document.Evidence.Summary.FailedRootCount);
    }

    [Theory]
    [InlineData(ProjectInputKind.AuthoredProject)]
    [InlineData(ProjectInputKind.RestoredProject)]
    public void OversizedProjectPreservesProviderRejection(ProjectInputKind kind)
    {
        int limit = kind == ProjectInputKind.AuthoredProject
            ? AuthoredProjectDependencyFactsQuery.MaxProjectBytes
            : RestoredProjectDependencyFactsQuery.MaxAssetsBytes;
        var inspection = ProjectInputInspection.Execute(
            kind,
            kind == ProjectInputKind.AuthoredProject
                ? "Example.csproj"
                : "project.assets.json",
            new byte[limit + 1]);

        if (kind == ProjectInputKind.AuthoredProject)
        {
            var failed =
                Assert.IsType<AuthoredProjectDependencyFactsResult.Failed>(
                    inspection.Content.AuthoredProject);
            Assert.Equal(
                AuthoredProjectDependencyFactsFailureReason.ConfiguredLimitExceeded,
                failed.Failure.Reason);
        }
        else
        {
            var failed =
                Assert.IsType<RestoredProjectDependencyTraversalResult.Failed>(
                    inspection.Content.RestoredTraversal);
            var document =
                Assert.IsType<RestoredProjectDependencyTraversalFailure.Document>(
                    failed.Failure);
            Assert.Equal(
                RestoredProjectDependencyFailureReason.ConfiguredLimitExceeded,
                document.Failure.Reason);
        }
        Assert.Equal(1, inspection.Content.Evidence.Summary.FailedRootCount);
        Assert.NotEmpty(inspection.Content.Evidence.Failures);
    }

    static InspectionEnvelope<ProjectInputDocument> Inspect(
        ProjectInputKind kind,
        string content) =>
        ProjectInputInspection.Execute(
            kind,
            kind == ProjectInputKind.AuthoredProject
                ? "Example.csproj"
                : "project.assets.json",
            Encoding.UTF8.GetBytes(content));

    internal const string Assets = """
        {
          "version": 3,
          "targets": {
            "net11.0": {
              "Newtonsoft.Json/13.0.3": { "type": "package" }
            }
          },
          "projectFileDependencyGroups": {
            "net11.0": [ "Newtonsoft.Json >= 13.0.3" ]
          },
          "project": {
            "frameworks": {
              "net11.0": {
                "dependencies": {
                  "Newtonsoft.Json": { "target": "Package", "version": "[13.0.3, )" }
                }
              }
            }
          }
        }
        """;
}
