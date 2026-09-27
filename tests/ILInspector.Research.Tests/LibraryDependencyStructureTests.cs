using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

public sealed class LibraryDependencyStructureTests
{
    static LibraryDependencyStructureDocument Document(
        string fixtureId = FixtureIds.ResearchDependencyStructure)
    {
        var available = Assert.IsType<LibraryDependencyStructureResult.Available>(
            LibraryDependencyStructure.Execute(
                LibraryBodyAnalysisService.ExecutePath(
                    FixtureCatalog.Get(fixtureId).AssemblyPath(),
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.MethodEvidence))));
        return available.Document;
    }

    static LibraryDependencyNamespaceNode Namespace(
        LibraryDependencyStructureDocument document,
        string name) =>
        Assert.Single(document.Namespaces, node => node.Namespace == name);

    static LibraryDependencyNamespaceEdge NamespaceEdge(
        LibraryDependencyStructureDocument document,
        string source,
        string target) =>
        Assert.Single(
            document.NamespaceEdges,
            edge => edge.SourceNamespace == source
                && edge.TargetNamespace == target);

    [Fact]
    public void LibraryDependencyStructure_RejectsScopedOrCallFreeExecution()
    {
        string path = FixtureCatalog.ResearchDependencyStructure.AssemblyPath();

        var callFree = Assert.IsType<LibraryDependencyStructureResult.Unavailable>(
            LibraryDependencyStructure.Execute(
                LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.None))));
        Assert.Equal(
            LibraryDependencyStructureUnavailableReason.MethodEvidenceNotRequested,
            callFree.Reason);

        LibraryBodyAnalysisExecution full = LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence));
        int oneBody = full.CallGraph.Methods[0].MetadataToken;
        var scoped = Assert.IsType<LibraryDependencyStructureResult.Unavailable>(
            LibraryDependencyStructure.Execute(
                LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.MethodEvidence,
                        bodyScope: new HashSet<int> { oneBody }))));
        Assert.Equal(
            LibraryDependencyStructureUnavailableReason.NonWholeLibraryScope,
            scoped.Reason);
        Assert.False(scoped.Receipt.HasFullMethodEvidenceScope);
    }

    [Fact]
    public void LibraryDependencyStructure_AttributesLiftedBodiesToLogicalOwner()
    {
        LibraryDependencyStructureDocument document = Document();

        // Lambda, local function, and async MoveNext calls are attributed to
        // Lifted.Owner; the sync iterator keeps its physical container.
        LibraryDependencyTypeEdge owner = Assert.Single(
            document.TypeEdges,
            edge => edge.SourceTypeKey == "Lifted.Owner"
                && edge.TargetTypeKey == "Epsilon.E");
        Assert.Equal(5, owner.Counts.Invocations);
        Assert.Contains(
            document.TypeEdges,
            edge => edge.SourceTypeKey.StartsWith("Lifted.Owner+<Iterator>", StringComparison.Ordinal)
                && edge.TargetTypeKey == "Epsilon.E");
        Assert.Equal(6, NamespaceEdge(document, "Lifted", "Epsilon").Counts.Invocations);
    }

    [Fact]
    public void LibraryDependencyStructure_AcceptsDistinctLiftedBodiesAtEqualOffsets()
    {
        LibraryCallGraphAnalysisResult callGraph = LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.ResearchDependencyStructure.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(LibraryBodyAnalysisFeatures.MethodEvidence))
            .CallGraph;
        DirectCall[] lambdaCalls =
        [
            .. callGraph.DirectCalls.Where(static call =>
                call.Caller.Name == "TwoLambdas"
                && call.Callee.Name == "Value"),
        ];

        Assert.Equal(2, lambdaCalls.Length);
        Assert.Equal(lambdaCalls[0].ILOffset, lambdaCalls[1].ILOffset);
        Assert.NotEqual(
            lambdaCalls[0].EvidenceMethod.MetadataToken,
            lambdaCalls[1].EvidenceMethod.MetadataToken);
        Assert.IsType<LibraryDependencyStructureResult.Available>(
            LibraryDependencyStructure.Execute(callGraph));
    }

    [Fact]
    public void LibraryDependencyStructure_ResolvesCallsThroughGenericInstantiations()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyTypeEdge user = Assert.Single(
            document.TypeEdges,
            static edge => edge.SourceTypeKey == "BoxUser.User"
                && edge.TargetTypeKey == "Boxes.Box`1");
        Assert.Equal(4, user.Counts.Invocations);
        Assert.Equal(
            2,
            Assert.Single(document.Types, static node => node.TypeKey == "Boxes.Box`1")
                .IntraTypeRelationshipCount);
        Assert.Equal(4, NamespaceEdge(document, "BoxUser", "Boxes").Counts.Invocations);
    }

    [Fact]
    public void LibraryDependencyStructure_KeysExternalNodesByExactReference()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyExternalNode[] referenced =
        [
            .. document.ExternalNamespaceEdges
                .Where(static edge => edge.SourceNamespace == "Referenced")
                .Select(edge => Assert.Single(
                    document.ExternalNodes,
                    node => node.Key == edge.ExternalKey)),
        ];

        LibraryDependencyExternalNode task = Assert.Single(
            referenced,
            static node => node.Namespace == "System.Threading.Tasks");
        LibraryDependencyExternalNode list = Assert.Single(
            referenced,
            static node => node.Namespace == "System.Collections.Generic");
        // Exactly as referenced: no forwarding to, or folding into, the core library.
        Assert.Equal("System.Runtime", task.Assembly!.Name);
        Assert.Equal("System.Collections", list.Assembly!.Name);
        Assert.NotEqual(task.Key, list.Key);
    }

    [Fact]
    public void LibraryDependencyStructure_ProjectsNestedAndGlobalNamespaces()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyTypeNode inner = Assert.Single(
            document.Types,
            node => node.TypeKey == "Beta.Outer+Inner");
        Assert.Equal("Beta", inner.Namespace);

        LibraryDependencyNamespaceNode global = Namespace(document, "");
        Assert.True(global.IsGlobalNamespace);
        Assert.Equal(1, global.TypeCount);
        Assert.Equal(1, NamespaceEdge(document, "", "Epsilon").Counts.Invocations);
    }

    [Fact]
    public void LibraryDependencyStructure_SeparatesInvocationAndFunctionReference()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyNamespaceEdge pointers = NamespaceEdge(document, "Pointers", "Epsilon");
        Assert.Equal(0, pointers.Counts.Invocations);
        Assert.Equal(1, pointers.Counts.FunctionReferences);
        // A function reference still orders the namespaces.
        Assert.Equal(
            Namespace(document, "Epsilon").Level + 1,
            Namespace(document, "Pointers").Level);
    }

    [Fact]
    public void LibraryDependencyStructure_ProjectsExternalGenericTargetToDeclaringNamespace()
    {
        LibraryDependencyStructureDocument document = Document();

        Assert.DoesNotContain(
            document.NamespaceEdges,
            edge => edge.SourceNamespace == "Generic");
        LibraryDependencyExternalNamespaceEdge external = Assert.Single(
            document.ExternalNamespaceEdges,
            edge => edge.SourceNamespace == "Generic");
        LibraryDependencyExternalNode node = Assert.Single(
            document.ExternalNodes,
            candidate => candidate.Key == external.ExternalKey);
        Assert.Equal("System.Collections.Generic", node.Namespace);
        Assert.False(node.IsIntrinsicCoreLibrary);
        Assert.NotNull(node.Assembly);
    }

    [Fact]
    public void LibraryDependencyStructure_DerivesCyclesAndLevels()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyNamespaceCycle cycle = Assert.Single(document.Cycles);
        Assert.Equal(["Alpha", "Beta", "Gamma"], cycle.Namespaces);
        Assert.All(
            cycle.Namespaces,
            name =>
            {
                Assert.Equal(0, Namespace(document, name).CycleIndex);
                Assert.Equal(1, Namespace(document, name).Level);
            });
        Assert.Equal(0, Namespace(document, "Epsilon").Level);
        Assert.Null(Namespace(document, "Epsilon").CycleIndex);
        Assert.Equal(2, Namespace(document, "Delta").Level);
    }

    [Fact]
    public void LibraryDependencyStructure_QualifiesAbsenceUnderIncompleteEvidence()
    {
        LibraryDependencyStructureDocument qualified = Document();
        Assert.Equal(LibraryDependencyCompleteness.Qualified, qualified.Completeness);
        Assert.True(qualified.Population.UnresolvedCallCount > 0);

        LibraryDependencyStructureDocument complete = Document(FixtureIds.AnalysisCallOverloads);
        Assert.Equal(LibraryDependencyCompleteness.Complete, complete.Completeness);
        Assert.Equal(0, complete.Population.UnresolvedCallCount);
        Assert.Equal(0, complete.Population.IncompleteBodyCount);
        Assert.Empty(complete.Cycles);
    }

    [Fact]
    public void LibraryDependencyStructure_PartitionsExaminedCallsExactly()
    {
        LibraryDependencyStructureDocument document = Document();
        LibraryDependencyPopulationReceipt population = document.Population;

        Assert.Equal(
            population.ExaminedCallCount,
            population.InternalCallCount
                + population.ExternalCallCount
                + population.RuntimeProvidedCallCount
                + population.UnresolvedCallCount);
        Assert.Equal(1, population.RuntimeProvidedCallCount);
        LibraryDependencyUnresolvedCount indirect = Assert.Single(population.UnresolvedReasons);
        Assert.Equal(LibraryDependencyUnresolvedReason.Indirect, indirect.Reason);
        Assert.Equal(1, indirect.Count);
        Assert.Equal(
            population.SameTypeCallCount,
            document.Types.Sum(static node => node.IntraTypeRelationshipCount));
        Assert.Equal(
            population.InternalCallCount - population.SameTypeCallCount,
            document.TypeEdges.Sum(static edge => edge.Counts.Total));
        Assert.Equal(
            population.ExternalCallCount,
            document.ExternalTypeEdges.Sum(static edge => edge.Counts.Total));
    }

    [Fact]
    public void LibraryDependencyStructure_BoundsExplanationWithExactRemainder()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyNamespaceEdge edge = NamespaceEdge(document, "Explained", "Epsilon");
        Assert.Equal(7, edge.ContributingTypeEdgeCount);
        Assert.Equal(2, edge.RemainingContributorCount);
        Assert.Equal(28, edge.Counts.Invocations);
        Assert.Equal(
            ["Explained.T7", "Explained.T6", "Explained.T5", "Explained.T4", "Explained.T3"],
            edge.ExplainingTypeEdges.Select(static explaining => explaining.SourceTypeKey));
    }

    [Fact]
    public void LibraryDependencyStructure_ExternalKeysNeverCollideAcrossSeparators()
    {
        // Untrusted assembly names and namespaces may contain any separator
        // text; C# cannot emit these, so the key function is gated directly.
        const string tail = ", Version=*, Culture=neutral, PublicKeyToken=null::";
        var first = new ILInspector.Metadata.AssemblyReferenceIdentity("E", null, null, null);
        var second = new ILInspector.Metadata.AssemblyReferenceIdentity("E" + tail + "Q", null, null, null);

        string firstKey = LibraryDependencyStructure.ExternalKey(first, "Q" + tail + "R");
        string secondKey = LibraryDependencyStructure.ExternalKey(second, "R");

        Assert.NotEqual(firstKey, secondKey);
        Assert.NotEqual(
            LibraryDependencyStructure.ExternalKey(new("A;namespace=1:B", null, null, null), ""),
            LibraryDependencyStructure.ExternalKey(new("A", null, null, null), "B"));
        Assert.NotEqual(
            LibraryDependencyStructure.ExternalKey(null, "N"),
            LibraryDependencyStructure.ExternalKey(new("<intrinsic-core-library>", null, null, null), "N"));
    }

    [Fact]
    public void NamespaceStructure_DerivesDeepChainsWithoutRecursion()
    {
        // A hostile assembly can declare an arbitrarily long namespace chain;
        // derivation must not depend on call-stack depth.
        const int depth = 200_000;
        string[] namespaces = [.. Enumerable.Range(0, depth).Select(static i => $"N{i:D6}")];
        LibraryDependencyCounts one = new(1, 0);
        LibraryDependencyNamespaceEdge[] edges =
        [
            .. Enumerable.Range(0, depth - 1).Select(i => new LibraryDependencyNamespaceEdge(
                namespaces[i], namespaces[i + 1], one, 1, [], 0)),
            new(namespaces[depth - 1], namespaces[depth - 2], one, 1, [], 0),
        ];

        var (cycles, cycleIndex, levels) = NamespaceStructure.Derive(namespaces, edges);

        LibraryDependencyNamespaceCycle cycle = Assert.Single(cycles);
        Assert.Equal([namespaces[depth - 2], namespaces[depth - 1]], cycle.Namespaces);
        Assert.Equal(0, levels[namespaces[depth - 1]]);
        Assert.Equal(depth - 2, levels[namespaces[0]]);
        Assert.Equal(2, cycleIndex.Count);
    }

    [Fact]
    public void LibraryDependencyStructure_UsesLibraryMetricsTypeKeys()
    {
        LibraryDependencyStructureDocument document = Document();

        Assert.All(
            document.Types,
            static node => Assert.Equal(
                LibraryStructuralReport.TypeKey(node.Type),
                node.TypeKey));
        HashSet<string> keys = [.. document.Types.Select(static node => node.TypeKey)];
        Assert.All(
            document.TypeEdges,
            edge =>
            {
                Assert.Contains(edge.SourceTypeKey, keys);
                Assert.Contains(edge.TargetTypeKey, keys);
            });
    }
}
