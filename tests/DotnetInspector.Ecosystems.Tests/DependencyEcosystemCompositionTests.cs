using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class DependencyEcosystemCompositionTests
{
    [Fact]
    public void SharedCoordinatesCountOnceAndDifferentVersionsRemainDistinct()
    {
        var selection = new RestoredProjectSelectionIdentity(
            "net11.0/linux-x64", new string('a', 64));
        DependencyGraphDocument graph = Graph(
        [
            Package(0, "Microsoft.Extensions.Hosting", "10.0.0"),
            Package(1, "System.Text.Json", "9.0.0"),
            Package(2, "SYSTEM.TEXT.JSON", "9.0"),
            Package(3, "System.Text.Json", "8.0.0"),
            Package(4, "WrongTurn", "0.1.14"),
            new(5, new DependencyGraphNodeIdentity.RestoredPackage(
                new(selection, PackageSourceCoordinate.Create(
                    "System.Text.Json", "9.0.0"))), Field("System.Text.Json 9.0.0")),
        ]);
        var hierarchy = DependencyHierarchyDocument.Create(graph);
        Assert.True(hierarchy.Occurrences.Length > 0);

        DependencyEcosystemCompositionDocument composition =
            EcosystemPackCatalog.InspectDependencyComposition(
                graph, ["net11.0", "net11.0/linux-x64", "net11.0"], true).Content;

        Assert.Equal(4, composition.PackageVersions);
        Assert.Equal(1, composition.UnclassifiedPackageVersions);
        Assert.Equal(2, Assert.Single(composition.Ecosystems,
            row => row.EcosystemId == EcosystemPackIds.Runtime.Value).PackageVersions);
        Assert.Equal(1, Assert.Single(composition.Ecosystems,
            row => row.EcosystemId == EcosystemPackIds.MicrosoftExtensions.Value).PackageVersions);
        Assert.Equal(["net11.0", "net11.0/linux-x64"], composition.Targets);
        Assert.True(composition.IsComplete);
    }

    [Fact]
    public void UnresolvedPlaceholdersDoNotBecomeExactOrUnclassifiedPackages()
    {
        var selection = new RestoredProjectSelectionIdentity(
            "net11.0", new string('b', 64));
        PackageDependencyEvidenceDeclarationIdentity Declaration(string id) =>
            new(new PackageDependencyEvidenceGroupIdentity.RestoredProject(
                new(selection, "net11.0")), id);
        DependencyGraphDocument graph = Graph(
        [
            Package(0, "System.Text.Json", "9.0.0"),
            new(1, new DependencyGraphNodeIdentity.PackageBoundary(
                0, Declaration("microsoft.extensions.logging"),
                "Microsoft.Extensions.Logging", "[8.0.0,)"), Field("Logging")),
            new(2, new DependencyGraphNodeIdentity.PackageFailure(
                0, Declaration("wrongturn"),
                "WrongTurn", "[0.1.14,)"), Field("WrongTurn")),
            new(3, new DependencyGraphNodeIdentity.PackageBudget(
                0, Declaration("system.memory"),
                "System.Memory", "[4.5.5,)"), Field("Memory")),
        ]) with
        {
            DepthBoundaries =
            [
                new(0, null, 0, [1], DependencyGraphDepthBoundaryProducerKind.Package),
            ],
        };

        DependencyEcosystemCompositionDocument composition =
            EcosystemPackCatalog.InspectDependencyComposition(
                graph, ["net11.0"], isComplete: true).Content;

        Assert.Equal(1, composition.PackageVersions);
        Assert.Equal(0, composition.UnclassifiedPackageVersions);
        Assert.Equal(1, Assert.Single(composition.Ecosystems).PackageVersions);
        Assert.False(composition.IsComplete);
    }

    [Fact]
    public void CompositionIncludesPackagesBeyondTheDiagramPreview()
    {
        DependencyGraphDocument graph = Graph(
        [
            .. Enumerable.Range(0, 92).Select(index =>
                Package(index, "System.Text.Json", $"9.0.{index}")),
        ]);

        DependencyEcosystemCompositionDocument composition =
            EcosystemPackCatalog.InspectDependencyComposition(
                graph, ["net11.0"], isComplete: false).Content;

        Assert.Equal(92, composition.PackageVersions);
        Assert.Equal(92, Assert.Single(composition.Ecosystems).PackageVersions);
        Assert.False(composition.IsComplete);
    }

    private static DependencyGraphDocument Graph(
        ImmutableArray<DependencyGraphNode> nodes) =>
        new(
            [new(1, nodes[0].Id)],
            nodes,
            [.. nodes.Skip(1).Select((node, index) => new DependencyGraphEdge(
                index, nodes[0].Id, node.Id, "package-dependency", [1], 1,
                DependencyGraphResolutionState.Resolved, null))],
            [],
            []);

    private static DependencyGraphNode Package(int id, string name, string version) =>
        new(id, new DependencyGraphNodeIdentity.Package(name, version),
            Field($"{name} {version}"));

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
