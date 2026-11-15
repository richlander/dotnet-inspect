using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class ExactTypeLibrarySelectionTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds = new(
        20_000, 0, 100, 20_000, 2_000_000, 8_000_000);

    [Theory]
    [InlineData("JsonSerializer")]
    [InlineData("Json.JsonSerializer")]
    [InlineData("System.Text.Json.JsonSerializer")]
    [InlineData("system.text.json.jsonserializer")]
    public async Task QuerySelectsTheIssuedDefinitionWithoutMemberProjection(string query)
    {
        await using var library = await JsonLibraryAsync();
        ExactTypeDefinitionSelection result = Select(library, query);
        Assert.Equal(ExactTypeInspectionOutcome.Available, result.Outcome);
        Assert.Equal("System.Text.Json", result.Definition!.Namespace);
        Assert.Equal(["JsonSerializer"], result.Definition.Segments);
        await library.RetireAsync();
    }

    [Theory]
    [InlineData("System.Text.Json.JsonSerializer", ExactTypeInspectionOutcome.Available)]
    [InlineData("system.text.json.JsonSerializer", ExactTypeInspectionOutcome.NotFound)]
    [InlineData("JsonSerializer", ExactTypeInspectionOutcome.NotFound)]
    public async Task DefinitionSelectionIsOrdinalAndDoesNotEnterQueryTiers(
        string identity, ExactTypeInspectionOutcome expected)
    {
        await using var library = await JsonLibraryAsync();
        var result = Select(library, identity, kind: ExactTypeSelectionKind.DefinitionIdentity);
        Assert.Equal(expected, result.Outcome);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public async Task CompetingNestedDefinitionsRemainAmbiguous()
    {
        await using var library = await JsonLibraryAsync();
        var result = Select(library, "DebugView", ApiSurfaceScope.IncludeAll);
        Assert.Equal(ExactTypeInspectionOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.Definition);
        Assert.Equal(3, result.Suggestions.Length);
    }

    [Theory]
    [InlineData("MetadataRows")]
    [InlineData("RetainedDeclarations")]
    [InlineData("RetainedTextCharacters")]
    public async Task ExhaustedInventoryDoesNotProveAbsence(string bound)
    {
        await using var library = await JsonLibraryAsync();
        ApiSurfaceExtractionBounds limits = bound switch
        {
            "MetadataRows" => new(20_000, 0, 100, 20_000, 0, 8_000_000),
            "RetainedDeclarations" => new(0, 0, 100, 0, 2_000_000, 8_000_000),
            _ => new(20_000, 0, 100, 20_000, 2_000_000, 0),
        };
        var result = Select(library, "NoSuchType10694", bounds: limits);
        Assert.Equal(ExactTypeInspectionOutcome.Unavailable, result.Outcome);
        Assert.Contains(bound, result.Failure, StringComparison.Ordinal);
        Assert.Null(result.Definition);
    }

    [Fact]
    public async Task AForwardingRootWithoutItsDefiningContextIsUnavailable()
    {
        byte[] bytes = await LibraryInspectionTestLibrary.RealNetstandardAsync();
        await using var library = await LibraryInspectionTestLibrary.CreateAsync(
            bytes, LibraryInspectionTestLibrary.Identity(bytes));
        var result = Select(library, "System.String");
        Assert.Equal(ExactTypeInspectionOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Definition);
    }

    [Fact]
    public async Task CallbackFailureReleasesTheLibraryLease()
    {
        await using var library = await JsonLibraryAsync();
        LibraryOperationLease lease = library.IssueOperation();
        Assert.Throws<InvalidOperationException>(() =>
            ExactTypeInspectionOperation.ExecuteWithLibraryTarget<int>(
                library.Reference, lease, "JsonSerializer", ApiSurfaceScope.Public,
                s_bounds, (_, _) => throw new InvalidOperationException("callback"),
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => lease.Snapshot(
            library.Reference.ApiAssembly, static (view, _) => view.Content.Length, TestContext.Current.CancellationToken));
        await library.RetireAsync();
    }

    [Theory]
    [InlineData(ApiSurfaceScope.Public, ExactTypeInspectionOutcome.NotFound)]
    [InlineData(ApiSurfaceScope.IncludeAll, ExactTypeInspectionOutcome.Available)]
    public async Task HiddenPublicTypeConsumesTheExistingExactTypeAdmission(
        ApiSurfaceScope scope, ExactTypeInspectionOutcome expected)
    {
        byte[] content = await LibraryInspectionTestLibrary.PinnedNet11Async(
            "runtime", "System.Private.CoreLib.dll");
        await using var library = await LibraryInspectionTestLibrary.CreateAsync(
            content, LibraryInspectionTestLibrary.Identity(content));
        var result = Select(library, "CurrencyWrapper", scope);
        Assert.Equal(expected, result.Outcome);
    }

    private static async Task<LibraryInspectionTestLibrary> JsonLibraryAsync()
    {
        byte[] content = await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        return await LibraryInspectionTestLibrary.CreateAsync(content,
            LibraryInspectionTestLibrary.Identity(content));
    }

    [Fact]
    public async Task IssuedDefinitionOverviewRetainsPartialEvidenceWithoutAQueryInventory()
    {
        byte[] content = LibraryInspectionTestLibrary.BuildMalformedRootAdjacencyImage(
            malformedAssemblyReference: true);
        await using var library = await LibraryInspectionTestLibrary.CreateAsync(
            content, LibraryInspectionTestLibrary.Identity(content));
        var name = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Healthy"])).Name;
        var result = ExactTypeInspectionOperation.ExecuteOverviewDefinition(
            new(library.Reference, new(name, new(10), s_bounds)),
            library.IssueOperation(), TestContext.Current.CancellationToken);
        Assert.IsType<TypeOverviewDocumentInspectionOutcome.Available>(result.Content);
        Assert.Contains(result.Diagnostics, item => item.Code == "type-document.root-adjacency-incomplete");
        await library.RetireAsync();
    }

    private static ExactTypeDefinitionSelection Select(
        LibraryInspectionTestLibrary library, string query,
        ApiSurfaceScope scope = ApiSurfaceScope.Public,
        ExactTypeSelectionKind kind = ExactTypeSelectionKind.Query,
        ApiSurfaceExtractionBounds? bounds = null) =>
        ExactTypeInspectionOperation.ExecuteWithLibraryTarget(
            library.Reference, library.IssueOperation(), query, scope,
            bounds ?? s_bounds, (envelope, _) => envelope.Content,
            kind, TestContext.Current.CancellationToken);
}
