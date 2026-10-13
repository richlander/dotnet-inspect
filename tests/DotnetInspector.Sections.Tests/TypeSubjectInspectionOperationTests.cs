using System.Reflection;
using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeSubjectInspectionOperationTests
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

        TypeSubjectInspectionContent document =
            Available(Execute(library, bounds: zeroPopulation));

        Assert.IsType<TypeSubjectMemberGroups.NotRequested>(
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
        Assert.Equal(
            MetadataTypeDeclarationBaseKind.Object,
            document.BaseKind);
        Assert.Equal(0, document.InterfaceCount);
        Assert.True(
            document.Subject.Attributes.HasFlag(
                TypeAttributes.Public));
        Assert.Null(document.Subject.DeclaringTypeDefinitionToken);

        string json =
            JsonSerializer.Serialize<TypeSubjectInspectionOutcome>(
                new TypeSubjectInspectionOutcome.Available(document));
        Assert.Contains("\"outcome\":\"available\"", json);
        Assert.Contains("\"kind\":\"not-requested\"", json);
        Assert.Contains("\"namespace\":\"System.Text.Json\"", json);

        await library.RetireAsync();
    }

    [Fact]
    public void DirectAssembly_SubjectOnlyReturnsTheSharedTypeDocument()
    {
        string path =
            typeof(System.Text.Json.JsonSerializer).Assembly.Location;
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "Type document direct-assembly test"));

        InspectionEnvelope<TypeSubjectInspectionOutcome> envelope =
            TypeSubjectInspectionOperation.Execute(
                assembly,
                new(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    s_bounds),
                TestContext.Current.CancellationToken);

        TypeSubjectInspectionContent document = Available(envelope);
        Assert.Null(document.Subject.LibraryCorrespondence);
        Assert.IsType<TypeSubjectMemberGroups.NotRequested>(
            document.Declarations);
        Assert.Equal(
            Name("System.Text.Json", "JsonSerializer"),
            document.Subject.Type);
    }

    [Fact]
    public void DirectAssembly_NestedSubjectRetainsByteNativeSegmentBoundaries()
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(TypeSubjectInspectionOperationTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "nested byte-native Type identity test"));
        MetadataTypeDefinitionName expected =
            Name(
                "DotnetInspector.Sections.Tests",
                nameof(TypeSubjectInspectionOperationTests),
                nameof(ByteNativeNestedFixture));

        InspectionEnvelope<TypeSubjectInspectionOutcome> envelope =
            TypeSubjectInspectionOperation.Execute(
                assembly,
                new(expected, s_bounds),
                TestContext.Current.CancellationToken);

        TypeSubjectInspectionContent document = Available(envelope);
        Assert.Equal(expected, document.Subject.Type);
        Assert.Equal(
            [
                nameof(TypeSubjectInspectionOperationTests),
                nameof(ByteNativeNestedFixture),
            ],
            document.Subject.Type.Segments);
    }

    [Theory]
    [InlineData(
        "System.Collections.Generic",
        "List`1",
        false,
        true)]
    [InlineData(
        "System.Text",
        "StringBuilder",
        false,
        false)]
    [InlineData(
        "System",
        "Delegate",
        false,
        false)]
    [InlineData(
        "System",
        "Delegate",
        true,
        true)]
    public void DirectAssembly_ExtensionPresenceUsesContextualRelations(
        string @namespace,
        string type,
        bool includeNonPublic,
        bool expectedExtensions)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(System.Text.StringBuilder).Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "Type extension Count test"));

        TypeSubjectExtensionPresenceInspectionResult result =
            TypeSubjectInspectionOperation.ExecuteWithExtensionPresence(
                assembly,
                new(
                    Name(@namespace, type),
                    s_bounds),
                includeNonPublic,
                TestContext.Current.CancellationToken);

        _ = Available(result.Document);
        var available =
            Assert.IsType<
                TypeExtensionMethodPresenceInspectionOutcome.Available>(
                    result.ExtensionPresence);
        Assert.Equal(expectedExtensions, available.Exists);
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

        TypeSubjectInspectionContent document =
            Available(
                Execute(
                    library,
                    type: Name(
                        "System.Text.Json.Serialization",
                        "JsonConverter`1"),
                    bounds: zeroPopulation));

        Assert.IsType<TypeSubjectMemberGroups.NotRequested>(
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
            JsonSerializer.Serialize<TypeSubjectInspectionOutcome>(
                new TypeSubjectInspectionOutcome.Available(document));
        Assert.Contains("\"Name\":\"T\"", json);
        Assert.Contains("\"DefinitionSegmentIndex\":0", json);
        Assert.Contains("\"MetadataIndex\":0", json);

        await library.RetireAsync();
    }

    private sealed class ByteNativeNestedFixture;

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

        TypeSubjectInspectionContent document =
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
            JsonSerializer.Serialize<TypeSubjectInspectionOutcome>(
                new TypeSubjectInspectionOutcome.Available(document));
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

        TypeSubjectInspectionContent firstDocument =
            Available(Execute(first));
        TypeSubjectInspectionContent secondDocument =
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

        InspectionEnvelope<TypeSubjectInspectionOutcome> inspection =
            Execute(library, count: new());
        TypeSubjectInspectionContent document = Available(inspection);
        TypeMemberGroupPopulationResult population =
            Assert.IsType<TypeSubjectMemberGroups.Available>(
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
        TypeSubjectInspectionContent document =
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
            source.IsHidden,
            source.IsCompilerGenerated,
            source.IsByRefLike,
            source.IsReadOnly,
            source.DefinesCoreLibraryRoot,
            source.DeclaringTypeDefinitionToken);

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeSubjectInspectionContent(
                    mismatched,
                    document.Declarations,
                    document.AssemblyBytes,
                    document.BaseKind,
                    document.InterfaceCount));
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

        TypeSubjectInspectionContent document =
            Available(
                Execute(
                    library,
                    rows: new(maximumRows: 2)));
        TypeMemberGroupPopulationResult population =
            Assert.IsType<TypeSubjectMemberGroups.Available>(
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

        TypeSubjectInspectionContent document =
            Available(
                Execute(
                    library,
                    count: new(),
                    bounds: memberBound));
        TypeSubjectMemberGroups.Incomplete declarations =
            Assert.IsType<TypeSubjectMemberGroups.Incomplete>(
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
                    Assert.IsType<TypeSubjectMemberGroups.Available>(
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

        TypeSubjectInspectionContent document =
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
            Assert.IsType<TypeSubjectMemberGroups.Rejected>(
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
                    Assert.IsType<TypeSubjectMemberGroups.Available>(
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

        TypeSubjectInspectionContent document =
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
            Assert.IsType<TypeSubjectMemberGroups.Rejected>(
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

        TypeSubjectInspectionOutcome.Rejected rejected =
            Assert.IsType<TypeSubjectInspectionOutcome.Rejected>(
                Execute(
                    library,
                    type:
                        Name(
                            "System.Text.Json",
                            "Missing")).Content);

        Assert.Equal(
            TypeSubjectInspectionRejection.TypeNotFound,
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

        TypeSubjectInspectionOutcome.Rejected rejected =
            Assert.IsType<TypeSubjectInspectionOutcome.Rejected>(
                Execute(
                    library,
                    type: Name("N", "C")).Content);

        Assert.Equal(
            TypeSubjectInspectionRejection.TypeAmbiguous,
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

        TypeSubjectInspectionOutcome.Incomplete incomplete =
            Assert.IsType<TypeSubjectInspectionOutcome.Incomplete>(
                Execute(
                    library,
                    bounds: metadataBound).Content);

        Assert.Equal(
            TypeSubjectInspectionBound.MetadataRows,
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

        TypeSubjectInspectionOutcome.Failed failed =
            Assert.IsType<TypeSubjectInspectionOutcome.Failed>(
                Execute(
                    library,
                    type: Name("Probe", "Type")).Content);

        Assert.Equal(
            TypeSubjectInspectionFailure.MalformedMetadata,
            failed.Reason);
        await library.RetireAsync();
    }

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
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
        return TypeSubjectInspectionOperation.Execute(
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

    private static TypeSubjectInspectionContent Available(
        InspectionEnvelope<TypeSubjectInspectionOutcome> envelope) =>
        Assert.IsType<TypeSubjectInspectionOutcome.Available>(
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
