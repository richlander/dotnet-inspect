using Inspector.Graph;

namespace Inspector.Graph.Consumer;

public sealed record Service(string Name);

public sealed record DependsOn(string Kind);

public sealed record DependencyReceipt(string ConfigurationSource);

public sealed record ServiceCharacteristic(string Name);

public sealed record ServiceLimit(string Reason);

public sealed record ServiceFailure(string Message);

public sealed record GraphConsumerObservation(
    IReadOnlyList<string> Nodes,
    IReadOnlyList<string> Relationships,
    IReadOnlyList<string> Receipts,
    IReadOnlyList<string> Characteristics,
    IReadOnlyList<string> Limits,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> NeighborhoodNodes,
    IReadOnlyList<string> FocusRelationships,
    GraphStructuralCompletion NeighborhoodCompletion,
    GraphStructuralCompletion FocusCompletion,
    GraphExecutionWorkReceipt NeighborhoodReceipt,
    Type SubjectType,
    Type RelationshipType);

public static class GraphDirectConsumer
{
    public static GraphConsumerObservation Execute()
    {
        var api = new Service("api");
        var database = new Service("database");
        var services = new List<GraphNode<Service>>
        {
            new(0, api, GraphNodeRole.Ordinary, [0]),
            new(1, database, GraphNodeRole.External, [0]),
        };
        var groups = new List<GraphGroup<Service>>
        {
            new(0, new Service("application"), parentId: null),
        };
        var relationship = new DependsOn("runtime");
        var occurrences =
            new List<
                GraphOccurrence<
                    Service,
                    DependsOn,
                    DependencyReceipt>>
            {
                new(
                    0,
                    relationship,
                    api,
                    database,
                    new("appsettings.json"),
                    []),
            };
        var edges = new List<GraphEdge<DependsOn>>
        {
            new(0, 0, 1, relationship, [0]),
        };
        var characteristics =
            new List<GraphCharacteristic<ServiceCharacteristic>>
            {
                new(
                    GraphTarget.Node(0),
                    new("entry-point"),
                    new(
                        GraphCharacteristicDerivationKind.Direct,
                        [])),
            };
        var seeds = new List<GraphSeed<Service>>
        {
            new(api, GraphTarget.Node(0), GraphSeedRole.Primary),
        };
        var limits = new List<GraphLimit<ServiceLimit>>
        {
            new(new("external boundary"), GraphTarget.Node(1)),
        };
        var failures = new List<GraphFailure<ServiceFailure>>
        {
            new(new("optional configuration missing")),
        };

        var document =
            new GraphDocument<
                Service,
                DependsOn,
                DependencyReceipt,
                ServiceCharacteristic,
                ServiceLimit,
                ServiceFailure>(
                GraphDocumentScope.Portable,
                services,
                groups,
                edges,
                occurrences,
                characteristics,
                seeds,
                limits,
                failures);

        services.Clear();
        groups.Clear();
        edges.Clear();
        occurrences.Clear();
        characteristics.Clear();
        seeds.Clear();
        limits.Clear();
        failures.Clear();

        GraphNeighborhoodResult neighborhood =
            GraphDocumentExecution.Neighborhood(
                document,
                new GraphNeighborhoodPlan<DependsOn>(
                    [relationship],
                    GraphTraversalDirection.Outgoing,
                    maxDepth: 1,
                    rootNodeIds: [0],
                    entries: []));
        GraphFocusResult focus =
            GraphDocumentExecution.Focus(
                document,
                new GraphFocusPlan<DependsOn>(
                    [relationship],
                    GraphTraversalDirection.Outgoing,
                    [
                        new(0, GraphScopeMembership.Inside),
                        new(1, GraphScopeMembership.Outside),
                    ],
                    originNodeIds: [0],
                    GraphFocusReachability.FromOrigins));

        return new(
            [.. document.Nodes.Select(node => node.Subject.Name)],
            [.. document.Edges.Select(edge => edge.Relationship.Kind)],
            [.. document.Occurrences.Select(
                occurrence => occurrence.Evidence.ConfigurationSource)],
            [.. document.Characteristics.Select(
                characteristic => characteristic.Payload.Name)],
            [.. document.Limits.Select(limit => limit.Payload.Reason)],
            [.. document.Failures.Select(failure => failure.Payload.Message)],
            [.. neighborhood.NodeIds.Select(id =>
                document.Nodes[id].Subject.Name)],
            [.. focus.ExitEdgeIds.Select(id =>
                document.Edges[id].Relationship.Kind)],
            neighborhood.Completion,
            focus.Completion,
            neighborhood.Receipt,
            typeof(Service),
            typeof(DependsOn));
    }
}
