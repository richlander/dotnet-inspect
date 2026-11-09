using DotnetInspector.Presentation;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeProjectionInspectionOperationTests
{
    [Theory]
    [InlineData("JsonElement", "struct")]
    [InlineData("JsonNamingPolicy", "class")]
    public async Task SelectedType_DoesNotRequireOtherTypesOrMemberInventory(
        string name, string kind)
    {
        byte[] content = await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content, LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName type = Name(name);
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 1, maxMembers: 0, maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000, maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 1_000_000);

        InspectionEnvelope<LibraryTypeProjectionInspectionOutcome> envelope =
            TypeProjectionInspectionOperation.Execute(
                new(library.Reference, type, bounds), library.IssueOperation(),
                TestContext.Current.CancellationToken);
        ResearchViews.TypeProjectionResult projection =
            Assert.IsType<LibraryTypeProjectionInspectionOutcome.Available>(
                envelope.Content).Projection;

        Assert.Equal($"System.Text.Json.{name}", projection.Identity.FullName);
        Assert.Equal(kind, projection.Identity.Kind);
        Assert.Null(projection.Composition);
        Assert.Null(projection.Graph);
        TypeSignature signature = Assert.IsType<TypeSignatureInspectionOutcome.Available>(
            TypeSignatureInspectionOperation.Execute(
                new(library.Reference, new(type, bounds)),
                library.IssueOperation(),
                TestContext.Current.CancellationToken).Content).Signature;
        TypeSignaturePresentation info = ExactTypeInfoTsvPresentation.Project(signature);
        Assert.Equal(info.Kind, projection.Identity.Kind);
        Assert.Equal(info.Namespace, projection.Identity.Namespace);
        await library.RetireAsync();
    }

    [Fact]
    public async Task ExplicitRelationships_FindPoliciesWithoutMemberInventory()
    {
        byte[] content = await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content, LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(
            maxTypes: 10_000, maxMembers: 0, maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000, maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 10_000_000);

        var envelope = TypeProjectionInspectionOperation.Execute(
            new(library.Reference, Name("JsonNamingPolicy"),
                bounds, IncludeRelationships: true),
            library.IssueOperation(), TestContext.Current.CancellationToken);
        ResearchViews.TypeProjectionResult projection =
            Assert.IsType<LibraryTypeProjectionInspectionOutcome.Available>(
                envelope.Content).Projection;

        Assert.Contains("System.Text.Json.JsonCamelCaseNamingPolicy",
            projection.DerivedTypes);
        Assert.NotNull(projection.Graph);
        Assert.Contains(projection.Graph.Edges,
            static edge => edge.Kind == ResearchViews.TypeRelationshipKind.DerivedFrom);
        Assert.Null(projection.Composition);
        await library.RetireAsync();
    }

    [Fact]
    public async Task MetadataBound_IsVisibleRatherThanNotFound()
    {
        byte[] content = await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content, LibraryInspectionTestLibrary.Identity(content));
        var bounds = new ApiSurfaceExtractionBounds(1, 0, 0, 0, 0, 0);

        var envelope = TypeProjectionInspectionOperation.Execute(
            new(library.Reference, Name("JsonElement"), bounds),
            library.IssueOperation(), TestContext.Current.CancellationToken);

        Assert.IsType<LibraryTypeProjectionInspectionOutcome.Unavailable>(envelope.Content);
        Assert.Contains(envelope.Diagnostics,
            static diagnostic => diagnostic.Code == "type-projection.unavailable");
        await library.RetireAsync();
    }

    private static MetadataTypeDefinitionName Name(string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("System.Text.Json", [name])).Name;
}
