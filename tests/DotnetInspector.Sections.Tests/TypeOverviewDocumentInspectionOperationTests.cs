using System.Reflection;
using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeOverviewDocumentInspectionOperationTests
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

        TypeOverviewDocument document =
            Available(Execute(library, bounds: zeroPopulation));

        Assert.IsType<TypeOverviewDocumentDeclarations.NotRequested>(
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
            JsonSerializer.Serialize<TypeOverviewDocumentInspectionOutcome>(
                new TypeOverviewDocumentInspectionOutcome.Available(document));
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

        TypeOverviewDocument document =
            Available(
                Execute(
                    library,
                    type: Name(
                        "System.Text.Json.Serialization",
                        "JsonConverter`1"),
                    bounds: zeroPopulation));

        Assert.IsType<TypeOverviewDocumentDeclarations.NotRequested>(
            document.Declarations);
        TypeOverviewDocumentGenericParameter parameter =
            Assert.Single(
                document.Subject.Signature.GenericParameters);
        Assert.Equal(0, parameter.DefinitionSegmentIndex);
        Assert.Equal(0, parameter.MetadataIndex);
        Assert.Equal("T", parameter.Name.ToString());
        Assert.Equal(
            GenericParameterAttributes.None,
            parameter.Attributes);

        string json =
            JsonSerializer.Serialize<TypeOverviewDocumentInspectionOutcome>(
                new TypeOverviewDocumentInspectionOutcome.Available(document));
        Assert.Contains("\"Name\":\"T\"", json);
        Assert.Contains("\"DefinitionSegmentIndex\":0", json);
        Assert.Contains("\"MetadataIndex\":0", json);

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

        TypeOverviewDocument firstDocument =
            Available(Execute(first));
        TypeOverviewDocument secondDocument =
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

        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> inspection =
            Execute(library, count: new());
        TypeOverviewDocument document = Available(inspection);
        TypeMemberGroupPopulationResult population =
            Assert.IsType<TypeOverviewDocumentDeclarations.Available>(
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
        TypeOverviewDocument document =
            Available(Execute(library, count: new()));
        TypeSubject source = document.Subject;
        var mismatched = new TypeSubject(
            source.LibraryCorrespondence,
            source.Assembly,
            source.ModuleVersionId,
            source.Type,
            checked(source.TypeDefinitionToken + 1),
            source.Signature,
            source.Category,
            source.Attributes,
            source.IsByRefLike,
            source.DefinesCoreLibraryRoot,
            source.DeclaringTypeDefinitionToken);

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeOverviewDocument(
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

        TypeOverviewDocument document =
            Available(
                Execute(
                    library,
                    rows: new(maximumRows: 2)));
        TypeMemberGroupPopulationResult population =
            Assert.IsType<TypeOverviewDocumentDeclarations.Available>(
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
    public async Task Rows_ProjectTruthfulSharedGenericParameterSpelling()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeMemberGroupRowsOutcome.Read rows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                Assert.IsType<TypeOverviewDocumentDeclarations.Available>(
                        Available(
                            Execute(
                                library,
                                rows: new(maximumRows: int.MaxValue)))
                        .Declarations)
                    .Population
                    .Rows);

        Assert.Null(
            Assert.Single(
                rows.Items,
                row => row.Binding.Name.ToString() == "Deserialize")
                .SharedGenericParameters);
        Assert.Equal(
            ["TValue"],
            Assert.Single(
                rows.Items,
                row =>
                    row.Binding.Name.ToString()
                        == "DeserializeAsyncEnumerable")
                .SharedGenericParameters?
                .Select(parameter => parameter.ToString()));

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

        TypeOverviewDocument document =
            Available(
                Execute(
                    library,
                    count: new(),
                    bounds: memberBound));
        TypeOverviewDocumentDeclarations.Incomplete declarations =
            Assert.IsType<TypeOverviewDocumentDeclarations.Incomplete>(
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
                    Assert.IsType<TypeOverviewDocumentDeclarations.Available>(
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

        TypeOverviewDocument document =
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
            Assert.IsType<TypeOverviewDocumentDeclarations.Rejected>(
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
                    Assert.IsType<TypeOverviewDocumentDeclarations.Available>(
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

        TypeOverviewDocument document =
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
            Assert.IsType<TypeOverviewDocumentDeclarations.Rejected>(
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

        TypeOverviewDocumentInspectionOutcome.Rejected rejected =
            Assert.IsType<TypeOverviewDocumentInspectionOutcome.Rejected>(
                Execute(
                    library,
                    type:
                        Name(
                            "System.Text.Json",
                            "Missing")).Content);

        Assert.Equal(
            TypeOverviewDocumentInspectionRejection.TypeNotFound,
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

        TypeOverviewDocumentInspectionOutcome.Rejected rejected =
            Assert.IsType<TypeOverviewDocumentInspectionOutcome.Rejected>(
                Execute(
                    library,
                    type: Name("N", "C")).Content);

        Assert.Equal(
            TypeOverviewDocumentInspectionRejection.TypeAmbiguous,
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

        TypeOverviewDocumentInspectionOutcome.Incomplete incomplete =
            Assert.IsType<TypeOverviewDocumentInspectionOutcome.Incomplete>(
                Execute(
                    library,
                    bounds: metadataBound).Content);

        Assert.Equal(
            TypeOverviewDocumentInspectionBound.MetadataRows,
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

        TypeOverviewDocumentInspectionOutcome.Failed failed =
            Assert.IsType<TypeOverviewDocumentInspectionOutcome.Failed>(
                Execute(
                    library,
                    type: Name("Probe", "Type")).Content);

        Assert.Equal(
            TypeOverviewDocumentInspectionFailure.MalformedMetadata,
            failed.Reason);
        await library.RetireAsync();
    }

    [Fact]
    public void DeclaredMemberNavigationRequest_IsCanonicalAcrossHosts()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");

        TypeOverviewDocumentInspectionPlan plan =
            TypeOverviewDocumentInspectionPlans.DeclaredMemberRows(
                    type,
                    s_bounds,
                    TypeMemberGroupSpelling.CSharp,
                    TypeMemberGroupAccessibilityFilter.Public,
                    includeHidden: false,
                    s_bounds.MaxMembers);

        Assert.Same(type, plan.Type);
        Assert.Same(s_bounds, plan.Bounds);
        TypeMemberGroupPopulationRequest declarations =
            Assert.IsType<TypeMemberGroupPopulationRequest>(
                plan.Declarations);
        Assert.Null(declarations.Count);
        Assert.Equal(
            s_bounds.MaxMembers,
            declarations.Rows!.MaximumRows);
        Assert.True(
            declarations.Rows.IncludeExactMemberCount);
        Assert.NotNull(declarations.Composition);
        Assert.NotNull(declarations.SelectorCounts);
        Assert.Equal(
            TypeMemberGroupSpelling.CSharp,
            declarations.Spelling);
        Assert.Equal(
            TypeMemberGroupAccessibilityFilter.Public,
            declarations.Accessibility);
        Assert.Equal(
            TypeMemberGroupReceiverFilter.All,
            declarations.Receiver);
        Assert.False(declarations.IncludeHidden);
    }

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
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
        return TypeOverviewDocumentInspectionOperation.Execute(
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

    private static TypeOverviewDocument Available(
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> envelope) =>
        Assert.IsType<TypeOverviewDocumentInspectionOutcome.Available>(
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
}
