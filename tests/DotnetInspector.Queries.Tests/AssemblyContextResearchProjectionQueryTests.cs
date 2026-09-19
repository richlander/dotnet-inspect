using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Gates the group-scoped Research projection queries: they project from workspace-owned content
/// with no filesystem path, address an exact <c>MethodDef</c>, carry the whole-assembly fact
/// context path-keyed resolution cannot reach, resolve references through the participant's own
/// binding policy, and report participant failure as a typed entry.
/// </summary>
public sealed class AssemblyContextResearchProjectionQueryTests
{
    [Fact]
    public async Task TypeProjection_ProjectsFromContentWithoutAFilesystemPath()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        Assert.Null(Assert.Single(group.Participants).Assembly.Path);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!));

        ResearchViews.TypeProjectionResult projection = Available(result);
        Assert.Equal(typeof(ResearchProjectionProbe).FullName, projection.Identity.FullName);
        Assert.Equal("class", projection.Identity.Kind);
        Assert.NotNull(projection.Composition);
        Assert.True(projection.Composition!.Methods > 0);
    }

    [Fact]
    public async Task MemberProjection_ProducesAnAnnotatedSourceDocumentFromContent()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        Assert.Null(projection.Projection.SourceDocumentFailure);
        Assert.Null(projection.ContextLimitation);
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.NotEmpty(document.Text);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.CSharp);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.Il);
    }

    [Fact]
    public async Task MemberProjection_CarriesTheWholeAssemblyFactContext()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        // Boxing is an assembly-scoped Analysis fact. Path-keyed resolution cannot reach a
        // snapshot, so its presence proves the query supplied the context rather than letting
        // the projection observe a consistent absence.
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.Contains(document.Facts, fact => fact.Descriptor == "alloc.box");
    }

    [Theory]
    [InlineData(
        nameof(ResearchProjectionProbe.InvokeStackAllocation),
        "safety.callee",
        CallSiteEvidenceKind.Localloc,
        "StackAllocationExpression",
        "stackalloc")]
    [InlineData(
        nameof(ResearchProjectionProbe.InvokeFunctionPointer),
        "safety.callee",
        CallSiteEvidenceKind.Calli,
        "IndirectInvocationExpression",
        "callback")]
    [InlineData(
        nameof(ResearchProjectionProbe.InvokeThrowing),
        "semantics.callee",
        CallSiteEvidenceKind.ExceptionConstruction,
        "ObjectCreationExpression",
        "InvalidOperationException")]
    public async Task MemberProjection_MapsCalleeInstructionEvidenceToExactSource(
        string member,
        string descriptor,
        CallSiteEvidenceKind kind,
        string nodeKind,
        string expectedText)
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                FindingEvidenceRequest(member)));

        AssemblyMemberFindingEvidence evidence = Assert.Single(
            Assert.IsAssignableFrom<IReadOnlyList<AssemblyMemberFindingEvidence>>(
                projection.FindingEvidence),
            candidate =>
                projection.Projection.SourceDocument!.Facts[candidate.FactId]
                    .Descriptor == descriptor);
        AssemblyMemberCalleeEvidenceCoordinate coordinate =
            Assert.Single(evidence.Coordinates);
        Assert.Equal(kind, coordinate.Kind);
        Assert.Equal(evidence.Member, coordinate.Location.Method);
        Assert.Null(evidence.UnavailableReason);
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(evidence.SourceDocument);
        AnnotatedSourceNode node =
            Assert.Single(evidence.NodeIds.Select(id => document.Nodes[id]));
        Assert.Equal(nodeKind, node.Kind);
        Assert.Equal(SourceLineKind.CSharp, node.Medium);
        Assert.Contains(expectedText, NodeText(document, node), StringComparison.Ordinal);
        Assert.Contains(
            projection.Projection.SourceDocumentFactIdentities!,
            identity =>
                identity.FactId == evidence.FactId
                && identity.InstanceKey == evidence.InstanceKey);
    }

    [Fact]
    public async Task MemberProjection_ProjectsMethodCostEvidenceWithoutSourceCoordinate()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                FindingEvidenceRequest(
                    nameof(ResearchProjectionProbe.InvokeAllocationInLoop))));

        AssemblyMemberFindingEvidence evidence = Assert.Single(
            Assert.IsAssignableFrom<IReadOnlyList<AssemblyMemberFindingEvidence>>(
                projection.FindingEvidence),
            candidate =>
                projection.Projection.SourceDocument!.Facts[candidate.FactId]
                    .Descriptor == "cost.callee");
        Assert.Equal(ResearchFindingEvidenceState.Method, evidence.State);
        CallSiteCostEvidenceInput input =
            Assert.Single(evidence.AggregateInputs);
        Assert.Equal(
            CallSiteCostEvidenceInputKind.AllocationInLoop,
            input.Kind);
        Assert.Null(input.Value);
        Assert.Empty(evidence.Coordinates);
        Assert.Null(evidence.SourceDocument);
        Assert.Empty(evidence.NodeIds);
        Assert.Null(evidence.UnavailableReason);
        Assert.Contains(
            projection.Projection.SourceDocumentFactIdentities!,
            identity =>
                identity.FactId == evidence.FactId
                && identity.InstanceKey == evidence.InstanceKey);
    }

    [Fact]
    public async Task CalleeEvidenceCorrespondence_ReportsZeroAndAmbiguousNodes()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                FindingEvidenceRequest(
                    nameof(ResearchProjectionProbe.InvokeStackAllocation))));
        AssemblyMemberCalleeEvidenceCoordinate coordinate = Assert.Single(
            Assert.Single(projection.FindingEvidence!).Coordinates);

        (int[] missingIds, string? missingFailure) =
            AssemblyContextMemberProjectionQuery.FindEvidenceNodes(
                new AnnotatedSourceDocument("", [], [], [], []),
                [coordinate]);
        Assert.Empty(missingIds);
        Assert.Contains("matched 0", missingFailure, StringComparison.Ordinal);

        var provenance = new AnnotatedSourceNodeProvenance(
            [coordinate.Location.ILOffset!.Value]);
        var ambiguous = new AnnotatedSourceDocument(
            "ab",
            [
                new AnnotatedSourceNode(
                    0,
                    "StackAllocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)],
                    Provenance: provenance),
                new AnnotatedSourceNode(
                    1,
                    "StackAllocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(1, 1)],
                    Provenance: provenance),
            ],
            [],
            [],
            []);
        (int[] ambiguousIds, string? ambiguousFailure) =
            AssemblyContextMemberProjectionQuery.FindEvidenceNodes(
                ambiguous,
                [coordinate]);
        Assert.Empty(ambiguousIds);
        Assert.Contains("matched 2", ambiguousFailure, StringComparison.Ordinal);

        AssemblyMemberCalleeEvidenceCoordinate laterCoordinate =
            new(
                ResearchEvidenceLocation.ForInstruction(
                    coordinate.Location.Method,
                    9),
                coordinate.Kind);
        var sourceOrderDiffersFromIlOrder = new AnnotatedSourceDocument(
            "ab",
            [
                new AnnotatedSourceNode(
                    0,
                    "StackAllocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)],
                    Provenance:
                        new AnnotatedSourceNodeProvenance([9])),
                new AnnotatedSourceNode(
                    1,
                    "StackAllocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(1, 1)],
                    Provenance:
                        new AnnotatedSourceNodeProvenance(
                            [coordinate.Location.ILOffset!.Value])),
            ],
            [],
            [],
            []);
        (int[] reorderedIds, string? reorderedFailure) =
            AssemblyContextMemberProjectionQuery.FindEvidenceNodes(
                sourceOrderDiffersFromIlOrder,
                [coordinate, laterCoordinate]);
        Assert.Null(reorderedFailure);
        Assert.Equal([0, 1], reorderedIds);

        var sharedNode = new AnnotatedSourceDocument(
            "a",
            [
                new AnnotatedSourceNode(
                    0,
                    "StackAllocationExpression",
                    SourceLineKind.CSharp,
                    [new AnnotatedSourceSpan(0, 1)],
                    Provenance:
                        new AnnotatedSourceNodeProvenance(
                            [
                                coordinate.Location.ILOffset!.Value,
                                9,
                            ])),
            ],
            [],
            [],
            []);
        (int[] sharedIds, string? sharedFailure) =
            AssemblyContextMemberProjectionQuery.FindEvidenceNodes(
                sharedNode,
                [coordinate, laterCoordinate]);
        Assert.Null(sharedFailure);
        Assert.Equal([0], sharedIds);
    }

    [Fact]
    public async Task MemberProjection_MapsAnInvocationNodeToItsTypedCallee()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeLocal))));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        AssemblyMemberInvocationDestination destination =
            Assert.Single(projection.InvocationDestinations);
        AnnotatedSourceNode node = document.Nodes[destination.NodeId];
        Assert.Equal("InvocationExpression", node.Kind);
        Assert.Equal(SourceLineKind.CSharp, node.Medium);
        Assert.Equal(
            nameof(ResearchProjectionProbe.LocalCallee),
            destination.Target.Member.Name);
        Assert.Equal(
            typeof(ResearchProjectionProbe).FullName,
            destination.Target.Member.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public async Task MemberProjection_MapsAnExternalInvocationWithoutParsingSource()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeExternal))));

        AssemblyMemberInvocationDestination destination =
            Assert.Single(projection.InvocationDestinations);
        Assert.Equal(nameof(Math.Abs), destination.Target.Member.Name);
        Assert.Equal(
            typeof(Math).FullName,
            destination.Target.Member.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public async Task MemberProjection_MapsNestedInvocationsToTheirOwnCallees()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeNested))));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Dictionary<string, AnnotatedSourceNode> nodesByCallee =
            projection.InvocationDestinations.ToDictionary(
                destination => destination.Target.Member.Name,
                destination => document.Nodes[destination.NodeId]);
        Assert.Equal(2, nodesByCallee.Count);
        Assert.Contains(
            "Math.Abs(Identity(value))",
            NodeText(document, nodesByCallee[nameof(Math.Abs)]),
            StringComparison.Ordinal);
        Assert.Contains(
            "Identity(value)",
            NodeText(document, nodesByCallee[nameof(ResearchProjectionProbe.Identity)]),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberProjection_DoesNotConfusePropertyArgumentsWithTheirInvocation()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(
                    nameof(ResearchProjectionProbe.InvokeWithPropertyArguments))));

        AssemblyMemberInvocationDestination destination =
            Assert.Single(projection.InvocationDestinations);
        Assert.Equal(
            nameof(ResearchProjectionProbe.PropertyArgumentCallee),
            destination.Target.Member.Name);
        Assert.DoesNotContain(
            projection.InvocationDestinations,
            item => item.Target.Member.Name
                == $"get_{nameof(ResearchProjectionValue.Value)}");
    }

    [Fact]
    public async Task MemberProjection_RetainsRepeatedCallSiteNodes()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeRepeated))));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.True(
            projection.InvocationDestinations.Count == 2,
            $"Expected two invocation destinations; projected nodes: {string.Join(
                ", ",
                document.Nodes
                    .Where(node => node.Medium == SourceLineKind.CSharp)
                    .Select(node =>
                        $"{node.Id}:{node.Kind}:"
                        + $"{string.Join("/", node.Provenance?.IlOffsets ?? [])}"))}");
        Assert.Equal(
            2,
            projection.InvocationDestinations
                .Select(destination => destination.NodeId)
                .Distinct()
                .Count());
        Assert.All(
            projection.InvocationDestinations,
            destination => Assert.Equal(
                nameof(Math.Abs),
                destination.Target.Member.Name));
    }

    [Fact]
    public async Task MemberProjection_ComposesCallRelationshipsWithTheFindingCensus()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeRepeated))
                    with
                    {
                        FactRows = true,
                        CallRelationships = true,
                    }));

        FactRow[] relationshipRows =
        [
            .. Assert.IsAssignableFrom<IReadOnlyList<FactRow>>(
                    projection.Projection.Facts)
                .Where(row =>
                    row.Id
                        == ResearchFactRegistry
                            .CallRelationshipDescriptorId),
        ];
        Assert.Equal(2, relationshipRows.Length);
        Assert.All(
            relationshipRows,
            row =>
            {
                Assert.Equal(nameof(AnnotationCategory.Relationship), row.Category);
                Assert.Contains(nameof(Math.Abs), row.Detail);
                Assert.NotNull(row.InstanceKey);
            });

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(
                projection.Projection.SourceDocument);
        int[] relationshipFactIds =
        [
            .. document.Facts
                .Where(fact =>
                    fact.Descriptor
                        == ResearchFactRegistry
                            .CallRelationshipDescriptorId)
                .Select(fact => fact.Id),
        ];
        Assert.Equal(2, relationshipFactIds.Length);
        int[] invocationNodeIds =
        [
            .. document.Targets
                .Where(target =>
                    relationshipFactIds.Contains(target.FactId)
                    && document.Nodes[target.NodeId].Medium
                        == SourceLineKind.CSharp
                    && document.Nodes[target.NodeId].Kind
                        == "InvocationExpression")
                .Select(target => target.NodeId)
                .Distinct(),
        ];
        Assert.Equal(2, invocationNodeIds.Length);
        Assert.Equal(2, projection.InvocationDestinations.Count);
        AssemblyMemberCallRelationshipOverlay overlay =
            Assert.IsType<AssemblyMemberCallRelationshipOverlay>(
                projection.CallRelationships);
        Assert.Equal(2, overlay.Relationships.Count);
        Assert.Single(
            overlay.Relationships
                .Select(relationship => relationship.Occurrence.EdgeRow)
                .Distinct());
        Assert.Equal(
            relationshipFactIds.Order(),
            overlay.Relationships
                .Select(relationship => relationship.Occurrence.FactId)
                .Order());
        Assert.All(
            overlay.Relationships,
            relationship => Assert.Equal(
                nameof(Math.Abs),
                relationship.Target.Member.Name));

        int[] sourceFactIds =
        [
            .. Assert.IsAssignableFrom<
                    IReadOnlyList<AnnotatedSourceFactIdentity>>(
                    projection.Projection.SourceDocumentFactIdentities)
                .Select(identity => identity.FactId),
        ];
        Assert.All(
            relationshipFactIds,
            factId => Assert.Contains(factId, sourceFactIds));
    }

    [Fact]
    public async Task MemberProjection_ReportsAnAvailableEmptyCallRelationshipOverlay()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.BoxInt))
                    with
                    {
                        FactRows = true,
                        CallRelationships = true,
                    }));

        AssemblyMemberCallRelationshipOverlay overlay =
            Assert.IsType<AssemblyMemberCallRelationshipOverlay>(
                projection.CallRelationships);
        Assert.Empty(overlay.Relationships);
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(
                projection.Projection.SourceDocument);
        Assert.DoesNotContain(
            document.Facts,
            fact => fact.Descriptor
                == ResearchFactRegistry.CallRelationshipDescriptorId);
    }

    [Fact]
    public async Task MemberProjection_ProjectsRepeatedDirectRecursionAsOneCycle()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                CycleRequest(
                    image,
                    "Target",
                    "InstanceRecursionApi",
                    "RecurseTwice")));

        AssemblyMemberCallCycleInspection cycles =
            Assert.IsType<AssemblyMemberCallCycleInspection>(
                projection.CallCycles);
        AssemblyMemberCallCycle cycle =
            Assert.Single(cycles.Findings);
        Assert.True(cycles.IsComplete);
        Assert.Single(cycle.EdgeRows);
        Assert.Equal(2, cycle.FactIds.Count);
        Assert.Single(cycle.Targets);
        Assert.Equal(
            "RecurseTwice",
            cycle.Targets[0].Member.Name);
        AssemblyMemberCallRelationshipOverlay relationships =
            Assert.IsType<AssemblyMemberCallRelationshipOverlay>(
                projection.CallRelationships);
        Assert.Equal(
            cycle.FactIds.Order(),
            relationships.Relationships
                .Select(relationship =>
                    relationship.Occurrence.FactId)
                .Order());
        Assert.All(
            relationships.Relationships,
            relationship => Assert.Equal(
                cycle.EdgeRows[0],
                relationship.Occurrence.EdgeRow));
    }

    [Fact]
    public async Task MemberProjection_ProjectsMutualRecursionAsAnOrderedCycle()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                CycleRequest(
                    image,
                    "Target",
                    "InstanceRecursionApi",
                    "IsEven")));

        AssemblyMemberCallCycle cycle = Assert.Single(
            Assert.IsType<AssemblyMemberCallCycleInspection>(
                projection.CallCycles)
                .Findings);
        Assert.Equal(2, cycle.EdgeRows.Count);
        Assert.Single(cycle.FactIds);
        Assert.Equal(
            ["IsOdd", "IsEven"],
            cycle.Targets.Select(target =>
                target.Member.Name));
    }

    [Fact]
    public async Task MemberProjection_ReportsACompleteEmptyCycleCensus()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                CycleRequest(
                    image,
                    "Target",
                    "Api",
                    "Leaf")));

        AssemblyMemberCallCycleInspection cycles =
            Assert.IsType<AssemblyMemberCallCycleInspection>(
                projection.CallCycles);
        Assert.True(cycles.IsComplete);
        Assert.Empty(cycles.Findings);
    }

    [Fact]
    public async Task MemberProjection_OmitsGeneratedBodyCycleWithoutFailingSourceCensus()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                CycleRequest(
                    image,
                    "Target",
                    "InstanceRecursionApi",
                    "RecurseAsync")));

        AssemblyMemberCallCycleInspection cycles =
            Assert.IsType<AssemblyMemberCallCycleInspection>(
                projection.CallCycles);
        Assert.Empty(cycles.Findings);
        Assert.False(cycles.IsComplete);
        Assert.True(cycles.Limits.HasFlag(
            AnnotatedCallGraphCycleLimit
                .IncompleteCorrespondence));
        Assert.NotNull(projection.Projection.SourceDocument);
        Assert.NotNull(projection.CallRelationships);
    }

    [Theory]
    [InlineData(
        "Wait",
        SynchronousCompletionKind.TaskWait,
        "Wait")]
    [InlineData(
        "Result",
        SynchronousCompletionKind.TaskResult,
        "get_Result")]
    [InlineData(
        "AwaiterResult",
        SynchronousCompletionKind.TaskAwaiterGetResult,
        "GetResult")]
    [InlineData(
        "ConfiguredAwaiterResult",
        SynchronousCompletionKind.TaskAwaiterGetResult,
        "GetResult")]
    public async Task MemberProjection_ProjectsSynchronousTaskCompletionOperations(
        string member,
        SynchronousCompletionKind expectedKind,
        string expectedTarget)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                SynchronousCompletionRequest(image, member)));

        AssemblyMemberSynchronousCompletion observation =
            Assert.Single(
                Assert.IsAssignableFrom<
                    IReadOnlyList<AssemblyMemberSynchronousCompletion>>(
                    projection.SynchronousCompletions));
        Assert.Equal(expectedKind, observation.Kind);
        AssemblyMemberCallRelationship relationship =
            Assert.Single(
                Assert.IsType<AssemblyMemberCallRelationshipOverlay>(
                        projection.CallRelationships)
                    .Relationships,
                candidate =>
                    candidate.Occurrence.FactId
                        == observation.FactId);
        Assert.Equal(
            expectedTarget,
            relationship.Target.Member.Name);
    }

    [Fact]
    public async Task MemberProjection_DoesNotClassifyACustomAwaiter()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                SynchronousCompletionRequest(
                    image,
                    "CustomAwaiterResult")));

        Assert.Empty(
            Assert.IsAssignableFrom<
                IReadOnlyList<AssemblyMemberSynchronousCompletion>>(
                projection.SynchronousCompletions));
        Assert.NotEmpty(
            Assert.IsType<AssemblyMemberCallRelationshipOverlay>(
                    projection.CallRelationships)
                .Relationships);
    }

    [Theory]
    [InlineData("One", 1)]
    [InlineData("Configured", 1)]
    public async Task MemberProjection_ProjectsClassicAwaitCompletionPaths(
        string member,
        int expectedCount)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                AwaitCompletionPathRequest(image, member)));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(
                projection.Projection.SourceDocument);
        IReadOnlyList<AssemblyMemberAwaitCompletionPath> paths =
            Assert.IsAssignableFrom<
                IReadOnlyList<AssemblyMemberAwaitCompletionPath>>(
                projection.AwaitCompletionPaths);
        Assert.Equal(expectedCount, paths.Count);
        Assert.Equal(expectedCount, paths.Select(path => path.NodeId).Distinct().Count());
        Assert.All(paths, path =>
        {
            AnnotatedSourceNode node = document.Nodes[path.NodeId];
            Assert.Equal(SourceLineKind.CSharp, node.Medium);
            Assert.Equal(
                AnnotatedSourceNodeKinds.AwaitExpression,
                node.Kind);
            Assert.Contains(
                "await",
                NodeText(document, node),
                StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Custom")]
    public async Task MemberProjection_DeclinedClassicAwaitShapesProduceNoPathClaim(
        string member)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                AwaitCompletionPathRequest(image, member)));

        Assert.Empty(
            Assert.IsAssignableFrom<
                IReadOnlyList<AssemblyMemberAwaitCompletionPath>>(
                projection.AwaitCompletionPaths));
    }

    [Fact]
    public async Task MemberProjection_DoesNotProjectAwaitPathsForOrdinaryMethod()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                AwaitCompletionPathRequest(
                    image,
                    "Result",
                    "SynchronousCompletionApi")));

        Assert.Empty(
            Assert.IsAssignableFrom<
                IReadOnlyList<AssemblyMemberAwaitCompletionPath>>(
                projection.AwaitCompletionPaths));
    }

    [Theory]
    [InlineData(
        "ThrownValue",
        AllocationExceptionPathKind.ThrownValue)]
    [InlineData(
        "ExceptionHandler",
        AllocationExceptionPathKind.ExceptionHandler)]
    public async Task MemberProjection_ProjectsAllocationExceptionPaths(
        string member,
        AllocationExceptionPathKind expectedKind)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                AllocationExceptionPathRequest(image, member)));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(
                projection.Projection.SourceDocument);
        AssemblyMemberAllocationExceptionPath path =
            Assert.Single(
                Assert.IsAssignableFrom<
                    IReadOnlyList<
                        AssemblyMemberAllocationExceptionPath>>(
                    projection.AllocationExceptionPaths));
        Assert.Equal(expectedKind, path.Kind);
        AnnotatedSourceFact fact = document.Facts[path.FactId];
        Assert.Equal("alloc.new", fact.Descriptor);
        Assert.Equal(AnnotatedSourceFactOrigin.Body, fact.Origin);
        Assert.Contains(
            document.Targets,
            target => target.FactId == path.FactId);
    }

    [Fact]
    public async Task MemberProjection_DoesNotCallConditionalBranchAFallback()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                AllocationExceptionPathRequest(
                    image,
                    "ConditionalBranch")));

        Assert.Empty(
            Assert.IsAssignableFrom<
                IReadOnlyList<AssemblyMemberAllocationExceptionPath>>(
                projection.AllocationExceptionPaths));
    }

    [Fact]
    public async Task MemberProjection_ProjectsBoundedLocalThrowPaths()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        var targetParticipant = new AssemblyContextParticipant(
            ResolvedAssembly(image, "local-throw-target"),
            policy);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([targetParticipant]);

        AssemblyMemberProjection projection =
            Assert.IsType<
                    AssemblyContextEntry<AssemblyMemberProjection>
                        .Available>(
                AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                    group,
                    targetParticipant,
                    LocalThrowPathRequest(image)))
                .Value;

        AssemblyMemberLocalThrowPathInspection inspection =
            Assert.IsType<AssemblyMemberLocalThrowPathInspection>(
                projection.LocalThrowPaths);
        Assert.True(
            inspection.Paths.Count == 1,
            $"Boundaries: {string.Join(", ", inspection.Boundaries)}; "
                + $"receipt: {inspection.Receipt}");
        AssemblyMemberLocalThrowPath path = inspection.Paths[0];
        Assert.Equal(
            ["Forward", "Throw"],
            path.Targets.Select(target => target.Member.Name));
        Assert.Equal(2, path.FactIds.Count);
        Assert.Equal(
            path.FactIds.Order(),
            Assert.IsType<AssemblyMemberCallRelationshipOverlay>(
                    projection.CallRelationships)
                .Relationships
                .Where(relationship =>
                    relationship.Target.Member.Name == "Forward")
                .Select(relationship =>
                    relationship.Occurrence.FactId)
                .Order());
        AssemblyMemberLocalThrowSite terminal =
            Assert.Single(path.TerminalThrows);
        Assert.Equal(
            "LocalThrowPathException",
            terminal.ExceptionType.Name);
        Assert.True(terminal.ConstructionOffset < terminal.ThrowOffset);
        Assert.True(terminal.ConstructorToken > 0);
        Assert.Equal(
            0x02000000,
            terminal.Definition.Definition.Value & 0xFF000000);
        Assert.DoesNotContain(
            path.Targets,
            target => target.Member.Name == "Entry");
        Assert.Equal(1, inspection.Receipt.RequestedDestinations);
        Assert.Equal(1, inspection.Receipt.ObservedReachablePairs);
        Assert.Equal(1, inspection.Receipt.ReturnedPaths);
    }

    [Fact]
    public async Task MemberProjection_RetainsOneEqualShortestWitnessWithoutPerEdgeAbsence()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        var targetParticipant = new AssemblyContextParticipant(
            ResolvedAssembly(image, "local-throw-alternatives"),
            policy);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([targetParticipant]);

        AssemblyMemberProjection projection =
            Assert.IsType<
                    AssemblyContextEntry<AssemblyMemberProjection>
                        .Available>(
                AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                    group,
                    targetParticipant,
                    LocalThrowPathRequest(
                        image,
                        "EntryAlternatives")))
                .Value;

        AssemblyMemberLocalThrowPathInspection inspection =
            Assert.IsType<AssemblyMemberLocalThrowPathInspection>(
                projection.LocalThrowPaths);
        AssemblyMemberLocalThrowPath path =
            Assert.Single(inspection.Paths);
        Assert.Equal(
            ["ForwardA", "Throw"],
            path.Targets.Select(target => target.Member.Name));
        AssemblyMemberCallRelationshipOverlay relationships =
            Assert.IsType<AssemblyMemberCallRelationshipOverlay>(
                projection.CallRelationships);
        int alternateFactId = Assert.Single(
                relationships.Relationships,
                relationship =>
                    relationship.Target.Member.Name == "ForwardB")
            .Occurrence.FactId;
        Assert.DoesNotContain(
            alternateFactId,
            path.FactIds);
        Assert.Equal(1, inspection.Receipt.RequestedDestinations);
        Assert.Equal(1, inspection.Receipt.ObservedReachablePairs);
        Assert.Equal(1, inspection.Receipt.ReturnedPaths);
    }

    [Fact]
    public async Task MemberProjection_SkipsRootPathSearchWithoutThrowDestinations()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerGraphTarget
                        .AssemblyPath()));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        var targetParticipant = new AssemblyContextParticipant(
            ResolvedAssembly(image, "local-throw-empty"),
            policy);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([targetParticipant]);

        AssemblyMemberProjection projection =
            Assert.IsType<
                    AssemblyContextEntry<AssemblyMemberProjection>
                        .Available>(
                AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                    group,
                    targetParticipant,
                    new AssemblyContextMemberProjectionRequest(
                        "Target.Api",
                        "Forward",
                        MethodToken: MethodToken(
                            image,
                            "Target",
                            "Api",
                            "Forward"),
                        SourceDocument: true,
                        FactRows: true,
                        AnalysisFeatures:
                            LibraryBodyAnalysisFeatures.Default
                                | LibraryBodyAnalysisFeatures.LocalThrows,
                        CallRelationships: true,
                        LocalThrowPaths: true)))
                .Value;

        AssemblyMemberLocalThrowPathInspection inspection =
            Assert.IsType<AssemblyMemberLocalThrowPathInspection>(
                projection.LocalThrowPaths);
        Assert.Empty(inspection.Paths);
        Assert.Equal(1, inspection.Receipt.RequestedRoots);
        Assert.Equal(0, inspection.Receipt.RequestedDestinations);
        Assert.Equal(0, inspection.Receipt.DestinationSearches);
        Assert.Equal(0, inspection.Receipt.SearchNodes);
        Assert.Equal(0, inspection.Receipt.SearchedEdges);
        Assert.Equal(0, inspection.Receipt.ObservedReachablePairs);
        Assert.Equal(0, inspection.Receipt.ReturnedPaths);
    }

    [Fact]
    public async Task MemberProjection_PreservesCoreLibThrowHelperDistinction()
    {
        MethodInfo helper =
            typeof(ArgumentNullException).GetMethod(
                "ThrowIfNull",
                [typeof(object), typeof(string)])
            ?? throw new InvalidOperationException(
                "CoreLib has no ArgumentNullException.ThrowIfNull overload.");
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    typeof(ArgumentNullException).Assembly.Location));
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy, image);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                new AssemblyContextMemberProjectionRequest(
                    "System.ArgumentNullException",
                    "ThrowIfNull",
                    MethodToken: helper.MetadataToken,
                    SourceDocument: true,
                    FactRows: true,
                    AnalysisFeatures:
                        LibraryBodyAnalysisFeatures.Default
                            | LibraryBodyAnalysisFeatures.LocalThrows,
                    CallRelationships: true,
                    LocalThrowPaths: true)));

        AssemblyMemberLocalThrowPath path =
            Assert.Single(
                Assert.IsType<AssemblyMemberLocalThrowPathInspection>(
                        projection.LocalThrowPaths)
                    .Paths,
                candidate =>
                    candidate.Targets[^1].Member.Name == "Throw");
        Assert.DoesNotContain(
            path.Targets,
            target => target.Member.Name == "ThrowIfNull");
        Assert.Contains(
            path.TerminalThrows,
            site =>
                site.ExceptionType.Name
                    == nameof(ArgumentNullException));
    }

    [Fact]
    public async Task MemberProjection_RetainsVersionDistinctInvocationTargets()
    {
        ImmutableArray<byte> image = BuildVersionedInvocationImage();
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [Participant(image, ContentIdentity(image), policy)]);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                new AssemblyContextMemberProjectionRequest(
                    "Shared.Entry",
                    "RunVersioned",
                    SourceDocument: true)
                {
                    MethodToken = MethodToken(
                        image,
                        "Shared",
                        "Entry",
                        "RunVersioned"),
                    InvocationDestinations = true,
                }));

        Assert.Equal(2, projection.InvocationDestinations.Count);
        Assert.Equal(
            [new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)],
            projection.InvocationDestinations
                .Select(destination =>
                    Assert.IsType<TypeReferenceOrigin.AssemblyReference>(
                        destination.Target.Member.DeclaringType
                            .Resolution?.Origin)
                    .Assembly.Version!)
                .Order()
                .ToArray());
        Assert.Equal(
            [new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)],
            projection.InvocationDestinations
                .Select(destination =>
                    Assert.IsType<AssemblyReferenceIdentity>(
                        destination.Target.OccurrenceAssemblyIdentity)
                    .Version!)
                .Order()
                .ToArray());
        Assert.All(
            projection.InvocationDestinations,
            destination => Assert.Null(
                destination.Target.DefinitionAssemblyIdentity));
    }

    [Fact]
    public async Task MemberProjection_RequiresSourceForInvocationDestinations()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.InvokeLocal)) with
                {
                    SourceDocument = false,
                    InvocationDestinations = true,
                }));

        Assert.Contains("source document", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberProjection_RequiresExactSourceForCallRelationships()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        ArgumentException missingSource = Assert.Throws<ArgumentException>(() =>
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.InvokeLocal)) with
                {
                    SourceDocument = false,
                    CallRelationships = true,
                }));
        Assert.Contains(
            "source document and an exact MethodDef token",
            missingSource.Message,
            StringComparison.Ordinal);

        ArgumentException missingToken = Assert.Throws<ArgumentException>(() =>
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.InvokeLocal)) with
                {
                    CallRelationships = true,
                }));
        Assert.Contains(
            "source document and an exact MethodDef token",
            missingToken.Message,
            StringComparison.Ordinal);

        ArgumentException missingRelationships =
            Assert.Throws<ArgumentException>(() =>
                AssemblyContextMemberProjectionQuery.Execute(
                    group,
                    InvocationRequest(
                        nameof(ResearchProjectionProbe.InvokeLocal))
                        with
                        {
                            CallCycles = true,
                        }));
        Assert.Contains(
            "exact call relationships",
            missingRelationships.Message,
            StringComparison.Ordinal);

        ArgumentException missingSynchronousRelationships =
            Assert.Throws<ArgumentException>(() =>
                AssemblyContextMemberProjectionQuery.Execute(
                    group,
                    InvocationRequest(
                        nameof(ResearchProjectionProbe.InvokeLocal))
                        with
                        {
                            SynchronousCompletions = true,
                        }));
        Assert.Contains(
            "exact call relationships",
            missingSynchronousRelationships.Message,
            StringComparison.Ordinal);

        ArgumentException missingLocalThrowRelationships =
            Assert.Throws<ArgumentException>(() =>
                AssemblyContextMemberProjectionQuery.Execute(
                    group,
                    InvocationRequest(
                        nameof(ResearchProjectionProbe.InvokeLocal))
                        with
                        {
                            AnalysisFeatures =
                                LibraryBodyAnalysisFeatures.Default
                                    | LibraryBodyAnalysisFeatures.LocalThrows,
                            LocalThrowPaths = true,
                        }));
        Assert.Contains(
            "exact call relationships and local-throw analysis",
            missingLocalThrowRelationships.Message,
            StringComparison.Ordinal);

        ArgumentException missingLocalThrowAnalysis =
            Assert.Throws<ArgumentException>(() =>
                AssemblyContextMemberProjectionQuery.Execute(
                    group,
                    InvocationRequest(
                        nameof(ResearchProjectionProbe.InvokeLocal))
                        with
                        {
                            CallRelationships = true,
                            LocalThrowPaths = true,
                        }));
        Assert.Contains(
            "exact call relationships and local-throw analysis",
            missingLocalThrowAnalysis.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberProjection_RequiresFactsAndSourceForFindingEvidence()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.InvokeStackAllocation)) with
                {
                    FactRows = false,
                    FindingEvidence = true,
                }));

        Assert.Contains("Facts rows and a source document", error.Message);
    }

    [Fact]
    public async Task MemberProjection_MethodTokenAddressesTheExactOverload()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        int token = typeof(ResearchProjectionProbe)
            .GetMethod(
                nameof(ResearchProjectionProbe.Overloaded),
                BindingFlags.Public | BindingFlags.Static,
                [typeof(string)])!
            .MetadataToken;

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.Overloaded)) with
                {
                    MethodToken = token,
                }));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.Contains("string", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Overloaded(int", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projection_ResolvesReferencesThroughTheParticipantBindingPolicy()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);

        Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        // Reference resolution is a policy question, not a name match: every request the
        // projection made names the participant it came from, so a sibling is selected by the
        // group's binding snapshot rather than by a matching simple name.
        Assert.NotEmpty(policy.Requests);
        Assert.All(
            policy.Requests,
            request => Assert.Same(
                participant.Assembly.Registration,
                Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(request.Origin)
                    .Registration));
    }

    [Fact]
    public async Task Projection_DoesNotAcquireAPolicySelectionOutsideTheGroup()
    {
        ImmutableArray<byte> image = SelfImage();
        int outsiderOpens = 0;
        ResolvedAssemblyReference outsider = ResolvedAssemblyReference.Create(
            ContentIdentity(image),
            path: null,
            () =>
            {
                outsiderOpens++;
                return new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false);
            },
            AssemblyResolutionProvenance.Package(
                "outsider",
                "1.0.0",
                "net11.0",
                rid: null));
        var policy = new SelectingBindingPolicy(outsider);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        Assert.NotEmpty(policy.Requests);
        Assert.Equal(0, outsiderOpens);
    }

    [Fact]
    public async Task Execute_CarriesRejectedParticipantBesideLaterResultsInGroupOrder()
    {
        ImmutableArray<byte> image = SelfImage();
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                Participant(image, ContentIdentity(image) with { Name = "WrongIdentity" }, policy),
                Participant(image, ContentIdentity(image), policy),
            ]);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!));

        Assert.False(result.IsComplete);
        var rejected = Assert.IsType<
            AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Rejected>(
            result.Assemblies[0]);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.IsType<AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Available>(
            result.Assemblies[1]);
    }

    [Fact]
    public async Task TypeProjection_ReportsAMissingTypeAsATypedParticipantFailure()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest("No.Such.Type"));

        var failed = Assert.IsType<
            AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Failed>(
            Assert.Single(result.Assemblies));
        Assert.Contains("No.Such.Type", failed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteParticipant_RefusesAParticipantOutsideTheGroup()
    {
        ImmutableArray<byte> image = SelfImage();
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        AssemblyContextParticipant outsider =
            Participant(image, ContentIdentity(image), policy);

        Assert.Throws<ArgumentException>(
            () => AssemblyContextTypeProjectionQuery.ExecuteParticipant(
                group,
                outsider,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!)));
    }

    [Fact]
    public async Task ExecuteParticipant_ProjectsOnlyTheRequestedParticipant()
    {
        var policy = new RecordingBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyContextEntry<AssemblyMemberProjection> entry =
            AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                group,
                Assert.Single(group.Participants),
                Request(nameof(ResearchProjectionProbe.BoxInt)));

        var available =
            Assert.IsType<AssemblyContextEntry<AssemblyMemberProjection>.Available>(entry);
        Assert.IsType<AnnotatedSourceDocument>(
            available.Value.Projection.SourceDocument);
    }

    [Fact]
    public async Task TypeProjection_DoesNotPublishAfterBindingVersionChanges()
    {
        using var policy = new ResearchPublicationBindingPolicy(
            changeOnVersionRead: 4);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        var failed = Assert.IsType<
            AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Failed>(
            Assert.Single(
                AssemblyContextTypeProjectionQuery.Execute(
                    group,
                    new AssemblyContextTypeProjectionRequest(
                        typeof(ResearchProjectionProbe).FullName!))
                    .Assemblies));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    [Fact]
    public async Task MemberProjection_DoesNotPublishAfterBindingVersionChanges()
    {
        var request = new AssemblyContextMemberProjectionRequest(
            typeof(ResearchProjectionProbe).FullName!,
            nameof(ResearchProjectionProbe.BoxInt),
            AnalysisFeatures: LibraryBodyAnalysisFeatures.None);
        int selectionCount;
        using (var stablePolicy = new ResearchPublicationBindingPolicy())
        await using (var stableWorkspace = new InspectionWorkspace())
        using (AssemblyContextGroup stable =
            ContentGroup(stableWorkspace, stablePolicy))
        {
            Available(
                AssemblyContextMemberProjectionQuery.Execute(
                    stable,
                    request));
            selectionCount = stablePolicy.SelectionCount;
        }
        Assert.True(selectionCount > 0);

        using var policy = new ResearchPublicationBindingPolicy(selectionCount);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        Task<AssemblyContextResult<AssemblyMemberProjection>> execution =
            Task.Run(
                () => AssemblyContextMemberProjectionQuery.Execute(
                    group,
                    request));
        bool reachedPublicationBoundary = policy.WaitForVersionRead();
        if (reachedPublicationBoundary)
            policy.ReplaceVersion();
        policy.ContinueVersionRead();
        Assert.True(reachedPublicationBoundary);

        var failed = Assert.IsType<
            AssemblyContextEntry<AssemblyMemberProjection>.Failed>(
            Assert.Single((await execution).Assemblies));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    static AssemblyContextMemberProjectionRequest Request(string member) =>
        new(
            typeof(ResearchProjectionProbe).FullName!,
            member,
            SourceDocument: true);

    static AssemblyContextMemberProjectionRequest InvocationRequest(string member)
    {
        MethodInfo method = typeof(ResearchProjectionProbe).GetMethod(
            member,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Missing invocation probe {member}.");
        return Request(member) with
        {
            MethodToken = method.MetadataToken,
            InvocationDestinations = true,
        };
    }

    static AssemblyContextMemberProjectionRequest FindingEvidenceRequest(
        string member)
    {
        MethodInfo method = typeof(ResearchProjectionProbe).GetMethod(
            member,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Missing callee-evidence probe {member}.");
        return Request(member) with
        {
            MethodToken = method.MetadataToken,
            FactRows = true,
            FindingEvidence = true,
        };
    }

    static AssemblyContextMemberProjectionRequest CycleRequest(
        ImmutableArray<byte> image,
        string typeNamespace,
        string typeName,
        string member)
    {
        int methodToken = MethodToken(
            image,
            typeNamespace,
            typeName,
            member);
        return new AssemblyContextMemberProjectionRequest(
            $"{typeNamespace}.{typeName}",
            member,
            MethodToken: methodToken,
            SourceDocument: true,
            FactRows: true,
            InvocationDestinations: true,
            CallRelationships: true,
            CallCycles: true);
    }

    static AssemblyContextMemberProjectionRequest
        SynchronousCompletionRequest(
            ImmutableArray<byte> image,
            string member) =>
        new(
            "Target.SynchronousCompletionApi",
            member,
            MethodToken: MethodToken(
                image,
                "Target",
                "SynchronousCompletionApi",
                member),
            SourceDocument: true,
            FactRows: true,
            InvocationDestinations: true,
            CallRelationships: true,
            SynchronousCompletions: true);

    static AssemblyContextMemberProjectionRequest
        AwaitCompletionPathRequest(
            ImmutableArray<byte> image,
            string member,
            string typeName = "AwaitCompletionPathApi") =>
        new(
            $"Target.{typeName}",
            member,
            MethodToken: MethodToken(
                image,
                "Target",
                typeName,
                member),
            SourceDocument: true,
            AwaitCompletionPaths: true);

    static AssemblyContextMemberProjectionRequest
        AllocationExceptionPathRequest(
            ImmutableArray<byte> image,
            string member) =>
        new(
            "Target.AllocationExceptionPathApi",
            member,
            MethodToken: MethodToken(
                image,
                "Target",
                "AllocationExceptionPathApi",
                member),
            SourceDocument: true,
            FactRows: true,
            AllocationExceptionPaths: true);

    static AssemblyContextMemberProjectionRequest
        LocalThrowPathRequest(
            ImmutableArray<byte> image,
            string member = "Entry") =>
        new(
            "Target.LocalThrowPathApi",
            member,
            MethodToken: MethodToken(
                image,
                "Target",
                "LocalThrowPathApi",
                member),
            SourceDocument: true,
            FactRows: true,
            AnalysisFeatures:
                LibraryBodyAnalysisFeatures.Default
                    | LibraryBodyAnalysisFeatures.LocalThrows,
            CallRelationships: true,
            LocalThrowPaths: true);

    static string NodeText(
        AnnotatedSourceDocument document,
        AnnotatedSourceNode node) =>
        string.Concat(
            node.Spans.Select(span =>
                document.Text.Substring(span.Start, span.Length)));

    static int MethodToken(
        ImmutableArray<byte> image,
        string typeNamespace,
        string typeName,
        string methodName)
    {
        using var reader = new PEReader(image);
        MetadataReader metadata = reader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = metadata.TypeDefinitions.Single(handle =>
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Namespace) == typeNamespace
                && metadata.GetString(type.Name) == typeName;
        });
        TypeDefinition definition = metadata.GetTypeDefinition(typeHandle);
        MethodDefinitionHandle methodHandle =
            definition.GetMethods().Single(handle =>
                metadata.GetString(metadata.GetMethodDefinition(handle).Name)
                    == methodName);
        return MetadataTokens.GetToken(methodHandle);
    }

    static ImmutableArray<byte> BuildVersionedInvocationImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("VersionedInvocation.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("VersionedInvocation"),
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

        AssemblyReferenceHandle v1 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Versioned.Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        AssemblyReferenceHandle v2 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Versioned.Target"),
            new Version(2, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle targetV1 = metadata.AddTypeReference(
            v1,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddString("Api"));
        TypeReferenceHandle targetV2 = metadata.AddTypeReference(
            v2,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddString("Api"));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        MemberReferenceHandle pingV1 = metadata.AddMemberReference(
            targetV1,
            metadata.GetOrAddString("Ping"),
            signatureHandle);
        MemberReferenceHandle pingV2 = metadata.AddMemberReference(
            targetV2,
            metadata.GetOrAddString("Ping"),
            signatureHandle);

        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Shared"),
            metadata.GetOrAddString("Entry"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(
            il,
            new ControlFlowBuilder());
        instructions.Call(pingV1);
        instructions.Call(pingV2);
        instructions.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(instructions, maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("RunVersioned"),
            signatureHandle,
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableCollectionsMarshal.AsImmutableArray(image.ToArray());
    }

    static AssemblyContextGroup ContentGroup(
        InspectionWorkspace workspace,
        IAssemblyBindingPolicy policy)
    {
        ImmutableArray<byte> image = SelfImage();
        return ContentGroup(
            workspace,
            policy,
            image);
    }

    static AssemblyContextGroup ContentGroup(
        InspectionWorkspace workspace,
        IAssemblyBindingPolicy policy,
        ImmutableArray<byte> image)
    {
        return workspace.CreateAssemblyContextGroup(
            [Participant(image, ContentIdentity(image), policy)]);
    }

    static AssemblyContextParticipant Participant(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity,
        IAssemblyBindingPolicy policy)
        => new(ResolvedAssembly(image, identity, "probe"), policy);

    static ResolvedAssemblyReference ResolvedAssembly(
        ImmutableArray<byte> image,
        string packageId) =>
        ResolvedAssembly(
            image,
            ContentIdentity(image),
            packageId);

    static ResolvedAssemblyReference ResolvedAssembly(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity,
        string packageId) =>
        ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(
                ImmutableCollectionsMarshal.AsArray(image)!,
                writable: false),
            AssemblyResolutionProvenance.Package(
                packageId,
                "1.0.0",
                "net11.0",
                rid: null));

    static ImmutableArray<byte> SelfImage() =>
        ImmutableCollectionsMarshal.AsImmutableArray(
            File.ReadAllBytes(
                typeof(AssemblyContextResearchProjectionQueryTests).Assembly.Location));

    static AssemblyReferenceIdentity ContentIdentity(ImmutableArray<byte> image)
    {
        using var reader = new PEReader(image);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
    }

    static TValue Available<TValue>(AssemblyContextResult<TValue> result)
        => Assert.IsType<AssemblyContextEntry<TValue>.Available>(
                Assert.Single(result.Assemblies))
            .Value;

    sealed class RecordingBindingPolicy : IAssemblyBindingPolicy
    {
        readonly List<AssemblyBindingRequest> _requests = [];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        internal IReadOnlyList<AssemblyBindingRequest> Requests => _requests;

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                _requests.Add(request);
                return AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable));

            }
        }
    }

    sealed class SelectingBindingPolicy(ResolvedAssemblyReference selection)
        : IAssemblyBindingPolicy
    {
        readonly List<AssemblyBindingRequest> _requests = [];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        internal IReadOnlyList<AssemblyBindingRequest> Requests => _requests;

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                _requests.Add(request);
                return AssemblyBindingSelection.Found(selection);

            }
        }
    }

}

