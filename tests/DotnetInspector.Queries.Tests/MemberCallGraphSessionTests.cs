using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class MemberCallGraphSessionTests
{
    static string CallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
    static string OwnershipPath =>
        FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
    static string TargetPath =>
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
    static string TargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    static int MemberToken(
        string assemblyPath,
        string typeName,
        string methodName)
    {
        Analysis.LibraryBodyIndex index =
            Analysis.LibraryBodyIndex.Open(assemblyPath);
        return index.Methods.First(
            method => method.DeclaringType.Name == typeName
                && method.Name == methodName).MetadataToken;
    }

    static string TargetAssemblyName() =>
        Analysis.LibraryBodyIndex.Open(TargetPath)
            .Methods.First().AssemblyName;

    static Analysis.CallTreeNode Child(
        Analysis.CallTreeNode node,
        string name) =>
        node.Children.Single(child => child.Member.Name == name);

    static Analysis.MemberRef GraphMember(
        string typeName,
        string methodName) =>
        new(
            Analysis.TypeRef.Definition(
                "Sample",
                "Sample",
                typeName),
            methodName,
            [],
            Analysis.TypeRef.CoreLib(
                "System",
                "Void"),
            Analysis.MemberKind.Method);

    static Analysis.MemberRef InspectionMember(
        InspectionGraphNode node) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                node.Subject)
                .Identity)
            .Member;

    [Fact]
    public void Callees_ScopedFirstPaint_BuildsScopedIndexOnly()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            run);

        MemberCallGraphView view = graph.Callees();

        Assert.Equal(CallGraphTier.Callees, view.Tier);
        Assert.Null(view.CallerRoot);
        Assert.Equal("Run", view.CalleeRoot!.Member.Name);
        Analysis.CallTreeNode ping = Child(view.CalleeRoot, "Ping");
        Assert.Equal(Analysis.CallTreeStatus.External, ping.Status);
        Assert.Empty(ping.Children);
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
        Assert.Equal(0, context.Sources[1].OpenCount);
    }

    [Fact]
    public void Callees_ScopedFirstPaint_MarksInAssemblyCalleeBounded()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView view = graph.Callees();

        Assert.Equal(
            Analysis.CallTreeStatus.DepthLimited,
            Child(view.CalleeRoot!, "Run").Status);
    }

    [Fact]
    public void CrossLibraryCalleeNeighborhood_CrossesBoundaryAndContinues()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int root = MemberToken(
            CallerPath,
            "Entry",
            "RunAcrossBoundary");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root);

        InspectionGraphDocument document =
            graph.CrossLibraryCalleeNeighborhood(
                new(
                    maxDepth: 2,
                    maxNodes: 10));

        Assert.Equal(
            InspectionGraphTraversalDirection.Outgoing,
            document.NeighborhoodRequest!.Direction);
        Assert.Equal(2, document.NeighborhoodRequest.MaxDepth);
        Assert.Same(
            CallGraphInspectionGraphCatalog.Call,
            Assert.Single(
                document.NeighborhoodRequest.Relationships));
        Assert.Equal(
            [
                ("RunAcrossBoundary", "Forward"),
                ("Forward", "Leaf"),
            ],
            document.Edges.Select(edge =>
                (
                    InspectionMember(
                        document.Nodes[edge.FromNodeId]).Name,
                    InspectionMember(
                        document.Nodes[edge.ToNodeId]).Name)));
        Assert.Equal(2, document.Occurrences.Length);
        Assert.All(
            document.Occurrences,
            occurrence => Assert.IsType<
                CallGraphCallSiteEvidence>(
                    occurrence.Evidence));
        var depth = Assert.IsType<
            InspectionGraphNeighborhoodDepthBoundEvidence>(
                Assert.Single(
                    document.Limits,
                    limit => ReferenceEquals(
                        limit.Descriptor,
                        InspectionGraphNeighborhoodCatalog
                            .DepthBound))
                    .Evidence);
        Assert.Equal(2, depth.MaxDepth);
        var nodes = Assert.IsType<
            CallGraphTraversalNodeBoundEvidence>(
                Assert.Single(
                    document.Limits,
                    limit => ReferenceEquals(
                        limit.Descriptor,
                        CallGraphInspectionGraphCatalog
                            .TraversalNodeBound))
                    .Evidence);
        Assert.Equal(10, nodes.MaxNodes);
        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public void CrossLibraryCalleeNeighborhood_DepthBoundStopsAfterBoundary()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int root = MemberToken(
            CallerPath,
            "Entry",
            "RunAcrossBoundary");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root);

        InspectionGraphDocument document =
            graph.CrossLibraryCalleeNeighborhood(
                new(
                    maxDepth: 1,
                    maxNodes: 10));

        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal(
            "Forward",
            InspectionMember(
                document.Nodes[edge.ToNodeId]).Name);
        Assert.Equal(
            InspectionGraphNodeRole.Truncated,
            document.Nodes[edge.ToNodeId].Role);
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .TraversalIncomplete));
    }

    [Fact]
    public void CrossLibraryCalleeNeighborhood_ZeroDepthRetainsOnlySeed()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int root = MemberToken(
            CallerPath,
            "Entry",
            "RunAcrossBoundary");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root);

        InspectionGraphDocument document =
            graph.CrossLibraryCalleeNeighborhood(
                new(
                    maxDepth: 0,
                    maxNodes: 10));

        Assert.Single(document.Nodes);
        Assert.Empty(document.Edges);
        Assert.Empty(document.Occurrences);
        Assert.Equal(
            "RunAcrossBoundary",
            InspectionMember(document.Nodes[0]).Name);
        Assert.Equal(
            0,
            Assert.IsType<
                InspectionGraphNeighborhoodDepthBoundEvidence>(
                    Assert.Single(
                        document.Limits,
                        limit => ReferenceEquals(
                            limit.Descriptor,
                            InspectionGraphNeighborhoodCatalog
                                .DepthBound))
                    .Evidence)
                .MaxDepth);
    }

    [Fact]
    public void CrossLibraryCalleeNeighborhood_NodeBoundRetainsOnlySeed()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int root = MemberToken(
            CallerPath,
            "Entry",
            "RunAcrossBoundary");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root);

        InspectionGraphDocument document =
            graph.CrossLibraryCalleeNeighborhood(
                new(
                    maxDepth: 3,
                    maxNodes: 1));

        Assert.Single(document.Nodes);
        Assert.Empty(document.Edges);
        Assert.Empty(document.Occurrences);
        Assert.Equal(
            "RunAcrossBoundary",
            InspectionMember(document.Nodes[0]).Name);
        Assert.Equal(
            InspectionGraphNodeRole.Unclassified,
            document.Nodes[0].Role);
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .TraversalIncomplete));
    }

    [Fact]
    public void CrossLibraryCalleeNeighborhood_OutsideGroupStaysExternal()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath);
        int root = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root);

        InspectionGraphDocument document =
            graph.CrossLibraryCalleeNeighborhood(
                new(
                    maxDepth: 3,
                    maxNodes: 10));

        InspectionGraphEdge edge = Assert.Single(document.Edges);
        InspectionGraphNode target =
            document.Nodes[edge.ToNodeId];
        Assert.Equal("Ping", InspectionMember(target).Name);
        Assert.Equal(
            InspectionGraphNodeRole.External,
            target.Role);
        Assert.Single(document.Occurrences);
    }

    [Fact]
    public void CrossLibraryCalleeNeighborhood_DisclosesCorrespondenceLimits()
    {
        using GraphContext context =
            GraphContext.Create(TargetV2Path, CallerPath);
        int root = MemberToken(
            TargetV2Path,
            "Api",
            "Ping");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root);

        InspectionGraphDocument document =
            graph.CrossLibraryCalleeNeighborhood(
                new(
                    maxDepth: 1,
                    maxNodes: 10));

        var evidence = Assert.IsType<
            CallGraphCorrespondenceIncompleteEvidence>(
                Assert.Single(
                    document.Limits,
                    limit => ReferenceEquals(
                        limit.Descriptor,
                        CallGraphInspectionGraphCatalog
                            .CorrespondenceIncomplete))
                    .Evidence);
        Assert.True(evidence.IncompleteEdgeCount > 0);
    }

    [Fact]
    public void CalleeNeighborhoodRequest_RequiresFiniteValidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MemberCallGraphCalleeNeighborhoodRequest(
                maxDepth: -1,
                maxNodes: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MemberCallGraphCalleeNeighborhoodRequest(
                maxDepth: 1,
                maxNodes: 0));
    }

    [Fact]
    public void AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runTwice = MemberToken(
            CallerPath,
            "Entry",
            "RunTwice");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runTwice,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
            });
        MemberCallGraphView view = graph.Callees();
        Assert.Equal(2, view.FocusCallSites.Length);
        CallGraphProjection projection =
            CallGraphProjection.Create(
                view.CallerRoot,
                view.CalleeRoot);
        Assert.Equal(2, projection.CallSites.Length);
        InspectionGraphDocument inspectionGraph =
            CallGraphInspectionGraphAdapter.Create(projection);
        InspectionGraphEdge inspectionEdge =
            Assert.Single(inspectionGraph.Edges);
        Assert.Equal(2, inspectionEdge.OccurrenceIds.Length);
        Assert.All(
            inspectionGraph.Occurrences,
            occurrence => Assert.IsType<
                CallGraphCallSiteEvidence>(
                    occurrence.Evidence));
        Assert.DoesNotContain(
            inspectionGraph.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .PhysicalOccurrencesUnavailable));

        using var source = MetadataSource.Open(CallerPath);
        AnnotatedMemberDocumentResult result =
            AnnotatedMemberDocumentQuery.Execute(
                new AnnotatedMemberDocumentInput(
                    source,
                    view));

        var complete =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                result);
        AnnotatedMemberDocument document = complete.Document;
        Assert.Equal(CallGraphTier.Callees, document.CallGraph.Tier);
        Assert.Single(document.CallGraph.Projection.Rows);
        Assert.Equal(2, document.CallGraph.Occurrences.Length);
        Assert.All(
            document.CallGraph.Occurrences,
            occurrence => Assert.Equal(1, occurrence.EdgeRow));
        Assert.Equal(
            2,
            document.CallGraph.Occurrences
                .Select(occurrence => occurrence.FactId)
                .Distinct()
                .Count());

        foreach (AnnotatedCallGraphOccurrence occurrence
            in document.CallGraph.Occurrences)
        {
            AnnotatedSourceFact fact =
                document.Source.Facts[occurrence.FactId];
            Assert.Equal(
                ResearchFactRegistry.CallRelationshipDescriptorId,
                fact.Descriptor);
            Assert.Equal(occurrence.ILOffset, fact.SourceOffset);

            AnnotatedSourceNode[] targets =
            [
                .. document.Source.Targets
                    .Where(target =>
                        target.FactId == occurrence.FactId)
                    .Select(target =>
                        document.Source.Nodes[target.NodeId]),
            ];
            Assert.Contains(
                targets,
                node => node.Medium == SourceLineKind.CSharp);
            Assert.Contains(
                targets,
                node => node.Medium == SourceLineKind.Il
                    && node.IlOffset == occurrence.ILOffset);
        }

        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
        Assert.Equal(0, context.Sources[1].OpenCount);
    }

    [Fact]
    public void AnnotatedOwnershipProgressesWithoutReacquiringGraphWork()
    {
        using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentAndReturnThroughHelper");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
            });
        using var source = MetadataSource.Open(OwnershipPath);

        MemberCallGraphView firstView = graph.Callees();
        var first =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(
                        source,
                        firstView)));
        Assert.Empty(first.Document.CallGraph.Ownership.Findings);
        Assert.True(
            first.Document.CallGraph.Ownership.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.BodyUnavailable));
        Assert.True(
            first.Document.CallGraph.Ownership.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.TraversalBoundary));
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);

        MemberCallGraphView fullView = graph.Callers();
        var full =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(
                        source,
                        fullView)));

        Finding<ArrayPoolOwnershipPathWitness> finding =
            Assert.Single(
                full.Document.CallGraph.Ownership.Findings);
        Assert.Equal(
            Analysis.AnalysisFindings.ResourceLifecycleDescriptor,
            finding.Descriptor);
        Assert.Equal(
            Analysis.ArrayPoolOwnershipUseKind.ReturnedToPool,
            finding.Payload.Outcome);
        ArrayPoolOwnershipPathStep step =
            Assert.Single(finding.Payload.Steps);
        Assert.Equal(0, step.CalleeParameterIndex);
        Assert.Contains(
            full.Document.CallGraph.Projection.Rows,
            row => row.Number == step.EdgeRow
                && full.Document.CallGraph.Projection
                    .Nodes[row.Edge.From].Member.Name
                    == "RentAndReturnThroughHelper"
                && full.Document.CallGraph.Projection
                    .Nodes[row.Edge.To].Member.Name
                    == "ReturnRentedArray");
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 1, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
        Assert.Equal(0, context.Sources[1].OpenCount);
    }

    [Theory]
    [InlineData(
        "RentAndForwardToReturn",
        Analysis.ArrayPoolOwnershipUseKind.ReturnedToPool,
        2,
        0)]
    [InlineData(
        "RentAndStoreThroughHelper",
        Analysis.ArrayPoolOwnershipUseKind.Stored,
        1,
        0)]
    [InlineData(
        "RentAndReturnFromHelper",
        Analysis.ArrayPoolOwnershipUseKind.ReturnedToCaller,
        1,
        0)]
    [InlineData(
        "RentAndReturnThroughInstance",
        Analysis.ArrayPoolOwnershipUseKind.ReturnedToPool,
        1,
        1)]
    [InlineData(
        "RentAndReturnThroughConstructor",
        Analysis.ArrayPoolOwnershipUseKind.ReturnedToPool,
        1,
        1)]
    public void AnnotatedOwnershipComposesTypedTerminalPaths(
        string methodName,
        Analysis.ArrayPoolOwnershipUseKind outcome,
        int edgeCount,
        int firstCalleeParameterIndex)
    {
        using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(OwnershipPath, "Entry", methodName);
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
            });
        MemberCallGraphView view = graph.Callers();
        using var source = MetadataSource.Open(OwnershipPath);

        var complete =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(source, view)));

        Finding<ArrayPoolOwnershipPathWitness> finding =
            Assert.Single(
                complete.Document.CallGraph.Ownership.Findings);
        Assert.Equal(outcome, finding.Payload.Outcome);
        Assert.Equal(edgeCount, finding.Payload.Steps.Length);
        Assert.Equal(
            firstCalleeParameterIndex,
            finding.Payload.Steps[0].CalleeParameterIndex);
        Assert.Equal(
            finding.Payload.Steps.Select(step => step.EdgeRow),
            finding.Payload.EdgeRows);
        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
    }

    [Fact]
    public void OwnershipWitnessBudgetPreservesPhysicalCallIdentity()
    {
        using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentAndReturnAtTwoSites");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
            });
        MemberCallGraphView view = graph.Callers();
        CallGraphProjection projection =
            CallGraphProjection.Create(
                view.CallerRoot,
                view.CalleeRoot);

        AnnotatedCallGraphOwnershipInspection all =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                projection);
        Finding<ArrayPoolOwnershipPathWitness>[] findings =
            [.. all.Findings];
        Assert.Equal(2, findings.Length);
        Assert.Equal(
            findings[0].Payload.Steps[0].EdgeRow,
            findings[1].Payload.Steps[0].EdgeRow);
        Assert.NotEqual(
            findings[0].Payload.Steps[0].ILOffset,
            findings[1].Payload.Steps[0].ILOffset);
        Assert.NotEqual(findings[0].Key, findings[1].Key);

        AnnotatedCallGraphOwnershipInspection bounded =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                projection,
                new ArrayPoolOwnershipSearchOptions
                {
                    MaxWitnesses = 1,
                });
        Assert.Single(bounded.Findings);
        Assert.True(
            bounded.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.WitnessBudget));
    }

    [Fact]
    public void OwnershipPathBudgetLeavesForwardedPathIncomplete()
    {
        using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentAndForwardToReturn");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
            });
        MemberCallGraphView view = graph.Callers();

        AnnotatedCallGraphOwnershipInspection result =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot),
                new ArrayPoolOwnershipSearchOptions
                {
                    MaxPaths = 1,
                });

        Assert.Empty(result.Findings);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.PathBudget));
    }

    [Fact]
    public void OwnershipForwardedToABodilessCalleeIsIncomplete()
    {
        using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentAndForwardExternally");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
            });
        MemberCallGraphView view = graph.Callers();

        AnnotatedCallGraphOwnershipInspection result =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot));

        Assert.Empty(result.Findings);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.BodyUnavailable));
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.TraversalBoundary));
    }

    [Theory]
    [InlineData("RentWithMethodGroup")]
    [InlineData("RentWithFunctionPointer")]
    public void OwnershipIndirectCallShapesDoNotProduceSafeFindings(
        string methodName)
    {
        using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(OwnershipPath, "Entry", methodName);
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
            });
        MemberCallGraphView view = graph.Callers();

        AnnotatedCallGraphOwnershipInspection result =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot));

        Assert.Empty(result.Findings);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));
    }

    [Fact]
    public void AnnotatedMemberDocument_ReportsOneCycleForRepeatedRecursiveCalls()
    {
        using GraphContext context =
            GraphContext.Create(TargetPath, CallerPath);
        int recurseTwice = MemberToken(
            TargetPath,
            "InstanceRecursionApi",
            "RecurseTwice");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            recurseTwice,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
            });
        MemberCallGraphView view = graph.Callees();
        Assert.Equal(2, view.FocusCallSites.Length);

        using var source = MetadataSource.Open(TargetPath);
        var complete =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(
                        source,
                        view)));

        AnnotatedCallGraphOverlay overlay =
            complete.Document.CallGraph;
        Finding<CallGraphCycleWitness> cycle =
            Assert.Single(overlay.Cycles.Findings);
        Assert.Equal(
            AnnotatedCallGraphCycleLimit.None,
            overlay.Cycles.Limits);
        Assert.True(cycle.Payload.IsDirect);
        Assert.Single(cycle.Payload.EdgeRows);
        Assert.Equal(2, overlay.Occurrences.Length);
        Assert.All(
            overlay.Occurrences,
            occurrence => Assert.Equal(
                cycle.Payload.EdgeRows[0],
                occurrence.EdgeRow));
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
        Assert.Equal(0, context.Sources[1].OpenCount);
    }

    [Fact]
    public void
        AnnotatedMemberDocument_DoesNotMergeGeneratedBodyOffsets()
    {
        string path =
            typeof(MemberCallGraphSession).Assembly.Location;
        int selectIds = MemberToken(
            path,
            "ApiInventoryQuery",
            "SelectIds");
        using GraphContext context =
            GraphContext.Create(path);
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            selectIds,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures
                        .MethodEvidence,
            });
        MemberCallGraphView view = graph.Callees();

        Assert.NotEmpty(view.FocusCallSites);
        Assert.All(
            view.FocusCallSites,
            call => Assert.Equal(
                selectIds,
                call.EvidenceMethod.MetadataToken));
        using var source = MetadataSource.Open(path);
        Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
            AnnotatedMemberDocumentQuery.Execute(
                new AnnotatedMemberDocumentInput(
                    source,
                    view)));
    }

    [Fact]
    public void AnnotatedMemberDocument_ReportsAMutualCycleAtTheCallerTier()
    {
        using GraphContext context =
            GraphContext.Create(TargetPath, CallerPath);
        int isEven = MemberToken(
            TargetPath,
            "InstanceRecursionApi",
            "IsEven");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            isEven,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
            });
        MemberCallGraphView view = graph.Callers();

        using var source = MetadataSource.Open(TargetPath);
        var complete =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(
                        source,
                        view)));

        AnnotatedCallGraphOverlay overlay =
            complete.Document.CallGraph;
        Finding<CallGraphCycleWitness> cycle =
            Assert.Single(overlay.Cycles.Findings);
        Assert.Equal(CallGraphTier.Callers, overlay.Tier);
        Assert.Equal(
            AnnotatedCallGraphCycleLimit.None,
            overlay.Cycles.Limits);
        Assert.False(cycle.Payload.IsDirect);
        Assert.Equal(2, cycle.Payload.EdgeRows.Length);
        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
        Assert.Equal(0, context.Sources[1].OpenCount);
    }

    [Fact]
    public void CycleFindingSurvivesUnrelatedGraphAndCorrespondenceLimits()
    {
        Analysis.MemberRef focus =
            GraphMember("Recursive", "Run");
        var root = new Analysis.CallTreeNode(
            focus,
            null,
            Analysis.CallTreeStatus.Expanded,
            [
                new Analysis.CallTreeNode(
                    focus,
                    null,
                    Analysis.CallTreeStatus.AlreadyShown,
                    []),
                new Analysis.CallTreeNode(
                    GraphMember("Boundary", "Unknown"),
                    null,
                    Analysis.CallTreeStatus.Truncated,
                    []),
            ]);
        var view = new MemberCallGraphView(
            CallGraphTier.Callees,
            root,
            CallerRoot: null)
        {
            FocusModuleVersionId =
                new Guid("11111111-1111-1111-1111-111111111111"),
            FocusMethodToken = 0x06000001,
            Diagnostics = new Analysis.CatalogCallGraphDiagnostics(
                IncompleteNodeCount: 1,
                IncompleteEdgeCount: 0,
                BindingIdentityConflictCount: 0),
        };
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(root);

        AnnotatedCallGraphCycleInspection result =
            CallGraphCycleFindings.Inspect(
                view,
                projection);

        Assert.Single(result.Findings);
        Assert.Equal(
            AnnotatedCallGraphCycleLimit.TraversalBoundary
                | AnnotatedCallGraphCycleLimit
                    .IncompleteCorrespondence,
            result.Limits);
    }

    [Fact]
    public void CycleFindingSurvivesAnExplicitBodyAnalysisFailure()
    {
        Analysis.MemberRef focus =
            GraphMember("Recursive", "Run");
        var root = new Analysis.CallTreeNode(
            focus,
            null,
            Analysis.CallTreeStatus.Expanded,
            [
                new Analysis.CallTreeNode(
                    focus,
                    null,
                    Analysis.CallTreeStatus.AlreadyShown,
                    []),
                new Analysis.CallTreeNode(
                    GraphMember("Failed", "Decode"),
                    null,
                    Analysis.CallTreeStatus.AnalysisIncomplete,
                    [])
                {
                    Diagnostic = new Analysis.AnalysisDiagnostic(
                        0x06000002,
                        "Failed.Decode",
                        "BadImageFormatException: invalid body"),
                },
            ]);
        var view = new MemberCallGraphView(
            CallGraphTier.Callees,
            root,
            CallerRoot: null)
        {
            FocusModuleVersionId =
                new Guid("11111111-1111-1111-1111-111111111111"),
            FocusMethodToken = 0x06000001,
        };
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(root);

        AnnotatedCallGraphCycleInspection result =
            CallGraphCycleFindings.Inspect(
                view,
                projection);

        Assert.Single(result.Findings);
        Assert.Equal(
            AnnotatedCallGraphCycleLimit.TraversalBoundary
                | AnnotatedCallGraphCycleLimit.AnalysisFailure,
            result.Limits);
    }

    [Fact]
    public void CycleFindingIdentityDoesNotDependOnEdgeRowNumbers()
    {
        Analysis.MemberRef focus =
            GraphMember("Recursive", "Run");
        Analysis.CallTreeNode firstRoot =
            new(
                focus,
                null,
                Analysis.CallTreeStatus.Expanded,
                [
                    new Analysis.CallTreeNode(
                        focus,
                        null,
                        Analysis.CallTreeStatus.AlreadyShown,
                        []),
                ]);
        Analysis.CallTreeNode shiftedRoot =
            new(
                focus,
                null,
                Analysis.CallTreeStatus.Expanded,
                [
                    new Analysis.CallTreeNode(
                        GraphMember("Other", "Call"),
                        null,
                        Analysis.CallTreeStatus.Leaf,
                        []),
                    new Analysis.CallTreeNode(
                        focus,
                        null,
                        Analysis.CallTreeStatus.AlreadyShown,
                        []),
                ]);
        var view = new MemberCallGraphView(
            CallGraphTier.Callees,
            firstRoot,
            CallerRoot: null)
        {
            FocusModuleVersionId =
                new Guid("11111111-1111-1111-1111-111111111111"),
            FocusMethodToken = 0x06000001,
        };

        Finding<CallGraphCycleWitness> first =
            Assert.Single(
                CallGraphCycleFindings.Inspect(
                    view,
                    CallGraphProjection.FromCallees(firstRoot))
                    .Findings);
        Finding<CallGraphCycleWitness> shifted =
            Assert.Single(
                CallGraphCycleFindings.Inspect(
                    view with { CalleeRoot = shiftedRoot },
                    CallGraphProjection.FromCallees(shiftedRoot))
                    .Findings);

        Assert.Equal([1], first.Payload.EdgeRows);
        Assert.Equal([2], shifted.Payload.EdgeRows);
        Assert.Equal(first.Key, shifted.Key);
    }

    [Fact]
    public void AnnotatedMemberDocument_RejectsSourceFromAnotherModule()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runTwice = MemberToken(
            CallerPath,
            "Entry",
            "RunTwice");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runTwice);
        MemberCallGraphView view = graph.Callees();

        using var source = MetadataSource.Open(TargetPath);
        AnnotatedMemberDocumentResult result =
            AnnotatedMemberDocumentQuery.Execute(
                new AnnotatedMemberDocumentInput(
                    source,
                    view));

        Assert.IsType<AnnotatedMemberDocumentResult.Failed>(result);
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
    }

    [Fact]
    public void AnnotatedMemberDocument_HonorsACalleeNodeBudget()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runTwice = MemberToken(
            CallerPath,
            "Entry",
            "RunTwice");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runTwice,
            new MemberCallGraphOptions
            {
                MaxNodes = 1,
            });
        MemberCallGraphView view = graph.Callees();
        Assert.Equal(
            Analysis.CallTreeStatus.Truncated,
            view.CalleeRoot!.Status);

        using var source = MetadataSource.Open(CallerPath);
        var complete =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(
                        source,
                        view)));

        Assert.Empty(complete.Document.CallGraph.Occurrences);
        Assert.DoesNotContain(
            complete.Document.Source.Facts,
            fact => fact.Descriptor
                == ResearchFactRegistry.CallRelationshipDescriptorId);
        Assert.Empty(
            complete.Document.CallGraph.Cycles.Findings);
        Assert.Equal(
            AnnotatedCallGraphCycleLimit.TraversalBoundary,
            complete.Document.CallGraph.Cycles.Limits);
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
    }

    [Fact]
    public void BodilessFocus_ProducesEveryProgressiveTier()
    {
        using GraphContext context =
            GraphContext.Create(TargetPath, CallerPath);
        Analysis.MethodIdentity focus =
            Analysis.LibraryBodyIndex.Open(TargetPath)
                .DeclaredMethods
                .First(method =>
                    method.DeclaringType.Name == "IBodilessApi"
                    && method.Name == "Invoke");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            focus.MetadataToken);

        MemberCallGraphView[] views =
        [
            graph.Callees(),
            graph.Callers(),
            graph.CrossLibrary(),
        ];

        Assert.All(
            views,
            view =>
            {
                Assert.Equal(
                    focus.ModuleVersionId,
                    view.FocusModuleVersionId);
                Assert.Equal(
                    focus.MetadataToken,
                    view.FocusMethodToken);
                Assert.Empty(view.FocusCallSites);
                Assert.Equal(
                    Analysis.CallTreeStatus.Bodiless,
                    view.CalleeRoot!.Status);
                Assert.True(
                    CallGraphProjection.FromCallees(
                        view.CalleeRoot)
                        .HasUnexploredTraversalBoundary);
            });
    }

    [Fact]
    public void DirectFullTier_SkipsScopedAndLaterCalleesReusesFull()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView crossLibrary = graph.CrossLibrary();
        MemberCallGraphView callees = graph.Callees();

        Assert.NotNull(crossLibrary.CallerRoot);
        Analysis.CallTreeNode run = Child(callees.CalleeRoot!, "Run");
        Assert.Contains(
            run.Children,
            child => child.Member.Name == "Ping");
        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public void Tiers_ShareSnapshotsAndBuildEachIndexAtMostOnce()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView[] first = [.. graph.Tiers()];
        MemberCallGraphView[] second = [.. graph.Tiers()];

        Assert.Equal(
            [
                CallGraphTier.Callees,
                CallGraphTier.Callers,
                CallGraphTier.CrossLibrary,
            ],
            first.Select(view => view.Tier));
        Assert.Equal(
            first.Select(view => view.Tier),
            second.Select(view => view.Tier));
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));

        Analysis.CallTreeNode ping =
            Child(Child(first[2].CalleeRoot!, "Run"), "Ping");
        Assert.Equal(TargetAssemblyName(), ping.Perf?.Source);
    }

    [Fact]
    public void DuplicateImages_BuildOneCrossLibraryIndex()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            run);

        _ = graph.CrossLibrary();

        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public void StreamOnlyParticipants_CanBuildCrossLibraryGraph()
    {
        using GraphContext context =
            GraphContext.CreateStreamOnly(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);

        MemberCallGraphView view = graph.CrossLibrary();

        Analysis.CallTreeNode ping =
            Child(Child(view.CalleeRoot!, "Run"), "Ping");
        Assert.Equal(TargetAssemblyName(), ping.Perf?.Source);
        Assert.All(context.Sources, source => Assert.Null(source.Assembly.Path));
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public void CrossLibrary_AcquisitionFailureIsTypedAndCached()
    {
        using GraphContext context =
            GraphContext.CreateWithFailingParticipant(
                CallerPath,
                TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            run);

        MemberCallGraphAcquisitionException first =
            Assert.Throws<MemberCallGraphAcquisitionException>(
                graph.CrossLibrary);
        MemberCallGraphAcquisitionException second =
            Assert.Throws<MemberCallGraphAcquisitionException>(
                graph.CrossLibrary);

        Assert.IsType<MemberCallGraphAcquisitionFailure.Rejected>(
            Assert.Single(first.Failures));
        Assert.IsType<MemberCallGraphAcquisitionFailure.Rejected>(
            Assert.Single(second.Failures));
        Assert.Equal(1, context.Sources[1].OpenCount);
        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 0),
            graph.BuildCounts);
    }

    [Fact]
    public void MalformedMetadata_IsTypedAndCached()
    {
        byte[] image = BuildMalformedMethodListImage();
        int openCount = 0;
        var assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "MalformedMethodList",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            openRead: () =>
            {
                Interlocked.Increment(ref openCount);
                return new MemoryStream(image, writable: false);
            },
            AssemblyResolutionProvenance.Local(
                "malformed call-graph test image"));
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        assembly,
                        MissingBindingPolicy.Instance),
                ]);
        using var graph = new MemberCallGraphSession(
            group,
            assembly,
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(1)));

        MemberCallGraphAcquisitionException first =
            Assert.Throws<MemberCallGraphAcquisitionException>(
                graph.Callers);
        MemberCallGraphAcquisitionException second =
            Assert.Throws<MemberCallGraphAcquisitionException>(
                graph.Callers);

        var failure =
            Assert.IsType<MemberCallGraphAcquisitionFailure.InvalidImage>(
                Assert.Single(first.Failures));
        Assert.IsType<BadImageFormatException>(failure.Error);
        Assert.IsType<MemberCallGraphAcquisitionFailure.InvalidImage>(
            Assert.Single(second.Failures));
        Assert.Equal(1, openCount);
        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 0),
            graph.BuildCounts);
    }

    [Fact]
    public void InvalidImageClassification_CoversMetadataDecoderExceptions()
    {
        Assert.All(
            new Exception[]
            {
                new BadImageFormatException(),
                new ArgumentOutOfRangeException(),
                new OverflowException(),
            },
            exception => Assert.True(
                MemberCallGraphSession.IsInvalidImageException(
                    exception)));
        Assert.False(
            MemberCallGraphSession.IsInvalidImageException(
                new InvalidOperationException()));
    }

    [Fact]
    public void WorkspaceDisposal_DisposesOwnedGraphBeforeSnapshots()
    {
        GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            run);
        _ = graph.CrossLibrary();
        Analysis.CatalogCallGraphScope catalogScope =
            Assert.IsType<Analysis.CatalogCallGraphScope>(
                graph.CatalogScope);
        Assert.True(context.Group.RetainedImageBytes > 0);

        context.Workspace.Dispose();

        Assert.Equal(0, context.Group.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(graph.Callees);
        Assert.Throws<ObjectDisposedException>(catalogScope.ReleaseGraph);
        graph.Dispose();
    }

    [Fact]
    public void OptionsRejectFeatureSetsThatCannotProduceScopedGraph()
    {
        using GraphContext context = GraphContext.Create(CallerPath);
        int run = MemberToken(CallerPath, "Entry", "Run");

        Assert.Throws<ArgumentException>(
            () => new MemberCallGraphSession(
                context.Group,
                context.Sources[0].Assembly,
                run,
                new()
                {
                    Features =
                        Analysis.LibraryBodyAnalysisFeatures.None,
                }));
        Assert.Throws<ArgumentException>(
            () => new MemberCallGraphSession(
                context.Group,
                context.Sources[0].Assembly,
                run,
                new()
                {
                    Features =
                        Analysis.LibraryBodyAnalysisFeatures.LeakTriage,
                }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MemberCallGraphSession(
                context.Group,
                context.Sources[0].Assembly,
                run,
                new()
                {
                    Features =
                        Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                        | (Analysis.LibraryBodyAnalysisFeatures)(1 << 20),
                }));
        Assert.Equal(0, context.Sources[0].OpenCount);
    }

    [Fact]
    public void CrossLibrary_VersionSkewRetainsIncompleteDiagnostics()
    {
        using GraphContext context =
            GraphContext.Create(TargetV2Path, CallerPath);
        int ping = MemberToken(TargetV2Path, "Api", "Ping");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            ping);

        MemberCallGraphView view = graph.CrossLibrary();

        Assert.DoesNotContain(
            view.CallerRoot!.Children,
            child => child.Member.Name == "Run");
        Assert.True(view.Diagnostics.IsIncomplete);
        Assert.True(view.Diagnostics.IncompleteEdgeCount > 0);
    }

    [Fact]
    public void Projection_DoesNotAcquireOrBuildMoreIndexes()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int run = MemberToken(CallerPath, "Entry", "Run");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            run);
        MemberCallGraphView view = graph.CrossLibrary();
        MemberCallGraphBuildCounts before = graph.BuildCounts;

        CallGraphProjection first = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);
        CallGraphProjection second = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);

        Assert.NotEmpty(first.Nodes);
        Assert.Equal(
            first.Nodes.Select(node => (node.Id, node.Label, node.Kind)),
            second.Nodes.Select(node => (node.Id, node.Label, node.Kind)));
        Assert.Equal(first.Edges.Length, second.Edges.Length);
        Assert.Equal(
            InspectionGraphDocumentScope.SessionBound,
            CallGraphInspectionGraphAdapter.Create(first).Scope);
        Assert.Equal(before, graph.BuildCounts);
        Assert.All(
            context.Sources,
            source => Assert.Equal(1, source.OpenCount));
    }

    [Fact]
    public async Task RunAsync_RaisesLayersInOrderAndCompletes()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);
        var layers = new List<CallGraphTier>();
        int completed = 0;
        graph.LayerReady += (_, view) => layers.Add(view.Tier);
        graph.Completed += (_, _) => completed++;

        await graph.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                CallGraphTier.Callees,
                CallGraphTier.Callers,
                CallGraphTier.CrossLibrary,
            ],
            layers);
        Assert.Equal(1, completed);
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 1, 1),
            graph.BuildCounts);
    }

    [Fact]
    public void RunAsync_CancellationAfterFirstLayerSkipsFullBuild()
    {
        using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int runOuter = MemberToken(CallerPath, "Entry", "RunOuter");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            runOuter);
        using var cancellation = new CancellationTokenSource();
        var layers = new List<CallGraphTier>();
        graph.LayerReady += (_, view) =>
        {
            layers.Add(view.Tier);
            cancellation.Cancel();
        };

        Task task = graph.RunAsync(cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(
            () => task.GetAwaiter().GetResult());
        Assert.Equal([CallGraphTier.Callees], layers);
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 0, 0),
            graph.BuildCounts);
    }

    static byte[] BuildMalformedMethodListImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("MalformedMethodList.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedMethodList"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            default,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Broken"),
            baseType: MetadataTokens.TypeDefinitionHandle(3),
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class GraphContext : IDisposable
    {
        GraphContext(
            InspectionWorkspace workspace,
            AssemblyContextGroup group,
            TestSource[] sources)
        {
            Workspace = workspace;
            Group = group;
            Sources = sources;
        }

        internal InspectionWorkspace Workspace { get; }
        internal AssemblyContextGroup Group { get; }
        internal TestSource[] Sources { get; }

        internal static GraphContext Create(params string[] paths) =>
            CreateCore(streamOnly: false, failingIndex: null, paths);

        internal static GraphContext CreateStreamOnly(
            params string[] paths) =>
            CreateCore(streamOnly: true, failingIndex: null, paths);

        internal static GraphContext CreateWithFailingParticipant(
            params string[] paths) =>
            CreateCore(streamOnly: false, failingIndex: 1, paths);

        static GraphContext CreateCore(
            bool streamOnly,
            int? failingIndex,
            params string[] paths)
        {
            TestSource[] sources = paths
                .Select(
                    (path, index) => TestSource.Create(
                        path,
                        streamOnly,
                        failingIndex == index))
                .ToArray();
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                    sources.Select(source => (
                        source.Assembly,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(
                                    source.SourcePath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                }))));
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    sources.Select(
                        source => new AssemblyContextParticipant(
                            source.Assembly,
                            policy)));
            return new(workspace, group, sources);
        }

        public void Dispose() => Workspace.Dispose();
    }

    sealed class TestSource
    {
        int _openCount;
        readonly byte[]? _content;
        readonly bool _fails;

        TestSource(
            string sourcePath,
            ResolvedAssemblyReference assembly,
            byte[]? content,
            bool fails)
        {
            SourcePath = sourcePath;
            Assembly = assembly;
            _content = content;
            _fails = fails;
        }

        internal string SourcePath { get; }
        internal ResolvedAssemblyReference Assembly { get; }
        internal int OpenCount => Volatile.Read(ref _openCount);

        internal static TestSource Create(
            string sourcePath,
            bool streamOnly,
            bool fails)
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    sourcePath,
                    AssemblyResolutionProvenance.Local(
                        "progressive graph test source"));
            byte[]? content =
                streamOnly ? File.ReadAllBytes(sourcePath) : null;
            TestSource? testSource = null;
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    source.Identity,
                    streamOnly ? null : sourcePath,
                    () => testSource!.Open(),
                    source.Provenance,
                    source.LastWriteTimeUtc);
            testSource = new TestSource(
                sourcePath,
                assembly,
                content,
                fails);
            return testSource;
        }

        Stream Open()
        {
            Interlocked.Increment(ref _openCount);
            if (_fails)
                throw new IOException("Synthetic graph participant failure.");
            return _content is null
                ? File.OpenRead(SourcePath)
                : new MemoryStream(_content, writable: false);
        }
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        internal static MissingBindingPolicy Instance { get; } =
            new();

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.NotFound();
        }
    }
}
