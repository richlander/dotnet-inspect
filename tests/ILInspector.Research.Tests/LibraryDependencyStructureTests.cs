using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

public sealed class LibraryDependencyStructureTests
{
    static LibraryDependencyStructureDocument Document(
        string fixtureId = FixtureIds.ResearchDependencyStructure)
    {
        var available =
            Assert.IsType<LibraryDependencyStructureResult.Available>(
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
        Assert.Single(
            document.Namespaces,
            node => node.Namespace == name);

    static LibraryDependencyNamespaceEdge NamespaceEdge(
        LibraryDependencyStructureDocument document,
        string source,
        string target) =>
        Assert.Single(
            document.NamespaceEdges,
            edge => edge.SourceNamespace == source
                && edge.TargetNamespace == target);

    [Fact]
    public void
        LibraryDependencyStructure_RejectsScopedOrCallFreeExecution()
    {
        string path =
            FixtureCatalog.ResearchDependencyStructure.AssemblyPath();

        var callFree =
            Assert.IsType<LibraryDependencyStructureResult.Unavailable>(
                LibraryDependencyStructure.Execute(
                    LibraryBodyAnalysisService.ExecutePath(
                        path,
                        LibraryBodyAnalysisRequest.Create(
                            LibraryBodyAnalysisFeatures.None))));
        Assert.Equal(
            LibraryDependencyStructureUnavailableReason
                .MethodEvidenceNotRequested,
            callFree.Reason);

        LibraryBodyAnalysisExecution full =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        int oneBody = full.CallGraph.Methods[0].MetadataToken;
        var scoped =
            Assert.IsType<LibraryDependencyStructureResult.Unavailable>(
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
    public void
        LibraryDependencyStructure_AttributesLiftedBodiesToLogicalOwner()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyTypeEdge owner = Assert.Single(
            document.TypeEdges,
            edge => edge.SourceTypeKey == "Lifted.Owner"
                && edge.TargetTypeKey == "Epsilon.E");
        Assert.Equal(5, owner.Counts.Invocations);
        Assert.Contains(
            document.TypeEdges,
            edge => edge.SourceTypeKey.StartsWith(
                    "Lifted.Owner+<Iterator>",
                    StringComparison.Ordinal)
                && edge.TargetTypeKey == "Epsilon.E");
        Assert.Equal(
            6,
            NamespaceEdge(
                document,
                "Lifted",
                "Epsilon").Counts.Invocations);
    }

    [Fact]
    public void
        LibraryDependencyStructure_AcceptsDistinctLiftedBodiesAtEqualOffsets()
    {
        LibraryCallGraphAnalysisResult callGraph =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.ResearchDependencyStructure.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence))
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
    public void
        LibraryDependencyStructure_ResolvesCallsThroughGenericInstantiations()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyTypeEdge user = Assert.Single(
            document.TypeEdges,
            static edge => edge.SourceTypeKey == "BoxUser.User"
                && edge.TargetTypeKey == "Boxes.Box`1");
        Assert.Equal(4, user.Counts.Invocations);
        Assert.Equal(
            2,
            Assert.Single(
                document.Types,
                static node => node.TypeKey == "Boxes.Box`1")
            .IntraTypeRelationshipCount);
        Assert.Equal(
            4,
            NamespaceEdge(
                document,
                "BoxUser",
                "Boxes").Counts.Invocations);
    }

    [Fact]
    public void
        LibraryDependencyStructure_KeysExternalNodesByExactReference()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyExternalNode[] referenced =
        [
            .. document.ExternalNamespaceEdges
                .Where(static edge =>
                    edge.SourceNamespace == "Referenced")
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
        Assert.Equal("System.Runtime", task.Assembly!.Name);
        Assert.Equal("System.Collections", list.Assembly!.Name);
        Assert.NotEqual(task.Key, list.Key);
    }

    [Fact]
    public void
        LibraryDependencyStructure_ProjectsNestedAndGlobalNamespaces()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyTypeNode inner = Assert.Single(
            document.Types,
            node => node.TypeKey == "Beta.Outer+Inner");
        Assert.Equal("Beta", inner.Namespace);

        LibraryDependencyNamespaceNode global = Namespace(document, "");
        Assert.True(global.IsGlobalNamespace);
        Assert.Equal(1, global.TypeCount);
        Assert.Equal(
            1,
            NamespaceEdge(document, "", "Epsilon").Counts.Invocations);
    }

