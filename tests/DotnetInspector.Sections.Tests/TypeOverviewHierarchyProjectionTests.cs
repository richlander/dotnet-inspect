namespace DotnetInspector.Sections.Tests;

public sealed partial class TypeOverviewDocumentInspectionOperationTests
{
    [Fact]
    public void HierarchyRequest_IsCarriedByOverviewPlan()
    {
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology> request =
            CompactHierarchy();
        var plan =
            new TypeOverviewDocumentInspectionPlan(
                Name(
                    "System.Text.Json",
                    "JsonSerializer"),
                new(maximumRows: 1),
                s_bounds,
                hierarchy: request);

        Assert.Same(request, plan.Hierarchy);
    }

    [Fact]
    public void HierarchyRequest_RejectsUnsupportedTerminalAndSpellingPlan()
    {
        var unsupported =
            new InspectionHierarchyRequest<
                TypeOverviewHierarchyTopology>(
                TypeOverviewHierarchyTopology
                    .TypeCategoriesAndMemberGroups,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Count());

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeOverviewDocumentInspectionPlan(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    new(maximumRows: 1),
                    s_bounds,
                    hierarchy: unsupported));

        Assert.Equal("hierarchy", exception.ParamName);
        Assert.Contains(
            "category Rows by Name",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HierarchyProjection_StreamsOwnerOrderAndCounts()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeOverviewDocument document =
            Available(
                Execute(
                    library,
                    rows: new(
                        maximumRows: 4096,
                        includeExactMemberCount: true)));
        var sink =
            new RecordingHierarchySink();

        TypeOverviewHierarchyProjection.Write(
            document,
            CompactHierarchy(),
            sink);

        TypeOverviewHierarchyNode.Category properties =
            Assert.IsType<TypeOverviewHierarchyNode.Category>(
                sink.Events[0].Node);
        Assert.Equal(MemberGroupCategory.Property, properties.Value);
        Assert.Equal(1, properties.LogicalCount);
        Assert.Equal(1, properties.ExactMemberCount);
        Assert.False(sink.Events[0].IsLastSibling);

        TypeOverviewHierarchyNode.Member property =
            Assert.IsType<TypeOverviewHierarchyNode.Member>(
                sink.Events[1].Node);
        Assert.Equal(
            "IsReflectionEnabledByDefault",
            property.Value.Binding.Name.ToString());
        Assert.True(sink.Events[1].IsLastSibling);

        TypeOverviewHierarchyNode.Category methods =
            Assert.IsType<TypeOverviewHierarchyNode.Category>(
                sink.Events[2].Node);
        Assert.Equal(MemberGroupCategory.Method, methods.Value);
        Assert.True(sink.Events[2].IsLastSibling);

        TypeOverviewHierarchyNode.Member[] methodMembers =
        [
            .. sink.Events
                .Skip(3)
                .Select(
                    recorded =>
                        Assert.IsType<
                            TypeOverviewHierarchyNode.Member>(
                            recorded.Node)),
        ];
        Assert.Equal(
            methods.LogicalCount,
            methodMembers.Length);
        Assert.Equal(
            methods.ExactMemberCount,
            methodMembers.Sum(
                static member =>
                    member.Value.ExactMemberCount!.Value));
        TypeMemberGroupRowsOutcome.Read sourceRows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                document.Members.Rows);
        string[] expectedMethodOrder =
        [
            .. sourceRows.Items
                .Where(
                    static member =>
                        member.Binding.Category
                            is MemberGroupCategory.Method)
                .Select(
                    static member =>
                        member.Binding.Name.ToString()),
        ];
        Assert.Equal(
            expectedMethodOrder,
            methodMembers.Select(
                static member =>
                    member.Value.Binding.Name.ToString()));

        TypeOverviewHierarchyNode.Member firstMethod =
            methodMembers[0];
        Assert.Equal(
            expectedMethodOrder[0],
            firstMethod.Value.Binding.Name.ToString());
        Assert.False(sink.Events[3].IsLastSibling);

        TypeOverviewHierarchyNode.Member lastMethod =
            methodMembers[^1];
        Assert.Equal(
            expectedMethodOrder[^1],
            lastMethod.Value.Binding.Name.ToString());
        Assert.True(sink.Events[^1].IsLastSibling);

        await library.RetireAsync();
    }

    [Fact]
    public async Task HierarchyProjection_RejectsPartialRows()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeOverviewDocument document =
            Available(
                Execute(
                    library,
                    rows: new(maximumRows: 1)));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => TypeOverviewHierarchyProjection.Write(
                    document,
                    CompactHierarchy(),
                    new RecordingHierarchySink()));

        Assert.Contains("complete member-group Rows", exception.Message);
        await library.RetireAsync();
    }

    [Fact]
    public async Task
        HierarchyProjection_RejectsFinalContinuedRowsPage()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeOverviewDocument document =
            Available(
                Execute(
                    library,
                    rows: new(maximumRows: 4)));
        TypeMemberGroupRowsOutcome.Read rows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                document.Members.Rows);
        TypeMemberGroupContinuation continuation =
            Assert.IsType<TypeMemberGroupContinuation>(
                rows.Continuation);

        Assert.Throws<ArgumentException>(
            () => new TypeOverviewDocumentInspectionPlan(
                document.Subject.Type,
                new(
                    maximumRows: 3,
                    continuation: continuation),
                s_bounds,
                hierarchy: CompactHierarchy()));

        do
        {
            document =
                Available(
                    Execute(
                        library,
                        rows: new(
                            maximumRows: 3,
                            continuation: continuation)));
            rows =
                Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    document.Members.Rows);
            continuation = rows.Continuation!;
        }
        while (rows.Continuation is not null);

        Assert.NotEmpty(rows.Items);
        Assert.All(
            rows.Items,
            static row => Assert.True(row.BaselineOrdinal > 1));
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => TypeOverviewHierarchyProjection.Write(
                    document,
                    CompactHierarchy(),
                    new RecordingHierarchySink()));

        Assert.Contains(
            "population origin",
            exception.Message,
            StringComparison.Ordinal);
        await library.RetireAsync();
    }

    private static InspectionHierarchyRequest<
        TypeOverviewHierarchyTopology> CompactHierarchy() =>
        new(
            TypeOverviewHierarchyTopology
                .TypeCategoriesAndMemberGroups,
            InspectionHierarchyNodeSpelling.FullSpelling,
            new InspectionHierarchyPopulationRequest.Rows(
                InspectionHierarchyNodeSpelling.Name,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Count())));

    private sealed class RecordingHierarchySink :
        IInspectionHierarchySink<TypeOverviewHierarchyNode>
    {
        internal List<RecordedHierarchyNode> Events { get; } = [];

        public void WriteNode(
            TypeOverviewHierarchyNode node,
            bool isLastSibling,
            Action<IInspectionHierarchySink<TypeOverviewHierarchyNode>>?
                writeChildren = null)
        {
            Events.Add(new(node, isLastSibling));
            writeChildren?.Invoke(this);
        }
    }

    private sealed record RecordedHierarchyNode(
        TypeOverviewHierarchyNode Node,
        bool IsLastSibling);
}
