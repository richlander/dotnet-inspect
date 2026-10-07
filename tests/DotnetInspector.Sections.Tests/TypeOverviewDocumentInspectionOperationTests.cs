using System.Text.Json;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed partial class TypeOverviewDocumentInspectionOperationTests
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
    public void PlanRequiresNestedExactMemberCounts()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeOverviewDocumentInspectionPlan(
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    new(
                        maximumRows: 10,
                        includeExactMemberCount: false),
                    s_bounds));

        Assert.Equal("rows", exception.ParamName);
    }

    [Fact]
    public async Task CompleteOverviewMapsCompactRowsAndCounts()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
            inspection = Execute(
                library,
                rows: new(maximumRows: int.MaxValue),
                includeHidden: true);
        TypeOverviewDocument document = Available(inspection);
        TypeMemberGroupRowsOutcome.Read rows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                document.Members.Rows);

        Assert.Equal(
            Name("System.Text.Json", "JsonSerializer"),
            document.Subject.Type);
        Assert.Equal(
            document.Subject.Assembly,
            document.Members.Binding.Assembly);
        Assert.Equal(
            document.Subject.ModuleVersionId,
            document.Members.Binding.ModuleVersionId);
        Assert.Equal(
            document.Subject.TypeDefinitionToken,
            document.Members.Binding.TypeDefinitionToken);
        Assert.Equal(10, rows.Items.Length);
        Assert.Null(rows.Continuation);
        Assert.Null(document.Members.Count);
        Assert.All(
            rows.Items,
            row =>
            {
                Assert.Equal(
                    document.Members.Binding,
                    row.Binding.Population);
                Assert.True(row.ExactMemberCount > 0);
            });
        Assert.Equal(
            104,
            rows.Items.Sum(row =>
                Assert.IsType<int>(row.ExactMemberCount)));
        Assert.Equal(
            "type-member-group-population-inspection/share",
            Assert.IsType<InspectionShare.NonProjectable>(
                    inspection.Share)
                .Path);
        Assert.Empty(inspection.Diagnostics);

        var outcome =
            new TypeOverviewDocumentInspectionOutcome.Available(
                document);
        string json = JsonSerializer.Serialize(
            (TypeOverviewDocumentInspectionOutcome)outcome,
            TypeMemberInspectionDocumentJsonContext.Default
                .TypeOverviewDocumentInspectionOutcome);
        TypeOverviewDocumentInspectionOutcome copy =
            JsonSerializer.Deserialize(
                json,
                TypeMemberInspectionDocumentJsonContext.Default
                    .TypeOverviewDocumentInspectionOutcome)!;
        TypeOverviewDocument roundTripped =
            Assert.IsType<
                TypeOverviewDocumentInspectionOutcome.Available>(
                    copy)
                .Document;

        Assert.Equal(
            document.Subject.TypeDefinitionToken,
            roundTripped.Subject.TypeDefinitionToken);
        Assert.Equal(
            104,
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    roundTripped.Members.Rows)
                .Items
                .Sum(row =>
                    Assert.IsType<int>(
                        row.ExactMemberCount)));
        Assert.DoesNotContain("displaySignature", json);
        Assert.DoesNotContain("canonicalSignature", json);

        await library.RetireAsync();
    }

    [Fact]
    public async Task BoundedOverviewPreservesCountedContinuation()
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
                    rows: new(maximumRows: 2),
                    includeHidden: true));
        TypeMemberGroupRowsOutcome.Read rows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                document.Members.Rows);
        TypeMemberGroupContinuation continuation =
            Assert.IsType<TypeMemberGroupContinuation>(
                rows.Continuation);

        Assert.Equal(2, rows.Items.Length);
        Assert.Equal(document.Members.Binding, continuation.Binding);
        Assert.True(continuation.IncludeExactMemberCount);
        Assert.All(
            rows.Items,
            row => Assert.True(row.ExactMemberCount > 0));

        await library.RetireAsync();
    }

    [Fact]
    public async Task ReceiverFilterAppliesBeforeNestedCounts()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeMemberGroupShape declared =
            Assert.Single(
                Rows(
                    Available(
                        Execute(
                            library,
                            new(maximumRows: int.MaxValue),
                            receiver:
                                TypeMemberGroupReceiverFilter
                                    .Static))),
                row => row.Binding.Name.ToString()
                    == "Deserialize");
        TypeMemberGroupShape extension =
            Assert.Single(
                Rows(
                    Available(
                        Execute(
                            library,
                            new(maximumRows: int.MaxValue),
                            receiver:
                                TypeMemberGroupReceiverFilter
                                    .Extension))),
                row => row.Binding.Name.ToString()
                    == "Deserialize");

        Assert.Equal(25, declared.ExactMemberCount);
        Assert.Equal(
            MemberGroupReceiverForms.Static,
            declared.Receivers);
        Assert.Equal(15, extension.ExactMemberCount);
        Assert.Equal(
            MemberGroupReceiverForms.Extension,
            extension.Receivers);
        Assert.Equal(
            (
                declared.Binding.Name,
                declared.Binding.Category,
                declared.Binding.Role),
            (
                extension.Binding.Name,
                extension.Binding.Category,
                extension.Binding.Role));

        await library.RetireAsync();
    }

    [Fact]
    public async Task RowRejectionIsPromotedToDocumentOutcome()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeMemberGroupContinuation continuation =
            Assert.IsType<TypeMemberGroupContinuation>(
                RowResult(
                        Available(
                            Execute(
                                library,
                                new(maximumRows: 1))))
                    .Continuation);
        var outOfRange = new TypeMemberGroupContinuation(
            continuation.Binding,
            nextOrdinal: 1_000,
            includeExactMemberCount: true);

        TypeOverviewDocumentInspectionOutcome.Rejected rejected =
            Assert.IsType<
                TypeOverviewDocumentInspectionOutcome.Rejected>(
                    Execute(
                        library,
                        new(
                            maximumRows: 1,
                            continuation: outOfRange))
                    .Content);

        Assert.Equal(
            TypeOverviewDocumentInspectionRejection
                .ContinuationOutOfRange,
            rejected.Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task MemberBoundIsTypedDocumentIncompleteness()
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

        TypeOverviewDocumentInspectionOutcome.Incomplete incomplete =
            Assert.IsType<
                TypeOverviewDocumentInspectionOutcome.Incomplete>(
                    Execute(
                        library,
                        new(maximumRows: int.MaxValue),
                        bounds: memberBound)
                    .Content);

        Assert.Equal(
            TypeOverviewDocumentInspectionBound.Members,
            incomplete.Bound);
        Assert.Equal(1, incomplete.Limit);
        Assert.Equal(2, incomplete.Measured);

        await library.RetireAsync();
    }

    [Fact]
    public async Task MissingTypeIsTypedDocumentRejection()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeOverviewDocumentInspectionOutcome.Rejected rejected =
            Assert.IsType<
                TypeOverviewDocumentInspectionOutcome.Rejected>(
                    Execute(
                        library,
                        new(maximumRows: 10),
                        type: Name(
                            "System.Text.Json",
                            "Missing"))
                    .Content);

        Assert.Equal(
            TypeOverviewDocumentInspectionRejection.TypeNotFound,
            rejected.Reason);
        await library.RetireAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        RootAdjacencyFailureIsOwnerIssuedBeforePresentation(
            bool malformedAssemblyReference)
    {
        byte[] content =
            LibraryInspectionTestLibrary
                .BuildMalformedRootAdjacencyImage(
                    malformedAssemblyReference);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
            inspection =
                Execute(
                    library,
                    new(maximumRows: int.MaxValue),
                    type: Name("N", "Healthy"));

        _ = Available(inspection);
        InspectionDiagnostic diagnostic =
            Assert.Single(inspection.Diagnostics);
        Assert.Equal(
            "type-document.root-adjacency-incomplete",
            diagnostic.Code);
        Assert.Equal(
            InspectionDiagnosticSeverity.Error,
            diagnostic.Severity);
        Assert.Contains(
            malformedAssemblyReference
                ? "AssemblyRef"
                : "forwarder",
            diagnostic.Summary.ToString(),
            StringComparison.OrdinalIgnoreCase);

        await library.RetireAsync();
    }

    private static InspectionEnvelope<
            TypeOverviewDocumentInspectionOutcome>
        Execute(
            LibraryInspectionTestLibrary library,
            TypeMemberGroupRowsRequest rows,
            MetadataTypeDefinitionName? type = null,
            TypeMemberGroupSpelling spelling =
                TypeMemberGroupSpelling.CSharp,
            TypeMemberGroupAccessibilityFilter accessibility =
                TypeMemberGroupAccessibilityFilter.Public,
            TypeMemberGroupReceiverFilter receiver =
                TypeMemberGroupReceiverFilter.All,
            bool includeHidden = false,
            ApiSurfaceExtractionBounds? bounds = null) =>
        TypeOverviewDocumentInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    type
                        ?? Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                    rows,
                    bounds ?? s_bounds,
                    spelling,
                    accessibility,
                    receiver,
                    includeHidden)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static TypeOverviewDocument Available(
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
            envelope) =>
        Assert.IsType<
                TypeOverviewDocumentInspectionOutcome.Available>(
                envelope.Content)
            .Document;

    private static IReadOnlyList<TypeMemberGroupShape> Rows(
        TypeOverviewDocument document) =>
        RowResult(document).Items;

    private static TypeMemberGroupRowsOutcome.Read RowResult(
        TypeOverviewDocument document) =>
        Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
            document.Members.Rows);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}
