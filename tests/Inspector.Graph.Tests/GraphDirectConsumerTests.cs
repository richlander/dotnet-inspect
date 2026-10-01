using Inspector.Graph.Consumer;

namespace Inspector.Graph.Tests;

public sealed class GraphDirectConsumerTests
{
    [Fact]
    public void GraphExecutionDirectConsumerRuns()
    {
        GraphConsumerObservation observation =
            GraphDirectConsumer.Execute();

        Assert.Equal(["api", "database"], observation.Nodes);
        Assert.Equal(["runtime"], observation.Relationships);
        Assert.Equal(["appsettings.json"], observation.Receipts);
        Assert.Equal(["entry-point"], observation.Characteristics);
        Assert.Equal(["external boundary"], observation.Limits);
        Assert.Equal(
            ["optional configuration missing"],
            observation.Failures);
        Assert.Equal(
            ["api", "database"],
            observation.NeighborhoodNodes);
        Assert.Equal(["runtime"], observation.FocusRelationships);
        Assert.Equal(["database"], observation.AdjacentNodes);
        Assert.Equal([1, 0], observation.Degrees);
        Assert.Equal(
            ["application", "storage"],
            observation.ProjectedGroups);
        Assert.Equal(
            ["runtime"],
            observation.ProjectedRelationships);
        Assert.Equal([0], observation.ProjectionSourceEdgeIds);
        Assert.Equal(
            [new GraphProjectedEdgeContributor(0, 1)],
            observation.ProjectionContributors);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            observation.NeighborhoodCompletion);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            observation.FocusCompletion);
        Assert.Equal(2, observation.NeighborhoodReceipt.NodesAdmitted);
        Assert.Equal(2, observation.AdjacencyReceipt.NodesAdmitted);
        Assert.Equal(2, observation.DegreeReceipt.NodesAdmitted);
        Assert.True(
            observation.NeighborhoodReceipt.TerminalSettled);
        Assert.True(observation.AdjacencyReceipt.TerminalSettled);
        Assert.True(observation.DegreeReceipt.TerminalSettled);
        Assert.Same(
            observation.AdjacencyReceipt.SourceDocument,
            observation.ProjectionReceipt.SourceDocument);
        Assert.Equal(
            1,
            observation.ProjectionReceipt.ProjectedEdgesIssued);
        Assert.Equal(
            1,
            observation.ProjectionReceipt.ContributorRankingsIssued);
        Assert.True(observation.ProjectionReceipt.TerminalSettled);
        Assert.Equal(typeof(Service), observation.SubjectType);
        Assert.Equal(typeof(DependsOn), observation.RelationshipType);
    }
}
