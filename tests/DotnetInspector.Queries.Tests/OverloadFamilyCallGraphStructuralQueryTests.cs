using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class OverloadFamilyCallGraphStructuralQueryTests
{
    static readonly Lazy<LibraryBodyAnalysisExecution> FixtureAnalysis =
        new(() => Analyze(
            FixtureCatalog.AnalysisOverloadFamilyLens.AssemblyPath()));

    [Fact]
    public void Derive_DirectSiblingChainRetainsTransitiveEntryOrigin()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "SiblingDelegationChain",
            "Parse",
            parameterCount: 1);

        OverloadFamilyCallGraphStructuralDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 3,
            maxNodes: 12);

        Assert.Equal(
            OverloadFamilyStructuralFactStatus.Complete,
            document.Status);
        Assert.Equal(3, document.FamilyComponents.Length);
        OverloadFamilyComponent entry =
            Assert.Single(document.EntryComponents);
        OverloadFamilyNodeStructuralFact terminal = NodeFact(
            document,
            "Parse",
            "Int32");
        Assert.Equal(
            [entry.Id],
            terminal.OriginEntryComponentIds);
        Assert.Equal(
            OverloadFamilyConvergenceKind.None,
            terminal.Convergence);
    }

    [Fact]
    public void Derive_TwoEntriesConvergeOnFamilyOverload()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "FamilyOverloadConvergence",
            "Convert",
            parameterCount: 1);

        OverloadFamilyCallGraphStructuralDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 2,
            maxNodes: 12);

        Assert.Equal(
            OverloadFamilyStructuralFactStatus.Complete,
            document.Status);
        OverloadFamilyComponent[] entries = [
            .. document.EntryComponents,
        ];
        Assert.Equal(2, entries.Length);
        OverloadFamilyNodeStructuralFact convergence =
            Assert.Single(document.FamilyConvergencePoints);
        Assert.Equal("Int32", ParameterType(document, convergence));
        Assert.Equal(
            entries.Select(static entry => entry.Id),
            convergence.OriginEntryComponentIds);
        Assert.DoesNotContain(
            document.ImplementationConvergencePoints,
            static _ => true);
    }

    [Fact]
    public void Derive_PublicEntriesConvergeOnlyOnPrivateHelper()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "PrivateHelperConvergence",
            "Normalize",
            parameterCount: 1);

        OverloadFamilyCallGraphStructuralDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 2,
            maxNodes: 16);

        Assert.Equal(2, document.EntryComponents.Count());
        Assert.Empty(document.FamilyConvergencePoints);
        OverloadFamilyNodeStructuralFact helper = Assert.Single(
            document.ImplementationConvergencePoints,
            fact => Member(document, fact).Name == "NormalizeCore");
        Assert.Equal(2, helper.OriginEntryComponentIds.Length);
        Assert.Null(helper.FamilyComponentId);
    }

    [Fact]
    public void Derive_IndependentImplementationsRemainSeparateEntries()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "IndependentImplementations",
            "Measure",
            parameterCount: 1);

        OverloadFamilyCallGraphStructuralDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 1,
            maxNodes: 12);

        Assert.Equal(2, document.EntryComponents.Count());
        Assert.All(
            document.FamilyComponents,
            component =>
            {
                Assert.True(component.IsEntry);
                Assert.Equal(
                    [component.Id],
                    component.OriginEntryComponentIds);
            });
        Assert.Empty(document.FamilyConvergencePoints);
        Assert.Empty(document.ImplementationConvergencePoints);
    }

    [Fact]
    public void Derive_RecursiveFamilyCollapsesIntoOneEntryComponent()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "RecursiveSiblings",
            "Traverse",
            parameterCount: 1);

        OverloadFamilyCallGraphStructuralDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 3,
            maxNodes: 12);

        OverloadFamilyComponent component =
            Assert.Single(document.FamilyComponents);
        Assert.True(component.IsEntry);
        Assert.Equal(2, component.MemberNodeIds.Length);
        Assert.Equal([component.Id], component.OriginEntryComponentIds);
        Assert.All(
            component.MemberNodeIds,
            nodeId => Assert.Equal(
                [component.Id],
                document.NodeFacts[nodeId].OriginEntryComponentIds));
    }

    [Fact]
    public void Derive_ConstructorNewObjectDoesNotCreateDelegation()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity delegationAnchor = Method(
            callGraph,
            "ConstructorDelegation",
            ".ctor",
            parameterCount: 0);
        MethodIdentity constructionAnchor = Method(
            callGraph,
            "ConstructorConstruction",
            ".ctor",
            parameterCount: 0);

        OverloadFamilyCallGraphStructuralDocument delegation = Execute(
            callGraph,
            delegationAnchor,
            maxDepth: 2,
            maxNodes: 12);
        OverloadFamilyCallGraphStructuralDocument construction = Execute(
            callGraph,
            constructionAnchor,
            maxDepth: 2,
            maxNodes: 12);

        Assert.Single(delegation.EntryComponents);
        Assert.Equal(2, construction.EntryComponents.Count());
        InspectionGraphEdge constructorEdge = Assert.Single(
            construction.Graph.Edges,
            edge =>
                construction.NodeFacts[edge.FromNodeId]
                    .FamilyComponentId.HasValue
                && construction.NodeFacts[edge.ToNodeId]
                    .FamilyComponentId.HasValue);
        Assert.IsType<CallGraphCallSiteEvidence>(
            Assert.Single(
                constructorEdge.OccurrenceIds.Select(
                    id => construction.Graph.Occurrences[id]))
                .Evidence);
        Assert.Equal(
            CallKind.NewObject,
            Assert.IsType<CallGraphCallSiteEvidence>(
                construction.Graph.Occurrences[
                    constructorEdge.OccurrenceIds[0]].Evidence)
                .CallKind);
        Assert.All(
            construction.FamilyComponents,
            component => Assert.Equal(
                [component.Id],
                component.OriginEntryComponentIds));
    }

    [Fact]
    public void Derive_IncompleteTraversalProducesCandidateFacts()
    {
        LibraryCallGraphAnalysisResult callGraph =
            FixtureAnalysis.Value.CallGraph;
        MethodIdentity anchor = Method(
            callGraph,
            "FamilyOverloadConvergence",
            "Convert",
            parameterCount: 1);

        OverloadFamilyCallGraphStructuralDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 0,
            maxNodes: 3);

        Assert.Equal(
            OverloadFamilyStructuralFactStatus.Candidate,
            document.Status);
        Assert.Equal(3, document.EntryComponents.Count());
        Assert.Empty(document.FamilyConvergencePoints);
        Assert.Contains(
            document.Graph.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog.TraversalIncomplete));
    }

    [Fact]
    public void Derive_SystemTextJsonRetainsEntriesAndSharedHelperConvergence()
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

        OverloadFamilyCallGraphStructuralDocument document = Execute(
            callGraph,
            anchor,
            maxDepth: 2,
            maxNodes: 500);

        Assert.Equal(
            15,
            document.FamilyComponents.Sum(component =>
                component.MemberNodeIds.Length));
        Assert.NotEmpty(document.EntryComponents);
        OverloadFamilyNodeStructuralFact convergence =
            Assert.Single(
                document.ImplementationConvergencePoints.Take(1));
        Assert.True(convergence.OriginEntryComponentIds.Length > 1);
        Assert.DoesNotContain(
            document.Graph.Seeds,
            seed => seed.Target.Id == convergence.NodeId);
    }

    static LibraryBodyAnalysisExecution Analyze(string path) =>
        LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence));

    static OverloadFamilyCallGraphStructuralDocument Execute(
        LibraryCallGraphAnalysisResult callGraph,
        MethodIdentity anchor,
        int maxDepth,
        int maxNodes) =>
        OverloadFamilyCallGraphStructuralQuery.Execute(
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

    static OverloadFamilyNodeStructuralFact NodeFact(
        OverloadFamilyCallGraphStructuralDocument document,
        string name,
        string parameterType) =>
        Assert.Single(
            document.NodeFacts,
            fact =>
                Member(document, fact).Name == name
                && ParameterType(document, fact) == parameterType);

    static string ParameterType(
        OverloadFamilyCallGraphStructuralDocument document,
        OverloadFamilyNodeStructuralFact fact) =>
        Assert.Single(Member(document, fact).ParameterTypes).Name;

    static MemberRef Member(
        OverloadFamilyCallGraphStructuralDocument document,
        OverloadFamilyNodeStructuralFact fact) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                document.Graph.Nodes[fact.NodeId].Subject)
                .Identity)
            .Member;
}
