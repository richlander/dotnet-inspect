using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class OverloadFamilyCallGraphQueryTests
{
    static readonly Lazy<LibraryBodyAnalysisExecution> FixtureAnalysis =
        new(() => Analyze(
            FixtureCatalog.AnalysisOverloadFamilyLens.AssemblyPath()));

    [Fact]
    public void SiblingDelegationRetainsExactPeerCallOccurrence()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "SiblingDelegation",
            "Parse",
            parameterCount: 1);

        InspectionGraphDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 1,
            maxNodes: 8);

        AssertPeerFamily(
            document,
            callGraph,
            anchor,
            expectedCount: 2);
        InspectionGraphEdge siblingEdge = Assert.Single(
            document.Edges,
            edge =>
                SeedNodeIds(document).Contains(edge.FromNodeId)
                && SeedNodeIds(document).Contains(edge.ToNodeId));
        CallGraphCallSiteEvidence callSite = Assert.IsType<
            CallGraphCallSiteEvidence>(
                Assert.Single(
                    siblingEdge.OccurrenceIds.Select(
                        id => document.Occurrences[id])).Evidence);
        Assert.Equal(CallKind.Call, callSite.CallKind);
        Assert.All(
            document.Seeds,
            seed => Assert.Equal(
                InspectionGraphNodeRole.Unclassified,
                document.Nodes[seed.Target.Id].Role));
    }

    [Fact]
    public void IndependentImplementationsRemainDisconnectedPeers()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "IndependentImplementations",
            "Measure",
            parameterCount: 1);

        InspectionGraphDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 1,
            maxNodes: 8);

        AssertPeerFamily(
            document,
            callGraph,
            anchor,
            expectedCount: 2);
        int[] roots = [.. SeedNodeIds(document)];
        Assert.DoesNotContain(
            document.Edges,
            edge =>
                roots.Contains(edge.FromNodeId)
                && roots.Contains(edge.ToNodeId));
    }

    [Fact]
    public void PublicOverloadsConvergeOnOnePrivateHelper()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "PrivateHelperConvergence",
            "Normalize",
            parameterCount: 1);

        InspectionGraphDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 2,
            maxNodes: 12);

        AssertPeerFamily(
            document,
            callGraph,
            anchor,
            expectedCount: 2);
        int helperId = Assert.Single(
            document.Nodes,
            node => Member(node).Name == "NormalizeCore").Id;
        Assert.Equal(
            2,
            document.Seeds.Count(seed =>
                IsReachable(
                    document,
                    seed.Target.Id,
                    helperId)));
        Assert.DoesNotContain(
            document.Seeds,
            seed => seed.Target.Id == helperId);
    }

    [Fact]
    public void ConstructorOverloadsRetainThisDelegationAsCall()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "ConstructorDelegation",
            ".ctor",
            parameterCount: 0);

        InspectionGraphDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 1,
            maxNodes: 8);

        AssertPeerFamily(
            document,
            callGraph,
            anchor,
            expectedCount: 2);
        InspectionGraphEdge constructorEdge = Assert.Single(
            document.Edges,
            edge =>
                SeedNodeIds(document).Contains(edge.FromNodeId)
                && SeedNodeIds(document).Contains(edge.ToNodeId));
        CallGraphCallSiteEvidence occurrence = Assert.IsType<
            CallGraphCallSiteEvidence>(
                Assert.Single(
                    constructorEdge.OccurrenceIds.Select(
                        id => document.Occurrences[id])).Evidence);
        Assert.Equal(CallKind.Call, occurrence.CallKind);
    }

    [Fact]
    public void RequestDisclosesDepthAndCombinedNodeBounds()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "PrivateHelperConvergence",
            "Normalize",
            parameterCount: 1);

        InspectionGraphDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 0,
            maxNodes: 2);

        Assert.Equal(2, document.Nodes.Length);
        Assert.Empty(document.Edges);
        Assert.Equal(2, document.Seeds.Length);
        Assert.Equal(
            2,
            document.Limits.Count(limit =>
                ReferenceEquals(
                    limit.Descriptor,
                    InspectionGraphNeighborhoodCatalog.DepthBound)));
        CallGraphTraversalNodeBoundEvidence nodeBound = Assert.IsType<
            CallGraphTraversalNodeBoundEvidence>(
                Assert.Single(
                    document.Limits,
                    limit => ReferenceEquals(
                        limit.Descriptor,
                        CallGraphInspectionGraphCatalog
                            .TraversalNodeBound)).Evidence);
        Assert.Equal(2, nodeBound.MaxNodes);
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .TraversalIncomplete));
    }

    [Fact]
    public void RequestRejectsPartialScopeMissingAnchorAndSingleton()
    {
        LibraryCallGraphAnalysisResult full =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity family = Method(
            full,
            "SiblingDelegation",
            "Parse",
            parameterCount: 1);
        LibraryBodyAnalysisExecution partial =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisOverloadFamilyLens.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence,
                    bodyScope:
                        new HashSet<int>
                        {
                            family.MetadataToken,
                        }));

        InspectionQueryException partialError =
            Assert.Throws<InspectionQueryException>(
                () => Execute(
                    partial.CallGraph,
                    family,
                    maxDepth: 1,
                    maxNodes: 8));
        Assert.Contains(
            "complete method evidence",
            partialError.Message,
            StringComparison.Ordinal);

        InspectionQueryException anchorError =
            Assert.Throws<InspectionQueryException>(
                () => Execute(
                    full,
                    family with
                    {
                        MetadataToken = 0x0600FFFF,
                    },
                    maxDepth: 1,
                    maxNodes: 8));
        Assert.Contains(
            "not a declared method",
            anchorError.Message,
            StringComparison.Ordinal);

        MethodIdentity singleton = Method(
            full,
            "SingleImplementation",
            "Only",
            parameterCount: 1);
        InspectionQueryException singletonError =
            Assert.Throws<InspectionQueryException>(
                () => Execute(
                    full,
                    singleton,
                    maxDepth: 1,
                    maxNodes: 8));
        Assert.Contains(
            "at least two",
            singletonError.Message,
            StringComparison.Ordinal);

        MethodIdentity oversized = Method(
            full,
            "ImplementationProfileSample",
            "Analyze",
            parameterCount: 1);
        InspectionQueryException boundError =
            Assert.Throws<InspectionQueryException>(
                () => Execute(
                    full,
                    oversized,
                    maxDepth: 1,
                    maxNodes: 2));
        Assert.Contains(
            "exceeds",
            boundError.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SystemTextJsonSerializeFamilyRetainsFifteenOrderedPeers()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "DocumentationQuery",
            "System.Text.Json.dll");
        LibraryCallGraphAnalysisResult callGraph =
            Analyze(path).CallGraph;
        MethodIdentity anchor = callGraph.DeclaredMethods.First(
            method =>
                method.DeclaringType.Name == "JsonSerializer"
                && method.Name == "Serialize");

        InspectionGraphDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 2,
            maxNodes: 500);

        AssertPeerFamily(
            document,
            callGraph,
            anchor,
            expectedCount: 15);
        Assert.Equal(
            callGraph.DeclaredMethods
                .Where(method =>
                    method.ModuleVersionId
                        == anchor.ModuleVersionId
                    && method.DeclaringType.Equals(
                        anchor.DeclaringType)
                    && method.Name == anchor.Name)
                .OrderBy(static method =>
                    method.MetadataToken)
                .Select(GraphNodeIdentity.FromMethod),
            document.Seeds.Select(seed =>
                CallGraphIdentity(seed.Subject).Identity));
        Assert.Contains(
            document.Nodes.Where(node =>
                !SeedNodeIds(document).Contains(node.Id)),
            node => document.Seeds.Count(seed =>
                IsReachable(
                    document,
                    seed.Target.Id,
                    node.Id)) > 1);
    }

    static LibraryBodyAnalysisExecution Analyze(string path) =>
        LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence));

    static InspectionGraphDocument Execute(
        LibraryCallGraphAnalysisResult callGraph,
        MethodIdentity anchor,
        int maxDepth,
        int maxNodes) =>
        OverloadFamilyCallGraphQuery.Execute(
            callGraph,
            new OverloadFamilyCallGraphRequest(
                anchor,
                maxDepth,
                maxNodes));

    static MethodIdentity Method(
        LibraryCallGraphAnalysisResult callGraph,
        string declaringType,
        string name,
        int parameterCount) =>
        callGraph.DeclaredMethods.First(
            method =>
                method.DeclaringType.Name == declaringType
                && method.Name == name
                && method.ParameterTypes.Length
                    == parameterCount);

    static void AssertPeerFamily(
        InspectionGraphDocument document,
        LibraryCallGraphAnalysisResult callGraph,
        MethodIdentity anchor,
        int expectedCount)
    {
        Assert.Equal(
            InspectionGraphMode.PeerSeeds,
            document.ModeRequest.Mode);
        Assert.Equal(expectedCount, document.Seeds.Length);
        Assert.Equal(
            document.ModeRequest.Seeds,
            document.Seeds.Select(static seed => seed.Subject));
        Assert.All(
            document.Seeds,
            seed =>
            {
                Assert.Equal(InspectionGraphSeedRole.Peer, seed.Role);
                Assert.Equal(
                    anchor.DeclaringType,
                    Member(seed).DeclaringType);
                Assert.Equal(
                    anchor.Name,
                    Member(seed).Name);
            });
        Assert.Equal(
            callGraph.DeclaredMethods
                .Where(method =>
                    method.ModuleVersionId
                        == anchor.ModuleVersionId
                    && method.DeclaringType.Equals(
                        anchor.DeclaringType)
                    && method.Name == anchor.Name)
                .OrderBy(static method =>
                    method.MetadataToken)
                .Select(GraphNodeIdentity.FromMethod),
            document.Seeds.Select(seed =>
                CallGraphIdentity(seed.Subject).Identity));
    }

    static HashSet<int> SeedNodeIds(
        InspectionGraphDocument document) =>
        [.. document.Seeds.Select(static seed => seed.Target.Id)];

    static MemberRef Member(InspectionGraphNode node) =>
        Member(node.Subject);

    static MemberRef Member(InspectionGraphSeed seed) =>
        Member(seed.Subject);

    static MemberRef Member(InspectionGraphSubject subject) =>
        CallGraphIdentity(subject).Member;

    static InspectionGraphMemberIdentity.CallGraph CallGraphIdentity(
        InspectionGraphSubject subject) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                subject).Identity);

    static bool IsReachable(
        InspectionGraphDocument document,
        int start,
        int target)
    {
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out int current))
        {
            foreach (InspectionGraphEdge edge in document.Edges.Where(
                edge => edge.FromNodeId == current))
            {
                if (edge.ToNodeId == target)
                    return true;
                if (seen.Add(edge.ToNodeId))
                    queue.Enqueue(edge.ToNodeId);
            }
        }
        return false;
    }
}
