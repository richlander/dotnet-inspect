using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Fixtures;

namespace ILInspector.Metadata.Tests;

public sealed class ApiLayoutFactsTests
{
    static string FixturePath => FixtureCatalog.MetadataMemorySafety.AssemblyPath();
    static readonly Lazy<ApiSurface> Surface = new(() =>
    {
        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(pe, includeAll: true);
    });

    [Theory]
    [InlineData("LayoutFactsExplicitFixture", ApiTypeLayout.Explicit, 32, 2)]
    [InlineData("LayoutFactsSequentialFixture", ApiTypeLayout.Sequential, 24, 4)]
    [InlineData("LayoutFactsDefaultFixture", ApiTypeLayout.Auto, 0, 0)]
    [InlineData("LayoutFactsDefaultFixture.Nested", ApiTypeLayout.Explicit, 16, 1)]
    public void LayoutValuesRetainTheirDefiningType(
        string name, ApiTypeLayout kind, int size, int packingSize)
    {
        ApiType type = Type(name);
        var facts = Assert.IsType<ApiTypeLayoutFacts>(type.LayoutDetails);
        Assert.Equal(kind, type.Layout);
        Assert.Equal(size, facts.Size);
        Assert.Equal(packingSize, facts.PackingSize);
        Assert.Equal(type.MetadataToken, facts.TypeToken);

        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinition definition = reader.GetTypeDefinition(
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(facts.TypeToken));
        Assert.Equal(reader.GetGuid(reader.GetModuleDefinition().Mvid), facts.ModuleVersionId);
        Assert.Equal(definition.GetLayout().Size, facts.Size);
        Assert.Equal(definition.GetLayout().PackingSize, facts.PackingSize);
    }

    [Theory]
    [InlineData("LayoutFactsExplicitFixture", "Zero", 0, false)]
    [InlineData("LayoutFactsExplicitFixture", "Nonzero", 12, false)]
    [InlineData("LayoutFactsExplicitFixture", "Static", null, true)]
    [InlineData("LayoutFactsSequentialFixture", "Value", null, false)]
    [InlineData("LayoutFactsDefaultFixture", "Value", null, false)]
    [InlineData("LayoutFactsDefaultFixture.Nested", "Value", 4, false)]
    public void FieldOffsetsPreserveZeroAndScopedAssociation(
        string typeName, string fieldName, int? offset, bool isStatic)
    {
        ApiType type = Type(typeName);
        ApiMember field = type.Members.Single(member => member.Name == fieldName);
        var facts = Assert.IsType<ApiFieldLayoutFacts>(field.FieldLayout);
        Assert.Equal(offset, facts.Offset);
        Assert.Equal(isStatic, field.IsStatic);
        Assert.Equal(field.DeclarationMetadataToken, facts.FieldToken);
        Assert.Equal(type.MetadataToken, facts.DeclaringTypeToken);
        Assert.Equal(type.LayoutDetails!.ModuleVersionId, facts.ModuleVersionId);

        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        FieldDefinition definition = reader.GetFieldDefinition(
            (FieldDefinitionHandle)MetadataTokens.EntityHandle(facts.FieldToken));
        Assert.Equal(MetadataTokens.GetToken(definition.GetDeclaringType()), facts.DeclaringTypeToken);
        Assert.Equal(offset ?? -1, definition.GetOffset());
    }

    [Fact]
    public void MethodsAndPropertiesDoNotAcquireFieldFacts()
    {
        ApiType type = Type("LayoutFactsExplicitFixture");
        Assert.Null(type.Members.Single(member => member.Name == "Method").FieldLayout);
        Assert.Null(type.Members.Single(member => member.Name == "Property").FieldLayout);
        Assert.All(type.Members.Where(member => member.Kind != "field"),
            member => Assert.Null(member.FieldLayout));
    }

    [Theory]
    [InlineData("LayoutFactsExplicitFixture")]
    [InlineData("LayoutFactsSequentialFixture")]
    [InlineData("LayoutFactsDefaultFixture")]
    [InlineData("LayoutFactsDefaultFixture.Nested")]
    public void HandleBasedProjectionMatchesFullExtraction(string name)
    {
        ApiType expected = Type(name);
        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        ApiType actual = MetadataDeclarationQuery.GetTypeSurface(
            pe.GetMetadataReader(),
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(expected.MetadataToken!.Value),
            includeNonPublicMembers: true);
        Assert.Equal(expected.Layout, actual.Layout);
        Assert.Equal(expected.LayoutDetails, actual.LayoutDetails);
        Assert.Equal(expected.MetadataToken, actual.MetadataToken);
        foreach (ApiMember field in expected.Members.Where(member => member.Kind == "field"))
        {
            ApiMember projected = actual.Members.Single(member => member.Name == field.Name);
            Assert.Equal(field.DeclarationMetadataToken, projected.DeclarationMetadataToken);
            Assert.Equal(field.FieldLayout, projected.FieldLayout);
        }
    }

