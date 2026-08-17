using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionGraphDocumentTests
{
    static readonly InspectionGraphEvidenceDescriptor TestEvidence =
        new("test.evidence", InspectionGraphOwner.Queries);

    static readonly InspectionGraphRelationshipDescriptor TestRelationship =
        new(
            "test.relationship",
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [
                MemberAdmission(InspectionGraphEndpointRole.Source),
                MemberAdmission(InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            new TestOccurrenceIdentityProjection(),
            [TestEvidence]);

    static MemberRef Member(string name) =>
        new(
            TypeRef.Definition("Sample", "Sample", "Graph"),
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

    static MethodIdentity Method(MemberRef member, int token) =>
        new(
            member.DeclaringType.Assembly,
            new Guid("11111111-1111-1111-1111-111111111111"),
            member.DeclaringType,
            member.Name,
            member.ParameterTypes,
            member.ReturnType,
            token,
            IsStatic: true);

    static DirectCall Call(
        MemberRef caller,
        MemberRef callee,
        int offset,
        CallKind kind = CallKind.Call,
        bool inLoop = false) =>
        new(
            Method(caller, 0x06000001),
            callee,
            offset,
            0x06000002,
            0x06000002,
            kind,
            inLoop)
        {
            ExactTarget = kind is CallKind.Call
                or CallKind.NewObject,
        };

    static CallTreeNode Node(
        MemberRef member,
        CallTreeStatus status,
        params CallTreeNode[] children) =>
        new(member, CallKind.Call, status, [.. children]);

    static InspectionGraphSubject Subject(string name)
    {
        MemberRef member = Member(name);
        return InspectionGraphSubject.ForMember(
            GraphNodeIdentity.FromMember(member),
            member);
    }

    [Fact]
    public void CallAdapter_PreservesTypedTopologyAndDisclosesEvidenceGap()
    {
        MemberRef focus = Member("Focus");
        MemberRef caller = Member("Caller");
        MemberRef callee = Member("Callee");
        var callerRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            Node(caller, CallTreeStatus.Leaf));
        var calleeRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            Node(callee, CallTreeStatus.External));
        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);

        InspectionGraphDocument document =
            CallGraphInspectionGraphAdapter.Create(projection);

        Assert.Equal([0, 1, 2], document.Nodes.Select(node => node.Id));
        Assert.Equal(
            [focus, caller, callee],
            document.Nodes.Select(node =>
                CallGraphMember(node.Subject)));
        Assert.Equal(
            projection.Nodes.Select(node => node.Identity),
            document.Nodes.Select(node =>
                Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
                    Assert.IsType<
                        InspectionGraphSubject.MemberSubject>(
                            node.Subject).Identity).Identity));
        Assert.Equal(
            [
                InspectionGraphNodeRole.Unclassified,
                InspectionGraphNodeRole.Ordinary,
                InspectionGraphNodeRole.External,
            ],
            document.Nodes.Select(node => node.Role));
        Assert.Equal([(1, 0), (0, 2)], document.Edges.Select(
            edge => (edge.FromNodeId, edge.ToNodeId)));
        Assert.All(
            document.Edges,
            edge => Assert.Same(
                CallGraphInspectionGraphCatalog.Call,
                edge.Relationship));
        Assert.Equal(
            [0, 1],
            document.Occurrences.Select(occurrence => occurrence.Id));
        Assert.All(
            document.Occurrences,
            occurrence => Assert.IsType<CallGraphLogicalEdgeEvidence>(
                occurrence.Evidence));
        Assert.Equal(
            document.Edges.Select(edge => edge.Id + 1),
            document.Occurrences.Select(occurrence =>
                ((CallGraphLogicalEdgeEvidence)occurrence.Evidence)
                    .RowNumber));
        Assert.Equal(
            document.Edges.Select(edge =>
                document.Nodes[edge.FromNodeId].Subject),
            document.Occurrences.Select(
                occurrence => occurrence.SourceSubject));
        Assert.Equal(
            document.Edges.Select(edge =>
                document.Nodes[edge.ToNodeId].Subject),
            document.Occurrences.Select(
                occurrence => occurrence.TargetSubject));
        InspectionGraphSeed seed = Assert.Single(document.Seeds);
        Assert.Equal(focus, CallGraphMember(seed.Subject));
        Assert.Equal(InspectionGraphTarget.Node(0), seed.Target);
        Assert.Equal(InspectionGraphSeedRole.Primary, seed.Role);
        Assert.Equal(
            InspectionGraphMode.SingleSeed,
            document.ModeRequest.Mode);
        Assert.Equal([seed.Subject], document.ModeRequest.Seeds);
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .TraversalIncomplete));
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .PhysicalOccurrencesUnavailable)
                && limit.Target is { Kind:
                    InspectionGraphTargetKind.Edge });
        Assert.Empty(document.Groups);
        Assert.Empty(document.Characteristics);
        Assert.Empty(document.Failures);
        Assert.Equal(
            InspectionGraphDocumentScope.Portable,
            document.Scope);
    }

    [Fact]
    public void CallAdapter_IsDeterministicAndDoesNotMutateProjection()
    {
        MemberRef focus = Member("Focus");
        MemberRef callee = Member("Callee");
        CallGraphProjection projection = CallGraphProjection.FromCallees(
            Node(
                focus,
                CallTreeStatus.Expanded,
                Node(callee, CallTreeStatus.Leaf)));

        InspectionGraphDocument first =
            CallGraphInspectionGraphAdapter.Create(projection);
        InspectionGraphDocument second =
            CallGraphInspectionGraphAdapter.Create(projection);

        Assert.Equal(
            first.Nodes.Select(node =>
                (node.Id, node.Subject, node.Role)),
            second.Nodes.Select(node =>
                (node.Id, node.Subject, node.Role)));
        Assert.Equal(
            first.Edges.Select(edge =>
                (edge.Id, edge.FromNodeId, edge.ToNodeId)),
            second.Edges.Select(edge =>
                (edge.Id, edge.FromNodeId, edge.ToNodeId)));
        Assert.Equal(
            first.Occurrences.Select(occurrence =>
                (occurrence.Id,
                    occurrence.SourceSubject,
                    occurrence.TargetSubject)),
            second.Occurrences.Select(occurrence =>
                (occurrence.Id,
                    occurrence.SourceSubject,
                    occurrence.TargetSubject)));
        Assert.Single(projection.Rows);
        Assert.Equal("Callee", projection.Nodes[1].Member.Name);
    }

    [Fact]
    public void CallAdapter_RetainsPhysicalSitesAndTypedAggregates()
    {
        MemberRef focus = Member("Focus");
        MemberRef callee = Member("Callee");
        DirectCall first = Call(
            focus,
            callee,
            offset: 4,
            kind: CallKind.CallVirtual);
        DirectCall second = Call(
            focus,
            callee,
            offset: 12,
            inLoop: true);
        CallTreeNode calleeNode =
            Node(callee, CallTreeStatus.Leaf) with
            {
                ParentEdgeCallSites = [first, second],
            };
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    focus,
                    CallTreeStatus.Expanded,
                    calleeNode));

        InspectionGraphDocument document =
            CallGraphInspectionGraphAdapter.Create(projection);

        Assert.Equal(2, projection.CallSites.Length);
        CallGraphEdge projectedEdge =
            Assert.Single(projection.Edges);
        Assert.Equal([0, 1], projectedEdge.CallSiteIds);
        Assert.True(projectedEdge.AnyCallInLoop);
        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal([0, 1], edge.OccurrenceIds);
        Assert.Equal(2, document.Occurrences.Length);
        Assert.All(
            document.Occurrences,
            occurrence => Assert.IsType<CallGraphCallSiteEvidence>(
                occurrence.Evidence));
        Assert.DoesNotContain(
            document.Limits,
            limit => ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .PhysicalOccurrencesUnavailable));

        AssertCharacteristic(
            document,
            CallGraphInspectionGraphCatalog
                .OccurrenceCallKind,
            InspectionGraphTarget.Occurrence(0),
            new InspectionGraphValue.Token("callvirt"));
        AssertCharacteristic(
            document,
            CallGraphInspectionGraphCatalog
                .OccurrenceDispatchKind,
            InspectionGraphTarget.Occurrence(0),
            new InspectionGraphValue.Token("virtual"));
        AssertCharacteristic(
            document,
            CallGraphInspectionGraphCatalog
                .OccurrenceInLoop,
            InspectionGraphTarget.Occurrence(1),
            new InspectionGraphValue.Boolean(true));
        AssertCharacteristic(
            document,
            CallGraphInspectionGraphCatalog
                .EdgeCallSiteMultiplicity,
            InspectionGraphTarget.Edge(0),
            new InspectionGraphValue.Integer(2));
        AssertCharacteristic(
            document,
            CallGraphInspectionGraphCatalog.EdgeAnyInLoop,
            InspectionGraphTarget.Edge(0),
            new InspectionGraphValue.Boolean(true));
        AssertCharacteristic(
            document,
            CallGraphInspectionGraphCatalog.EdgeCallKinds,
            InspectionGraphTarget.Edge(0),
            new InspectionGraphValue.TokenSet(
                ["callvirt", "call"]));
        AssertCharacteristic(
            document,
            CallGraphInspectionGraphCatalog.EdgeDispatchKinds,
            InspectionGraphTarget.Edge(0),
            new InspectionGraphValue.TokenSet(
                ["virtual", "direct"]));
    }

    [Fact]
    public void CallAdapter_TreatsPartialPhysicalEvidenceAsIncomplete()
    {
        MemberRef focus = Member("Focus");
        MemberRef firstPeer = Member("FirstPeer");
        MemberRef secondPeer = Member("SecondPeer");
        DirectCall looped = Call(
            focus,
            firstPeer,
            offset: 4,
            inLoop: true);
        DirectCall plain = Call(
            focus,
            firstPeer,
            offset: 12);
        CallTreeNode callerRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            Node(
                firstPeer,
                CallTreeStatus.Expanded,
                Node(
                    focus,
                    CallTreeStatus.AlreadyShown) with
                {
                    ParentEdgeCallSites = [looped],
                }));
        CallTreeNode calleeRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            Node(
                secondPeer,
                CallTreeStatus.Leaf) with
            {
                ParentEdgeCallSites = [looped, plain],
            });
        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);
        int partialEdgeId = Assert.Single(
            projection.Rows,
            row => row.Edge.From == projection.Focus.Id
                && projection.Nodes[row.Edge.To].Member
                    == secondPeer).Number - 1;

        InspectionGraphDocument document =
            CallGraphInspectionGraphAdapter.Create(projection);

        InspectionGraphEdge partial =
            document.Edges[partialEdgeId];
        int occurrenceId = Assert.Single(partial.OccurrenceIds);
        var evidence = Assert.IsType<CallGraphCallSiteEvidence>(
            document.Occurrences[occurrenceId].Evidence);
        Assert.Equal(plain.ILOffset, evidence.ILOffset);
        Assert.Contains(
            document.Limits,
            limit => ReferenceEquals(
                    limit.Descriptor,
                    CallGraphInspectionGraphCatalog
                        .PhysicalOccurrencesUnavailable)
                && limit.Target
                    == InspectionGraphTarget.Edge(partialEdgeId));
        Assert.DoesNotContain(
            document.Characteristics,
            characteristic =>
                characteristic.Target
                    == InspectionGraphTarget.Edge(partialEdgeId));
        Assert.Contains(
            document.Characteristics,
            characteristic =>
                characteristic.Target
                    == InspectionGraphTarget.Occurrence(occurrenceId));
    }

    [Fact]
    public void CallAdapter_PreservesAcquisitionDistinctReceipts()
    {
        string path =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();

        CallTreeNode MakeTree()
        {
            LibraryBodyIndex index = LibraryBodyIndex.Open(path);
            MethodIdentity method = index.DeclaredMethods.Single(
                candidate =>
                    candidate.DeclaringType.Name
                        == "InstanceRecursionApi"
                    && candidate.Name == "IsEven");
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "inspection graph acquisition test"));
            using var scope = new CatalogCallGraphScope(
                new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(path)),
                [new CatalogCallGraphParticipant(index, assembly)]);
            return scope.Detach(
                scope.BuildCallerTree(
                    index,
                    method.MetadataToken));
        }

        CallTreeNode first = MakeTree();
        CallTreeNode second = MakeTree();
        CallTreeNode combined = first with
        {
            Children = [.. first.Children, .. second.Children],
        };
        CallGraphProjection projection =
            CallGraphProjection.FromCallers(combined);

        InspectionGraphDocument document =
            CallGraphInspectionGraphAdapter.Create(projection);

        Assert.Equal(4, projection.CallSites.Length);
        Assert.Equal(4, document.Occurrences.Length);
        Assert.Equal(
            InspectionGraphDocumentScope.SessionBound,
            document.Scope);
        Assert.Equal(
            4,
            document.Occurrences
                .Select(occurrence =>
                    Assert.IsType<CallGraphCallSiteEvidence>(
                        occurrence.Evidence).Identity)
                .Distinct()
                .Count());
    }

    static void AssertCharacteristic(
        InspectionGraphDocument document,
        InspectionGraphCharacteristicDescriptor descriptor,
        InspectionGraphTarget target,
        InspectionGraphValue expected)
    {
        InspectionGraphCharacteristic characteristic =
            Assert.Single(
                document.Characteristics,
                item => ReferenceEquals(
                        item.Descriptor,
                        descriptor)
                    && item.Target == target);
        Assert.Equal(expected, characteristic.Value);
    }

    [Fact]
    public void Document_RejectsDefaultCollectionsAndNonDenseIds()
    {
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                default(ImmutableArray<InspectionGraphNode>),
                [],
                [],
                [],
                [],
                [],
                [],
                []));

        InspectionGraphSubject subject = Subject("Member");
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [
                    new InspectionGraphNode(
                        1,
                        subject,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                ],
                [],
                [],
                [],
                [],
                [],
                [],
                []));
    }

    [Fact]
    public void Document_SnapshotsCollectionsAndRejectsDefaultTarget()
    {
        var nodes = new List<InspectionGraphNode>
        {
            new(
                0,
                Subject("Member"),
                InspectionGraphNodeRole.Ordinary,
                []),
        };
        var document = new InspectionGraphDocument(
            InspectionGraphDocumentScope.SessionBound,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects),
            nodes,
            [],
            [],
            [],
            [],
            [],
            [],
            []);

        nodes.Clear();

        Assert.Single(document.Nodes);
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                document.Nodes,
                [],
                [],
                [],
                [],
                [
                    new InspectionGraphSeed(
                        document.Nodes[0].Subject,
                        default,
                        InspectionGraphSeedRole.Primary),
                ],
                [],
                []));
    }

    [Fact]
    public void ModeRequest_RejectsInvalidSeedCardinalityAndDuplicates()
    {
        InspectionGraphSubject first = Subject("First");

        Assert.Throws<ArgumentException>(
            () => InspectionGraphModeRequest.PeerSeeds([first]));
        Assert.Throws<ArgumentException>(
            () => InspectionGraphModeRequest.PeerSeeds([first, first]));
    }

    [Fact]
    public void NeighborhoodRequest_ValidatesAndSnapshotsSelection()
    {
        InspectionGraphSubject member = Subject("Member");
        var relationships =
            new List<InspectionGraphRelationshipDescriptor>
            {
                CallGraphInspectionGraphCatalog.Call,
            };
        InspectionGraphNeighborhoodRequest request =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                member,
                relationships,
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 2);

        relationships.Clear();

        Assert.Equal(
            [CallGraphInspectionGraphCatalog.Call],
            request.Relationships);
        Assert.Equal(member, request.Seed);
        Assert.Equal(2, request.MaxDepth);
        Assert.Equal(
            InspectionGraphTraversalDirection.Outgoing,
            request.Direction);
        Assert.Throws<ArgumentException>(
            () => InspectionGraphNeighborhoodRequest.SingleSeed(
                member,
                [],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1));
        Assert.Throws<ArgumentException>(
            () => InspectionGraphNeighborhoodRequest.SingleSeed(
                member,
                default(ImmutableArray<
                    InspectionGraphRelationshipDescriptor>),
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1));
        Assert.Throws<ArgumentException>(
            () => InspectionGraphNeighborhoodRequest.SingleSeed(
                member,
                [
                    CallGraphInspectionGraphCatalog.Call,
                    CallGraphInspectionGraphCatalog.Call,
                ],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InspectionGraphNeighborhoodRequest.SingleSeed(
                member,
                [CallGraphInspectionGraphCatalog.Call],
                (InspectionGraphTraversalDirection)42,
                maxDepth: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InspectionGraphNeighborhoodRequest.SingleSeed(
                member,
                [CallGraphInspectionGraphCatalog.Call],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InspectionGraphNeighborhoodDepthBoundEvidence(-1));
    }

    [Fact]
    public void NeighborhoodRequest_RequiresDirectionalSeedAdmission()
    {
        InspectionGraphSubject package =
            InspectionGraphSubject.ForRealizedPackage(
                new RealizedMemberCoordinate.Package(
                    "sample.package",
                    "1.0.0",
                    "feed",
                    "net11.0",
                    null));

        InspectionQueryException unsupported = Assert.Throws<
            InspectionQueryException>(
                () => InspectionGraphNeighborhoodRequest.SingleSeed(
                    package,
                    [CallGraphInspectionGraphCatalog.Call],
                    InspectionGraphTraversalDirection.Outgoing,
                    maxDepth: 1));
        InspectionQueryException wrongDirection = Assert.Throws<
            InspectionQueryException>(
                () => InspectionGraphNeighborhoodRequest.SingleSeed(
                    package,
                    [
                        InspectionGraphIntegrationsCatalog
                            .IntegrationObserved,
                    ],
                    InspectionGraphTraversalDirection.Incoming,
                    maxDepth: 1));
        InspectionGraphNeighborhoodRequest outgoing =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                package,
                [
                    InspectionGraphIntegrationsCatalog
                        .IntegrationObserved,
                ],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1);

        Assert.Contains("package seed", unsupported.Message);
        Assert.Contains("call", unsupported.Message);
        Assert.Contains("incoming", wrongDirection.Message);
        Assert.Equal(package, outgoing.Seed);
    }

    [Fact]
    public void RelationshipDescriptor_ValidatesAndSnapshotsSeedAdmissions()
    {
        static InspectionGraphRelationshipDescriptor Descriptor(
            IEnumerable<InspectionGraphSeedAdmission> admissions) =>
            new(
                "test.seed-admission",
                InspectionGraphOwner.Queries,
                InspectionGraphRelationshipSemantics.Observed,
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                admissions,
                InspectionGraphEndpointProjection.Exact,
                new TestOccurrenceIdentityProjection(),
                [TestEvidence]);

        var admissions = new List<InspectionGraphSeedAdmission>
        {
            MemberAdmission(InspectionGraphEndpointRole.Source),
            MemberAdmission(InspectionGraphEndpointRole.Target),
        };
        InspectionGraphRelationshipDescriptor descriptor =
            Descriptor(admissions);

        admissions.Clear();

        Assert.Equal(2, descriptor.SeedAdmissions.Length);
        Assert.Equal(
            descriptor.SeedAdmissions,
            descriptor.GetSeedAdmissions(
                InspectionGraphSubjectKind.Member));
        Assert.Empty(descriptor.GetSeedAdmissions(
            InspectionGraphSubjectKind.Package));
        Assert.Throws<ArgumentException>(
            () => Descriptor([]));
        Assert.Throws<ArgumentException>(
            () => Descriptor(
                default(ImmutableArray<InspectionGraphSeedAdmission>)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Descriptor(
                [
                    Admission(
                        InspectionGraphSubjectKind.Member,
                        (InspectionGraphSeedAdmissionKind)42,
                        InspectionGraphEndpointRole.Source),
                ]));
        Assert.Throws<ArgumentException>(
            () => Descriptor(
                [
                    Admission(
                        InspectionGraphSubjectKind.Package,
                        InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                        InspectionGraphEndpointRole.Source),
                ]));
        Assert.Throws<ArgumentException>(
            () => Descriptor(
                [
                    MemberAdmission(InspectionGraphEndpointRole.Source),
                    MemberAdmission(InspectionGraphEndpointRole.Source),
                ]));
    }

    [Fact]
    public void RelationshipCatalogsDeclareCurrentSeedAdmissions()
    {
        InspectionGraphSeedAdmission[] outwardIntegrationAdmissions =
        [
            Admission(
                InspectionGraphSubjectKind.Member,
                InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                InspectionGraphEndpointRole.Source),
            Admission(
                InspectionGraphSubjectKind.Type,
                InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                InspectionGraphEndpointRole.Target),
            Admission(
                InspectionGraphSubjectKind.Assembly,
                InspectionGraphSeedAdmissionKind.OwnedSubjects,
                InspectionGraphEndpointRole.Source),
            Admission(
                InspectionGraphSubjectKind.Package,
                InspectionGraphSeedAdmissionKind.OwnedSubjects,
                InspectionGraphEndpointRole.Source),
        ];

        Assert.Equal(
            [
                MemberAdmission(InspectionGraphEndpointRole.Source),
                MemberAdmission(InspectionGraphEndpointRole.Target),
            ],
            CallGraphInspectionGraphCatalog.Call.SeedAdmissions);
        Assert.Equal(
            outwardIntegrationAdmissions,
            InspectionGraphIntegrationsCatalog.Extension.SeedAdmissions);
        Assert.Equal(
            outwardIntegrationAdmissions,
            InspectionGraphIntegrationsCatalog
                .IntegrationObserved.SeedAdmissions);
        Assert.Equal(
            [
                Admission(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                Admission(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
                Admission(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
                Admission(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphIntegrationsCatalog
                .MetadataReference.SeedAdmissions);
        Assert.Equal(
            [
                Admission(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                Admission(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.OccurrenceEndpoint,
                    InspectionGraphEndpointRole.Source),
                Admission(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
                Admission(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
            ],
            InspectionGraphIntegrationsCatalog
                .IntegrationOpportunity.SeedAdmissions);
    }

    [Fact]
    public void AdmissionsMatchDeclaredEndpointDomains()
    {
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphRelationshipDescriptor(
                "test.wrong-occurrence-endpoint",
                InspectionGraphOwner.Queries,
                InspectionGraphRelationshipSemantics.Observed,
                [InspectionGraphSubjectKind.Assembly],
                [InspectionGraphSubjectKind.Type],
                [InspectionGraphSubjectKind.Type],
                [InspectionGraphSubjectKind.Type],
                [
                    Admission(
                        InspectionGraphSubjectKind.Assembly,
                        InspectionGraphSeedAdmissionKind.OccurrenceEndpoint,
                        InspectionGraphEndpointRole.Source),
                ],
                InspectionGraphEndpointProjection.Exact,
                new TestOccurrenceIdentityProjection(),
                [TestEvidence]));
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphRelationshipDescriptor(
                "test.wrong-edge-role",
                InspectionGraphOwner.Queries,
                InspectionGraphRelationshipSemantics.Observed,
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Type],
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Type],
                [
                    Admission(
                        InspectionGraphSubjectKind.Member,
                        InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                        InspectionGraphEndpointRole.Target),
                ],
                InspectionGraphEndpointProjection.Exact,
                new TestOccurrenceIdentityProjection(),
                [TestEvidence]));
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphRelationshipDescriptor(
                "test.invalid-owned-endpoint",
                InspectionGraphOwner.Queries,
                InspectionGraphRelationshipSemantics.Observed,
                [InspectionGraphSubjectKind.Assembly],
                [InspectionGraphSubjectKind.Assembly],
                [InspectionGraphSubjectKind.Assembly],
                [InspectionGraphSubjectKind.Assembly],
                [
                    Admission(
                        InspectionGraphSubjectKind.Member,
                        InspectionGraphSeedAdmissionKind.OwnedSubjects,
                        InspectionGraphEndpointRole.Source),
                ],
                InspectionGraphEndpointProjection.Exact,
                new TestOccurrenceIdentityProjection(),
                [TestEvidence]));
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphRelationshipDescriptor(
                "test.self-owned-endpoint",
                InspectionGraphOwner.Queries,
                InspectionGraphRelationshipSemantics.Observed,
                [InspectionGraphSubjectKind.Assembly],
                [InspectionGraphSubjectKind.Assembly],
                [InspectionGraphSubjectKind.Assembly],
                [InspectionGraphSubjectKind.Assembly],
                [
                    Admission(
                        InspectionGraphSubjectKind.Assembly,
                        InspectionGraphSeedAdmissionKind.OwnedSubjects,
                        InspectionGraphEndpointRole.Source),
                ],
                InspectionGraphEndpointProjection.Exact,
                new TestOccurrenceIdentityProjection(),
                [TestEvidence]));
    }

    [Fact]
    public void Document_RequiresModeRequestAndSeedBindingsToAgree()
    {
        InspectionGraphSubject first = Subject("First");
        InspectionGraphSubject second = Subject("Second");
        InspectionGraphNode[] nodes =
        [
            new(
                0,
                first,
                InspectionGraphNodeRole.Ordinary,
                []),
            new(
                1,
                second,
                InspectionGraphNodeRole.Ordinary,
                []),
        ];

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.SingleSeed(first),
                nodes,
                [],
                [],
                [],
                [],
                [],
                [],
                []));
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.PeerSeeds([first, second]),
                nodes,
                [],
                [],
                [],
                [],
                [
                    new InspectionGraphSeed(
                        first,
                        InspectionGraphTarget.Node(0),
                        InspectionGraphSeedRole.Primary),
                    new InspectionGraphSeed(
                        second,
                        InspectionGraphTarget.Node(1),
                        InspectionGraphSeedRole.Peer),
                ],
                [],
                []));
    }

    [Fact]
    public void SubjectCurrenciesRemainStructuralAndTyped()
    {
        var firstPackage = new RealizedMemberCoordinate.Package(
            "sample.package",
            "1.0.0",
            "feed",
            "net11.0",
            null);
        var secondPackage = new RealizedMemberCoordinate.Package(
            "sample.package",
            "2.0.0",
            "feed",
            "net11.0",
            null);

        Assert.Equal(
            Subject("Member"),
            Subject("Member"));
        Assert.Equal(
            InspectionGraphSubject.ForStructuralType(
                TypeRef.Definition("Sample", "Sample", "Type")),
            InspectionGraphSubject.ForStructuralType(
                TypeRef.Definition("Sample", "Sample", "Type")));
        Assert.NotEqual(
            InspectionGraphSubject.ForRealizedPackage(firstPackage),
            InspectionGraphSubject.ForRealizedPackage(secondPackage));
        Assert.Equal(
            InspectionGraphSubjectKind.Package,
            InspectionGraphSubject.ForRealizedPackage(firstPackage).Kind);
    }

    [Fact]
    public void Document_RejectsSeedWhoseSubjectDiffersFromTarget()
    {
        InspectionGraphSubject first =
            Subject("First");
        InspectionGraphSubject second =
            Subject("Second");

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [
                    new InspectionGraphNode(
                        0,
                        first,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                ],
                [],
                [],
                [],
                [],
                [
                    new InspectionGraphSeed(
                        second,
                        InspectionGraphTarget.Node(0),
                        InspectionGraphSeedRole.Primary),
                ],
                [],
                []));
    }

    [Fact]
    public void Document_RejectsDuplicateNodeSubjects()
    {
        InspectionGraphSubject subject =
            Subject("Member");

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [
                    new InspectionGraphNode(
                        0,
                        subject,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                    new InspectionGraphNode(
                        1,
                        subject,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                ],
                [],
                [],
                [],
                [],
                [],
                [],
                []));
    }

    [Fact]
    public void MemberSubjectsUseProducerIdentityNotStructuralPayload()
    {
        MemberRef member = Member("SamePayload");
        InspectionGraphSubject first =
            InspectionGraphSubject.ForMember(
                GraphNodeIdentity.CreateDocumentLocal(),
                member);
        InspectionGraphSubject second =
            InspectionGraphSubject.ForMember(
                GraphNodeIdentity.CreateDocumentLocal(),
                member);
        var synthetic = new InspectionGraphRelationshipDescriptor(
            "test.identity",
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Synthetic,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [
                MemberAdmission(InspectionGraphEndpointRole.Source),
                MemberAdmission(InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            InspectionGraphOccurrenceIdentityProjection
                .SyntheticNoOccurrence,
            []);

        var document = new InspectionGraphDocument(
            InspectionGraphDocumentScope.Portable,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects),
            [
                new InspectionGraphNode(
                    0,
                    first,
                    InspectionGraphNodeRole.Ordinary,
                    []),
                new InspectionGraphNode(
                    1,
                    second,
                    InspectionGraphNodeRole.Ordinary,
                    []),
            ],
            [],
            [
                new InspectionGraphEdge(
                    0,
                    0,
                    1,
                    synthetic,
                    []),
            ],
            [],
            [],
            [],
            [],
            []);

        Assert.NotEqual(document.Nodes[0].Subject, document.Nodes[1].Subject);
        Assert.Equal(
            member,
            CallGraphMember(document.Nodes[0].Subject));
    }

    [Fact]
    public void PortableDocumentRejectsSessionBoundSubject()
    {
        InspectionGraphSubject subject =
            InspectionGraphSubject.ForType(
                new TestBoundTypeIdentity());

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.Portable,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [
                    new InspectionGraphNode(
                        0,
                        subject,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                ],
                [],
                [],
                [],
                [],
                [],
                [],
                []));
    }

    [Fact]
    public void EdgeAndOccurrenceEndpointKindsRemainDistinct()
    {
        InspectionGraphSubject edgeSource =
            InspectionGraphSubject.ForStructuralType(
                TypeRef.Definition("Sample", "Sample", "Type"));
        InspectionGraphSubject edgeTarget =
            InspectionGraphSubject.ForRealizedPackage(
                new RealizedMemberCoordinate.Package(
                    "sample.package",
                    "1.0.0",
                    "feed",
                    "net11.0",
                    null));
        InspectionGraphSubject occurrenceSource = Subject("Source");
        InspectionGraphSubject occurrenceTarget = Subject("Target");
        var relationship = new InspectionGraphRelationshipDescriptor(
            "test.rollup-relationship",
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Package],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
                new(
                    InspectionGraphSubjectKind.Member,
                    InspectionGraphSeedAdmissionKind.OccurrenceEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Member,
                    InspectionGraphSeedAdmissionKind.OccurrenceEndpoint,
                    InspectionGraphEndpointRole.Target),
            ],
            new TestRollupEndpointProjection(),
            new TestOccurrenceIdentityProjection(),
            [TestEvidence]);
        var occurrence = new InspectionGraphOccurrence(
            0,
            relationship,
            occurrenceSource,
            occurrenceTarget,
            new TestOccurrenceEvidence(0),
            []);

        var document = new InspectionGraphDocument(
            InspectionGraphDocumentScope.Portable,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects),
            [
                new InspectionGraphNode(
                    0,
                    edgeSource,
                    InspectionGraphNodeRole.Ordinary,
                    []),
                new InspectionGraphNode(
                    1,
                    edgeTarget,
                    InspectionGraphNodeRole.Ordinary,
                    []),
            ],
            [],
            [
                new InspectionGraphEdge(
                    0,
                    0,
                    1,
                    relationship,
                    [0]),
            ],
            [occurrence],
            [],
            [],
            [],
            []);

        Assert.Single(document.Edges);
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.Portable,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [
                    new InspectionGraphNode(
                        0,
                        occurrenceSource,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                    new InspectionGraphNode(
                        1,
                        edgeTarget,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                ],
                [],
                [
                    new InspectionGraphEdge(
                        0,
                        0,
                        1,
                        relationship,
                        [0]),
                ],
                [occurrence],
                [],
                [],
                [],
                []));
    }

    [Fact]
    public void Document_RejectsMismatchedAndReversedOccurrenceEndpoints()
    {
        InspectionGraphSubject source =
            Subject("Source");
        InspectionGraphSubject target =
            Subject("Target");
        var nodes = new[]
        {
            new InspectionGraphNode(
                0,
                source,
                InspectionGraphNodeRole.Ordinary,
                []),
            new InspectionGraphNode(
                1,
                target,
                InspectionGraphNodeRole.Ordinary,
                []),
        };
        var edge = new InspectionGraphEdge(
            0,
            0,
            1,
            TestRelationship,
            [0]);
        var otherRelationship =
            new InspectionGraphRelationshipDescriptor(
                "test.other",
                InspectionGraphOwner.Queries,
                InspectionGraphRelationshipSemantics.Observed,
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [
                    MemberAdmission(InspectionGraphEndpointRole.Source),
                    MemberAdmission(InspectionGraphEndpointRole.Target),
                ],
                InspectionGraphEndpointProjection.Exact,
                new TestOccurrenceIdentityProjection(),
                [TestEvidence]);

        Assert.Throws<ArgumentException>(
            () => Document(
                nodes,
                edge,
                new InspectionGraphOccurrence(
                    0,
                    otherRelationship,
                    source,
                    target,
                    new TestOccurrenceEvidence(0),
                    [])));
        Assert.Throws<ArgumentException>(
            () => Document(
                nodes,
                edge,
                new InspectionGraphOccurrence(
                    0,
                    TestRelationship,
                    target,
                    source,
                    new TestOccurrenceEvidence(0),
                    [])));
    }

    [Fact]
    public void Document_RejectsDuplicateDescriptorIdAndOccurrenceIdentity()
    {
        InspectionGraphSubject source = Subject("Source");
        InspectionGraphSubject target = Subject("Target");
        var duplicateRelationship =
            new InspectionGraphRelationshipDescriptor(
                TestRelationship.Id,
                InspectionGraphOwner.Queries,
                InspectionGraphRelationshipSemantics.Observed,
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [InspectionGraphSubjectKind.Member],
                [
                    MemberAdmission(InspectionGraphEndpointRole.Source),
                    MemberAdmission(InspectionGraphEndpointRole.Target),
                ],
                InspectionGraphEndpointProjection.Exact,
                new TestOccurrenceIdentityProjection(),
                [TestEvidence]);
        var evidence = new TestOccurrenceEvidence(0);

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [
                    new InspectionGraphNode(
                        0,
                        source,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                    new InspectionGraphNode(
                        1,
                        target,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                ],
                [],
                [
                    new InspectionGraphEdge(
                        0,
                        0,
                        1,
                        TestRelationship,
                        [0]),
                ],
                [
                    new InspectionGraphOccurrence(
                        0,
                        duplicateRelationship,
                        source,
                        target,
                        evidence,
                        []),
                ],
                [],
                [],
                [],
                []));

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [
                    new InspectionGraphNode(
                        0,
                        source,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                    new InspectionGraphNode(
                        1,
                        target,
                        InspectionGraphNodeRole.Ordinary,
                        []),
                ],
                [],
                [
                    new InspectionGraphEdge(
                        0,
                        0,
                        1,
                        TestRelationship,
                        [0, 1]),
                ],
                [
                    new InspectionGraphOccurrence(
                        0,
                        TestRelationship,
                        source,
                        target,
                        new TestOccurrenceEvidence(0),
                        []),
                    new InspectionGraphOccurrence(
                        1,
                        TestRelationship,
                        source,
                        target,
                        new TestOccurrenceEvidence(0),
                        []),
                ],
                [],
                [],
                [],
                []));
    }

    [Fact]
    public void RolledUpCharacteristicCitesTypedSourceTargets()
    {
        InspectionGraphSubject source = Subject("Source");
        var descriptor = new InspectionGraphCharacteristicDescriptor(
            "test.rollup",
            InspectionGraphOwner.Queries,
            InspectionGraphValueCatalog.Integer,
            [InspectionGraphTargetKind.Node],
            [],
            [InspectionGraphCharacteristicDerivationKind.RolledUp],
            InspectionGraphAggregationPolicy.Sum);
        var document = new InspectionGraphDocument(
            InspectionGraphDocumentScope.SessionBound,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects),
            [
                new InspectionGraphNode(
                    0,
                    source,
                    InspectionGraphNodeRole.Ordinary,
                    []),
            ],
            [],
            [],
            [],
            [
                new InspectionGraphCharacteristic(
                    descriptor,
                    InspectionGraphTarget.Node(0),
                    new InspectionGraphValue.Integer(1),
                    new InspectionGraphCharacteristicDerivation(
                        InspectionGraphCharacteristicDerivationKind
                            .RolledUp,
                        [InspectionGraphTarget.Node(0)])),
            ],
            [],
            [],
            []);

        Assert.Equal(
            InspectionGraphTarget.Node(0),
            Assert.Single(document.Characteristics)
                .Derivation.Sources[0]);
    }

    [Fact]
    public void DiagnosticDescriptorsRetainTypedProducerEvidence()
    {
        var evidenceDescriptor = new InspectionGraphEvidenceDescriptor(
            "test.limit-detail",
            InspectionGraphOwner.Queries);
        var limitDescriptor = new InspectionGraphLimitDescriptor(
            "test.limit",
            InspectionGraphOwner.Queries,
            [evidenceDescriptor]);
        var evidence = new TestDiagnosticEvidence(evidenceDescriptor);
        var document = new InspectionGraphDocument(
            InspectionGraphDocumentScope.SessionBound,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects),
            [],
            [],
            [],
            [],
            [],
            [],
            [new InspectionGraphLimit(limitDescriptor, Evidence: evidence)],
            []);

        Assert.Same(
            evidence,
            Assert.Single(document.Limits).Evidence);
        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [],
                [],
                [],
                [],
                [],
                [],
                [
                    new InspectionGraphLimit(
                        new InspectionGraphLimitDescriptor(
                            "test.other-limit",
                            InspectionGraphOwner.Queries),
                        Evidence: evidence),
                ],
                []));
    }

    [Fact]
    public void Document_RejectsConflictingUnusedEvidenceDescriptorIds()
    {
        var first = new InspectionGraphEvidenceDescriptor(
            "test.shared-evidence",
            InspectionGraphOwner.Queries);
        var second = new InspectionGraphEvidenceDescriptor(
            "test.shared-evidence",
            InspectionGraphOwner.Analysis);

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                [],
                [],
                [],
                [],
                [],
                [],
                [
                    new InspectionGraphLimit(
                        new InspectionGraphLimitDescriptor(
                            "test.first-limit",
                            InspectionGraphOwner.Queries,
                            [first])),
                    new InspectionGraphLimit(
                        new InspectionGraphLimitDescriptor(
                            "test.second-limit",
                            InspectionGraphOwner.Analysis,
                            [second])),
                ],
                []));
    }

    [Fact]
    public void Document_RequiresOccurrenceUnlessRelationshipIsSynthetic()
    {
        InspectionGraphSubject source =
            Subject("Source");
        InspectionGraphSubject target =
            Subject("Target");
        var nodes = new[]
        {
            new InspectionGraphNode(
                0,
                source,
                InspectionGraphNodeRole.Ordinary,
                []),
            new InspectionGraphNode(
                1,
                target,
                InspectionGraphNodeRole.Ordinary,
                []),
        };

        Assert.Throws<ArgumentException>(
            () => new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                nodes,
                [],
                [
                    new InspectionGraphEdge(
                        0,
                        0,
                        1,
                        TestRelationship,
                        []),
                ],
                [],
                [],
                [],
                [],
                []));

        var synthetic = new InspectionGraphRelationshipDescriptor(
            "test.synthetic",
            InspectionGraphOwner.Queries,
            InspectionGraphRelationshipSemantics.Synthetic,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [
                MemberAdmission(InspectionGraphEndpointRole.Source),
                MemberAdmission(InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            InspectionGraphOccurrenceIdentityProjection
                .SyntheticNoOccurrence,
            []);
        var document = new InspectionGraphDocument(
            InspectionGraphDocumentScope.SessionBound,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects),
            nodes,
            [],
            [
                new InspectionGraphEdge(
                    0,
                    0,
                    1,
                    synthetic,
                    []),
            ],
            [],
            [],
            [],
            [],
            []);

        Assert.Single(document.Edges);
        Assert.Empty(document.Occurrences);
    }

    static InspectionGraphDocument Document(
        IEnumerable<InspectionGraphNode> nodes,
        InspectionGraphEdge edge,
        InspectionGraphOccurrence occurrence) =>
        new(
            InspectionGraphDocumentScope.SessionBound,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects),
            nodes,
            [],
            [edge],
            [occurrence],
            [],
            [],
            [],
            []);

    static InspectionGraphSeedAdmission Admission(
        InspectionGraphSubjectKind subjectKind,
        InspectionGraphSeedAdmissionKind kind,
        InspectionGraphEndpointRole role) =>
        new(subjectKind, kind, role);

    static InspectionGraphSeedAdmission MemberAdmission(
        InspectionGraphEndpointRole role) =>
        Admission(
            InspectionGraphSubjectKind.Member,
            InspectionGraphSeedAdmissionKind.EdgeEndpoint,
            role);

    sealed record TestOccurrenceEvidence(int Identity)
        : IInspectionGraphOccurrenceEvidence
    {
        public InspectionGraphEvidenceDescriptor Descriptor =>
            TestEvidence;
    }

    sealed class TestOccurrenceIdentityProjection
        : InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            ((TestOccurrenceEvidence)occurrence.Evidence).Identity;
    }

    sealed class TestRollupEndpointProjection
        : InspectionGraphEndpointProjection
    {
        public override bool Supports(
            InspectionGraphOccurrence occurrence,
            InspectionGraphEndpointRole role,
            InspectionGraphSubject endpoint) =>
            role switch
            {
                InspectionGraphEndpointRole.Source =>
                    occurrence.SourceSubject.Kind
                        == InspectionGraphSubjectKind.Member
                    && endpoint.Kind
                        == InspectionGraphSubjectKind.Type,
                InspectionGraphEndpointRole.Target =>
                    occurrence.TargetSubject.Kind
                        == InspectionGraphSubjectKind.Member
                    && endpoint.Kind
                        == InspectionGraphSubjectKind.Package,
                _ => false,
            };
    }

    sealed record TestBoundTypeIdentity
        : InspectionGraphTypeIdentity
    {
        public override bool IsPortable => false;
    }

    static MemberRef CallGraphMember(InspectionGraphSubject subject) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                subject).Identity).Member;

    sealed record TestDiagnosticEvidence(
        InspectionGraphEvidenceDescriptor Descriptor)
        : IInspectionGraphDiagnosticEvidence;
}
