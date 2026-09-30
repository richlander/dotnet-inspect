#:project ../src/ILInspector.Analysis/ILInspector.Analysis.csproj
#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/Inspector.Graph/Inspector.Graph.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true

using System.Diagnostics;
using System.Globalization;

using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Graph;

bool measureConstruction =
    args.Length > 0 && args[0] == "--construction";
int pathStart = measureConstruction ? 1 : 0;
if (args.Length == pathStart)
{
    Console.WriteLine(
        "Usage: measure-graph-degree [--construction] <assembly>...");
    return 2;
}

if (measureConstruction)
{
    Console.WriteLine(
        "asset\tnodes\tedges\tchecksum\tsamples\tmedian_ms\tp95_ms"
            + "\tmedian_bytes");
    foreach (string path in args[pathStart..])
    {
        GraphInput input = GraphInput.Load(path);
        ulong checksum = ChecksumDocument(input);
        for (var index = 0; index < 5; index++)
        {
            var warmup = input.CreateDocument();
            RequireDocumentEquivalent(input, warmup);
        }

        var times = new List<double>();
        var allocations = new List<long>();
        long started = Stopwatch.GetTimestamp();
        do
        {
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long timestamp = Stopwatch.GetTimestamp();
            var result = input.CreateDocument();
            times.Add(
                Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
            allocations.Add(
                GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore);
            RequireDocumentEquivalent(input, result);
            GC.KeepAlive(result);
        }
        while ((times.Count < 20
                    || Stopwatch.GetElapsedTime(started)
                        < TimeSpan.FromSeconds(2))
                && times.Count < 5_000);

        times.Sort();
        allocations.Sort();
        Console.WriteLine(
            string.Join(
                '\t',
                Path.GetFileName(path),
                input.Document.Nodes.Length,
                input.Document.Edges.Length,
                checksum.ToString("x16", CultureInfo.InvariantCulture),
                times.Count,
                Percentile(times, 0.50).ToString(
                    "F6",
                    CultureInfo.InvariantCulture),
                Percentile(times, 0.95).ToString(
                    "F6",
                    CultureInfo.InvariantCulture),
                Percentile(allocations, 0.50)));
    }
}
else
{
    Console.WriteLine(
        "asset\tscenario\tnodes\tedges\tselected_edges"
            + "\tadjacency_entries\trows\tchecksum\tsamples"
            + "\tmedian_ms\tp95_ms\tmedian_bytes");
    foreach (string path in args[pathStart..])
    {
        GraphInput input = GraphInput.Load(path);
        foreach (Scenario scenario in Scenario.All)
        {
            GraphNeighborPlan<Relationship> plan =
                new(
                    scenario.Relationships,
                    scenario.Direction,
                    GraphSelfLoopPolicy.Exclude);
            GraphDistinctNeighborDegreeResult expected =
                GraphDocumentExecution.DistinctNeighborDegree(
                    input.Document,
                    plan);
            ulong checksum = Checksum(expected);
            for (var index = 0; index < 5; index++)
            {
                GraphDistinctNeighborDegreeResult warmup =
                    GraphDocumentExecution.DistinctNeighborDegree(
                        input.Document,
                        plan);
                RequireEquivalent(expected, warmup);
            }

            var times = new List<double>();
            var allocations = new List<long>();
            long started = Stopwatch.GetTimestamp();
            do
            {
                long allocatedBefore =
                    GC.GetAllocatedBytesForCurrentThread();
                long timestamp = Stopwatch.GetTimestamp();
                GraphDistinctNeighborDegreeResult result =
                    GraphDocumentExecution.DistinctNeighborDegree(
                        input.Document,
                        plan);
                times.Add(
                    Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
                allocations.Add(
                    GC.GetAllocatedBytesForCurrentThread()
                        - allocatedBefore);
                RequireEquivalent(expected, result);
                GC.KeepAlive(result);
            }
            while ((times.Count < 20
                        || Stopwatch.GetElapsedTime(started)
                            < TimeSpan.FromSeconds(2))
                    && times.Count < 5_000);

            times.Sort();
            allocations.Sort();
            Console.WriteLine(
                string.Join(
                    '\t',
                    Path.GetFileName(path),
                    scenario.Name,
                    input.Document.Nodes.Length,
                    input.Document.Edges.Length,
                    expected.Receipt.SelectedEdgesIndexed,
                    expected.Receipt.AdjacencyEntriesExamined,
                    expected.Rows.Length,
                    checksum.ToString(
                        "x16",
                        CultureInfo.InvariantCulture),
                    times.Count,
                    Percentile(times, 0.50).ToString(
                        "F6",
                        CultureInfo.InvariantCulture),
                    Percentile(times, 0.95).ToString(
                        "F6",
                        CultureInfo.InvariantCulture),
                    Percentile(allocations, 0.50)));
        }
    }
}

return 0;

static ulong ChecksumDocument(GraphInput input)
{
    const ulong offset = 14695981039346656037;
    const ulong prime = 1099511628211;
    ulong hash = offset;
    foreach (GraphNode<MetadataTypeDefinitionAddress> node
        in input.Document.Nodes)
    {
        hash = (hash ^ (uint)node.Id) * prime;
        hash = (hash ^ (uint)node.Subject.Definition.Value) * prime;
    }
    foreach (GraphEdge<Relationship> edge in input.Document.Edges)
    {
        hash = (hash ^ (uint)edge.Id) * prime;
        hash = (hash ^ (uint)edge.FromNodeId) * prime;
        hash = (hash ^ (uint)edge.ToNodeId) * prime;
        hash = (hash ^ (uint)edge.Relationship) * prime;
    }
    return hash;
}

static ulong Checksum(GraphDistinctNeighborDegreeResult result)
{
    const ulong offset = 14695981039346656037;
    const ulong prime = 1099511628211;
    ulong hash = offset;
    foreach (GraphNodeDegree row in result.Rows)
    {
        hash = (hash ^ (uint)row.NodeId) * prime;
        hash = (hash ^ (uint)row.Degree) * prime;
    }
    GraphExecutionWorkReceipt receipt = result.Receipt;
    hash = (hash ^ (uint)receipt.CanonicalNodesExamined) * prime;
    hash = (hash ^ (uint)receipt.CanonicalEdgesExamined) * prime;
    hash = (hash ^ (uint)receipt.SelectedEdgesIndexed) * prime;
    hash = (hash ^ (uint)receipt.StructuralViewsBuilt) * prime;
    hash = (hash ^ (uint)receipt.AdjacencyEntriesExamined) * prime;
    hash = (hash ^ (uint)receipt.NodesAdmitted) * prime;
    hash = (hash ^ (receipt.TerminalSettled ? 1u : 0u)) * prime;
    return hash;
}

static void RequireDocumentEquivalent(
    GraphInput expected,
    GraphDocument<
        MetadataTypeDefinitionAddress,
        Relationship,
        OccurrenceEvidence,
        Characteristic,
        Limit,
        Failure> actual)
{
    if (expected.Document.Scope != actual.Scope
        || !expected.Nodes.SequenceEqual(actual.Nodes)
        || !expected.Edges.SequenceEqual(actual.Edges)
        || !actual.Groups.IsEmpty
        || !actual.Occurrences.IsEmpty
        || !actual.Characteristics.IsEmpty
        || !actual.Seeds.IsEmpty
        || !actual.Limits.IsEmpty
        || !actual.Failures.IsEmpty)
    {
        throw new InvalidOperationException(
            "Graph document construction changed.");
    }
}

static void RequireEquivalent(
    GraphDistinctNeighborDegreeResult expected,
    GraphDistinctNeighborDegreeResult actual)
{
    if (!expected.Rows.SequenceEqual(actual.Rows)
        || expected.Completion != actual.Completion
        || expected.Receipt != actual.Receipt)
    {
        throw new InvalidOperationException(
            "Degree result or work receipt changed.");
    }
}

static T Percentile<T>(IReadOnlyList<T> sorted, double percentile)
{
    int index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
}

enum Relationship
{
    SignatureUse,
    BodyUse,
}

enum OccurrenceEvidence
{
    None,
}

enum Characteristic
{
    None,
}

enum Limit
{
    None,
}

enum Failure
{
    None,
}

readonly record struct LogicalEdge(
    int SourceNodeId,
    int TargetNodeId,
    Relationship Relationship);

sealed record Scenario(
    string Name,
    Relationship[] Relationships,
    GraphTraversalDirection Direction)
{
    internal static Scenario[] All { get; } =
    [
        new(
            "signature-incoming",
            [Relationship.SignatureUse],
            GraphTraversalDirection.Incoming),
        new(
            "body-outgoing",
            [Relationship.BodyUse],
            GraphTraversalDirection.Outgoing),
        new(
            "signature-both-control",
            [Relationship.SignatureUse],
            GraphTraversalDirection.Both),
        new(
            "combined-incoming-control",
            [Relationship.SignatureUse, Relationship.BodyUse],
            GraphTraversalDirection.Incoming),
        new(
            "combined-outgoing-control",
            [Relationship.SignatureUse, Relationship.BodyUse],
            GraphTraversalDirection.Outgoing),
    ];
}

sealed record GraphInput(
    GraphNode<MetadataTypeDefinitionAddress>[] Nodes,
    GraphEdge<Relationship>[] Edges,
    GraphDocument<
        MetadataTypeDefinitionAddress,
        Relationship,
        OccurrenceEvidence,
        Characteristic,
        Limit,
        Failure> Document)
{
    internal GraphDocument<
        MetadataTypeDefinitionAddress,
        Relationship,
        OccurrenceEvidence,
        Characteristic,
        Limit,
        Failure> CreateDocument() =>
        new(
            GraphDocumentScope.Portable,
            Nodes,
            [],
            Edges,
            [],
            [],
            [],
            [],
            []);

    internal static GraphInput Load(string path)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        MetadataLibrarySignatureUseOutcome signatureOutcome =
            session.LibrarySignatureUses(
                new(MetadataOperationPolicy.Unbounded),
                CancellationToken.None);
        MetadataLibrarySignatureUseResult signature =
            signatureOutcome switch
            {
                MetadataLibrarySignatureUseOutcome.Available available =>
                    available.Result,
                _ => throw new InvalidOperationException(
                    "Signature-use acquisition was unavailable."),
            };
        AnalysisLibraryBodyUseOutcome bodyOutcome =
            AnalysisLibraryBodyUseService.ExecutePath(
                path,
                new(),
                CancellationToken.None);
        AnalysisLibraryBodyUseResult body =
            bodyOutcome switch
            {
                AnalysisLibraryBodyUseOutcome.Available available =>
                    available.Result,
                _ => throw new InvalidOperationException(
                    "Body-use acquisition was unavailable."),
            };

        MetadataLibrarySignatureType[] types =
        [
            .. signature.Types.OrderBy(
                static type => type.Type.Definition.Value),
        ];
        var nodeIds =
            new Dictionary<MetadataTypeDefinitionAddress, int>(
                types.Length);
        var nodes =
            new GraphNode<MetadataTypeDefinitionAddress>[types.Length];
        for (var nodeId = 0; nodeId < types.Length; nodeId++)
        {
            MetadataLibrarySignatureType type = types[nodeId];
            nodeIds.Add(type.Type, nodeId);
            nodes[nodeId] =
                new(
                    nodeId,
                    type.Type,
                    GraphNodeRole.Ordinary,
                    []);
        }

        var logicalEdges = new HashSet<LogicalEdge>();
        foreach (MetadataLibrarySignatureUseOccurrence occurrence
            in signature.Occurrences)
        {
            logicalEdges.Add(
                new(
                    nodeIds[occurrence.Source],
                    nodeIds[occurrence.Target],
                    Relationship.SignatureUse));
        }
        foreach (AnalysisLibraryBodyUseOccurrence occurrence
            in body.Occurrences)
        {
            logicalEdges.Add(
                new(
                    nodeIds[occurrence.Source],
                    nodeIds[occurrence.Target],
                    Relationship.BodyUse));
        }

        GraphEdge<Relationship>[] edges =
        [
            .. logicalEdges
                .OrderBy(static edge => edge.SourceNodeId)
                .ThenBy(static edge => edge.TargetNodeId)
                .ThenBy(static edge => edge.Relationship)
                .Select(
                    static (edge, edgeId) =>
                        new GraphEdge<Relationship>(
                            edgeId,
                            edge.SourceNodeId,
                            edge.TargetNodeId,
                            edge.Relationship,
                            [])),
        ];
        return new GraphInput(
            nodes,
            edges,
            new GraphDocument<
                MetadataTypeDefinitionAddress,
                Relationship,
                OccurrenceEvidence,
                Characteristic,
                Limit,
                Failure>(
                GraphDocumentScope.Portable,
                nodes,
                [],
                edges,
                [],
                [],
                [],
                [],
                []));
    }
}