    [Fact]
    public void FactsSurviveReaderDisposalAndJson()
    {
        ApiType original = Type("LayoutFactsExplicitFixture");
        ApiType restored = JsonSerializer.Deserialize<ApiType>(JsonSerializer.Serialize(original))!;
        Assert.Equal(original.Layout, restored.Layout);
        Assert.Equal(original.LayoutDetails, restored.LayoutDetails);
        foreach (ApiMember field in original.Members.Where(member => member.Kind == "field"))
        {
            Assert.Equal(field.FieldLayout,
                restored.Members.Single(member => member.Name == field.Name).FieldLayout);
        }
    }

    [Fact]
    public void MissingOlderFactsAreNotKnownDefaultsOrZeroOffsets()
    {
        ApiType type = JsonSerializer.Deserialize<ApiType>("""{"Name":"Old"}""")!;
        ApiMember field = JsonSerializer.Deserialize<ApiMember>("""{"Name":"Field","Kind":"field"}""")!;
        Assert.Null(type.LayoutDetails);
        Assert.Null(field.FieldLayout);
        Assert.DoesNotContain("LayoutDetails", JsonSerializer.Serialize(type));
        Assert.DoesNotContain("FieldLayout", JsonSerializer.Serialize(field));

        Assert.NotNull(Type("LayoutFactsDefaultFixture").LayoutDetails);
        Assert.NotNull(Type("LayoutFactsDefaultFixture").Members
            .Single(member => member.Name == "Value").FieldLayout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheapSurfacesKeepDetailedFactsUnavailable(bool typesOnly)
    {
        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        ApiSurface surface = typesOnly
            ? ApiSurfaceExtractor.Extract(pe, typesOnly: true)
            : ApiSurfaceExtractor.ExtractSummary(pe);
        Assert.NotEmpty(surface.Types);
        Assert.All(surface.Types, type =>
        {
            Assert.Null(type.LayoutDetails);
            Assert.All(type.Members, member => Assert.Null(member.FieldLayout));
        });
        Assert.Equal(ApiTypeLayout.Explicit,
            surface.Types.Single(type => type.Name == "LayoutFactsExplicitFixture").Layout);
    }

    [Fact]
    public void PresentDefaultClassLayoutRemainsAnObservedDefault()
    {
        using var pe = new PEReader(new MemoryStream(LayoutImage(size: 0, packingSize: 0)));
        Assert.Equal(1, pe.GetMetadataReader().GetTableRowCount(TableIndex.ClassLayout));
        ApiType type = Assert.Single(ApiSurfaceExtractor.Extract(pe).Types);
        var facts = Assert.IsType<ApiTypeLayoutFacts>(type.LayoutDetails);
        Assert.Equal(0, facts.Size);
        Assert.Equal(0, facts.PackingSize);
    }

    [Fact]
    public void UnrepresentableOffsetRemainsUnavailableRatherThanZero()
    {
        using var pe = new PEReader(new MemoryStream(LayoutImage(offset: uint.MaxValue)));
        MetadataReader reader = pe.GetMetadataReader();
        Assert.Equal(1, reader.GetTableRowCount(TableIndex.FieldLayout));
        Assert.Equal(-1, reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(1)).GetOffset());
        ApiType type = Assert.Single(ApiSurfaceExtractor.Extract(pe).Types);
        var facts = Assert.IsType<ApiFieldLayoutFacts>(Assert.Single(type.Members).FieldLayout);
        Assert.Null(facts.Offset);
        Assert.Equal(type.MetadataToken, facts.DeclaringTypeToken);
    }

    [Fact]
    public void InvalidTypeSizeUsesExistingVisibleFailurePaths()
    {
        using var pe = new PEReader(new MemoryStream(LayoutImage(size: uint.MaxValue)));
        ApiSurface surface = ApiSurfaceExtractor.Extract(pe);
        Assert.Empty(surface.Types);
        Assert.NotEmpty(surface.InspectionFailures);
        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetTypeSurface(
                pe.GetMetadataReader(), MetadataTokens.TypeDefinitionHandle(2)));
    }

    static ApiType Type(string name) => Surface.Value.Types.Single(type => type.Name == name);

    static byte[] LayoutImage(uint size = 16, ushort packingSize = 1, uint offset = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Layout.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Layout"),
            new Version(1, 0), default, default, default, default);
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0), default, default, default, default);
        var objectType = metadata.AddTypeReference(runtime,
            metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        var field = metadata.AddFieldDefinition(FieldAttributes.Public,
            metadata.GetOrAddString("Value"), metadata.GetOrAddBlob(new byte[] { 6, 8 }));
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default,
            metadata.GetOrAddString("<Module>"), default, field, MetadataTokens.MethodDefinitionHandle(1));
        var type = metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.ExplicitLayout,
            default, metadata.GetOrAddString("Layout"), objectType, field, MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeLayout(type, packingSize, size);
        metadata.AddFieldLayout(field, 0);
        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly).Serialize(image);
        byte[] bytes = image.ToArray();
        using var pe = new PEReader(new MemoryStream(bytes));
        int fieldLayoutOffset = pe.PEHeaders.MetadataStartOffset
            + pe.GetMetadataReader().GetTableMetadataOffset(TableIndex.FieldLayout);
        // SRM's builder accepts signed offsets; the table stores an unsigned value.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(fieldLayoutOffset), offset);
        return bytes;
    }
}
