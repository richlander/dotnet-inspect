using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class JsonSerializableAttributeTests
{
    const string ProbeAssemblyIdentity =
        "Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

    [Fact]
    public void ReadJsonSerializableRoots_ParsesAssemblyQualifiedNestedGenerics()
    {
        string left = Qualified("Samples.Left");
        string right = Qualified("Samples.Right");
        string boxedRight = Generic("Samples.Box`1", right);
        string nested = Generic(
            "Samples.Outer`1+Inner`1",
            left,
            boxedRight);

        ApiTypeShape root = Assert.IsType<ApiTypeShape>(
            ReadRoot(nested).Type);

        Assert.Equal(ApiTypeShapeKind.GenericInstance, root.Kind);
        Assert.Equal(
            ["Outer`1", "Inner`1"],
            root.Definition?.DefinitionName?.Segments);
        Assert.Equal(2, root.TypeArguments.Length);
        Assert.Equal(ApiTypeShapeKind.Named, root.TypeArguments[0].Kind);
        Assert.Equal(ApiTypeShapeKind.GenericInstance, root.TypeArguments[1].Kind);
        Assert.Equal(
            ["Box`1"],
            root.TypeArguments[1].Definition?.DefinitionName?.Segments);
        Assert.Equal(
            ["Right"],
            root.TypeArguments[1].TypeArguments[0].Definition?
                .DefinitionName?.Segments);
    }

    [Fact]
    public void ReadJsonSerializableRoots_RejectsMalformedGenericDelimitersAndArity()
    {
        string left = Qualified("Samples.Left");
        string right = Qualified("Samples.Right");
        const string definition = "Samples.Pair`2";
        string leadingDelimiter =
            $"{definition}[,[{left}],[{right}]], {ProbeAssemblyIdentity}";
        string doubledDelimiter =
            $"{definition}[[{left}],,[{right}]], {ProbeAssemblyIdentity}";
        string trailingDelimiter =
            $"{definition}[[{left}],[{right}],], {ProbeAssemblyIdentity}";
        string tooFewArguments =
            Generic("Samples.Pair`2", left);
        string tooManyArguments =
            Generic("Samples.Single`1", left, right);

        foreach (string serializedTypeName in new[]
        {
            leadingDelimiter,
            doubledDelimiter,
            trailingDelimiter,
            tooFewArguments,
            tooManyArguments,
        })
        {
            ApiJsonSerializableRoot root = ReadRoot(serializedTypeName);
            Assert.Null(root.Type);
            Assert.Equal(
                "serializer root type shape is unsupported",
                root.UnsupportedReason);
        }
    }

    [Fact]
    public void ReadJsonSerializableRoots_DoesNotAliasBogusPrimitiveAssembly()
    {
        ApiTypeShape root = Assert.IsType<ApiTypeShape>(
            ReadRoot(
                "System.Int32, Bogus, Version=1.0.0.0, Culture=neutral, "
                + "PublicKeyToken=cc7b13ffcd2ddd51").Type);

        Assert.Equal(ApiTypeShapeKind.Named, root.Kind);
        Assert.Equal("Bogus", root.Definition?.Assembly.Name);
        Assert.Equal(
            ["Int32"],
            root.Definition?.DefinitionName?.Segments);
    }

    [Fact]
    public void ReadJsonSerializableRoots_RetainsFullyMalformedAuthenticRow()
    {
        using var stream = new MemoryStream(
            BuildImage(serializedTypeName: null),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition context = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<ApiJsonSerializableRoot> roots =
            AttributeReader.ReadJsonSerializableRoots(
                reader,
                context.GetCustomAttributes(),
                new ApiAssemblyIdentity(
                    "Probe",
                    new Version(1, 0, 0, 0),
                    culture: null,
                    publicKeyToken: null),
                out int attributeCount);

        Assert.Equal(1, attributeCount);
        ApiJsonSerializableRoot root = Assert.Single(roots);
        Assert.Null(root.Type);
        Assert.Null(root.TypeInfoPropertyName);
        Assert.Equal(
            "JsonSerializable metadata is malformed or unsupported",
            root.UnsupportedReason);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReadJsonSerializableRoots_RequiresExactSystemTypeIdentity(
        bool fromSystemTextJson,
        bool nestedSystemType)
    {
        using var stream = new MemoryStream(
            BuildImage(
                Qualified("Samples.Value"),
                systemTypeFromSystemTextJson: fromSystemTextJson,
                nestedSystemType: nestedSystemType),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition context = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<ApiJsonSerializableRoot> roots =
            AttributeReader.ReadJsonSerializableRoots(
                reader,
                context.GetCustomAttributes(),
                new ApiAssemblyIdentity(
                    "Probe",
                    new Version(1, 0, 0, 0),
                    culture: null,
                    publicKeyToken: null),
                out int attributeCount);

        Assert.Equal(1, attributeCount);
        ApiJsonSerializableRoot root = Assert.Single(roots);
        Assert.Null(root.Type);
        Assert.Equal(
            "JsonSerializable metadata is malformed or unsupported",
            root.UnsupportedReason);
    }

    static string Qualified(string typeName) =>
        $"{typeName}, {ProbeAssemblyIdentity}";

    static string Generic(string definition, params string[] arguments) =>
        $"{definition}[{string.Join(
            ",",
            arguments.Select(argument => $"[{argument}]"))}], "
            + ProbeAssemblyIdentity;

    static ApiJsonSerializableRoot ReadRoot(string serializedTypeName)
    {
        using var stream = new MemoryStream(
            BuildImage(serializedTypeName),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition context = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<ApiJsonSerializableRoot> roots =
            AttributeReader.ReadJsonSerializableRoots(
                reader,
                context.GetCustomAttributes(),
                new ApiAssemblyIdentity(
                    "Probe",
                    new Version(1, 0, 0, 0),
                    culture: null,
                    publicKeyToken: null),
                out int attributeCount);
        Assert.Equal(1, attributeCount);
        return Assert.Single(roots);
    }

    static byte[] BuildImage(
        string? serializedTypeName,
        bool systemTypeFromSystemTextJson = false,
        bool nestedSystemType = false)
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
        AssemblyReferenceHandle systemTextJson =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Text.Json"),
                new Version(10, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xcc, 0x7b, 0x13, 0xff,
                        0xcd, 0x2d, 0xdd, 0x51,
                    }),
                default,
                default);
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(10, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0xb0, 0x3f, 0x5f, 0x7f,
                        0x11, 0xd5, 0x0a, 0x3a,
                    }),
                default,
                default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            systemTextJson,
            metadata.GetOrAddString("System.Text.Json.Serialization"),
            metadata.GetOrAddString("JsonSerializableAttribute"));
        EntityHandle systemTypeScope = systemTypeFromSystemTextJson
            ? systemTextJson
            : systemRuntime;
        TypeReferenceHandle systemType;
        if (nestedSystemType)
        {
            TypeReferenceHandle systemContainer =
                metadata.AddTypeReference(
                    systemTypeScope,
                    default,
                    metadata.GetOrAddString("System"));
            systemType = metadata.AddTypeReference(
                systemContainer,
                default,
                metadata.GetOrAddString("Type"));
        }
        else
        {
            systemType = metadata.AddTypeReference(
                systemTypeScope,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Type"));
        }
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            1,
            returnType => returnType.Void(),
            parameters =>
                parameters.AddParameter().Type().Type(
                    systemType,
                    isValueType: false));
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
        TypeDefinitionHandle context = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Context"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        if (serializedTypeName is not null)
        {
            value.WriteSerializedString(serializedTypeName);
            value.WriteUInt16(0);
        }
        metadata.AddCustomAttribute(
            context,
            constructor,
            metadata.GetOrAddBlob(value));

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
