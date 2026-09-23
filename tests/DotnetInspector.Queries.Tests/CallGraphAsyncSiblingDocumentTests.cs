using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class CallGraphAsyncSiblingDocumentTests
{
    [Fact]
    public void
        Create_RealRepositoryAddsSeparateRelationshipFromObservedCall()
    {
        LibraryBodyAnalysisExecution analysis = Analyze(
            FixtureCatalog.AnalysisAsyncSiblingRepository
                .AssemblyPath());
        OptimizationOpportunity read = Assert.Single(
            analysis.Optimization.Opportunities,
            opportunity =>
                opportunity.Method.Name == "Main"
                && opportunity.AsyncSibling?.SynchronousCall
                    .Callee.Name == nameof(File.ReadAllText));
        AsyncSiblingOpportunityEvidence evidence =
            Assert.IsType<AsyncSiblingOpportunityEvidence>(
                read.AsyncSibling);
        Assert.Equal(
            nameof(File.ReadAllTextAsync),
            evidence.AsyncCandidate.Name);

        CallTreeNode source =
            analysis.CallGraph.BuildCallTree(
                read.Method.MetadataToken,
                maxDepth: 3,
                maxNodes: 50);
        var candidate = new CallTreeNode(
            evidence.AsyncCandidate,
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
                analysis.Optimization);

        Assert.Same(analysis.Receipt, document.Receipt);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures
                    .AsyncSiblingOpportunities,
            document.Receipt.Features);
        Assert.Equal(
            analysis.Optimization.Opportunities,
            document.Opportunities);
        Assert.True(document.HasFullMethodEvidenceScope);
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
            document.Opportunities.IndexOf(read);
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
                .Opportunity);
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
        LibraryBodyAnalysisExecution analysis =
            Analyze(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        OptimizationOpportunity opportunity = Assert.Single(
            analysis.Optimization.Opportunities,
            candidate =>
                candidate.Method.DeclaringType.Name
                    == "ClassicAsyncSiblingFixture"
                && candidate.Method.Name
                    == "CallsSyncSiblingFromAsync"
                && candidate.AsyncSibling is not null);
        AsyncSiblingOpportunityEvidence evidence =
            opportunity.AsyncSibling!;
        MethodIdentity candidateMethod = Assert.Single(
            analysis.CallGraph.DeclaredMethods,
            method =>
                GraphNodeIdentity.FromMethod(method)
                    == GraphNodeIdentity.FromMember(
                        evidence.AsyncCandidate));
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    analysis.CallGraph.BuildCallTree(
                        opportunity.Method.MetadataToken,
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
                analysis.Optimization);

        int opportunityIndex =
            document.Opportunities.IndexOf(opportunity);
        CallGraphAsyncSiblingRelationshipJoin join =
            Assert.Single(
                document.RelationshipJoins,
                candidate =>
                    candidate.OpportunityIndex
                        == opportunityIndex);
        InspectionGraphOccurrence relationship =
            document.Graph.Occurrences[
                join.RelationshipOccurrenceId!.Value];
        OptimizationOpportunity retained =
            Assert.IsType<
                CallGraphAsyncSiblingOpportunityEvidence>(
                    relationship.Evidence)
                .Opportunity;
        Assert.Equal(opportunity.Method, retained.Method);
        Assert.NotEqual(
            retained.Method,
            retained.AsyncSibling!.SynchronousCall.Caller);
        Assert.Equal(
            retained.EvidenceMethodToken,
            retained.AsyncSibling.SynchronousCall
                .EvidenceMethod.MetadataToken);
        Assert.Equal(
            "MoveNext",
            retained.AsyncSibling.SynchronousCall
                .EvidenceMethod.Name);
    }

    [Fact]
    public void
        Create_RetainsOutsideCandidateAsExplicitNotProjectedJoin()
    {
        LibraryBodyAnalysisExecution analysis =
            Analyze(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        OptimizationOpportunity opportunity = Assert.Single(
            analysis.Optimization.Opportunities,
            candidate =>
                candidate.Method.DeclaringType.Name
                    == "ClassicAsyncSiblingFixture"
                && candidate.Method.Name
                    == "CallsSyncSiblingFromAsync"
                && candidate.AsyncSibling is not null);
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                analysis.CallGraph.BuildCallTree(
                    opportunity.Method.MetadataToken,
                    maxDepth: 3,
                    maxNodes: 50));
        InspectionGraphDocument baseline =
            CallGraphInspectionGraphAdapter.Create(projection);

        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.Optimization);

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
        Assert.Contains(opportunity, document.Opportunities);
    }

    [Fact]
    public void
        Create_DoesNotDeriveFromLogicalCallFallback()
    {
        LibraryBodyAnalysisExecution analysis =
            Analyze(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        OptimizationOpportunity opportunity = Assert.Single(
            analysis.Optimization.Opportunities,
            candidate =>
                candidate.Method.DeclaringType.Name
                    == "ClassicAsyncSiblingFixture"
                && candidate.Method.Name
                    == "CallsSyncSiblingFromAsync"
                && candidate.AsyncSibling is not null);
        AsyncSiblingOpportunityEvidence evidence =
            opportunity.AsyncSibling!;
        MethodIdentity candidateMethod = Assert.Single(
            analysis.CallGraph.DeclaredMethods,
            method =>
                GraphNodeIdentity.FromMethod(method)
                    == GraphNodeIdentity.FromMember(
                        evidence.AsyncCandidate));
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    WithoutPhysicalCalls(
                        analysis.CallGraph.BuildCallTree(
                            opportunity.Method.MetadataToken,
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
                analysis.Optimization);

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
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .PhysicalOccurrencesUnavailable));
    }

    [Fact]
    public void Create_RejectsUnrequestedAsyncSiblingEvidence()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .MethodEvidence));
        MethodIdentity method =
            analysis.CallGraph.DeclaredMethods[0];
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                analysis.CallGraph.BuildCallTree(
                    method.MetadataToken,
                    maxDepth: 1,
                    maxNodes: 10));

        var error = Assert.Throws<InvalidOperationException>(
            () => CallGraphAsyncSiblingDocumentAdapter
                .Create(
                    projection,
                    analysis.Optimization));

        Assert.Contains(
            "were not requested",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        Composer_RepeatedRelationshipContributionReusesOccurrence()
    {
        LibraryBodyAnalysisExecution analysis = Analyze(
            FixtureCatalog.AnalysisAsyncSiblingRepository
                .AssemblyPath());
        OptimizationOpportunity opportunity = Assert.Single(
            analysis.Optimization.Opportunities,
            candidate =>
                candidate.Method.Name == "Main"
                && candidate.AsyncSibling?.SynchronousCall
                    .Callee.Name == nameof(File.ReadAllText));
        AsyncSiblingOpportunityEvidence evidence =
            opportunity.AsyncSibling!;
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    analysis.CallGraph.BuildCallTree(
                        opportunity.Method.MetadataToken,
                        maxDepth: 3,
                        maxNodes: 50),
                    new CallTreeNode(
                        evidence.AsyncCandidate,
                        Kind: null,
                        CallTreeStatus.External,
                        []),
                ],
                maxNodes: 75);
        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.Optimization);
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
        LibraryBodyAnalysisExecution analysis = Analyze(
            FixtureCatalog.AnalysisAsyncSiblingRepository
                .AssemblyPath());
        OptimizationOpportunity opportunity = Assert.Single(
            analysis.Optimization.Opportunities,
            candidate =>
                candidate.Method.Name == "Main"
                && candidate.AsyncSibling?.SynchronousCall
                    .Callee.Name == nameof(File.ReadAllText));
        AsyncSiblingOpportunityEvidence evidence =
            opportunity.AsyncSibling!;
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                [
                    analysis.CallGraph.BuildCallTree(
                        opportunity.Method.MetadataToken,
                        maxDepth: 3,
                        maxNodes: 50),
                    new CallTreeNode(
                        evidence.AsyncCandidate,
                        Kind: null,
                        CallTreeStatus.External,
                        []),
                ],
                maxNodes: 75);
        CallGraphAsyncSiblingDocument document =
            CallGraphAsyncSiblingDocumentAdapter.Create(
                projection,
                analysis.Optimization);
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

    static LibraryBodyAnalysisExecution Analyze(string path)
    {
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        return LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures
                    .AsyncSiblingOpportunities),
            resolver);
    }

    static CallGraphAsyncSiblingRelationshipJoin JoinFor(
        CallGraphAsyncSiblingDocument document,
        OptimizationOpportunity opportunity)
    {
        int index = document.Opportunities.IndexOf(opportunity);
        return Assert.Single(
            document.RelationshipJoins,
            join => join.OpportunityIndex == index);
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
