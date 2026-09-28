using Inspector.Graph.Consumer;

namespace Inspector.Graph.Tests;

public sealed class GraphDirectConsumerTests
{
    [Fact]
    public void OrdinaryApplicationPayloadsRemainTypedAndSnapshotted()
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
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            observation.NeighborhoodCompletion);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            observation.FocusCompletion);
        Assert.Equal(2, observation.NeighborhoodReceipt.NodesAdmitted);
        Assert.True(
            observation.NeighborhoodReceipt.TerminalSettled);
        Assert.Equal(typeof(Service), observation.SubjectType);
        Assert.Equal(typeof(DependsOn), observation.RelationshipType);
    }
}
