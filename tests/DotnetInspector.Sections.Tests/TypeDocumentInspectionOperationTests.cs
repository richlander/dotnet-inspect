using System.Reflection;
using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeDocumentInspectionOperationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public async Task SubjectOnly_AvoidsPopulationAndRichSurfaceWork()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var zeroPopulation = new ApiSurfaceExtractionBounds(
            maxTypes: 0,
            maxMembers: 0,
            maxInspectionFailures: 0,
            maxTypeForwarders: 0,
            s_bounds.MaxMetadataRows,
            maxRetainedTextCharacters: 0);

        TypeDocumentInspectionContent document =
            Available(Execute(library, bounds: zeroPopulation));

        Assert.IsType<TypeDocumentDeclarations.NotRequested>(
            document.Declarations);
        Assert.Equal(
            "System.Text.Json",
            document.Subject.Assembly.Name.ToString());
        Assert.Equal(
            Name("System.Text.Json", "JsonSerializer"),
            document.Subject.Type);
        Assert.True(document.Subject.TypeDefinitionToken > 0);
        Assert.NotEqual(Guid.Empty, document.Subject.ModuleVersionId);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Class,
            document.Subject.Category);
        Assert.True(
            document.Subject.Attributes.HasFlag(
                TypeAttributes.Public));
        Assert.Null(document.Subject.DeclaringTypeDefinitionToken);

        string json =
            JsonSerializer.Serialize<TypeDocumentInspectionOutcome>(
                new TypeDocumentInspectionOutcome.Available(document));
        Assert.Contains("\"outcome\":\"available\"", json);
        Assert.Contains("\"kind\":\"not-requested\"", json);
        Assert.Contains("\"namespace\":\"System.Text.Json\"", json);

        await library.RetireAsync();
    }

    [Fact]
    public async Task GenericSubjectOnly_RetainsDetachedDeclarationSignature()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var zeroPopulation = new ApiSurfaceExtractionBounds(
            maxTypes: 0,
            maxMembers: 0,
            maxInspectionFailures: 0,
            maxTypeForwarders: 0,
            s_bounds.MaxMetadataRows,
            maxRetainedTextCharacters: 0);

        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    type: Name(
                        "System.Text.Json.Serialization",
                        "JsonConverter`1"),
                    bounds: zeroPopulation));

        Assert.IsType<TypeDocumentDeclarations.NotRequested>(
            document.Declarations);
        TypeDocumentGenericParameter parameter =
            Assert.Single(
                document.Subject.Signature.GenericParameters);
        Assert.Equal(0, parameter.DefinitionSegmentIndex);
        Assert.Equal(0, parameter.MetadataIndex);
        Assert.Equal("T", parameter.Name.ToString());
        Assert.Equal(
            GenericParameterAttributes.None,
            parameter.Attributes);

        string json =
            JsonSerializer.Serialize<TypeDocumentInspectionOutcome>(
                new TypeDocumentInspectionOutcome.Available(document));
        Assert.Contains("\"Name\":\"T\"", json);
        Assert.Contains("\"DefinitionSegmentIndex\":0", json);
        Assert.Contains("\"MetadataIndex\":0", json);

        await library.RetireAsync();
    }

    [Fact]
    public async Task ReadonlyStructSubject_RetainsDeclarationModifier()
    {
        byte[] content =
            await File.ReadAllBytesAsync(
                typeof(DateTime).Assembly.Location,
                TestContext.Current.CancellationToken);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    type: Name("System", "DateTime")));

        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            document.Subject.Category);
        Assert.True(document.Subject.IsReadOnly);
        Assert.False(document.Subject.IsByRefLike);

        string json =
            JsonSerializer.Serialize<TypeDocumentInspectionOutcome>(
                new TypeDocumentInspectionOutcome.Available(document));
        Assert.Contains("\"IsReadOnly\":true", json);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        SubjectOnly_RetainsDistinctLibraryAuthorityForIdenticalBytes()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        ManagedMetadataIdentity.Assembly identity =
            LibraryInspectionTestLibrary.Identity(content);
        await using LibraryInspectionTestLibrary first =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                identity);
        await using LibraryInspectionTestLibrary second =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                identity);

        TypeDocumentInspectionContent firstDocument =
            Available(Execute(first));
        TypeDocumentInspectionContent secondDocument =
            Available(Execute(second));

        await first.RetireAsync();
        await second.RetireAsync();

        Assert.Same(
            first.Reference,
            firstDocument.Subject.RequestedLibrary);
        Assert.Same(
            first.Reference,
            firstDocument.Subject.DefiningLibrary);
        Assert.Same(
            first.Reference.ApiAssembly,
            firstDocument.Subject.DefiningApiContent);
        Assert.Same(
            second.Reference,
            secondDocument.Subject.RequestedLibrary);
        Assert.Same(
            second.Reference.ApiAssembly,
            secondDocument.Subject.DefiningApiContent);
        Assert.NotSame(
            firstDocument.Subject.RequestedLibrary,
            secondDocument.Subject.RequestedLibrary);
        Assert.NotSame(
            firstDocument.Subject.DefiningApiContent,
            secondDocument.Subject.DefiningApiContent);
        Assert.Equal(
            firstDocument.Subject.Assembly,
            secondDocument.Subject.Assembly);
        Assert.Equal(
            firstDocument.Subject.ModuleVersionId,
            secondDocument.Subject.ModuleVersionId);
        Assert.Equal(
            firstDocument.Subject.TypeDefinitionToken,
            secondDocument.Subject.TypeDefinitionToken);
    }

    [Fact]
    public async Task CountOnly_BindsSubjectAndPopulation()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<TypeDocumentInspectionOutcome> inspection =
            Execute(library, count: new());
        TypeDocumentInspectionContent document = Available(inspection);
        TypeMemberGroupPopulationResult population =
            Assert.IsType<TypeDocumentDeclarations.Available>(
                    document.Declarations)
                .Population;

        Assert.Equal(
            10,
            Assert.IsType<TypeMemberGroupCountOutcome.Counted>(
                    population.Count)
                .Value);
        Assert.Null(population.Rows);
        Assert.Equal(
            document.Subject.Assembly,
            population.Binding.Assembly);
        Assert.Equal(
            document.Subject.ModuleVersionId,
            population.Binding.ModuleVersionId);
        Assert.Equal(
            document.Subject.TypeDefinitionToken,
            population.Binding.TypeDefinitionToken);
        Assert.Equal(
            document.Subject.Type,
            population.Binding.Type);
        var share =
            Assert.IsType<InspectionShare.NonProjectable>(
                inspection.Share);
        Assert.Equal(
            "type-member-group-population-inspection/share",
            share.Path);
        Assert.Empty(inspection.Diagnostics);

        await library.RetireAsync();
    }

    [Fact]
    public async Task DocumentRejectsMismatchedExactTypeBinding()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeDocumentInspectionContent document =
            Available(Execute(library, count: new()));
        TypeSubject source = document.Subject;
        var mismatched = new TypeSubject(
            source.LibraryCorrespondence!,
            source.Assembly,
            source.ModuleVersionId,
            source.Type,
            checked(source.TypeDefinitionToken + 1),
            source.Signature,
            source.Category,
            source.Attributes,
            source.IsByRefLike,
            source.IsReadOnly,
            source.DefinesCoreLibraryRoot,
            source.DeclaringTypeDefinitionToken);

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDocumentInspectionContent(
                    mismatched,
                    document.Declarations,
                    document.AssemblyBytes));
        Assert.Equal("declarations", exception.ParamName);

        await library.RetireAsync();
    }

    [Fact]
    public async Task BoundedRows_PreserveOneExactTypeBinding()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    rows: new(maximumRows: 2)));
        TypeMemberGroupPopulationResult population =
            Assert.IsType<TypeDocumentDeclarations.Available>(
                    document.Declarations)
                .Population;
        TypeMemberGroupRowsOutcome.Read rows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                population.Rows);

        Assert.Equal(2, rows.Items.Length);
        Assert.NotNull(rows.Continuation);
        Assert.All(
            rows.Items,
            row =>
            {
                Assert.Equal(
                    population.Binding,
                    row.Binding.Population);
                Assert.Equal(
                    document.Subject.TypeDefinitionToken,
                    row.Binding.Population.TypeDefinitionToken);
            });

        await library.RetireAsync();
    }

    [Fact]
    public void HierarchyRequest_RequiresRows()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDocumentInspectionPlan(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    s_bounds,
                    hierarchy:
                       CompactHierarchy()));

        Assert.Equal("hierarchy", exception.ParamName);

        var request =
            CompactHierarchy();
        var declarations =
            new TypeMemberGroupPopulationRequest(
                count: null,
                rows: new(maximumRows: 1));
        var plan =
            new TypeDocumentInspectionPlan(
                Name(
                    "System.Text.Json",
                    "JsonSerializer"),
                s_bounds,
                declarations,
                request);

        Assert.Same(request, plan.Hierarchy);
    }

    [Fact]
    public void HierarchyRequest_RejectsUnsupportedTerminalAndSpellingPlan()
    {
        var unsupported =
            new InspectionHierarchyRequest<
                TypeDocumentHierarchyTopology>(
                TypeDocumentHierarchyTopology
                    .TypeCategoriesAndMemberGroups,
                InspectionHierarchyNodeSpelling.FullSpelling,
                new InspectionHierarchyPopulationRequest.Count());
        var declarations =
            new TypeMemberGroupPopulationRequest(
                count: null,
                rows: new(maximumRows: 1));

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDocumentInspectionPlan(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    s_bounds,
                    declarations,
                    unsupported));

        Assert.Equal("hierarchy", exception.ParamName);
        Assert.Contains(
            "category Rows by Name",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HierarchyRequest_RequiresNestedExactMemberCounts()
    {
        var declarations =
            new TypeMemberGroupPopulationRequest(
                count: null,
                rows: new(
                    maximumRows: 1,
                    includeExactMemberCount: false));

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDocumentInspectionPlan(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    s_bounds,
                    declarations,
                    CompactHierarchy()));

        Assert.Equal("hierarchy", exception.ParamName);
        Assert.Contains(
            "exact-Member Counts",
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
        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    rows: new(
                        maximumRows: 4096,
                        includeExactMemberCount: true)));
        var sink =
            new RecordingHierarchySink();

        TypeDocumentHierarchyProjection.Write(
            document,
            CompactHierarchy(),
            sink);

        TypeDocumentHierarchyNode.Category properties =
            Assert.IsType<TypeDocumentHierarchyNode.Category>(
                sink.Events[0].Node);
        Assert.Equal(MemberGroupCategory.Property, properties.Value);
        Assert.Equal(1, properties.LogicalCount);
        Assert.Equal(1, properties.ExactMemberCount);
        Assert.False(sink.Events[0].IsLastSibling);

        TypeDocumentHierarchyNode.Member property =
            Assert.IsType<TypeDocumentHierarchyNode.Member>(
                sink.Events[1].Node);
        Assert.Equal(
            "IsReflectionEnabledByDefault",
            property.Value.Binding.Name.ToString());
        Assert.True(sink.Events[1].IsLastSibling);

        TypeDocumentHierarchyNode.Category methods =
            Assert.IsType<TypeDocumentHierarchyNode.Category>(
                sink.Events[2].Node);
        Assert.Equal(MemberGroupCategory.Method, methods.Value);
        Assert.True(sink.Events[2].IsLastSibling);

        TypeDocumentHierarchyNode.Member[] methodMembers =
            [.. sink.Events
                .Skip(3)
                .Select(
                    recorded =>
                        Assert.IsType<
                            TypeDocumentHierarchyNode.Member>(
                            recorded.Node))];
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
                Assert.IsType<TypeDocumentDeclarations.Available>(
                        document.Declarations)
                    .Population.Rows);
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

        TypeDocumentHierarchyNode.Member firstMethod =
            methodMembers[0];
        Assert.Equal(
            expectedMethodOrder[0],
            firstMethod.Value.Binding.Name.ToString());
        Assert.False(sink.Events[3].IsLastSibling);

        TypeDocumentHierarchyNode.Member lastMethod =
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
        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    rows: new(maximumRows: 1)));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => TypeDocumentHierarchyProjection.Write(
                    document,
                    CompactHierarchy(),
                    new RecordingHierarchySink()));

        Assert.Contains("complete member-group Rows", exception.Message);
        await library.RetireAsync();
    }

    [Fact]
    public async Task MemberBound_PreservesAvailableSubject()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var memberBound = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            maxMembers: 1,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            s_bounds.MaxRetainedTextCharacters);

        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    count: new(),
                    bounds: memberBound));
        TypeDocumentDeclarations.Incomplete declarations =
            Assert.IsType<TypeDocumentDeclarations.Incomplete>(
                document.Declarations);

        Assert.Equal(
            Name("System.Text.Json", "JsonSerializer"),
            document.Subject.Type);
        Assert.Equal(
            TypeMemberGroupPopulationBound.Members,
            declarations.Bound);
        Assert.Equal(1, declarations.Limit);
        Assert.Equal(2, declarations.Measured);

        await library.RetireAsync();
    }

    [Fact]
    public async Task StaleContinuation_PreservesSubjectAndSkipsPopulation()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeMemberGroupContinuation continuation =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    Assert.IsType<TypeDocumentDeclarations.Available>(
                            Available(
                                    Execute(
                                        library,
                                        rows: new(maximumRows: 1)))
                                .Declarations)
                        .Population.Rows)
                .Continuation!;
        var stale = new TypeMemberGroupContinuation(
            new(
                continuation.Binding.Assembly,
                continuation.Binding.ModuleVersionId,
                continuation.Binding.Type,
                checked(
                    continuation.Binding.TypeDefinitionToken
                    + 1),
                continuation.Binding.Spelling,
                continuation.Binding.IncludeHidden,
                continuation.Binding.Accessibility,
                continuation.Binding.Receiver,
                continuation.Binding.Ordering),
            continuation.NextOrdinal,
            continuation.IncludeExactMemberCount);
        var zeroMembers = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            maxMembers: 0,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            s_bounds.MaxRetainedTextCharacters);

        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    rows: new(
                        maximumRows: 1,
                        continuation: stale),
                    bounds: zeroMembers));
        Assert.Equal(
            TypeMemberGroupPopulationInspectionRejection
                .StaleContinuation,
            Assert.IsType<TypeDocumentDeclarations.Rejected>(
                    document.Declarations)
                .Reason);
        Assert.Equal(
            continuation.Binding.TypeDefinitionToken,
            document.Subject.TypeDefinitionToken);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        IncompatibleContinuation_PreservesSubjectAndSkipsPopulation()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeMemberGroupContinuation continuation =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    Assert.IsType<TypeDocumentDeclarations.Available>(
                            Available(
                                    Execute(
                                        library,
                                        rows: new(maximumRows: 1)))
                                .Declarations)
                        .Population.Rows)
                .Continuation!;
        var zeroMembers = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            maxMembers: 0,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            s_bounds.MaxRetainedTextCharacters);

        TypeDocumentInspectionContent document =
            Available(
                Execute(
                    library,
                    rows: new(
                        maximumRows: 1,
                        continuation: continuation),
                    spelling: TypeMemberGroupSpelling.Metadata,
                    bounds: zeroMembers));

        Assert.Equal(
            TypeMemberGroupPopulationInspectionRejection
                .IncompatibleContinuation,
            Assert.IsType<TypeDocumentDeclarations.Rejected>(
                    document.Declarations)
                .Reason);
        Assert.Equal(
            continuation.Binding.TypeDefinitionToken,
            document.Subject.TypeDefinitionToken);

        await library.RetireAsync();
    }

    [Fact]
    public async Task MissingType_IsTypedNonSuccess()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeDocumentInspectionOutcome.Rejected rejected =
            Assert.IsType<TypeDocumentInspectionOutcome.Rejected>(
                Execute(
                    library,
                    type:
                        Name(
                            "System.Text.Json",
                            "Missing")).Content);

        Assert.Equal(
            TypeDocumentInspectionRejection.TypeNotFound,
            rejected.Reason);
        await library.RetireAsync();
    }

    [Fact]
    public async Task AmbiguousType_IsTypedNonSuccess()
    {
        byte[] content =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                duplicatePublicType: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeDocumentInspectionOutcome.Rejected rejected =
            Assert.IsType<TypeDocumentInspectionOutcome.Rejected>(
                Execute(
                    library,
                    type: Name("N", "C")).Content);

        Assert.Equal(
            TypeDocumentInspectionRejection.TypeAmbiguous,
            rejected.Reason);
        await library.RetireAsync();
    }

    [Fact]
    public async Task MetadataBound_IsTypedNonSuccess()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var metadataBound = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            s_bounds.MaxMembers,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            maxMetadataRows: 0,
            s_bounds.MaxRetainedTextCharacters);

        TypeDocumentInspectionOutcome.Incomplete incomplete =
            Assert.IsType<TypeDocumentInspectionOutcome.Incomplete>(
                Execute(
                    library,
                    bounds: metadataBound).Content);

        Assert.Equal(
            TypeDocumentInspectionBound.MetadataRows,
            incomplete.Bound);
        Assert.Equal(0, incomplete.Limit);
        Assert.True(incomplete.Measured > 0);
        await library.RetireAsync();
    }

    [Fact]
    public async Task MalformedContent_IsTypedNonSuccess()
    {
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                [1, 2, 3],
                LibraryInspectionTestLibrary.ProbeIdentity());

        TypeDocumentInspectionOutcome.Failed failed =
            Assert.IsType<TypeDocumentInspectionOutcome.Failed>(
                Execute(
                    library,
                    type: Name("Probe", "Type")).Content);

        Assert.Equal(
            TypeDocumentInspectionFailure.MalformedMetadata,
            failed.Reason);
        await library.RetireAsync();
    }

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Execute(
            LibraryInspectionTestLibrary library,
            MetadataTypeDefinitionName? type = null,
            TypeMemberGroupCountRequest? count = null,
            TypeMemberGroupRowsRequest? rows = null,
            TypeMemberGroupSpelling spelling =
                TypeMemberGroupSpelling.CSharp,
            ApiSurfaceExtractionBounds? bounds = null)
    {
        TypeMemberGroupPopulationRequest? declarations =
            count is null && rows is null
                ? null
                : new(
                    count,
                    rows,
                    spelling: spelling);
        return TypeDocumentInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    type
                        ?? Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                    bounds ?? s_bounds,
                    declarations)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);
    }

    private static TypeDocumentInspectionContent Available(
        InspectionEnvelope<TypeDocumentInspectionOutcome> envelope) =>
        Assert.IsType<TypeDocumentInspectionOutcome.Available>(
                envelope.Content)
            .Document;

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private static InspectionHierarchyRequest<
        TypeDocumentHierarchyTopology> CompactHierarchy() =>
        new(
            TypeDocumentHierarchyTopology
                .TypeCategoriesAndMemberGroups,
            InspectionHierarchyNodeSpelling.FullSpelling,
            new InspectionHierarchyPopulationRequest.Rows(
                InspectionHierarchyNodeSpelling.Name,
                new InspectionHierarchyPopulationRequest.Rows(
                    InspectionHierarchyNodeSpelling.Name,
                    new InspectionHierarchyPopulationRequest.Count())));

    private sealed class RecordingHierarchySink :
        IInspectionHierarchySink<TypeDocumentHierarchyNode>
    {
        internal List<RecordedHierarchyNode> Events { get; } = [];

        public void WriteNode(
            TypeDocumentHierarchyNode node,
            bool isLastSibling,
            Action<IInspectionHierarchySink<TypeDocumentHierarchyNode>>?
                writeChildren = null)
        {
            Events.Add(new(node, isLastSibling));
            writeChildren?.Invoke(this);
        }
    }

    private sealed record RecordedHierarchyNode(
        TypeDocumentHierarchyNode Node,
        bool IsLastSibling);
}