/// <summary>Probe members the group-scoped Research projections address.</summary>
public static class ResearchProjectionProbe
{
    static object? _accessorValue;

    public static object BoxInt(int value) => value;

    public static bool GenericObjectEqualsInLocal<T>(
        T left,
        T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) =>
            x!.Equals(y);
    }

    public static object BoxedValue => 42;

    public static object AccessorBoxedValue
    {
        get => _accessorValue ?? 42;
        set => _accessorValue = value ?? 43;
    }

    public static int Overloaded(int value) => value + 1;

    public static int Overloaded(string value) => value.Length;

    public static int InvokeLocal(int value) => LocalCallee(value);

    public static int LocalCallee(int value) => value + 1;

    public static int InvokeStackAllocation(int value) =>
        StackAllocationCallee(value);

    static int StackAllocationCallee(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }

    public static int InvokeAllocationInLoop(int count) =>
        AllocationInLoop(count);

    static int AllocationInLoop(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += new object().GetHashCode();
        return total;
    }

    public static unsafe int InvokeFunctionPointer(
        delegate*<int, int> callback,
        int value) =>
        FunctionPointerCallee(callback, value);

    static unsafe int FunctionPointerCallee(
        delegate*<int, int> callback,
        int value) =>
        callback(value);

    public static void InvokeThrowing() => ThrowingCallee();

    static void ThrowingCallee() =>
        throw new InvalidOperationException("callee evidence");

    public static int InvokeExternal(int value) => Math.Abs(value);

    public static int InvokeNested(int value) => Math.Abs(Identity(value));

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int Identity(int value) => value;

    public static int InvokeWithPropertyArguments(
        ResearchProjectionValue first,
        ResearchProjectionValue second) =>
        PropertyArgumentCallee(first.Value, second.Value);

    public static int PropertyArgumentCallee(int first, int second) =>
        first + second;

    public static int InvokeRepeated(int value)
    {
        int first = Math.Abs(value);
        int second = Math.Abs(value);
        return first + second;
    }

    public static class Nested
    {
        public static object BoxNested(int value) => value;
    }
}

public sealed class ResearchProjectionValue
{
    public int Value { get; init; }
}
