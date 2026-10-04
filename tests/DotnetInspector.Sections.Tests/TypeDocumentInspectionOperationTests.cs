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
    public async Task
        RealJsonSerializer_ComposesExactSubjectAndPopulation()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<TypeDocumentInspectionOutcome> inspection =
            Execute(
                library,
                new TypeMemberGroupRowsRequest(
                    maximumRows: int.MaxValue),
                includeHidden: true);
        TypeDocument document =
            Assert.IsType<TypeDocumentInspectionOutcome.Available>(
                    inspection.Content)
                .Document;
        TypeMemberGroupPopulationBinding binding =
            document.Members.Binding;

        Assert.Equal(binding.Assembly, document.Subject.Assembly);
        Assert.Equal(
            binding.ModuleVersionId,
            document.Subject.ModuleVersionId);
        Assert.Equal(binding.Type, document.Subject.Type);
        Assert.Equal(
            binding.TypeDefinitionToken,
            document.Subject.TypeDefinitionToken);
        Assert.Equal(
            10,
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    document.Members.Rows)
                .Items.Length);
        Assert.Null(document.Members.Count);
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
        TypeDocument document =
            Assert.IsType<TypeDocumentInspectionOutcome.Available>(
                    Execute(
                        library,
                        rows: null,
                        count: new()).Content)
                .Document;

        var subject = new TypeSubject(
            document.Subject.Assembly,
            document.Subject.ModuleVersionId,
            document.Subject.Type,
            checked(document.Subject.TypeDefinitionToken + 1));
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDocument(subject, document.Members));
        Assert.Equal("members", exception.ParamName);

        await library.RetireAsync();
    }

    [Fact]
    public async Task DocumentPreservesTypedPopulationNonSuccess()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeDocumentInspectionOutcome.Rejected missing =
            Assert.IsType<TypeDocumentInspectionOutcome.Rejected>(
                Execute(
                    library,
                    rows: null,
                    count: new(),
                    type: Name(
                        "System.Text.Json",
                        "Missing")).Content);
        Assert.Equal(
            TypeMemberGroupPopulationInspectionRejection.TypeNotFound,
            missing.Reason);

        var bounded = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            maxMembers: 1,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            s_bounds.MaxRetainedTextCharacters);
        TypeDocumentInspectionOutcome.Incomplete incomplete =
            Assert.IsType<TypeDocumentInspectionOutcome.Incomplete>(
                Execute(
                    library,
                    rows: null,
                    count: new(),
                    bounds: bounded).Content);
        Assert.Equal(
            TypeMemberGroupPopulationBound.Members,
            incomplete.Bound);
        Assert.Equal(1, incomplete.Limit);
        Assert.Equal(2, incomplete.Measured);

        await library.RetireAsync();

        await using LibraryInspectionTestLibrary malformed =
            await LibraryInspectionTestLibrary.CreateAsync(
                [1, 2, 3],
                LibraryInspectionTestLibrary.ProbeIdentity());
        TypeDocumentInspectionOutcome.Failed failed =
            Assert.IsType<TypeDocumentInspectionOutcome.Failed>(
                Execute(
                    malformed,
                    rows: null,
                    count: new(),
                    type: Name("Probe", "Type")).Content);
        Assert.Equal(
            TypeDocumentInspectionFailure.MalformedMetadata,
            failed.Reason);

        await malformed.RetireAsync();
    }

    private static InspectionEnvelope<TypeDocumentInspectionOutcome> Execute(
        LibraryInspectionTestLibrary library,
        TypeMemberGroupRowsRequest? rows,
        TypeMemberGroupCountRequest? count = null,
        MetadataTypeDefinitionName? type = null,
        bool includeHidden = false,
        ApiSurfaceExtractionBounds? bounds = null) =>
        TypeDocumentInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    type
                        ?? Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                    new(
                        count,
                        rows,
                        includeHidden: includeHidden),
                    bounds ?? s_bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}
