using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class JsonSourceGenerationOptionsAttributeTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    public void DuplicateRowsAreUnsupportedRegardlessOfOrder(
        int first,
        int? second)
    {
        using var stream = new MemoryStream(
            BuildImage(first, second),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        bool found =
            AttributeReader.TryGetJsonSourceGenerationPropertyNamingPolicy(
                reader,
                type.GetCustomAttributes(),
                out JsonWireNamingPolicy? policy);

        Assert.True(found);
        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    [Fact]
    public void SingleRowPreservesItsPolicy()
    {
        using var stream = new MemoryStream(
            BuildImage(1),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        bool found =
            AttributeReader.TryGetJsonSourceGenerationPropertyNamingPolicy(
                reader,
                type.GetCustomAttributes(),
                out JsonWireNamingPolicy? policy);

        Assert.True(found);
        Assert.Equal(JsonWireNamingPolicy.CamelCase, policy);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public void DuplicatePolicyArgumentsWithinOneRowAreUnsupported(
        int first,
        int second)
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => PolicyValue(
                    metadata,
                    0x54,
                    0x55,
                    "PropertyNamingPolicy",
                    first,
                    second)));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    [Theory]
    [InlineData(0x53, 0x55, "PropertyNamingPolicy")]
    [InlineData(0x54, 0x08, "PropertyNamingPolicy")]
    [InlineData(0x54, 0x55, "Bogus")]
    public void SemanticallyMalformedOptionIsUnsupported(
        byte kind,
        byte type,
        string name)
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => PolicyValue(
                    metadata,
                    kind,
                    type,
                    name,
                    1)));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    static JsonWireNamingPolicy? ReadPolicy(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        Assert.True(
            AttributeReader.TryGetJsonSourceGenerationPropertyNamingPolicy(
                reader,
                type.GetCustomAttributes(),
                out JsonWireNamingPolicy? policy));
        return policy;
    }

    static byte[] BuildImage(int first, int? second = null)
        => BuildImageCore(
            (metadata, target, constructor) =>
            {
                metadata.AddCustomAttribute(
                    target,
                    constructor,
                    PolicyValue(metadata, first));
                if (second is >= 0)
                {
                    metadata.AddCustomAttribute(
                        target,
                        constructor,
                        PolicyValue(metadata, second.Value));
                }
                else if (second is < 0)
                {
                    metadata.AddCustomAttribute(
                        target,
                        constructor,
                        metadata.GetOrAddBlob(new byte[] { 0 }));
                }
            });

    static byte[] BuildSingleRow(
        Func<MetadataBuilder, BlobHandle> valueFactory)
        => BuildImageCore(
            (metadata, target, constructor) =>
                metadata.AddCustomAttribute(
                    target,
                    constructor,
                    valueFactory(metadata)));

    static byte[] BuildImageCore(
        Action<MetadataBuilder, TypeDefinitionHandle, MemberReferenceHandle>
            addAttributes)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemTextJson = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Text.Json"),
            new Version(10, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            systemTextJson,
            metadata.GetOrAddString("System.Text.Json.Serialization"),
            metadata.GetOrAddString(
                "JsonSourceGenerationOptionsAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Context"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        addAttributes(metadata, target, constructor);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobHandle PolicyValue(MetadataBuilder metadata, int value)
        => PolicyValue(
            metadata,
            0x54,
            0x55,
            "PropertyNamingPolicy",
            value);

    static BlobHandle PolicyValue(
        MetadataBuilder metadata,
        byte kind,
        byte type,
        string name,
        params int[] values)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteUInt16((ushort)values.Length);
        foreach (int value in values)
        {
            blob.WriteByte(kind);
            blob.WriteByte(type);
            if (type == 0x55)
            {
                blob.WriteSerializedString(
                    "System.Text.Json.Serialization.JsonKnownNamingPolicy, "
                        + "System.Text.Json");
            }
            blob.WriteSerializedString(name);
            blob.WriteInt32(value);
        }
        return metadata.GetOrAddBlob(blob);
    }
}
