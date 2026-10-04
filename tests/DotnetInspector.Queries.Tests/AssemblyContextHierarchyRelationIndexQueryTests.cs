using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextHierarchyRelationIndexQueryTests
{
    [Fact]
    public async Task PreparationIsSettledPerParticipantAndPolicy()
    {
        string path = typeof(object).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, path);
        AssemblyContextParticipant participant = group.Participants[0];

        var first = Assert.IsType<
            AssemblyContextHierarchyRelationIndexPreparation.Ready>(
                AssemblyContextHierarchyRelationIndexQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        MetadataOperationPolicy.Unbounded,
                        TestContext.Current.CancellationToken));
        var second = Assert.IsType<
            AssemblyContextHierarchyRelationIndexPreparation.Ready>(
                AssemblyContextHierarchyRelationIndexQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        new(
                            long.MaxValue,
                            maxRetainedMethodSemanticsAssociations:
                                long.MaxValue,
                            maxRetainedHierarchyRelations:
                                long.MaxValue),
                        TestContext.Current.CancellationToken));
        var limited = Assert.IsType<
            AssemblyContextHierarchyRelationIndexPreparation.Ready>(
                AssemblyContextHierarchyRelationIndexQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        new(
                            long.MaxValue,
                            maxRetainedHierarchyRelations: 0),
                        TestContext.Current.CancellationToken));

        Assert.Same(first, second);
        Assert.NotSame(first, limited);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            first.Receipt.Disposition);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            limited.Receipt.Disposition);
    }

    [Fact]
    public async Task PreparedExecutionAnswersMultipleTargets()
    {
        string path = typeof(object).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, path);
        var ready = Assert.IsType<
            AssemblyContextHierarchyRelationIndexPreparation.Ready>(
                AssemblyContextHierarchyRelationIndexQuery
                    .PrepareParticipant(
                        group,
                        group.Participants[0],
                        MetadataOperationPolicy.Unbounded,
                        TestContext.Current.CancellationToken));
        using AssemblyContextHierarchyRelationIndexExecution execution =
            ready.OpenExecution();

        MetadataHierarchyRelationAnalysisResult objectBases =
            RequireAvailable(
                execution.Analyze(
                    Request(
                        TypeName("System", "Object"),
                        MetadataHierarchyRelationKind.BaseType,
                        materializeRows: false),
                    TestContext.Current.CancellationToken));
        MetadataHierarchyRelationAnalysisResult enumerableImplementers =
            RequireAvailable(
                execution.Analyze(
                    Request(
                        TypeName(
                            "System.Collections.Generic",
                            "IEnumerable`1"),
                        MetadataHierarchyRelationKind.Interface,
                        materializeRows: true),
                    TestContext.Current.CancellationToken));

        Assert.True(objectBases.CandidateCount > 0);
        Assert.Empty(objectBases.Relations.Evidence);
        Assert.True(enumerableImplementers.CandidateCount > 0);
        Assert.Equal(
            enumerableImplementers.CandidateCount,
            enumerableImplementers.Relations.Evidence.Length);
    }

    [Fact]
    public async Task PreparedExecutionRetainsIndexUntilFinalRelease()
    {
        string path = typeof(object).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, path);
        AssemblyContextParticipant participant = group.Participants[0];
        AssemblyContextHierarchyRelationIndexPreparation.Ready? ready =
            null;
        AssemblyContextHierarchyRelationIndexExecution? firstExecution =
            null;
        AssemblyContextHierarchyRelationIndexExecution? secondExecution =
            null;

        AssemblyImageAccessResult<int> result =
            await group.UseAndReleaseAssemblySessionAsync(
                participant.Assembly,
                (_, _) =>
                {
                    ready = Assert.IsType<
                        AssemblyContextHierarchyRelationIndexPreparation
                            .Ready>(
                            AssemblyContextHierarchyRelationIndexQuery
                                .PrepareParticipant(
                                    group,
                                    participant,
                                    MetadataOperationPolicy.Unbounded,
                                    TestContext.Current.CancellationToken));
                    firstExecution = ready.OpenExecution();
                    secondExecution = ready.OpenExecution();
                    return Task.FromResult(1);
                });

        Assert.IsType<AssemblyImageAccessResult<int>.Available>(result);
        Assert.NotNull(ready);
        Assert.NotNull(firstExecution);
        Assert.NotNull(secondExecution);
        Assert.True(group.RetainedImageBytes > 0);
        Assert.True(
            RequireAvailable(
                    firstExecution.Analyze(
                        ObjectBaseRequest(),
                        TestContext.Current.CancellationToken))
                .CandidateCount > 0);
        Assert.True(
            RequireAvailable(
                    secondExecution.Analyze(
                        ObjectBaseRequest(),
                        TestContext.Current.CancellationToken))
                .CandidateCount > 0);
        Assert.Throws<ObjectDisposedException>(
            () => ready.Analyze(
                ObjectBaseRequest(),
                TestContext.Current.CancellationToken));

        firstExecution.Dispose();

        Assert.True(group.RetainedImageBytes > 0);
        Assert.Throws<ObjectDisposedException>(
            () => firstExecution.Analyze(
                ObjectBaseRequest(),
                TestContext.Current.CancellationToken));
        Assert.True(
            RequireAvailable(
                    secondExecution.Analyze(
                        ObjectBaseRequest(),
                        TestContext.Current.CancellationToken))
                .CandidateCount > 0);

        secondExecution.Dispose();

        Assert.Equal(0, group.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(
            () => secondExecution.Analyze(
                ObjectBaseRequest(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ParticipantReleaseKeepsSiblingIndexPrepared()
    {
        string path = typeof(object).Assembly.Location;
        var first = Participant(path, "first hierarchy participant");
        var second = Participant(path, "second hierarchy participant");
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([first, second]);
        var firstReady = Assert.IsType<
            AssemblyContextHierarchyRelationIndexPreparation.Ready>(
                AssemblyContextHierarchyRelationIndexQuery
                    .PrepareParticipant(
                        group,
                        first,
                        MetadataOperationPolicy.Unbounded,
                        TestContext.Current.CancellationToken));
        var secondReady = Assert.IsType<
            AssemblyContextHierarchyRelationIndexPreparation.Ready>(
                AssemblyContextHierarchyRelationIndexQuery
                    .PrepareParticipant(
                        group,
                        second,
                        MetadataOperationPolicy.Unbounded,
                        TestContext.Current.CancellationToken));

        AssemblyImageAccessResult<int> result =
            await group.UseAndReleaseAssemblySessionAsync(
                first.Assembly,
                (_, _) => Task.FromResult(1));

        Assert.IsType<AssemblyImageAccessResult<int>.Available>(result);
        Assert.Throws<ObjectDisposedException>(
            () => firstReady.Analyze(
                ObjectBaseRequest(),
                TestContext.Current.CancellationToken));
        Assert.True(
            RequireAvailable(
                    secondReady.Analyze(
                        ObjectBaseRequest(),
                        TestContext.Current.CancellationToken))
                .CandidateCount > 0);
        Assert.True(group.RetainedImageBytes > 0);
    }

    [Fact]
    public async Task ParticipantFailureIsSettledOnce()
    {
        string path = typeof(object).Assembly.Location;
        ResolvedAssemblyReference valid =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "hierarchy preparation test"));
        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                valid.Identity,
                path: null,
                () => throw new IOException(
                    "Hierarchy participant failed."),
                AssemblyResolutionProvenance.Local(
                    "hierarchy preparation failure"));
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(
            rejected,
            NoResolverAssemblyBindingPolicy.Instance);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var first = Assert.IsType<
            AssemblyContextHierarchyRelationIndexPreparation
                .ParticipantRejected>(
                    AssemblyContextHierarchyRelationIndexQuery
                        .PrepareParticipant(
                            group,
                            participant,
                            MetadataOperationPolicy.Unbounded,
                            TestContext.Current.CancellationToken));
        AssemblyContextHierarchyRelationIndexPreparation second =
            AssemblyContextHierarchyRelationIndexQuery
                .PrepareParticipant(
                    group,
                    participant,
                    MetadataOperationPolicy.Unbounded,
                    TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(
            CandidateOpenFailureKind.Unreadable,
            first.Failure.Kind);
    }

    private static MetadataHierarchyRelationAnalysisRequest
        ObjectBaseRequest() =>
        Request(
            TypeName("System", "Object"),
            MetadataHierarchyRelationKind.BaseType,
            materializeRows: false);

    private static MetadataHierarchyRelationAnalysisRequest Request(
        MetadataTypeDefinitionName target,
        MetadataHierarchyRelationKind kind,
        bool materializeRows) =>
        new(
            new(target, kind),
            MetadataOperationPolicy.Unbounded,
            materializeRows: materializeRows);

    private static MetadataHierarchyRelationAnalysisResult
        RequireAvailable(
            MetadataHierarchyRelationAnalysisOutcome outcome) =>
        Assert.IsType<
                MetadataHierarchyRelationAnalysisOutcome.Available>(
                outcome)
            .Result;

    private static MetadataTypeDefinitionName TypeName(
        string typeNamespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    typeNamespace,
                    [name]))
            .Name;

    private static AssemblyContextParticipant Participant(
        string path,
        string provenance) =>
        new(
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(provenance)),
            NoResolverAssemblyBindingPolicy.Instance);

    private static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path) =>
        workspace.CreateAssemblyContextGroup(
            [Participant(path, "hierarchy preparation test")]);
}
