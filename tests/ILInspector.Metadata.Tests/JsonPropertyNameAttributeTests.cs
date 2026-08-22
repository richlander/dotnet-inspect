using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class JsonPropertyNameAttributeTests
{
    [Fact]
    public void UnexpectedNamedArgumentProducesMalformedRowMarker()
    {
        using var stream = new MemoryStream(BuildImage(), writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<string?> values = AttributeReader.ReadJsonPropertyNames(
            reader,
            type.GetCustomAttributes());

        Assert.Equal([null], values);
    }

    [Fact]
    public void JsonStringEnumMemberNameUnexpectedNamedArgumentProducesMalformedRowMarker()
    {
        using var stream = new MemoryStream(
            BuildImage("JsonStringEnumMemberNameAttribute"),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<string?> values =
            AttributeReader.ReadJsonStringEnumMemberNames(
                reader,
                type.GetCustomAttributes());

        Assert.Equal([null], values);
    }

    [Fact]
    public void JsonStringEnumMemberNameDuplicateRowsPreserveOrderedEvidence()
    {
        using var stream = new MemoryStream(
            BuildImage(
                "JsonStringEnumMemberNameAttribute",
                duplicateValidRows: true),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<string?> values =
            AttributeReader.ReadJsonStringEnumMemberNames(
                reader,
                type.GetCustomAttributes());

        Assert.Equal(["ok", "ok"], values);
    }

    [Fact]
    public void SameNameAttributeFromUntrustedAssemblyIsIgnored()
    {
        using var stream = new MemoryStream(
            BuildImage(trustedAssembly: false),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<string?> values = AttributeReader.ReadJsonPropertyNames(
            reader,
            type.GetCustomAttributes());

        Assert.Empty(values);
    }

    [Fact]
    public void SameNameEnumMemberAttributeFromUntrustedAssemblyIsIgnored()
    {
        using var stream = new MemoryStream(
            BuildImage(
                "JsonStringEnumMemberNameAttribute",
                trustedAssembly: false),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<string?> values =
            AttributeReader.ReadJsonStringEnumMemberNames(
                reader,
                type.GetCustomAttributes());

        Assert.Empty(values);
    }

    [Theory]
    [InlineData("JsonIncludeAttribute", true)]
    [InlineData("JsonIncludeAttribute", false)]
    [InlineData("JsonIgnoreAttribute", true)]
    [InlineData("JsonIgnoreAttribute", false)]
    [InlineData("FlagsAttribute", true)]
    [InlineData("FlagsAttribute", false)]
    public void MarkerAttributesRequirePlatformIdentity(
        string attributeTypeName,
        bool trustedAssembly)
    {
        bool isFlags = attributeTypeName == "FlagsAttribute";
        using var stream = new MemoryStream(
            BuildImage(
                attributeTypeName,
                trustedAssembly: trustedAssembly,
                markerConstructor: true,
                attributeNamespace:
                    isFlags
                        ? "System"
                        : "System.Text.Json.Serialization",
                assemblyName:
                    isFlags ? "System.Runtime" : "System.Text.Json"),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        bool found = attributeTypeName switch
        {
            "JsonIncludeAttribute" =>
                AttributeReader.HasJsonIncludeAttribute(
                    reader,
                    type.GetCustomAttributes()),
            "JsonIgnoreAttribute" =>
                AttributeReader.HasJsonIgnoreAttribute(
                    reader,
                    type.GetCustomAttributes()),
            _ => AttributeReader.HasFlagsAttribute(
                reader,
                type.GetCustomAttributes()),
        };

        Assert.Equal(trustedAssembly, found);
    }

    static byte[] BuildImage(
        string attributeTypeName = "JsonPropertyNameAttribute",
        bool duplicateValidRows = false,
        bool trustedAssembly = true,
        bool markerConstructor = false,
        string attributeNamespace =
            "System.Text.Json.Serialization",
        string assemblyName = "System.Text.Json")
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
            metadata.GetOrAddString(assemblyName),
            new Version(10, 0, 0, 0),
            default,
            trustedAssembly
                ? metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xcc, 0x7b, 0x13, 0xff,
                        0xcd, 0x2d, 0xdd, 0x51,
                    })
                : default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            systemTextJson,
            metadata.GetOrAddString(attributeNamespace),
            metadata.GetOrAddString(attributeTypeName));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            markerConstructor ? 0 : 1,
            returnType => returnType.Void(),
            parameters =>
            {
                if (!markerConstructor)
                    parameters.AddParameter().Type().String();
            });
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
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        if (markerConstructor)
        {
            value.WriteUInt16(0);
        }
        else
        {
            value.WriteSerializedString("ok");
            value.WriteUInt16(
                duplicateValidRows ? (ushort)0 : (ushort)1);
            if (!duplicateValidRows)
            {
                value.WriteByte(0x54);
                value.WriteByte(0x0E);
                value.WriteSerializedString("Bogus");
                value.WriteSerializedString("x");
            }
        }
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        metadata.AddCustomAttribute(
            target,
            constructor,
            valueHandle);
        if (duplicateValidRows)
            metadata.AddCustomAttribute(target, constructor, valueHandle);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