    [Fact]
    public void
        LibraryDependencyStructure_SeparatesInvocationAndFunctionReference()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyNamespaceEdge pointers =
            NamespaceEdge(document, "Pointers", "Epsilon");
        Assert.Equal(0, pointers.Counts.Invocations);
        Assert.Equal(1, pointers.Counts.FunctionReferences);
        Assert.Equal(
            Namespace(document, "Epsilon").Level + 1,
            Namespace(document, "Pointers").Level);
    }

    [Fact]
    public void
        LibraryDependencyStructure_ProjectsExternalGenericTargetToDeclaringNamespace()
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

        LibraryDependencyNamespaceCycle cycle =
            Assert.Single(document.Cycles);
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
    public void
        LibraryDependencyStructure_QualifiesAbsenceUnderIncompleteEvidence()
    {
        LibraryDependencyStructureDocument qualified = Document();
        Assert.Equal(
            LibraryDependencyCompleteness.Qualified,
            qualified.Completeness);
        Assert.True(qualified.Population.UnresolvedCallCount > 0);

        LibraryDependencyStructureDocument complete =
            Document(FixtureIds.AnalysisCallOverloads);
        Assert.Equal(
            LibraryDependencyCompleteness.Complete,
            complete.Completeness);
        Assert.Equal(0, complete.Population.UnresolvedCallCount);
        Assert.Equal(0, complete.Population.IncompleteBodyCount);
        Assert.Empty(complete.Cycles);
    }

    [Fact]
    public void
        LibraryDependencyStructure_PartitionsExaminedCallsExactly()
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
        LibraryDependencyUnresolvedCount indirect =
            Assert.Single(population.UnresolvedReasons);
        Assert.Equal(
            LibraryDependencyUnresolvedReason.Indirect,
            indirect.Reason);
        Assert.Equal(1, indirect.Count);
        Assert.Equal(
            population.SameTypeCallCount,
            document.Types.Sum(
                static node => node.IntraTypeRelationshipCount));
        Assert.Equal(
            population.InternalCallCount
                - population.SameTypeCallCount,
            document.TypeEdges.Sum(static edge => edge.Counts.Total));
        Assert.Equal(
            population.ExternalCallCount,
            document.ExternalTypeEdges.Sum(
                static edge => edge.Counts.Total));
    }

    [Fact]
    public void
        LibraryDependencyStructure_BoundsExplanationWithExactRemainder()
    {
        LibraryDependencyStructureDocument document = Document();

        LibraryDependencyNamespaceEdge edge =
            NamespaceEdge(document, "Explained", "Epsilon");
        Assert.Equal(7, edge.ContributingTypeEdgeCount);
        Assert.Equal(2, edge.RemainingContributorCount);
        Assert.Equal(28, edge.Counts.Invocations);
        Assert.Equal(
            [
                "Explained.T7",
                "Explained.T6",
                "Explained.T5",
                "Explained.T4",
                "Explained.T3",
            ],
            edge.ExplainingTypeEdges.Select(
                static explaining => explaining.SourceTypeKey));
    }

    [Fact]
    public void
        LibraryDependencyStructure_ExternalKeysNeverCollideAcrossSeparators()
    {
        const string tail =
            ", Version=*, Culture=neutral, PublicKeyToken=null::";
        var first =
            new ILInspector.Metadata.AssemblyReferenceIdentity(
                "E",
                null,
                null,
                null);
        var second =
            new ILInspector.Metadata.AssemblyReferenceIdentity(
                "E" + tail + "Q",
                null,
                null,
                null);

        string firstKey =
            LibraryDependencyStructure.ExternalKey(
                first,
                "Q" + tail + "R");
        string secondKey =
            LibraryDependencyStructure.ExternalKey(second, "R");

        Assert.NotEqual(firstKey, secondKey);
        Assert.NotEqual(
            LibraryDependencyStructure.ExternalKey(
                new("A;namespace=1:B", null, null, null),
                ""),
            LibraryDependencyStructure.ExternalKey(
                new("A", null, null, null),
                "B"));
        Assert.NotEqual(
            LibraryDependencyStructure.ExternalKey(null, "N"),
            LibraryDependencyStructure.ExternalKey(
                new(
                    "<intrinsic-core-library>",
                    null,
                    null,
                    null),
                "N"));
    }

    [Fact]
    public void
        LibraryDependencyStructure_UsesLibraryMetricsTypeKeys()
    {
        LibraryDependencyStructureDocument document = Document();

        Assert.All(
            document.Types,
            static node => Assert.Equal(
                LibraryStructuralReport.TypeKey(node.Type),
                node.TypeKey));
        HashSet<string> keys =
            [.. document.Types.Select(static node => node.TypeKey)];
        Assert.All(
            document.TypeEdges,
            edge =>
            {
                Assert.Contains(edge.SourceTypeKey, keys);
                Assert.Contains(edge.TargetTypeKey, keys);
            });
    }

    [Fact]
    public void
        LibraryDependencyStructure_GraphComponentsMatchIndependentOracle()
    {
        LibraryDependencyStructureDocument document = Document();
        LibraryDependencyGraphExecution execution = document.GraphExecution;

        Assert.Equal(
            document.Population.InternalCallCount
                + document.Population.ExternalCallCount,
            execution.Graph.Occurrences.Length);
        Assert.Equal(
            execution.Graph.Occurrences.Length,
            execution.GroupProjection.Receipt
                .DistinctSelectedOccurrences);
        int[] internalGroupIds =
        [
            .. execution.Graph.Groups
                .Where(static group =>
                    group.Subject.Kind
                        == LibraryDependencyGraphSubjectKind
                            .InternalNamespace)
                .Select(static group => group.Id),
        ];
        Assert.Equal(
            document.Namespaces.Length,
            internalGroupIds.Length);
        Assert.Equal(
            internalGroupIds.Length,
            execution.Components.Receipt
                .SelectedSourceGroupsAdmitted);
        Assert.Equal(
            internalGroupIds,
            execution.Components.Memberships.Select(
                static membership => membership.SourceGroupId));
        Assert.DoesNotContain(
            execution.Components.Memberships,
            membership =>
                execution.Graph.Groups[membership.SourceGroupId]
                    .Subject.Kind
                    == LibraryDependencyGraphSubjectKind.External);

        IndependentTopology oracle = IndependentTopology.Create(
            document.Namespaces.Select(static node => node.Namespace),
            document.NamespaceEdges);
        Assert.Equal(
            oracle.Cycles,
            document.Cycles.Select(
                static cycle =>
                    string.Join("|", cycle.Namespaces)));
        Assert.All(
            document.Namespaces,
            node => Assert.Equal(
                oracle.Levels[node.Namespace],
                node.Level));

        Dictionary<int, int> componentByGroup =
            execution.Components.Memberships.ToDictionary(
                static membership => membership.SourceGroupId,
                static membership => membership.ComponentId);
        Assert.All(
            execution.Components.CondensationEdges,
            condensation =>
            {
                Assert.NotEmpty(condensation.SourceEdgeIds);
                Assert.All(
                    condensation.SourceEdgeIds,
                    edgeId =>
                    {
                        var edge = execution.Graph.Edges[edgeId];
                        int sourceGroup =
                            execution.Graph.Nodes[edge.FromNodeId]
                                .GroupIds[0];
                        int targetGroup =
                            execution.Graph.Nodes[edge.ToNodeId]
                                .GroupIds[0];
                        Assert.Equal(
                            condensation.FromComponentId,
                            componentByGroup[sourceGroup]);
                        Assert.Equal(
                            condensation.ToComponentId,
                            componentByGroup[targetGroup]);
                    });
            });
        Assert.Equal(
            execution.Components.Receipt
                .DistinctCanonicalSourceEdgeContributorsRetained,
            execution.Components.CondensationEdges.Sum(
                static edge => edge.SourceEdgeIds.Length));
    }

    sealed record IndependentTopology(
        IReadOnlyList<string> Cycles,
        IReadOnlyDictionary<string, int> Levels)
    {
        internal static IndependentTopology Create(
            IEnumerable<string> namespaceNames,
            IEnumerable<LibraryDependencyNamespaceEdge> edges)
        {
            string[] names =
            [
                .. namespaceNames.Order(StringComparer.Ordinal),
            ];
            var successors = names.ToDictionary(
                static name => name,
                static _ => new HashSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);
            foreach (LibraryDependencyNamespaceEdge edge in edges)
            {
                successors[edge.SourceNamespace].Add(
                    edge.TargetNamespace);
            }

            Dictionary<string, HashSet<string>> reachable =
                names.ToDictionary(
                    static name => name,
                    name => Reachable(name, successors),
                    StringComparer.Ordinal);
            var components = new List<string[]>();
            var componentByName =
                new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (componentByName.ContainsKey(name))
                    continue;
                string[] members =
                [
                    .. names.Where(candidate =>
                        reachable[name].Contains(candidate)
                        && reachable[candidate].Contains(name)),
                ];
                int componentId = components.Count;
                components.Add(members);
                foreach (string member in members)
                    componentByName.Add(member, componentId);
            }

            var componentSuccessors =
                components.Select(static _ => new HashSet<int>()).ToArray();
            foreach ((string source, HashSet<string> targets)
                in successors)
            {
                int sourceComponent = componentByName[source];
                foreach (string target in targets)
                {
                    int targetComponent = componentByName[target];
                    if (sourceComponent != targetComponent)
                    {
                        componentSuccessors[sourceComponent].Add(
                            targetComponent);
                    }
                }
            }
            var levels = new int[components.Count];
            var settled = new bool[components.Count];
            int Settle(int component)
            {
                if (settled[component])
                    return levels[component];
                settled[component] = true;
                levels[component] =
                    componentSuccessors[component].Count == 0
                        ? 0
                        : 1 + componentSuccessors[component]
                            .Max(Settle);
                return levels[component];
            }
            for (var component = 0;
                component < components.Count;
                component++)
            {
                Settle(component);
            }
            return new(
                [
                    .. components
                        .Where(static members => members.Length >= 2)
                        .Select(static members =>
                            string.Join("|", members))
                        .Order(StringComparer.Ordinal),
                ],
                names.ToDictionary(
                    static name => name,
                    name => levels[componentByName[name]],
                    StringComparer.Ordinal));
        }

        static HashSet<string> Reachable(
            string start,
            IReadOnlyDictionary<string, HashSet<string>> successors)
        {
            var result =
                new HashSet<string>(StringComparer.Ordinal) { start };
            var pending = new Stack<string>();
            pending.Push(start);
            while (pending.TryPop(out string? current))
            {
                foreach (string next in successors[current])
                {
                    if (result.Add(next))
                        pending.Push(next);
                }
            }
            return result;
        }
    }
}
