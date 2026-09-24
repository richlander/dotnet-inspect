using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;
using InertText;
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

    static string ExternalFocusRole(
        InspectionGraphDocument document,
        InspectionGraphEdge edge) =>
        Assert.IsType<InspectionGraphValue.Token>(
            Assert.Single(
                document.Characteristics,
                characteristic =>
                    ReferenceEquals(
                        characteristic.Descriptor,
                        ExternalFocusedCallGraphInspectionCatalog
                            .EdgeRole)
                    && characteristic.Target
                        == InspectionGraphTarget.Edge(edge.Id))
                .Value)
            .Value;

    [Fact]
    public async Task Callees_ScopedFirstPaint_BuildsScopedIndexOnly()
    {
        await using GraphContext context =
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
    public async Task Callees_ScopedFirstPaint_MarksInAssemblyCalleeBounded()
    {
        await using GraphContext context =
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
    public async Task CrossLibraryCalleeNeighborhood_RetainsBoundaryAndOmitsExternalContinuation()
    {
        await using GraphContext context =
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
            ],
            document.Edges.Select(edge =>
                (
                    InspectionMember(
                        document.Nodes[edge.FromNodeId]).Name,
                    InspectionMember(
                        document.Nodes[edge.ToNodeId]).Name)));
        Assert.Equal(
            "boundary",
            ExternalFocusRole(
                document,
                Assert.Single(document.Edges)));
        Assert.Single(document.Occurrences);
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
    public async Task CrossLibraryCalleeNeighborhood_RetainsShortestLocalConnector()
    {
        await using GraphContext context =
            GraphContext.Create(CallerPath, TargetPath);
        int root = MemberToken(
            CallerPath,
            "Entry",
            "RunOuter");
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
            [
                ("RunOuter", "Run", "connector"),
                ("Run", "Ping", "boundary"),
            ],
            document.Edges.Select(edge =>
                (
                    InspectionMember(
                        document.Nodes[edge.FromNodeId]).Name,
                    InspectionMember(
                        document.Nodes[edge.ToNodeId]).Name,
                    ExternalFocusRole(document, edge))));
        Assert.Equal(2, document.Occurrences.Length);
        Assert.All(
            document.Occurrences,
            occurrence => Assert.IsType<
                CallGraphCallSiteEvidence>(
                    occurrence.Evidence));
    }

    [Fact]
    public async Task CrossLibraryCalleeNeighborhood_DepthBoundStopsAfterBoundary()
    {
        await using GraphContext context =
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
    public async Task CrossLibraryCalleeNeighborhood_ZeroDepthRetainsOnlySeed()
    {
        await using GraphContext context =
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
    public async Task CrossLibraryCalleeNeighborhood_NodeBoundRetainsOnlySeed()
    {
        await using GraphContext context =
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
    public async Task CrossLibraryCalleeNeighborhood_OutsideGroupIsUnclassifiedBoundary()
    {
        await using GraphContext context =
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
        Assert.Equal(
            "unclassified-boundary",
            ExternalFocusRole(document, edge));
        InspectionGraphLimit limit = Assert.Single(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                ExternalFocusedCallGraphInspectionCatalog
                    .BoundaryClassificationIncomplete));
        Assert.Equal(
            InspectionGraphTarget.Edge(edge.Id),
            limit.Target);
    }

    [Fact]
    public async Task CrossLibraryCalleeNeighborhood_VersionSkewIsNotAnExactParticipant()
    {
        await using GraphContext context =
            GraphContext.Create(CallerPath, TargetV2Path);
        int root = MemberToken(
            CallerPath,
            "Entry",
            "Run");
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
            "unclassified-boundary",
            ExternalFocusRole(document, edge));
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                ExternalFocusedCallGraphInspectionCatalog
                    .BoundaryClassificationIncomplete));
        Assert.DoesNotContain(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .CorrespondenceIncomplete));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CrossLibraryCalleeNeighborhood_EquivalentIdentitySpellingMatchesGeneration(
        int equivalentIdentityIndex)
    {
        await using GraphContext context =
            GraphContext.CreateWithEquivalentIdentity(
                equivalentIdentityIndex,
                CallerPath,
                TargetPath);
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

        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal(
            ("RunAcrossBoundary", "Forward"),
            (
                InspectionMember(
                    document.Nodes[edge.FromNodeId]).Name,
                InspectionMember(
                    document.Nodes[edge.ToNodeId]).Name));
        Assert.Equal(
            "boundary",
            ExternalFocusRole(document, edge));
        Assert.DoesNotContain(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                ExternalFocusedCallGraphInspectionCatalog
                    .BoundaryClassificationIncomplete));
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
    public async Task AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite()
    {
        await using GraphContext context =
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
    public async Task AnnotatedOwnershipProgressesWithoutReacquiringGraphWork()
    {
        await using GraphContext context =
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
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
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

        Finding<ResourceOwnershipPathWitness> finding =
            Assert.Single(
                full.Document.CallGraph.Ownership.Findings);
        Assert.Equal(
            Analysis.AnalysisFindings.ResourceLifecycleDescriptor,
            finding.Descriptor);
        Assert.Equal(
            ResourceOwnershipPathOutcome.Released,
            finding.Payload.Outcome);
        ResourceOwnershipPathStep step =
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

        MemberCallGraphView crossLibraryView = graph.CrossLibrary();
        var crossLibrary =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(
                        source,
                        crossLibraryView)));
        Finding<ResourceOwnershipPathWitness> crossLibraryFinding =
            Assert.Single(
                crossLibrary.Document.CallGraph.Ownership.Findings);
        Assert.Equal(finding.Key, crossLibraryFinding.Key);
        Assert.Equal(
            ResourceOwnershipPathOutcome.Released,
            crossLibraryFinding.Payload.Outcome);
        Assert.True(crossLibraryFinding.Payload.IsComplete);
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 1, 1),
            graph.BuildCounts);
        Assert.All(
            context.Sources,
            participant => Assert.Equal(1, participant.OpenCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceEffectsReuseParticipantSnapshotsAcrossProgression(
        bool targetIsDesignated)
    {
        await using GraphContext context =
            targetIsDesignated
                ? GraphContext.CreateWithDesignatedParticipant(
                    1,
                    CallerPath,
                    TargetPath)
                : GraphContext.Create(CallerPath, TargetPath);
        int root = MemberToken(
            CallerPath,
            "Entry",
            "UseEcho");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
                ResourceEffects = CrossParticipantAcquisition(),
            });

        Assert.All(
            context.Sources,
            participant => Assert.Equal(0, participant.OpenCount));

        MemberCallGraphView callees = graph.Callees();

        Analysis.ResourceOwnershipMethodSummary rootSummary =
            Assert.Single(
            callees.ResourceOwnershipSummaries,
            summary => summary.Method.MetadataToken == root);
        Assert.Single(rootSummary.Acquisitions);
        int[] callerTierOpenCounts =
            targetIsDesignated ? [1, 0] : [1, 1];
        Assert.Equal(
            callerTierOpenCounts,
            context.Sources.Select(participant => participant.OpenCount));

        _ = graph.Callers();

        Assert.Equal(
            callerTierOpenCounts,
            context.Sources.Select(participant => participant.OpenCount));

        _ = graph.CrossLibrary();

        Assert.Equal(
            [1, 1],
            context.Sources.Select(participant => participant.OpenCount));
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 1, 1),
            graph.BuildCounts);
    }

    [Fact]
    public async Task ResourceEffectsReuseAmbiguousParticipantSnapshots()
    {
        int root = MemberToken(
            CallerPath,
            "Entry",
            "UseEcho");
        await using (GraphContext baseline =
            GraphContext.CreateWithAmbiguousDesignatedParticipants(
                [1, 2],
                CallerPath,
                TargetPath,
                TargetPath))
        {
            using var baselineGraph = new MemberCallGraphSession(
                baseline.Group,
                baseline.Sources[0].Assembly,
                root,
                new MemberCallGraphOptions
                {
                    Features =
                        Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                        | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
                });

            _ = baselineGraph.Callees();
            Assert.Equal(
                [1, 0, 0],
                baseline.Sources.Select(participant => participant.OpenCount));
            _ = baselineGraph.Callers();
            Assert.Equal(
                [1, 0, 0],
                baseline.Sources.Select(participant => participant.OpenCount));
            _ = baselineGraph.CrossLibrary();
            Assert.Equal(
                [1, 1, 2],
                baseline.Sources.Select(participant => participant.OpenCount));
        }

        await using GraphContext context =
            GraphContext.CreateWithAmbiguousDesignatedParticipants(
                [1, 2],
                CallerPath,
                TargetPath,
                TargetPath);
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
                ResourceEffects = CrossParticipantAcquisition(),
            });

        Assert.Equal(
            [0, 0, 0],
            context.Sources.Select(participant => participant.OpenCount));

        MemberCallGraphView callees = graph.Callees();
        Analysis.ResourceOwnershipMethodSummary rootSummary =
            Assert.Single(
                callees.ResourceOwnershipSummaries,
                summary => summary.Method.MetadataToken == root);

        Assert.True(rootSummary.IsComplete);
        Assert.False(callees.ResourceOwnershipPublicationComplete);
        Assert.Equal(
            [1, 1, 1],
            context.Sources.Select(participant => participant.OpenCount));

        _ = graph.Callers();

        Assert.Equal(
            [1, 1, 1],
            context.Sources.Select(participant => participant.OpenCount));

        _ = graph.CrossLibrary();

        Assert.Equal(
            [1, 1, 2],
            context.Sources.Select(participant => participant.OpenCount));
        Assert.Equal(
            new MemberCallGraphBuildCounts(1, 1, 1),
            graph.BuildCounts);
    }

    [Theory]
    [InlineData(
        "RentAndForwardToReturn",
        ResourceOwnershipPathOutcome.Released,
        2,
        0)]
    [InlineData(
        "RentAndReturnThroughGenericHelper",
        ResourceOwnershipPathOutcome.Released,
        1,
        0)]
    [InlineData(
        "RentAndReturnThroughNestedGenericHelpers",
        ResourceOwnershipPathOutcome.Released,
        2,
        0)]
    [InlineData(
        "RentAndReturnCompoundThroughGenericHelper",
        ResourceOwnershipPathOutcome.Released,
        1,
        0)]
    [InlineData(
        "RentAndReturnCompoundThroughNestedGenericHelpers",
        ResourceOwnershipPathOutcome.Released,
        2,
        0)]
    [InlineData(
        "RentAndReturnNamedCompoundThroughGenericHelper",
        ResourceOwnershipPathOutcome.Released,
        1,
        0)]
    [InlineData(
        "RentAndReturnNamedCompoundThroughNestedGenericHelpers",
        ResourceOwnershipPathOutcome.Released,
        2,
        0)]
    [InlineData(
        "RentAndReturnConstructedCompoundThroughGenericHelper",
        ResourceOwnershipPathOutcome.Released,
        2,
        0)]
    [InlineData(
        "RentAndReturnNamedConstructedCompoundThroughGenericHelper",
        ResourceOwnershipPathOutcome.Released,
        2,
        0)]
    [InlineData(
        "RentAndStoreThroughHelper",
        ResourceOwnershipPathOutcome.Stored,
        1,
        0)]
    [InlineData(
        "RentAndReturnFromHelper",
        ResourceOwnershipPathOutcome.ReturnedToCaller,
        1,
        0)]
    [InlineData(
        "RentAndReturnThroughInstance",
        ResourceOwnershipPathOutcome.Released,
        1,
        1)]
    [InlineData(
        "RentAndReturnThroughConstructor",
        ResourceOwnershipPathOutcome.Released,
        1,
        1)]
    public async Task AnnotatedOwnershipComposesTypedTerminalPaths(
        string methodName,
        ResourceOwnershipPathOutcome outcome,
        int edgeCount,
        int firstCalleeParameterIndex)
    {
        await using GraphContext context =
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
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();
        using var source = MetadataSource.Open(OwnershipPath);

        var complete =
            Assert.IsType<AnnotatedMemberDocumentResult.Complete>(
                AnnotatedMemberDocumentQuery.Execute(
                    new AnnotatedMemberDocumentInput(source, view)));

        Finding<ResourceOwnershipPathWitness> finding =
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
        AnnotatedCallGraphOwnershipInspection legacy =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                complete.Document.CallGraph.Projection);
        Finding<ArrayPoolOwnershipPathWitness> legacyFinding =
            Assert.Single(legacy.Findings);
        Assert.Equal(
            finding.Payload.Steps.Select(step =>
                (
                    step.EdgeRow,
                    step.CallerModuleVersionId,
                    step.CallerMethodToken,
                    step.ILOffset,
                    step.OperandToken,
                    step.CalleeParameterIndex)),
            legacyFinding.Payload.Steps.Select(step =>
                (
                    step.EdgeRow,
                    step.CallerModuleVersionId,
                    step.CallerMethodToken,
                    step.ILOffset,
                    step.OperandToken,
                    step.CalleeParameterIndex)));
        Assert.Equal(
            outcome switch
            {
                ResourceOwnershipPathOutcome.Released =>
                    Analysis.ArrayPoolOwnershipUseKind.ReturnedToPool,
                ResourceOwnershipPathOutcome.Stored =>
                    Analysis.ArrayPoolOwnershipUseKind.Stored,
                ResourceOwnershipPathOutcome.ReturnedToCaller =>
                    Analysis.ArrayPoolOwnershipUseKind.ReturnedToCaller,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(outcome)),
            },
            legacyFinding.Payload.Outcome);
        Assert.Equal(
            new MemberCallGraphBuildCounts(0, 1, 0),
            graph.BuildCounts);
        Assert.Equal(1, context.Sources[0].OpenCount);
    }

    [Fact]
    public async Task GenericOwnershipPreservesOpenGenericLocalRelease()
    {
        await using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentAndReturnDirectlyOpenGeneric");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();
        CallGraphProjection projection =
            CallGraphProjection.Create(
                view.CallerRoot,
                view.CalleeRoot);

        ResourceOwnershipPathInspection inspection =
            ResourceOwnershipPathFindings.Inspect(
                view,
                projection,
                ResourceOwnershipSearchOptions.ArrayPool);
        Finding<ResourceOwnershipPathWitness> finding =
            Assert.Single(inspection.Findings);
        Assert.Equal(
            ResourceOwnershipPathOutcome.Released,
            finding.Payload.Outcome);
        Assert.Empty(finding.Payload.Steps);
        Assert.True(finding.Payload.IsComplete);
        Assert.False(
            inspection.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));

        AnnotatedCallGraphOwnershipInspection legacy =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                projection);
        Finding<ArrayPoolOwnershipPathWitness> legacyFinding =
            Assert.Single(legacy.Findings);
        Assert.Equal(
            Analysis.ArrayPoolOwnershipUseKind.ReturnedToPool,
            legacyFinding.Payload.Outcome);
        Assert.Empty(legacyFinding.Payload.Steps);
    }

    [Fact]
    public async Task
        GenericOwnershipPreservesMixedSourceReleaseAlongsideStorage()
    {
        await using GraphContext context =
            GraphContext.Create(OwnershipPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentOrAllocateStoreThenReturn");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                    | Analysis.LibraryBodyAnalysisFeatures.OwnershipFlow,
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();
        CallGraphProjection projection =
            CallGraphProjection.Create(
                view.CallerRoot,
                view.CalleeRoot);

        ResourceOwnershipPathInspection generic =
            ResourceOwnershipPathFindings.Inspect(
                view,
                projection,
                ResourceOwnershipSearchOptions.ArrayPool);
        AnnotatedCallGraphOwnershipInspection legacy =
            ArrayPoolOwnershipPathFindings.Inspect(
                view,
                projection);

        Assert.Equal(
            [
                ResourceOwnershipPathOutcome.Released,
                ResourceOwnershipPathOutcome.Stored,
            ],
            generic.Findings
                .Select(finding => finding.Payload.Outcome)
                .Order()
                .ToArray());
        Assert.Equal(
            [
                Analysis.ArrayPoolOwnershipUseKind.ReturnedToPool,
                Analysis.ArrayPoolOwnershipUseKind.Stored,
            ],
            legacy.Findings
                .Select(finding => finding.Payload.Outcome)
                .Order()
                .ToArray());
        Assert.Contains(
            generic.Findings,
            finding =>
                finding.Payload.Outcome
                    == ResourceOwnershipPathOutcome.Released
                && !finding.Payload.IsComplete);
    }

    [Fact]
    public async Task OwnershipWitnessBudgetPreservesPhysicalCallIdentity()
    {
        await using GraphContext context =
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
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();
        CallGraphProjection projection =
            CallGraphProjection.Create(
                view.CallerRoot,
                view.CalleeRoot);

        ResourceOwnershipPathInspection all =
            ResourceOwnershipPathFindings.Inspect(
                view,
                projection,
                ResourceOwnershipSearchOptions.ArrayPool);
        Finding<ResourceOwnershipPathWitness>[] findings =
            [.. all.Findings];
        Assert.Equal(2, findings.Length);
        Assert.Equal(
            findings[0].Payload.Steps[0].EdgeRow,
            findings[1].Payload.Steps[0].EdgeRow);
        Assert.NotEqual(
            findings[0].Payload.Steps[0].ILOffset,
            findings[1].Payload.Steps[0].ILOffset);
        Assert.NotEqual(findings[0].Key, findings[1].Key);

        ResourceOwnershipPathInspection bounded =
            ResourceOwnershipPathFindings.Inspect(
                view,
                projection,
                new ResourceOwnershipSearchOptions
                {
                    MaxWitnesses = 1,
                    ResourceKind =
                        Analysis.ArrayPoolResourceEffectModel.BufferKind,
                });
        Assert.Single(bounded.Findings);
        Assert.True(
            bounded.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.WitnessBudget));
    }

    [Fact]
    public async Task OwnershipPathBudgetLeavesForwardedPathIncomplete()
    {
        await using GraphContext context =
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
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();
        Assert.True(view.ResourceOwnershipPublicationComplete);

        ResourceOwnershipPathInspection result =
            ResourceOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot),
                new ResourceOwnershipSearchOptions
                {
                    MaxPaths = 1,
                    ResourceKind =
                        Analysis.ArrayPoolResourceEffectModel.BufferKind,
                });

        Assert.Empty(result.Findings);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.PathBudget));
    }

    [Fact]
    public async Task OwnershipForwardedToABodilessCalleeIsIncomplete()
    {
        await using GraphContext context =
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
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();
        Assert.True(view.ResourceOwnershipPublicationComplete);

        ResourceOwnershipPathInspection result =
            ResourceOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot),
                ResourceOwnershipSearchOptions.ArrayPool);

        Assert.Empty(result.Findings);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.BodyUnavailable));
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.TraversalBoundary));
        Assert.False(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));
    }

    [Fact]
    public async Task OwnershipPositiveWitnessSurvivesAnIncompleteSiblingPath()
    {
        await using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentReturnAndForwardExternally");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();
        ResourceOwnershipPathInspection result =
            ResourceOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot),
                ResourceOwnershipSearchOptions.ArrayPool);
        Finding<ResourceOwnershipPathWitness> finding =
            Assert.Single(result.Findings);

        Assert.Equal(
            ResourceOwnershipPathOutcome.Released,
            finding.Payload.Outcome);
        Assert.True(finding.Payload.IsComplete);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.BodyUnavailable));
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.TraversalBoundary));
        Assert.False(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));
    }

    [Fact]
    public async Task OwnershipStoredWitnessSurvivesUnrelatedResolutionFailure()
    {
        (ResourceOwnershipPathInspection Baseline,
            MemberCallGraphView BaselineView) =
            await Inspect(OwnershipIsolationAdmission());
        (ResourceOwnershipPathInspection WithUnrelated,
            MemberCallGraphView WithUnrelatedView) =
            await Inspect(
                OwnershipIsolationAdmission(
                    includeUnrelatedFailure: true));

        Finding<ResourceOwnershipPathWitness>[] expected =
            [.. Baseline.Findings];
        Assert.Single(expected);
        Assert.All(
            expected,
            finding =>
            {
                Assert.Equal(
                    ResourceOwnershipPathOutcome.Stored,
                    finding.Payload.Outcome);
                Assert.True(finding.Payload.IsComplete);
                Assert.Single(finding.Payload.Steps);
            });
        Assert.False(
            Baseline.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));
        Assert.True(BaselineView.ResourceOwnershipPublicationComplete);

        Assert.Equal(
            expected.Select(finding => finding.Key),
            WithUnrelated.Findings.Select(finding => finding.Key));
        Assert.All(
            WithUnrelated.Findings,
            finding =>
            {
                Assert.Equal(
                    ResourceOwnershipPathOutcome.Stored,
                    finding.Payload.Outcome);
                Assert.True(finding.Payload.IsComplete);
                Assert.Single(finding.Payload.Steps);
            });
        Assert.True(
            WithUnrelated.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));
        Assert.False(WithUnrelatedView.ResourceOwnershipPublicationComplete);

        async Task<(
            ResourceOwnershipPathInspection Inspection,
            MemberCallGraphView View)> Inspect(
                Analysis.ResourceEffectAdmission admission)
        {
            await using GraphContext context =
                GraphContext.Create(OwnershipPath, TargetPath);
            using var graph = new MemberCallGraphSession(
                context.Group,
                context.Sources[0].Assembly,
                MemberToken(
                    OwnershipPath,
                    "Entry",
                    "ExerciseOwnershipIsolation"),
                new MemberCallGraphOptions
                {
                    Features =
                        Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
                    ResourceEffects = admission,
                });
            MemberCallGraphView view = graph.Callers();
            ResourceOwnershipPathInspection inspection =
                ResourceOwnershipPathFindings.Inspect(
                    view,
                    CallGraphProjection.Create(
                        view.CallerRoot,
                        view.CalleeRoot));
            return (inspection, view);
        }
    }

    [Theory]
    [InlineData("RentWithMethodGroup")]
    [InlineData("RentWithFunctionPointer")]
    public async Task OwnershipIndirectCallShapesDoNotProduceSafeFindings(
        string methodName)
    {
        await using GraphContext context =
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
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView view = graph.Callers();

        ResourceOwnershipPathInspection result =
            ResourceOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot),
                ResourceOwnershipSearchOptions.ArrayPool);

        Assert.Empty(result.Findings);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));
    }

    [Fact]
    public async Task GenericOwnershipKeepsResourceKindsDistinctOnOneGraph()
    {
        await using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "ExerciseTwoResourceDomainsThroughHelper");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
                ResourceEffects = TwoResourceAdmission(),
            });
        MemberCallGraphView view = graph.Callers();
        Analysis.ResourceOwnershipMethodSummary focus =
            Assert.Single(
                view.ResourceOwnershipSummaries,
                summary =>
                    summary.Method.MetadataToken == root);
        Assert.Equal(2, focus.Acquisitions.Length);
        Assert.Equal(
            [0, 0],
            focus.Acquisitions
                .SelectMany(flow => flow.Uses)
                .Where(use => use.IsForwarded)
                .Select(use => use.CalleeParameterIndex)
                .Order()
                .ToArray());
        Analysis.ResourceOwnershipMethodSummary forward =
            Assert.Single(
                view.ResourceOwnershipSummaries,
                summary =>
                    summary.Method.Name == "ForwardResource");
        Assert.Equal(
            [0, 0],
            forward.Parameters
                .SelectMany(flow => flow.Uses)
                .Where(use =>
                    use.Kind
                        == Analysis.ResourceOwnershipUseKind.Released)
                .Select(use => use.CalleeParameterIndex)
                .Order()
                .ToArray());

        ResourceOwnershipPathInspection result =
            ResourceOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot));
        Finding<ResourceOwnershipPathWitness>[] findings =
            [.. result.Findings];

        Assert.Equal(2, findings.Length);
        Assert.All(
            findings,
            finding =>
            {
                Assert.Equal(
                    ResourceOwnershipPathOutcome.Released,
                    finding.Payload.Outcome);
                Assert.True(finding.Payload.IsComplete);
                Assert.Single(finding.Payload.Steps);
            });
        Assert.Equal(
            2,
            findings
                .Select(finding =>
                    finding.Payload.ResourceKind.Identity)
                .Distinct()
                .Count());
        Assert.Equal(
            findings[0].Payload.Steps[0].EdgeRow,
            findings[1].Payload.Steps[0].EdgeRow);
        Assert.NotEqual(
            findings[0].Payload.Steps[0].ILOffset,
            findings[1].Payload.Steps[0].ILOffset);
        Assert.NotEqual(findings[0].Key, findings[1].Key);
    }

    [Fact]
    public async Task GenericOwnershipKeysIncludeBoundResourceArguments()
    {
        await using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "ExerciseGenericResourceArguments");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
                ResourceEffects = TwoResourceAdmission(),
            });
        MemberCallGraphView view = graph.Callers();
        Analysis.ResourceOwnershipMethodSummary focus =
            Assert.Single(
                view.ResourceOwnershipSummaries,
                summary =>
                    summary.Method.MetadataToken == root);
        Assert.Equal(2, focus.Acquisitions.Length);
        Assert.All(
            focus.Acquisitions,
            flow => Assert.Contains(
                flow.Uses,
                static use => use.IsForwarded));
        Analysis.ResourceOwnershipMethodSummary store =
            Assert.Single(
                view.ResourceOwnershipSummaries,
                summary =>
                    summary.Method.Name == "StoreResource");
        Assert.Contains(
            Assert.Single(store.Parameters).Uses,
            static use =>
                use.Kind
                    == Analysis.ResourceOwnershipUseKind.Stored);

        Analysis.TypeRef nested = CollisionArgument(
            OwnershipPath,
            "NestedCollisionExposure");
        Analysis.TypeRef namespaced = CollisionArgument(
            TargetPath,
            "NamespacedCollisionExposure");
        Assert.Equal(
            nested.ToQualifiedDisplayString(),
            namespaced.ToQualifiedDisplayString());
        Assert.NotEqual(nested, namespaced);
        var collisionAssembly = new AssemblyReferenceIdentity(
            "Collision.Types",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        Analysis.ResourceOccurrenceType[] collisionArguments =
        [
            new(
                nested,
                collisionAssembly,
                DefinitionJoinKind.Exact,
                GenericScopeKind: null,
                Element: null,
                Arguments: [],
                Forwarding: []),
            new(
                namespaced,
                collisionAssembly,
                DefinitionJoinKind.Exact,
                GenericScopeKind: null,
                Element: null,
                Arguments: [],
                Forwarding: []),
        ];
        focus = focus with
        {
            Acquisitions =
            [
                .. focus.Acquisitions.Select((flow, index) =>
                {
                    Analysis.ResourceOccurrenceResourceKind kind =
                        Assert.Single(
                            flow.Obligation.ResourceKinds);
                    var replacementKind =
                        new Analysis.ResourceOccurrenceResourceKind(
                            kind.Identity,
                            [collisionArguments[index]]);
                    return flow with
                    {
                        Obligation =
                            new Analysis.ResourceOccurrenceRoot.Acquisition(
                                flow.Obligation.Call,
                                [replacementKind],
                                flow.Obligation.Authorities),
                    };
                }),
            ],
        };
        view = view with
        {
            ResourceOwnershipSummaries =
            [
                .. view.ResourceOwnershipSummaries.Select(summary =>
                    summary.Method.MetadataToken == root
                        ? focus
                        : summary),
            ],
        };
        CallGraphProjection projection =
            CallGraphProjection.Create(
                view.CallerRoot,
                view.CalleeRoot);
        ResourceOwnershipPathInspection result =
            ResourceOwnershipPathFindings.Inspect(
                view,
                projection);
        Finding<ResourceOwnershipPathWitness>[] findings =
            [.. result.Findings];

        Assert.Equal(2, findings.Length);
        Assert.Single(
            findings
                .Select(finding =>
                    finding.Payload.ResourceKind.Identity)
                .Distinct());
        Assert.Single(
            findings
                .Select(finding =>
                    Assert.Single(
                        finding.Payload.ResourceKind.Arguments)
                        .Type
                        .ToQualifiedDisplayString())
                .Distinct());
        Assert.Equal(
            2,
            findings
                .Select(static finding => finding.Key)
                .Distinct()
                .Count());
        ResourceOwnershipPathInspection repeated =
            ResourceOwnershipPathFindings.Inspect(view, projection);
        Assert.Equal(
            findings.Select(static finding => finding.Key),
            repeated.Findings.Select(static finding => finding.Key));

        static Analysis.TypeRef CollisionArgument(
            string path,
            string callerName)
        {
            Analysis.LibraryBodyIndex index =
                Analysis.LibraryBodyIndex.Open(path);
            Analysis.DirectCall call = Assert.Single(
                index.DirectCalls,
                candidate =>
                    candidate.Caller.Name == "Expose"
                    && candidate.Caller.DeclaringType.Name
                        == callerName
                    && candidate.Callee.Name == "Capture");
            return Assert.Single(call.Callee.TypeArguments);
        }
    }

    [Fact]
    public async Task GenericOwnershipRequiresExactBoundDomainEvidence()
    {
        await using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "RentAndReturnThroughGenericHelper");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
                ResourceEffects =
                    Analysis.ArrayPoolResourceEffectModel.Create(),
            });
        MemberCallGraphView original = graph.Callers();
        Analysis.ResourceOwnershipMethodSummary focus =
            Assert.Single(
                original.ResourceOwnershipSummaries,
                summary =>
                    summary.Method.MetadataToken == root);
        Analysis.ResourceOwnershipUse forwarding =
            Assert.Single(
                Assert.Single(focus.Acquisitions).Uses,
                static use => use.IsForwarded);
        Assert.NotNull(
            Assert.Single(forwarding.MethodArguments).TypeEvidence);
        Analysis.TypeRef token =
            Analysis.TypeRef.Definition(
                "DomainX",
                "Domain",
                "Token");
        var version1 = new AssemblyReferenceIdentity(
            "DomainX",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var version2 = version1 with
        {
            Version = new Version(2, 0, 0, 0),
        };
        Analysis.ResourceOccurrenceType first =
            ExactType(token, version1);
        Analysis.ResourceOccurrenceType second =
            ExactType(token, version2);
        CallGraphProjection projection =
            CallGraphProjection.Create(
                original.CallerRoot,
                original.CalleeRoot);

        ResourceOwnershipPathInspection matching =
            ResourceOwnershipPathFindings.Inspect(
                WithArgument(first),
                projection);
        Finding<ResourceOwnershipPathWitness> witness =
            Assert.Single(matching.Findings);
        Assert.Equal(
            ResourceOwnershipPathOutcome.Released,
            witness.Payload.Outcome);
        Assert.True(witness.Payload.IsComplete);

        ResourceOwnershipPathInspection mismatched =
            ResourceOwnershipPathFindings.Inspect(
                WithArgument(second),
                projection);
        Assert.Empty(mismatched.Findings);
        Assert.False(mismatched.IsComplete);

        ResourceOwnershipPathInspection unavailable =
            ResourceOwnershipPathFindings.Inspect(
                WithArgument(exact: null),
                projection);
        Assert.Empty(unavailable.Findings);
        Assert.True(
            unavailable.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));

        MemberCallGraphView WithArgument(
            Analysis.ResourceOccurrenceType? exact)
        {
            Analysis.ResourceOwnershipAcquisitionFlow flow =
                Assert.Single(focus.Acquisitions);
            Analysis.ResourceOccurrenceResourceKind kind =
                Assert.Single(flow.Obligation.ResourceKinds);
            var replacementKind =
                new Analysis.ResourceOccurrenceResourceKind(
                    kind.Identity,
                    [first]);
            flow = flow with
            {
                Obligation =
                    new Analysis.ResourceOccurrenceRoot.Acquisition(
                        flow.Obligation.Call,
                        [replacementKind],
                        flow.Obligation.Authorities),
                Uses =
                [
                    .. flow.Uses.Select(use =>
                        use.IsForwarded
                            ? use with
                            {
                                MethodArguments =
                                [
                                    new(
                                        token,
                                        exact),
                                ],
                            }
                            : use),
                ],
            };
            Analysis.ResourceOwnershipMethodSummary replacement =
                focus with { Acquisitions = [flow] };
            return original with
            {
                ResourceOwnershipSummaries =
                [
                    .. original.ResourceOwnershipSummaries.Select(
                        summary =>
                            summary.Method.MetadataToken == root
                                ? replacement
                                : summary),
                ],
            };
        }

        static Analysis.ResourceOccurrenceType ExactType(
            Analysis.TypeRef type,
            AssemblyReferenceIdentity assembly) =>
            new(
                type,
                assembly,
                DefinitionJoinKind.Exact,
                GenericScopeKind: null,
                Element: null,
                Arguments: [],
                Forwarding: []);
    }

    [Fact]
    public async Task FieldReceiverMutationIsIncompleteNotStored()
    {
        await using GraphContext context =
            GraphContext.Create(OwnershipPath, TargetPath);
        int root = MemberToken(
            OwnershipPath,
            "Entry",
            "ExerciseTrackedResourceMutation");
        using var graph = new MemberCallGraphSession(
            context.Group,
            context.Sources[0].Assembly,
            root,
            new MemberCallGraphOptions
            {
                Features =
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
                ResourceEffects = TwoResourceAdmission(),
            });
        MemberCallGraphView view = graph.Callers();

        ResourceOwnershipPathInspection result =
            ResourceOwnershipPathFindings.Inspect(
                view,
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot));

        Assert.Empty(result.Findings);
        Assert.True(
            result.Limits.HasFlag(
                AnnotatedCallGraphOwnershipLimit.AnalysisFailure));
        Analysis.ResourceOwnershipMethodSummary mutation =
            Assert.Single(
                view.ResourceOwnershipSummaries,
                summary =>
                    summary.Method.Name
                        == "MutateTrackedResource");
        Analysis.ResourceOwnershipParameterFlow parameter =
            Assert.Single(mutation.Parameters);
        Assert.False(parameter.IsComplete);
        Assert.DoesNotContain(
            parameter.Uses,
            static use =>
                use.Kind
                    == Analysis.ResourceOwnershipUseKind.Stored);
    }

    [Fact]
    public async Task AnnotatedMemberDocument_ReportsOneCycleForRepeatedRecursiveCalls()
    {
        await using GraphContext context =
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
    public async Task
        AnnotatedMemberDocument_DoesNotMergeGeneratedBodyOffsets()
    {
        string path =
            typeof(MemberCallGraphSession).Assembly.Location;
        int selectIds = MemberToken(
            path,
            "ApiInventoryQuery",
            "SelectIds");
        await using GraphContext context =
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
    public async Task AnnotatedMemberDocument_ReportsAMutualCycleAtTheCallerTier()
    {
        await using GraphContext context =
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
    public async Task AnnotatedMemberDocument_RejectsSourceFromAnotherModule()
    {
        await using GraphContext context =
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
    public async Task AnnotatedMemberDocument_HonorsACalleeNodeBudget()
    {
        await using GraphContext context =
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
    public async Task BodilessFocus_ProducesEveryProgressiveTier()
    {
        await using GraphContext context =
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
    public async Task DirectFullTier_SkipsScopedAndLaterCalleesReusesFull()
    {
        await using GraphContext context =
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
    public async Task Tiers_ShareSnapshotsAndBuildEachIndexAtMostOnce()
    {
        await using GraphContext context =
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
    public async Task DuplicateImages_BuildOneCrossLibraryIndex()
    {
        await using GraphContext context =
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
    public async Task StreamOnlyParticipants_CanBuildCrossLibraryGraph()
    {
        await using GraphContext context =
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
    public async Task CrossLibrary_AcquisitionFailureIsTypedAndCached()
    {
        await using GraphContext context =
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
    public async Task MalformedMetadata_IsTypedAndCached()
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
        await using var workspace = new InspectionWorkspace();
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
    public async Task WorkspaceDisposal_DisposesOwnedGraphBeforeSnapshots()
    {
        await using GraphContext context =
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

        await context.Workspace.DisposeAsync();

        Assert.Equal(0, context.Group.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(graph.Callees);
        Assert.Throws<ObjectDisposedException>(catalogScope.ReleaseGraph);
        graph.Dispose();
    }

    [Fact]
    public async Task OptionsRejectFeatureSetsThatCannotProduceScopedGraph()
    {
        await using GraphContext context = GraphContext.Create(CallerPath);
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
    public async Task CrossLibrary_ResolvedVersionSkewIsNotIncomplete()
    {
        await using GraphContext context =
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
        Assert.False(view.Diagnostics.IsIncomplete);
        Assert.Equal(0, view.Diagnostics.IncompleteEdgeCount);
    }

    [Fact]
    public async Task Projection_DoesNotAcquireOrBuildMoreIndexes()
    {
        await using GraphContext context =
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
        await using GraphContext context =
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
    public async Task RunAsync_CancellationAfterFirstLayerSkipsFullBuild()
    {
        await using GraphContext context =
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

    static Analysis.ResourceEffectAdmission TwoResourceAdmission()
    {
        var model =
            new Analysis.ResourceEffectModelIdentity(
                "test.generic-research-ownership");
        var firstKind =
            new Analysis.ResourceKindIdentity(
                "test.generic-research-ownership.first");
        var secondKind =
            new Analysis.ResourceKindIdentity(
                "test.generic-research-ownership.second");
        var genericKind =
            new Analysis.ResourceKindIdentity(
                "test.generic-research-ownership.generic");
        Analysis.ResourceTypeExpression.Named entry =
            FixtureEntryType();
        Analysis.ResourceTypeExpression.Named byteType =
            CoreType("Byte");
        var byteArray =
            new Analysis.ResourceTypeExpression.SzArray(byteType);
        Analysis.ResourceTypeExpression.Named voidType =
            CoreType("Void");
        Analysis.ResourceTypeExpression.Named objectType =
            CoreType("Object");
        var first =
            new Analysis.ResourceKindReference(firstKind, []);
        var second =
            new Analysis.ResourceKindReference(secondKind, []);
        var methodFirst =
            new Analysis.ResourceEffectGenericVariable(
                Analysis.ResourceEffectGenericVariableKind.Method,
                0);
        var methodSecond =
            new Analysis.ResourceEffectGenericVariable(
                Analysis.ResourceEffectGenericVariableKind.Method,
                1);
        var genericFirst =
            new Analysis.ResourceKindReference(
                genericKind,
                [methodFirst]);
        var genericSecond =
            new Analysis.ResourceKindReference(
                genericKind,
                [methodSecond]);
        var definition =
            new Analysis.ResourceEffectModelDefinition(
                Analysis.ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new Analysis.ResourceKindDefinition(
                        firstKind,
                        arity: 0,
                        [Provenance(model, 0)]),
                    new Analysis.ResourceKindDefinition(
                        secondKind,
                        arity: 0,
                        [Provenance(model, 1)]),
                    new Analysis.ResourceKindDefinition(
                        genericKind,
                        arity: 1,
                        [Provenance(model, 2)]),
                ],
                [],
                [
                    Declaration(
                        model,
                        entry,
                        "AcquireFirstResource",
                        [],
                        byteArray,
                        new Analysis.ResourceEffect.Acquire(
                            first,
                            new Analysis.ResourceEffectLocation.Return(),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            Correspondence: null,
                            Lender: null),
                        2),
                    Declaration(
                        model,
                        entry,
                        "AcquireSecondResource",
                        [],
                        byteArray,
                        new Analysis.ResourceEffect.Acquire(
                            second,
                            new Analysis.ResourceEffectLocation.Return(),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            Correspondence: null,
                            Lender: null),
                        3),
                    Declaration(
                        model,
                        entry,
                        "ObserveResource",
                        [byteArray],
                        voidType,
                        new Analysis.ResourceEffect.Release(
                            new Analysis.ResourceEffectLocation.Parameter(0),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            first,
                            Correspondence: null,
                            Observation: null),
                        4),
                    Declaration(
                        model,
                        entry,
                        "ObserveResource",
                        [byteArray],
                        voidType,
                        new Analysis.ResourceEffect.Release(
                            new Analysis.ResourceEffectLocation.Parameter(0),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            second,
                            Correspondence: null,
                            Observation: null),
                        5),
                    Declaration(
                        model,
                        entry,
                        "AcquirePair",
                        [],
                        objectType,
                        new Analysis.ResourceEffect.Acquire(
                            genericFirst,
                            new Analysis.ResourceEffectLocation.Return(),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            Correspondence: null,
                            Lender: null),
                        6,
                        genericArity: 2),
                    Declaration(
                        model,
                        entry,
                        "AcquirePair",
                        [],
                        objectType,
                        new Analysis.ResourceEffect.Acquire(
                            genericSecond,
                            new Analysis.ResourceEffectLocation.Return(),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            Correspondence: null,
                            Lender: null),
                        7,
                        genericArity: 2),
                    Declaration(
                        model,
                        entry,
                        "AcquireTrackedResource",
                        [],
                        FixtureTrackedResourceType(),
                        new Analysis.ResourceEffect.Acquire(
                            genericFirst,
                            new Analysis.ResourceEffectLocation.Return(),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            Correspondence: null,
                            Lender: null),
                        8,
                        genericArity: 1),
                ]);
        Analysis.ResourceEffectAdmissionOutcome outcome =
            Analysis.ResourceEffectAdmissionBuilder.Admit([definition]);
        return Assert.IsType<
            Analysis.ResourceEffectAdmissionOutcome.Admitted>(
                outcome).Admission;
    }

    static Analysis.ResourceEffectAdmission OwnershipIsolationAdmission(
        bool includeUnrelatedFailure = false)
    {
        var model =
            new Analysis.ResourceEffectModelIdentity(
                "test.generic-research-ownership.isolation");
        var kind =
            new Analysis.ResourceKindIdentity(
                "test.generic-research-ownership.isolation-token");
        Analysis.ResourceTypeExpression.Named entry =
            FixtureEntryType();
        Analysis.ResourceTypeExpression.Named token =
            FixtureOwnershipTokenType();
        var resource = new Analysis.ResourceKindReference(kind, []);
        var definition =
            new Analysis.ResourceEffectModelDefinition(
                Analysis.ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new Analysis.ResourceKindDefinition(
                        kind,
                        arity: 0,
                        [Provenance(model, 0)]),
                ],
                [],
                [
                    Declaration(
                        model,
                        entry,
                        "AcquireOwnershipToken",
                        [],
                        token,
                        new Analysis.ResourceEffect.Acquire(
                            resource,
                            new Analysis.ResourceEffectLocation.Return(),
                            new Analysis.ResourceEffectCompletion.NormalReturn(),
                            Correspondence: null,
                            Lender: null),
                        1),
                ]);
        var conflictOrdinary =
            new Analysis.ResourceEffectModelIdentity(
                "test.generic-research-ownership.isolation-conflict-ordinary");
        var conflictTransparent =
            new Analysis.ResourceEffectModelIdentity(
                "test.generic-research-ownership.isolation-conflict-transparent");
        Analysis.ResourceEffectModelDefinition Conflict(
            Analysis.ResourceEffectModelIdentity conflictModel,
            Analysis.ResourceOperationBoundary boundary) =>
            new(
                Analysis.ResourceEffectLanguageIdentity.Version1,
                conflictModel,
                [],
                [],
                [
                    Declaration(
                        conflictModel,
                        entry,
                        "ObserveResource",
                        [
                            new Analysis.ResourceTypeExpression.SzArray(
                                CoreType("Byte")),
                        ],
                        CoreType("Void"),
                        new Analysis.ResourceEffect.Operation(
                            boundary,
                            Analysis.ResourceOperationThrows.Possible,
                            Guard: null),
                        0),
                ]);
        ImmutableArray<Analysis.ResourceEffectModelDefinition> definitions =
            includeUnrelatedFailure
                ?
                [
                    definition,
                    Conflict(
                        conflictOrdinary,
                        Analysis.ResourceOperationBoundary.Ordinary),
                    Conflict(
                        conflictTransparent,
                        Analysis.ResourceOperationBoundary.Transparent),
                ]
                : [definition];
        Analysis.ResourceEffectAdmissionOutcome outcome =
            Analysis.ResourceEffectAdmissionBuilder.Admit(definitions);
        return Assert.IsType<
            Analysis.ResourceEffectAdmissionOutcome.Admitted>(
                outcome).Admission;
    }

    static Analysis.ResourceEffectAdmission CrossParticipantAcquisition()
    {
        var model =
            new Analysis.ResourceEffectModelIdentity(
                "test.cross-participant-acquisition");
        var kind =
            new Analysis.ResourceKindIdentity(
                "test.cross-participant-kind");
        var methodVariable =
            new Analysis.ResourceEffectGenericVariable(
                Analysis.ResourceEffectGenericVariableKind.Method,
                0);
        var methodType =
            new Analysis.ResourceTypeExpression.Variable(
                methodVariable);
        Analysis.ResourceTypeExpression.Named declaringType =
            new(
                new Analysis.ResourceAssemblySelector(
                    "ILInspector.Analysis.CallerGraphTarget",
                    publicKeyToken: null,
                    Analysis.ResourceAssemblyVersionPolicy.Any),
                "Target",
                [new Analysis.ResourceTypeNameSegment("GenericApi", 0)]);
        var definition =
            new Analysis.ResourceEffectModelDefinition(
                Analysis.ResourceEffectLanguageIdentity.Version1,
                model,
                [
                    new Analysis.ResourceKindDefinition(
                        kind,
                        arity: 0,
                        [Provenance(model, 0)]),
                ],
                [],
                [
                    Declaration(
                        model,
                        declaringType,
                        "Echo",
                        [methodType],
                        methodType,
                        new Analysis.ResourceEffect.Acquire(
                            new Analysis.ResourceKindReference(kind, []),
                            new Analysis.ResourceEffectLocation.Return(),
                            new Analysis.ResourceEffectCompletion
                                .NormalReturn(),
                            Correspondence: null,
                            Lender: null),
                        ordinal: 1,
                        genericArity: 1),
                ]);
        Analysis.ResourceEffectAdmissionOutcome outcome =
            Analysis.ResourceEffectAdmissionBuilder.Admit([definition]);
        return Assert.IsType<
            Analysis.ResourceEffectAdmissionOutcome.Admitted>(
                outcome).Admission;
    }

    static Analysis.ResourceEffectTypedDeclaration Declaration(
        Analysis.ResourceEffectModelIdentity model,
        Analysis.ResourceTypeExpression.Named declaringType,
        string name,
        ImmutableArray<Analysis.ResourceTypeExpression> parameters,
        Analysis.ResourceTypeExpression returnType,
        Analysis.ResourceEffect effect,
        int ordinal,
        int genericArity = 0) =>
        new(
            new Analysis.ResourceEffectTargetSelector.Member(
                new Analysis.ResourceEffectMemberSelector(
                    declaringType,
                    name,
                    Analysis.ResourceEffectMemberKind.Method,
                    isStatic: true,
                    genericArity,
                    Analysis.ResourceEffectCallingConvention.Default,
                    hasThis: false,
                    explicitThis: false,
                    [
                        .. parameters.Select(parameter =>
                            new Analysis.ResourceEffectParameterSelector(
                                parameter,
                                Analysis.ResourceEffectRefKind.Value)),
                    ],
                    returnType)),
            effect,
            [Provenance(model, ordinal)]);

    static Analysis.ResourceTypeExpression.Named FixtureEntryType() =>
        new(
            new Analysis.ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                Analysis.ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new Analysis.ResourceTypeNameSegment("Entry", 0)]);

    static Analysis.ResourceTypeExpression.Named
        FixtureTrackedResourceType() =>
        new(
            new Analysis.ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                Analysis.ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new Analysis.ResourceTypeNameSegment("TrackedResource", 0)]);

    static Analysis.ResourceTypeExpression.Named
        FixtureOwnershipTokenType() =>
        new(
            new Analysis.ResourceAssemblySelector(
                "ILInspector.Analysis.OwnershipFlowFixtures",
                publicKeyToken: null,
                Analysis.ResourceAssemblyVersionPolicy.Any),
            "Ownership",
            [new Analysis.ResourceTypeNameSegment("OwnershipToken", 0)]);

    static Analysis.ResourceTypeExpression.Named CoreType(string name) =>
        new(
            new Analysis.ResourceAssemblySelector(
                "System.Runtime",
                "b03f5f7f11d50a3a",
                Analysis.ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            "System",
            [new Analysis.ResourceTypeNameSegment(name, 0)]);

    static Analysis.ResourceDeclarationProvenance Provenance(
        Analysis.ResourceEffectModelIdentity model,
        int ordinal) =>
        new(
            model,
            Analysis.ResourceDeclarationAuthority.CallerSupplied,
            new InertString(
                TextPolicy.Field,
                $"generic-research-ownership-test:{ordinal}"),
            ordinal);

    sealed class GraphContext : IAsyncDisposable
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
            CreateCore(
                streamOnly: false,
                failingIndex: null,
                equivalentIdentityIndex: null,
                designatedIndexes: null,
                ambiguousTargetIndexes: null,
                paths);

        internal static GraphContext CreateStreamOnly(
            params string[] paths) =>
            CreateCore(
                streamOnly: true,
                failingIndex: null,
                equivalentIdentityIndex: null,
                designatedIndexes: null,
                ambiguousTargetIndexes: null,
                paths);

        internal static GraphContext CreateWithFailingParticipant(
            params string[] paths) =>
            CreateCore(
                streamOnly: false,
                failingIndex: 1,
                equivalentIdentityIndex: null,
                designatedIndexes: null,
                ambiguousTargetIndexes: null,
                paths);

        internal static GraphContext CreateWithEquivalentIdentity(
            int equivalentIdentityIndex,
            params string[] paths) =>
            CreateCore(
                streamOnly: false,
                failingIndex: null,
                equivalentIdentityIndex,
                designatedIndexes: null,
                ambiguousTargetIndexes: null,
                paths);

        internal static GraphContext CreateWithDesignatedParticipant(
            int designatedIndex,
            params string[] paths) =>
            CreateWithDesignatedParticipants(
                [designatedIndex],
                paths);

        internal static GraphContext CreateWithDesignatedParticipants(
            IReadOnlyList<int> designatedIndexes,
            params string[] paths) =>
            CreateCore(
                streamOnly: false,
                failingIndex: null,
                equivalentIdentityIndex: null,
                designatedIndexes,
                ambiguousTargetIndexes: null,
                paths);

        internal static GraphContext
            CreateWithAmbiguousDesignatedParticipants(
                IReadOnlyList<int> targetIndexes,
                params string[] paths) =>
            CreateCore(
                streamOnly: true,
                failingIndex: null,
                equivalentIdentityIndex: null,
                designatedIndexes: targetIndexes,
                ambiguousTargetIndexes: targetIndexes,
                paths);

        static GraphContext CreateCore(
            bool streamOnly,
            int? failingIndex,
            int? equivalentIdentityIndex,
            IReadOnlyList<int>? designatedIndexes,
            IReadOnlyList<int>? ambiguousTargetIndexes,
            params string[] paths)
        {
            TestSource[] sources = paths
                .Select(
                    (path, index) => TestSource.Create(
                        path,
                        streamOnly,
                        failingIndex == index,
                        equivalentIdentityIndex == index,
                        designatedIndexes?.Contains(index) == true))
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
            IAssemblyBindingPolicy callerPolicy =
                ambiguousTargetIndexes is null
                    ? policy
                    : new AmbiguousTargetBindingPolicy(
                        policy,
                        [
                            .. ambiguousTargetIndexes.Select(
                                index => sources[index].Assembly),
                        ]);
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    sources.Select(
                        (source, index) => new AssemblyContextParticipant(
                            source.Assembly,
                            index == 0 ? callerPolicy : policy)));
            return new(workspace, group, sources);
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
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
            bool fails,
            bool useEquivalentIdentity = false,
            bool designated = false)
        {
            AssemblyResolutionProvenance provenance =
                designated
                    ? AssemblyResolutionProvenance.Designated(
                        "progressive graph test source")
                    : AssemblyResolutionProvenance.Local(
                        "progressive graph test source");
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    sourcePath,
                    provenance);
            byte[]? content =
                streamOnly ? File.ReadAllBytes(sourcePath) : null;
            TestSource? testSource = null;
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    useEquivalentIdentity
                        ? new AssemblyReferenceIdentity(
                            source.Identity.Name.ToLowerInvariant(),
                            source.Identity.Version,
                            Culture: "neutral",
                            PublicKeyToken:
                                source.Identity.PublicKeyToken)
                        : source.Identity,
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

    sealed class AmbiguousTargetBindingPolicy(
        IAssemblyBindingPolicy inner,
        ImmutableArray<ResolvedAssemblyReference> targets)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version => inner.Version;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            if (request.Target
                    is AssemblyBindingTarget.AssemblyReference reference
                && targets.Any(target =>
                string.Equals(
                    target.Identity.Name,
                    reference.Identity.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return new(
                    Version,
                    AssemblyBindingSelection.Multiple(targets));
            }

            return inner.Select(request);
        }
    }
}
