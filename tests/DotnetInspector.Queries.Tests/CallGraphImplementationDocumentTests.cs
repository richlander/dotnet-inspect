using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspector.Queries.Tests;

public sealed class CallGraphImplementationDocumentTests
{
    [Fact]
    public void
        Create_RetainsPopulationAndJoinsExistingOverloadOccurrence()
    {
        LibraryBodyAnalysisExecution analysis = AnalyzeFixture(
            LibraryBodyAnalysisFeatures.ImplementationProfiles);
        MethodImplementationProfile wrapper = Assert.Single(
            AnalyzeProfiles(analysis),
            profile =>
                profile.Method.ParameterTypes.Length == 1
                && profile.Method.ParameterTypes[0].Name == "Int32");
        CallGraphProjection projection = CallGraphProjection.FromCallees(
            analysis.CallGraph.BuildCallTree(
                wrapper.Method.MetadataToken,
                maxDepth: 3,
                maxNodes: 25));
        InspectionGraphDocument baseline =
            CallGraphInspectionGraphAdapter.Create(projection);

        CallGraphImplementationDocument document =
            CallGraphImplementationDocumentAdapter.Create(
                projection,
                analysis.ImplementationProfiles);

        Assert.Same(analysis.Receipt, document.Receipt);
        Assert.Same(
            analysis.ImplementationProfiles.Coverage,
            document.Coverage);
        Assert.Equal(
            analysis.ImplementationProfiles.Profiles,
            document.Profiles);
        Assert.Equal(
            analysis.ImplementationProfiles.OverloadRelationships,
            document.OverloadRelationships);
        Assert.Equal(
            baseline.Nodes.Select(NodeShape),
            document.Graph.Nodes.Select(NodeShape));
        Assert.Equal(
            baseline.Edges.Select(EdgeShape),
            document.Graph.Edges.Select(EdgeShape));
        Assert.Equal(
            baseline.Occurrences.Select(OccurrenceShape),
            document.Graph.Occurrences.Select(OccurrenceShape));

        OverloadCallRelationship relationship = Assert.Single(
            document.OverloadRelationships,
            candidate =>
                candidate.Caller == wrapper.Method);
        int relationshipIndex =
            document.OverloadRelationships.IndexOf(relationship);
        CallGraphOverloadRelationshipJoin join =
            document.OverloadRelationshipJoins[relationshipIndex];
        Assert.Equal(
            CallGraphImplementationJoinMatch.Found,
            join.Caller.Match);
        Assert.Equal(
            CallGraphImplementationJoinMatch.Found,
            join.Callee.Match);
        Assert.Equal(
            CallGraphImplementationJoinMatch.Found,
            join.EdgeMatch);
        Assert.Equal(
            CallGraphImplementationJoinMatch.Found,
            join.OccurrenceMatch);
        Assert.Contains(
            join.OccurrenceId!.Value,
            document.Graph.Edges[join.EdgeId!.Value].OccurrenceIds);
        Assert.Same(
            CallGraphInspectionGraphCatalog.Call,
            document.Graph.Edges[join.EdgeId.Value].Relationship);
        Assert.IsType<CallGraphCallSiteEvidence>(
            document.Graph.Occurrences[
                join.OccurrenceId.Value].Evidence);
    }

    [Fact]
    public void
        Create_PreservesPhysicalProfilesBehindOneLogicalAsyncNode()
    {
        LibraryBodyAnalysisExecution analysis = AnalyzeFixture(
            LibraryBodyAnalysisFeatures.ImplementationProfiles);
        MethodImplementationProfile[] profiles =
        [
            .. analysis.ImplementationProfiles.Profiles.Where(
                profile =>
                    profile.Method.DeclaringType.Name
                        == "ImplementationProfileSample"
                    && profile.Method.Name == "AnalyzeAsync"
                    && profile.Method.ParameterTypes.Length == 1
                    && profile.Method.ParameterTypes[0].Name
                        == "Int32"),
        ];
        Assert.Equal(2, profiles.Length);
        MethodIdentity logicalOwner = profiles[0].Method;
        Assert.All(
            profiles,
            profile => Assert.Equal(logicalOwner, profile.Method));
        Assert.Equal(
            2,
            profiles.Select(profile =>
                profile.EvidenceMethod.MetadataToken)
                .Distinct()
                .Count());
        CallGraphProjection projection = CallGraphProjection.FromCallees(
            analysis.CallGraph.BuildCallTree(
                logicalOwner.MetadataToken,
                maxDepth: 3,
                maxNodes: 25));

        CallGraphImplementationDocument document =
            CallGraphImplementationDocumentAdapter.Create(
                projection,
                analysis.ImplementationProfiles);

        CallGraphImplementationProfileJoin[] joins =
        [
            .. document.ProfileJoins.Where(join =>
                document.Profiles[join.ProfileIndex].Method
                    == logicalOwner),
        ];
        Assert.Equal(2, joins.Length);
        Assert.All(
            joins,
            join =>
            {
                Assert.Equal(
                    CallGraphImplementationJoinMatch.Found,
                    join.LogicalOwner.Match);
                Assert.Equal(0, join.LogicalOwner.NodeId);
            });
        Assert.Single(
            joins,
            join =>
                join.EvidenceMethod.Match
                    == CallGraphImplementationJoinMatch.Found);
        Assert.Single(
            joins,
            join =>
                join.EvidenceMethod.Match
                    == CallGraphImplementationJoinMatch.NotProjected);
    }

