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

    [Fact]
    public void UnexpectedConstructorIsUnsupported()
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildImageCore(
                (metadata, target, constructor) =>
                {
                    var value = new BlobBuilder();
                    value.WriteUInt16(1);
                    value.WriteInt32(0);
                    value.WriteUInt16(0);
                    metadata.AddCustomAttribute(
                        target,
                        constructor,
                        metadata.GetOrAddBlob(value));
                },
                constructorParameterCount: 1));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedRowPairedWithValidRowIsUnsupportedRegardlessOfOrder(
        bool malformedFirst)
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildImageWithMalformedAndValidRows(malformedFirst));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
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

    [Theory]
    [InlineData("IgnoreReadOnlyFields")]
    [InlineData("IgnoreReadOnlyProperties")]
    [InlineData("IncludeFields")]
    [InlineData("UseStringEnumConverter")]
    public void EnabledWireShapingBooleanOptionIsUnsupported(string name)
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => BooleanValue(metadata, name, value: true)));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    [Fact]
    public void DefaultWireShapingOptionRemainsSupported()
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => BooleanValue(
                    metadata,
                    "IncludeFields",
                    value: false)));

        Assert.Equal(JsonWireNamingPolicy.None, policy);
    }

    [Fact]
    public void FormattingOptionRemainsSupported()
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => BooleanValue(
                    metadata,
                    "WriteIndented",
                    value: true)));

        Assert.Equal(JsonWireNamingPolicy.None, policy);
    }

    [Fact]
    public void ByteBackedReadCommentHandlingIsDecoded()
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => ByteEnumValue(
                    metadata,
                    "ReadCommentHandling",
                    "System.Text.Json.JsonCommentHandling",
                    value: 1)));

        Assert.Equal(JsonWireNamingPolicy.None, policy);
    }

    [Fact]
    public void UnsignedEnumIdentityIsUnsupported()
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => ByteEnumValue(
                    metadata,
                    "ReadCommentHandling",
                    "System.Text.Json.JsonCommentHandling",
                    value: 1,
                    trustedAssembly: false)));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    [Theory]
    [InlineData(
        "DefaultIgnoreCondition",
        "System.Text.Json.Serialization.JsonIgnoreCondition")]
    [InlineData(
        "DictionaryKeyPolicy",
        "System.Text.Json.Serialization.JsonKnownNamingPolicy")]
    [InlineData(
        "NumberHandling",
        "System.Text.Json.Serialization.JsonNumberHandling")]
    [InlineData(
        "ReferenceHandler",
        "System.Text.Json.Serialization.JsonKnownReferenceHandler")]
    public void NonDefaultWireShapingEnumOptionIsUnsupported(
        string name,
        string type)
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => EnumValue(
                    metadata,
                    name,
                    type,
                    value: 1)));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    [Theory]
    [InlineData("Converters")]
    [InlineData("TypeClassifiers")]
    public void CustomWireShapingTypeListIsUnsupported(string name)
    {
        JsonWireNamingPolicy? policy = ReadPolicy(
            BuildSingleRow(
                metadata => EmptyTypeArrayValue(metadata, name)));

        Assert.Equal(JsonWireNamingPolicy.Unsupported, policy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNameOptionsAttributeFromUntrustedAssemblyIsIgnored(
        bool malformed)
    {
        byte[] image = BuildSingleRow(
            metadata => malformed
                ? metadata.GetOrAddBlob(new byte[] { 0 })
                : PolicyValue(metadata, 1),
            trustedAssembly: false);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        bool found =
            AttributeReader.TryGetJsonSourceGenerationPropertyNamingPolicy(
                reader,
                type.GetCustomAttributes(),
                out _);

        Assert.False(found);
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

    static byte[] BuildImageWithMalformedAndValidRows(bool malformedFirst)
        => BuildImageCore(
            (metadata, target, constructor) =>
            {
                foreach (bool malformed in malformedFirst
                    ? new[] { true, false }
                    : new[] { false, true })
                {
                    metadata.AddCustomAttribute(
                        target,
                        constructor,
                        malformed
                            ? metadata.GetOrAddBlob(new byte[] { 0 })
                            : PolicyValue(metadata, 1));
                }
            });

    static byte[] BuildSingleRow(
        Func<MetadataBuilder, BlobHandle> valueFactory,
        bool trustedAssembly = true)
        => BuildImageCore(
            (metadata, target, constructor) =>
                metadata.AddCustomAttribute(
                    target,
                    constructor,
                    valueFactory(metadata)),
            trustedAssembly);

    static byte[] BuildImageCore(
        Action<MetadataBuilder, TypeDefinitionHandle, MemberReferenceHandle>
            addAttributes,
        bool trustedAssembly = true,
        int constructorParameterCount = 0)
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
            metadata.GetOrAddString("System.Text.Json.Serialization"),
            metadata.GetOrAddString(
                "JsonSourceGenerationOptionsAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            constructorParameterCount,
            returnType => returnType.Void(),
            parameters =>
            {
                for (int index = 0; index < constructorParameterCount; index++)
                    parameters.AddParameter().Type().Int32();
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
                        + SystemTextJsonAssemblyIdentity);
            }
            blob.WriteSerializedString(name);
            blob.WriteInt32(value);
        }
        return metadata.GetOrAddBlob(blob);
    }

    static BlobHandle BooleanValue(
        MetadataBuilder metadata,
        string name,
        bool value)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteUInt16(1);
        blob.WriteByte(0x54);
        blob.WriteByte(0x02);
        blob.WriteSerializedString(name);
        blob.WriteByte(value ? (byte)1 : (byte)0);
        return metadata.GetOrAddBlob(blob);
    }

    static BlobHandle EnumValue(
        MetadataBuilder metadata,
        string name,
        string type,
        int value)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteUInt16(1);
        blob.WriteByte(0x54);
        blob.WriteByte(0x55);
        blob.WriteSerializedString(type + ", " + SystemTextJsonAssemblyIdentity);
        blob.WriteSerializedString(name);
        blob.WriteInt32(value);
        return metadata.GetOrAddBlob(blob);
    }

    static BlobHandle ByteEnumValue(
        MetadataBuilder metadata,
        string name,
        string type,
        byte value,
        bool trustedAssembly = true)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteUInt16(1);
        blob.WriteByte(0x54);
        blob.WriteByte(0x55);
        blob.WriteSerializedString(
            type + ", "
                + (trustedAssembly
                    ? SystemTextJsonAssemblyIdentity
                    : "System.Text.Json"));
        blob.WriteSerializedString(name);
        blob.WriteByte(value);
        return metadata.GetOrAddBlob(blob);
    }

    static BlobHandle EmptyTypeArrayValue(
        MetadataBuilder metadata,
        string name)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteUInt16(1);
        blob.WriteByte(0x54);
        blob.WriteByte(0x1D);
        blob.WriteByte(0x50);
        blob.WriteSerializedString(name);
        blob.WriteUInt32(0);
        return metadata.GetOrAddBlob(blob);
    }

    const string SystemTextJsonAssemblyIdentity =
        "System.Text.Json, Version=10.0.0.0, Culture=neutral, "
        + "PublicKeyToken=cc7b13ffcd2ddd51";
}
