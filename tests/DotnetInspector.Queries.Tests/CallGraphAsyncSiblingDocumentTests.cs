using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class CallGraphAsyncSiblingDocumentTests
{
    [Fact]
    public void
        Create_RealRepositoryAddsSeparateRelationshipFromObservedCall()
    {
        AsyncSiblingAnalysis analysis = Analyze(
            FixtureCatalog.AnalysisAsyncSiblingRepository
                .AssemblyPath());
        AsyncSiblingRow read = Assert.Single(
            analysis.Rows,
            row =>
                row.Caller.Name == "Main"
                && row.Callee.Name == nameof(File.ReadAllText));
        Assert.Equal(
            nameof(File.ReadAllTextAsync),
            read.Alternative.Name);

        CallTreeNode source =
            analysis.CallGraph.BuildCallTree(
                read.Caller.MetadataToken,
                maxDepth: 3,
                maxNodes: 50);
        var candidate = new CallTreeNode(
            read.Alternative,
            Kind: null,
            CallTreeStatus.External,
            []);
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [source, candidate],
                maxNodes: 75);
        InspectionGraphDocument baseline =
            CallGraphInspectionGraphAdapter.Create(projection);

        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.SyncCalls);

        Assert.Same(analysis.SyncCalls, document.Result);
        Assert.Same(
            analysis.SyncCalls.Receipts.Single().Receipt,
            document.Receipt);
        Assert.Equal(
            analysis.Rows,
            document.Rows);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.MethodEvidence,
            analysis.Body.Receipt.Features);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(
            baseline.Nodes.Select(NodeShape),
            document.Graph.Nodes.Select(NodeShape));
        Assert.Equal(
            baseline.Edges.Select(EdgeShape),
            document.Graph.Edges
                .Take(baseline.Edges.Length)
                .Select(EdgeShape));
        Assert.Equal(
            baseline.Occurrences.Select(OccurrenceShape),
            document.Graph.Occurrences
                .Take(baseline.Occurrences.Length)
                .Select(OccurrenceShape));

        int opportunityIndex =
            document.Rows.IndexOf(read);
        CallGraphAsyncSiblingRelationshipJoin join =
            Assert.Single(
                document.RelationshipJoins,
                candidateJoin =>
                    candidateJoin.OpportunityIndex
                        == opportunityIndex);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.Source.Match);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.Candidate.Match);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.ObservedCallEdgeMatch);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.ObservedCallOccurrenceMatch);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.RelationshipEdgeMatch);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.RelationshipOccurrenceMatch);

        InspectionGraphEdge callEdge =
            document.Graph.Edges[
                join.ObservedCallEdgeId!.Value];
        InspectionGraphOccurrence callOccurrence =
            document.Graph.Occurrences[
                join.ObservedCallOccurrenceId!.Value];
        InspectionGraphEdge relationshipEdge =
            document.Graph.Edges[
                join.RelationshipEdgeId!.Value];
        InspectionGraphOccurrence relationshipOccurrence =
            document.Graph.Occurrences[
                join.RelationshipOccurrenceId!.Value];
        Assert.Same(
            CallGraphInspectionGraphCatalog.Call,
            callEdge.Relationship);
        Assert.IsType<CallGraphCallSiteEvidence>(
            callOccurrence.Evidence);
        Assert.Same(
            CallGraphAsyncSiblingCatalog.Opportunity,
            relationshipEdge.Relationship);
        Assert.NotSame(
            callEdge.Relationship,
            relationshipEdge.Relationship);
        Assert.Equal(
            join.Source.NodeId,
            relationshipEdge.FromNodeId);
        Assert.Equal(
            join.Candidate.NodeId,
            relationshipEdge.ToNodeId);
        Assert.Equal(
            [callOccurrence.Id],
            relationshipOccurrence.DerivedFromOccurrenceIds);
        Assert.Equal(
            read,
            Assert.IsType<
                CallGraphAsyncSiblingOpportunityEvidence>(
                    relationshipOccurrence.Evidence)
                .Row);
        Assert.DoesNotContain(
            callEdge.OccurrenceIds,
            occurrenceId =>
                occurrenceId
                    == relationshipOccurrence.Id);
    }

    [Fact]
    public void
        Create_ClassicAsyncPreservesAuthoredSourceAndMoveNextEvidence()
    {
        AsyncSiblingAnalysis analysis =
            Analyze(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        AsyncSiblingRow opportunity = Assert.Single(
            analysis.Rows,
            candidate =>
                candidate.Caller.DeclaringType.Name
                    == "ClassicAsyncSiblingFixture"
                && candidate.Caller.Name
                    == "CallsSyncSiblingFromAsync");
        MethodIdentity candidateMethod = Assert.Single(
            analysis.CallGraph.DeclaredMethods,
            method =>
                GraphNodeIdentity.FromMethod(method)
                    == GraphNodeIdentity.FromMember(
                        opportunity.Alternative));
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    analysis.CallGraph.BuildCallTree(
                        opportunity.Caller.MetadataToken,
                        maxDepth: 3,
                        maxNodes: 50),
                    analysis.CallGraph.BuildCallTree(
                        candidateMethod.MetadataToken,
                        maxDepth: 1,
                        maxNodes: 10),
                ],
                maxNodes: 75);

        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.SyncCalls);

        int opportunityIndex =
            document.Rows.IndexOf(opportunity);
        CallGraphAsyncSiblingRelationshipJoin join =
            Assert.Single(
                document.RelationshipJoins,
                candidate =>
                    candidate.OpportunityIndex
                        == opportunityIndex);
        InspectionGraphOccurrence relationship =
            document.Graph.Occurrences[
                join.RelationshipOccurrenceId!.Value];
        AsyncSiblingRow retained =
            Assert.IsType<
                CallGraphAsyncSiblingOpportunityEvidence>(
                    relationship.Evidence)
                .Row;
        Assert.Equal(opportunity.Caller, retained.Caller);
        Assert.NotEqual(
            retained.Caller,
            retained.SynchronousCall.Caller);
        Assert.Equal(
            retained.EvidenceMethodToken,
            retained.SynchronousCall.EvidenceMethod.MetadataToken);
        Assert.Equal(
            "MoveNext",
            retained.SynchronousCall.EvidenceMethod.Name);
    }

    [Fact]
    public void
        Create_RetainsOutsideCandidateAsExplicitNotProjectedJoin()
    {
        AsyncSiblingAnalysis analysis =
            Analyze(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        AsyncSiblingRow opportunity = Assert.Single(
            analysis.Rows,
            candidate =>
                candidate.Caller.DeclaringType.Name
                    == "ClassicAsyncSiblingFixture"
                && candidate.Caller.Name
                    == "CallsSyncSiblingFromAsync");
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                analysis.CallGraph.BuildCallTree(
                    opportunity.Caller.MetadataToken,
                    maxDepth: 3,
                    maxNodes: 50));
        InspectionGraphDocument baseline =
            CallGraphInspectionGraphAdapter.Create(projection);

        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.SyncCalls);

        CallGraphAsyncSiblingRelationshipJoin join =
            JoinFor(document, opportunity);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.Source.Match);
        Assert.Equal(
            InspectionGraphJoinMatch.NotProjected,
            join.Candidate.Match);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.ObservedCallOccurrenceMatch);
        Assert.Equal(
            InspectionGraphJoinMatch.NotProjected,
            join.RelationshipEdgeMatch);
        Assert.Equal(
            InspectionGraphJoinMatch.NotProjected,
            join.RelationshipOccurrenceMatch);
        Assert.Equal(baseline.Edges.Length, document.Graph.Edges.Length);
        Assert.Equal(
            baseline.Occurrences.Length,
            document.Graph.Occurrences.Length);
        Assert.Contains(opportunity, document.Rows);
    }

    [Fact]
    public void
        Create_DoesNotDeriveFromLogicalCallFallback()
    {
        AsyncSiblingAnalysis analysis =
            Analyze(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        AsyncSiblingRow opportunity = Assert.Single(
            analysis.Rows,
            candidate =>
                candidate.Caller.DeclaringType.Name
                    == "ClassicAsyncSiblingFixture"
                && candidate.Caller.Name
                    == "CallsSyncSiblingFromAsync");
        MethodIdentity candidateMethod = Assert.Single(
            analysis.CallGraph.DeclaredMethods,
            method =>
                GraphNodeIdentity.FromMethod(method)
                    == GraphNodeIdentity.FromMember(
                        opportunity.Alternative));
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    WithoutPhysicalCalls(
                        analysis.CallGraph.BuildCallTree(
                            opportunity.Caller.MetadataToken,
                            maxDepth: 3,
                            maxNodes: 50)),
                    analysis.CallGraph.BuildCallTree(
                        candidateMethod.MetadataToken,
                        maxDepth: 1,
                        maxNodes: 10),
                ],
                maxNodes: 75);

        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.SyncCalls);

        CallGraphAsyncSiblingRelationshipJoin join =
            JoinFor(document, opportunity);
        Assert.Equal(
            InspectionGraphJoinMatch.Found,
            join.ObservedCallEdgeMatch);
        Assert.Equal(
            InspectionGraphJoinMatch.NotProjected,
            join.ObservedCallOccurrenceMatch);
        Assert.Equal(
            InspectionGraphJoinMatch.NotProjected,
            join.RelationshipOccurrenceMatch);
        Assert.Empty(
            document.Graph.Edges.Where(edge =>
                ReferenceEquals(
                    edge.Relationship,
                    CallGraphAsyncSiblingCatalog.Opportunity)));
        Assert.Contains(
            document.Graph.Limits,
            limit => ReferenceEquals(
                limit.Payload.Descriptor,
                CallGraphInspectionGraphCatalog
                    .PhysicalOccurrencesUnavailable));
    }

    [Fact]
    public void Create_RejectsUnrequestedAsyncSiblingEvidence()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        AsyncSiblingAnalysis analysis = Analyze(path);
        MethodIdentity method =
            analysis.CallGraph.DeclaredMethods[0];
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                analysis.CallGraph.BuildCallTree(
                    method.MetadataToken,
                    maxDepth: 1,
                    maxNodes: 10));
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        SyncCallsInAsyncResult countOnly =
            SyncCallsInAsyncQuery.Execute(
                session,
                analysis.Binding,
                [SyncCallsInAsyncClosing.Count]);

        var error = Assert.Throws<InvalidOperationException>(
            () => CallGraphAsyncSiblingDocumentAdapter
                .Create(
                    projection,
                    countOnly));

        Assert.Contains(
            "were not requested",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        Composer_RepeatedRelationshipContributionReusesOccurrence()
    {
        AsyncSiblingAnalysis analysis = Analyze(
            FixtureCatalog.AnalysisAsyncSiblingRepository
                .AssemblyPath());
        AsyncSiblingRow opportunity = Assert.Single(
            analysis.Rows,
            candidate =>
                candidate.Caller.Name == "Main"
                && candidate.Callee.Name
                    == nameof(File.ReadAllText));
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    analysis.CallGraph.BuildCallTree(
                        opportunity.Caller.MetadataToken,
                        maxDepth: 3,
                        maxNodes: 50),
                    new CallTreeNode(
                        opportunity.Alternative,
                        Kind: null,
                        CallTreeStatus.External,
                        []),
                ],
                maxNodes: 75);
        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.SyncCalls);
        CallGraphAsyncSiblingRelationshipJoin join =
            JoinFor(document, opportunity);
        InspectionGraphOccurrence occurrence =
            document.Graph.Occurrences[
                join.RelationshipOccurrenceId!.Value];
        var contribution =
            new InspectionGraphRelationshipContribution(
                join.Source.NodeId!.Value,
                join.Candidate.NodeId!.Value,
                occurrence.Relationship,
                occurrence.SourceSubject,
                occurrence.TargetSubject,
                occurrence.Evidence,
                occurrence.DerivedFromOccurrenceIds);

        InspectionGraphRelationshipComposition repeated =
            InspectionGraphRelationshipComposer.Compose(
                document.Graph,
                [contribution, contribution]);

        Assert.Equal(
            document.Graph.Edges.Length,
            repeated.Document.Edges.Length);
        Assert.Equal(
            document.Graph.Occurrences.Length,
            repeated.Document.Occurrences.Length);
        Assert.Equal(2, repeated.Joins.Length);
        Assert.All(
            repeated.Joins,
            repeatedJoin =>
            {
                Assert.Equal(
                    join.RelationshipEdgeId,
                    repeatedJoin.EdgeId);
                Assert.Equal(
                    join.RelationshipOccurrenceId,
                    repeatedJoin.OccurrenceId);
            });
    }

    [Fact]
    public void
        Composer_ContradictoryEvidenceForOccurrenceIdentityFails()
    {
        AsyncSiblingAnalysis analysis = Analyze(
            FixtureCatalog.AnalysisAsyncSiblingRepository
                .AssemblyPath());
        AsyncSiblingRow opportunity = Assert.Single(
            analysis.Rows,
            candidate =>
                candidate.Caller.Name == "Main"
                && candidate.Callee.Name
                    == nameof(File.ReadAllText));
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    analysis.CallGraph.BuildCallTree(
                        opportunity.Caller.MetadataToken,
                        maxDepth: 3,
                        maxNodes: 50),
                    new CallTreeNode(
                        opportunity.Alternative,
                        Kind: null,
                        CallTreeStatus.External,
                        []),
                ],
                maxNodes: 75);
        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.SyncCalls);
        CallGraphAsyncSiblingRelationshipJoin join =
            JoinFor(document, opportunity);
        InspectionGraphOccurrence occurrence =
            document.Graph.Occurrences[
                join.RelationshipOccurrenceId!.Value];
        var contradictory =
            new InspectionGraphRelationshipContribution(
                join.Source.NodeId!.Value,
                join.Candidate.NodeId!.Value,
                occurrence.Relationship,
                occurrence.SourceSubject,
                occurrence.TargetSubject,
                occurrence.Evidence,
                []);

        var error = Assert.Throws<InvalidOperationException>(
            () => InspectionGraphRelationshipComposer.Compose(
                document.Graph,
                [contradictory]));

        Assert.Contains(
            "contradictory evidence",
            error.Message,
            StringComparison.Ordinal);
    }

    static AsyncSiblingAnalysis Analyze(string path)
    {
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "async-sibling graph test"));
        var binding = new AssemblyReferenceBindingAccess(
            assembly,
            resolver);
        LibraryBodyAnalysisExecution body =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .MethodEvidence),
                resolver);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assembly);
        SyncCallsInAsyncResult syncCalls =
            SyncCallsInAsyncQuery.Execute(
                session,
                binding,
                [SyncCallsInAsyncClosing.Rows]);
        return new(body, syncCalls, binding);
    }

    static CallGraphAsyncSiblingRelationshipJoin JoinFor(
        CallGraphAsyncSiblingDocument document,
        AsyncSiblingRow opportunity)
    {
        int index = document.Rows.IndexOf(opportunity);
        return Assert.Single(
            document.RelationshipJoins,
            join => join.OpportunityIndex == index);
    }

    sealed record AsyncSiblingAnalysis(
        LibraryBodyAnalysisExecution Body,
        SyncCallsInAsyncResult SyncCalls,
        AssemblyReferenceBindingAccess Binding)
    {
        internal LibraryCallGraphAnalysisResult CallGraph =>
            Body.CallGraph;

        internal ImmutableArray<AsyncSiblingRow> Rows =>
            Assert.IsType<SyncCallsInAsyncAnswer.Rows>(
                    SyncCalls.AnswerTo(
                        SyncCallsInAsyncClosing.Rows))
                .Calls;
    }

    static CallTreeNode WithoutPhysicalCalls(
        CallTreeNode node) =>
        node with
        {
            Children =
            [
                .. node.Children.Select(
                    WithoutPhysicalCalls),
            ],
            ParentEdgeCallSites =
                ImmutableArray<DirectCall>.Empty,
            ParentEdgeCallerDefinition = null,
        };

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
        InspectionGraphRelationshipDescriptor Relationship,
        ImmutableArray<int> OccurrenceIds) EdgeShape(
            InspectionGraphEdge edge) =>
        (
            edge.Id,
            edge.FromNodeId,
            edge.ToNodeId,
            edge.Relationship,
            edge.OccurrenceIds);

    static (
        int Id,
        InspectionGraphSubject Source,
        InspectionGraphSubject Target,
        IInspectionGraphOccurrenceEvidence Evidence,
        ImmutableArray<int> DerivedFrom) OccurrenceShape(
            InspectionGraphOccurrence occurrence) =>
        (
            occurrence.Id,
            occurrence.SourceSubject,
            occurrence.TargetSubject,
            occurrence.Evidence,
            occurrence.DerivedFromOccurrenceIds);
}