    [Fact]
    public void
        Create_SystemTextJsonRetainsEverySelectedSerializeProfile()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "DocumentationQuery",
            "System.Text.Json.dll");
        HashSet<int> tokens = JsonSerializerSerializeTokens(path);
        Assert.Equal(15, tokens.Count);
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles,
                    bodyScope: tokens));
        MethodImplementationProfile[] profiles =
        [
            .. analysis.ImplementationProfiles.Profiles.Where(
                profile =>
                    profile.Method.DeclaringType.Namespace
                        == "System.Text.Json"
                    && profile.Method.DeclaringType.Name
                        == "JsonSerializer"
                    && profile.Method.Name == "Serialize"),
        ];
        Assert.Equal(15, profiles.Length);
        MethodImplementationProfile focus =
            profiles.MaxBy(
                static profile => profile.DirectCallCount)!;
        CallGraphProjection projection = CallGraphProjection.FromCallees(
            analysis.CallGraph.BuildCallTree(
                focus.Method.MetadataToken,
                maxDepth: 2,
                maxNodes: 50));

        CallGraphImplementationDocument document =
            CallGraphImplementationDocumentAdapter.Create(
                projection,
                analysis.ImplementationProfiles);

        CallGraphImplementationProfileJoin[] joins =
        [
            .. document.ProfileJoins.Where(join =>
                tokens.Contains(
                    document.Profiles[join.ProfileIndex]
                        .Method.MetadataToken)),
        ];
        Assert.Equal(15, joins.Length);
        Assert.Contains(
            joins,
            join =>
                join.LogicalOwner.Match
                    == CallGraphImplementationJoinMatch.Found);
        Assert.Contains(
            joins,
            join =>
                join.LogicalOwner.Match
                    == CallGraphImplementationJoinMatch.NotProjected);
        Assert.DoesNotContain(
            joins,
            join =>
                join.LogicalOwner.Match
                    == CallGraphImplementationJoinMatch.Ambiguous);
        Assert.True(
            profiles.Select(profile => (
                profile.InstructionCount,
                profile.DirectCallCount,
                profile.DistinctCalleeCount))
                .Distinct()
                .Count() > 1);
        Assert.Same(
            analysis.ImplementationProfiles.Coverage,
            document.Coverage);
        Assert.False(document.Coverage.HasFullMethodEvidenceScope);
        Assert.NotEmpty(document.Coverage.UnavailableBodies);
    }

    [Fact]
    public void
        MultiRootProjection_SystemTextJsonRetainsEverySerializeRootAndSharedHelper()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "DocumentationQuery",
            "System.Text.Json.dll");
        int[] tokens =
        [
            .. JsonSerializerSerializeTokens(path)
                .Order(),
        ];
        Assert.Equal(15, tokens.Length);
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .ImplementationProfiles,
                    bodyScope: tokens.ToHashSet()));
        CallTreeNode[] roots =
        [
            .. tokens.Select(token =>
                analysis.CallGraph.BuildCallTree(
                    token,
                    maxDepth: 2,
                    maxNodes: 50)),
        ];

        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                roots,
                maxNodes: 500);

        Assert.Equal(15, projection.RootNodeIds.Length);
        Assert.Equal(
            projection.RootNodeIds,
            projection.RootNodeIds.Order());
        for (var index = 0; index < tokens.Length; index++)
        {
            MethodIdentity method = Assert.Single(
                analysis.CallGraph.DeclaredMethods,
                candidate =>
                    candidate.MetadataToken == tokens[index]);
            Assert.Equal(
                CallGraphNodeMatch.Found,
                projection.FindNode(
                    method,
                    out CallGraphNode node));
            Assert.Equal(
                projection.RootNodeIds[index],
                node.Id);
        }
        Assert.Contains(
            Enumerable.Range(0, projection.Nodes.Length)
                .Except(projection.RootNodeIds),
            nodeId => projection.RootNodeIds.Count(rootId =>
                IsReachable(projection, rootId, nodeId)) > 1);
    }

    [Fact]
    public void Create_RejectsUnrequestedImplementationProfiles()
    {
        LibraryBodyAnalysisExecution analysis = AnalyzeFixture(
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity focus = Assert.Single(
            analysis.CallGraph.DeclaredMethods,
            method =>
                method.DeclaringType.Name
                    == "ImplementationProfileSample"
                && method.Name == "Other");
        CallGraphProjection projection = CallGraphProjection.FromCallees(
            analysis.CallGraph.BuildCallTree(
                focus.MetadataToken,
                maxDepth: 1,
                maxNodes: 10));

        var error = Assert.Throws<InvalidOperationException>(
            () => CallGraphImplementationDocumentAdapter.Create(
                projection,
                analysis.ImplementationProfiles));

        Assert.Contains(
            "were not requested",
            error.Message,
            StringComparison.Ordinal);
    }

    static LibraryBodyAnalysisExecution AnalyzeFixture(
        LibraryBodyAnalysisFeatures features) =>
        LibraryBodyAnalysisService.ExecutePath(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisRequest.Create(features));

    static IEnumerable<MethodImplementationProfile> AnalyzeProfiles(
        LibraryBodyAnalysisExecution analysis) =>
        analysis.ImplementationProfiles.Profiles.Where(
            profile =>
                profile.Method.DeclaringType.Name
                    == "ImplementationProfileSample"
                && profile.Method.Name == "Analyze");

    static (
        int Id,
        InspectionGraphSubject Subject,
        InspectionGraphNodeRole Role) NodeShape(
            InspectionGraphNode node) =>
        (node.Id, node.Subject, node.Role);

    static (
        int Id,
        int From,
        int To,
        InspectionGraphRelationshipDescriptor Relationship) EdgeShape(
            InspectionGraphEdge edge) =>
        (
            edge.Id,
            edge.FromNodeId,
            edge.ToNodeId,
            edge.Relationship);

    static (
        int Id,
        InspectionGraphSubject Source,
        InspectionGraphSubject Target,
        IInspectionGraphOccurrenceEvidence Evidence) OccurrenceShape(
            InspectionGraphOccurrence occurrence) =>
        (
            occurrence.Id,
            occurrence.SourceSubject,
            occurrence.TargetSubject,
            occurrence.Evidence);

    static bool IsReachable(
        CallGraphProjection projection,
        int rootNodeId,
        int targetNodeId)
    {
        var seen = new HashSet<int> { rootNodeId };
        var queue = new Queue<int>();
        queue.Enqueue(rootNodeId);
        while (queue.TryDequeue(out int nodeId))
        {
            if (nodeId == targetNodeId)
                return true;
            foreach (CallGraphEdge edge in projection.Edges)
            {
                if (edge.From == nodeId && seen.Add(edge.To))
                    queue.Enqueue(edge.To);
            }
        }
        return false;
    }

    static HashSet<int> JsonSerializerSerializeTokens(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle =
            Assert.Single(
                reader.TypeDefinitions,
                handle =>
                {
                    TypeDefinition type =
                        reader.GetTypeDefinition(handle);
                    return reader.GetString(type.Namespace)
                            == "System.Text.Json"
                        && reader.GetString(type.Name)
                            == "JsonSerializer";
                });
        TypeDefinition definition =
            reader.GetTypeDefinition(typeHandle);
        return
        [
            .. definition.GetMethods()
                .Where(handle =>
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(handle);
                    return reader.GetString(method.Name)
                            == "Serialize"
                        && method.Attributes.HasFlag(
                            MethodAttributes.Public);
                })
                .Select(static handle =>
                    MetadataTokens.GetToken(handle)),
        ];
    }
}
