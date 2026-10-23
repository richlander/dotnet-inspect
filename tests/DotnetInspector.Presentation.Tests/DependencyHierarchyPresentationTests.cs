using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public sealed class DependencyHierarchyPresentationTests
{
    [Fact]
    public void WriteTree_StreamsSharedOccurrencesAndMarkers()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(SharedDag());
        using var output = new StringWriter();

        DependencyHierarchyPresentation.WriteTree(
            document,
            [
                .. document.Occurrences.Select(
                    static occurrence => occurrence.Identity),
            ],
            output);

        Assert.Equal(
            """
            └─ Root
               ├─ A
               │  └─ Shared
               │     └─ Leaf
               └─ B
                  └─ (revisit) Shared

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void WriteTree_WindowedRowMarksFragmentAndDepthBoundary()
    {
        DependencyGraphDocument graph = SharedDag() with
        {
            DepthBoundaries =
            [
                new DependencyGraphDepthBoundary(
                    NodeId: 3,
                    PackageProjectionId: null,
                    MaximumDepth: 2,
                    [1],
                    DependencyGraphDepthBoundaryProducerKind.Restored),
                new DependencyGraphDepthBoundary(
                    NodeId: 3,
                    PackageProjectionId: null,
                    MaximumDepth: 4,
                    [1],
                    DependencyGraphDepthBoundaryProducerKind.Restored),
            ],
        };
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(graph);
        DependencyHierarchyOccurrence leaf =
            document.Occurrences.Single(
                static occurrence => occurrence.IncomingEdgeId == 4);
        using var output = new StringWriter();

        DependencyHierarchyPresentation.WriteTree(
            document,
            [leaf.Identity],
            output);

        Assert.Equal(
            """
            ├─ Root
            └─ (fragment) (bounded at depths 2, 4) Shared
               └─ Leaf

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void WriteTree_MarksCycleOccurrence()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(
                new DependencyGraphDocument(
                    [new DependencyGraphRootOccurrence(1, 0)],
                    [
                        Node(0, "Root"),
                        Node(1, "A"),
                        Node(2, "B"),
                    ],
                    [
                        Edge(0, 0, 1),
                        Edge(1, 1, 2),
                        Edge(2, 2, 1),
                    ],
                    [],
                    []));
        using var output = new StringWriter();

        DependencyHierarchyPresentation.WriteTree(
            document,
            [
                .. document.Occurrences.Select(
                    static occurrence => occurrence.Identity),
            ],
            output);

        Assert.Equal(
            """
            └─ Root
               └─ A
                  └─ B
                     └─ (cycle) A

            """.ReplaceLineEndings(),
            output.ToString());
    }

    // Package dependency is the mainline relationship, so only the other
    // kinds in a mixed graph are named.
    [Fact]
    public void WriteTree_NamesOnlyNonPackageRelationshipsInMixedGraph()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(
                new DependencyGraphDocument(
                    [new DependencyGraphRootOccurrence(1, 0)],
                    [
                        Node(0, "Root"),
                        Node(1, "Project"),
                        Node(2, "Package"),
                    ],
                    [
                        Edge(0, 0, 1, "project-reference"),
                        Edge(1, 1, 2),
                    ],
                    [],
                    []));
        using var output = new StringWriter();

        DependencyHierarchyPresentation.WriteTree(
            document,
            [
                .. document.Occurrences.Select(
                    static occurrence => occurrence.Identity),
            ],
            output);

        Assert.Equal(
            """
            └─ Root
               └─ Project (project-reference)
                  └─ Package

            """.ReplaceLineEndings(),
            output.ToString());
    }

    private static DependencyGraphDocument SharedDag() =>
        new(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                Node(0, "Root"),
                Node(1, "A"),
                Node(2, "B"),
                Node(3, "Shared"),
                Node(4, "Leaf"),
            ],
            [
                Edge(0, 0, 1),
                Edge(1, 0, 2),
                Edge(2, 1, 3),
                Edge(3, 2, 3),
                Edge(4, 3, 4),
            ],
            [],
            []);

    private static DependencyGraphNode Node(int id, string label) =>
        new(
            id,
            new DependencyGraphNodeIdentity.Package(
                label,
                "1.0.0"),
            new InertString(TextPolicy.Field, label));

    private static DependencyGraphEdge Edge(
        int id,
        int source,
        int target,
        string relationship = "package-dependency") =>
        new(
            id,
            source,
            target,
            relationship,
            [1],
            MinimumDepth: 1,
            DependencyGraphResolutionState.Resolved,
            EvidenceIdentity: null);
}
