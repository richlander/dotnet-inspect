using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

// A setter whose return carries modreq(IsExternalInit) is spelled `init` in its
// accessor kind and in the signature text on every surface
// (docs/design/api-population-scope.md#spelling-within-api-visibility-scope, #10679).
public sealed class InitAccessorSpellingTests
{
    [Theory]
    [InlineData(ApiSurfaceExtractionScope.Public, "public int V { get; init; }", "int V { get; init; }", null)]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll, "public int V { get; init; }", "int V { get; init; }", null)]
    [InlineData(ApiSurfaceExtractionScope.Public, "public int V { get; private init; }", "int V { get; private init; }", "private")]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll, "public int V { get; private init; }", "int V { get; private init; }", "private")]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll, "public int V { get; protected init; }", "int V { get; protected init; }", "protected")]
    [InlineData(ApiSurfaceExtractionScope.Public, "public required int V { get; init; }", "required int V { get; init; }", null)]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll, "public required int V { get; init; }", "required int V { get; init; }", null)]
    public void InitSetter_IsSpelledInitInKindAndSignature(
        ApiSurfaceExtractionScope scope,
        string property,
        string signature,
        string? setterAccessibility)
    {
        ApiMember member = Property(
            Extract($"public class C {{ {property} }}", scope),
            "C",
            "V");

        Assert.Equal(signature, member.Signature);
        Assert.Equal(
            ["get", "init"],
            member.SignatureModel!.Accessors.Select(accessor => accessor.Kind));
        ApiAccessor init = member.SignatureModel.Accessors[1];
        Assert.Equal(setterAccessibility, init.Accessibility);
        Assert.Equal("set_V", init.Name);
        Assert.NotNull(init.StructuralReturnType);
        Assert.True(init.SignatureMatchesProperty);
    }

    [Theory]
    [InlineData(ApiSurfaceExtractionScope.Public)]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll)]
    public void RecordPositionalProperty_IsSpelledInit(ApiSurfaceExtractionScope scope)
    {
        ApiMember member = Property(
            Extract("public record R(int X);", scope),
            "R",
            "X");

        Assert.Equal("int X { get; init; }", member.Signature);
        Assert.Equal(
            ["get", "init"],
            member.SignatureModel!.Accessors.Select(accessor => accessor.Kind));
    }

    [Theory]
    [InlineData(ApiSurfaceExtractionScope.Public, "public int V { get; set; }", "int V { get; set; }")]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll, "public int V { get; private set; }", "int V { get; private set; }")]
    public void OrdinarySetter_StaysSet(
        ApiSurfaceExtractionScope scope,
        string property,
        string signature)
    {
        ApiMember member = Property(
            Extract($"public class C {{ {property} }}", scope),
            "C",
            "V");

        Assert.Equal(signature, member.Signature);
        Assert.Equal(
            ["get", "set"],
            member.SignatureModel!.Accessors.Select(accessor => accessor.Kind));
        Assert.Null(member.SignatureModel.Accessors[1].StructuralReturnType);
    }

    // The metadata declaration surface spells the same accessor kind.
    [Fact]
    public void MetadataDeclarationSurface_SpellsInit()
    {
        byte[] image = Compile(
            "public class C { public required int V { get; init; } public int W { get; private init; } public int S { get; set; } }");
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle handle = reader.TypeDefinitions.Single(
            candidate => reader.GetString(reader.GetTypeDefinition(candidate).Name) == "C");

        ApiType type = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            handle,
            includeNonPublicMembers: true);

        Assert.Equal("int V { get; init; }", Single(type, "V").Signature);
        Assert.Equal("int W { get; private init; }", Single(type, "W").Signature);
        Assert.Equal("int S { get; set; }", Single(type, "S").Signature);
        Assert.Equal(
            ["get", "init"],
            Single(type, "W").SignatureModel!.Accessors.Select(accessor => accessor.Kind));

        static ApiMember Single(ApiType type, string name) =>
            Assert.Single(
                type.Members,
                member => member.Kind == "property" && member.Name == name);
    }

    static ApiMember Property(ApiSurface surface, string typeName, string propertyName) =>
        Assert.Single(
            Assert.Single(surface.Types, type => type.Name == typeName).Members,
            member => member.Kind == "property" && member.Name == propertyName);

    static ApiSurface Extract(string source, ApiSurfaceExtractionScope scope)
    {
        using var peReader = new PEReader(new MemoryStream(Compile(source)));
        return ApiSurfaceExtractor.Extract(peReader, scope);
    }

    static byte[] Compile(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "InitAccessorSpelling",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result =
            compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return image.ToArray();
    }
}
