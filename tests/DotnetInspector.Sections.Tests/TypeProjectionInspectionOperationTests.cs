using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredSelection_DoesNotRetainCollidingDottedType(bool nested)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("TypeCollision.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("TypeCollision"),
            new Version(1, 0, 0, 0), default, default, default, default);
        FieldDefinitionHandle fields = MetadataTokens.FieldDefinitionHandle(1);
        MethodDefinitionHandle methods = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default,
            metadata.GetOrAddString("<Module>"), default, fields, methods);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("A.B"), metadata.GetOrAddString("C"),
            default, fields, methods);
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("A"), metadata.GetOrAddString("B"),
            default, fields, methods);
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Interface | TypeAttributes.Abstract,
            default, metadata.GetOrAddString("C"), default, fields, methods);
        metadata.AddNestedType(inner, outer);
        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata), new BlobBuilder(),
            flags: CorFlags.ILOnly).Serialize(image);
        byte[] content = image.ToArray();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content, LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName type =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    nested ? "A" : "A.B", nested ? ["B", "C"] : ["C"])).Name;
        var bounds = new ApiSurfaceExtractionBounds(
            1, 0, 100, 0, 100_000, 100_000);

        var envelope = TypeProjectionInspectionOperation.Execute(
            new(library.Reference, type, bounds), library.IssueOperation(),
            TestContext.Current.CancellationToken);

        ResearchViews.TypeProjectionResult projection =
            Assert.IsType<LibraryTypeProjectionInspectionOutcome.Available>(
                envelope.Content).Projection;
        Assert.Equal(nested ? "A" : "A.B", projection.Identity.Namespace);
        Assert.Null(projection.Composition);
        await library.RetireAsync();
    }

    private static MetadataTypeDefinitionName Name(string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("System.Text.Json", [name])).Name;
}
