using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class JsonPropertyNameAttributeTests
{
    [Theory]
    [InlineData(nameof(JsonIgnoreConditionProbe.WhenWriting))]
    [InlineData(nameof(JsonIgnoreConditionProbe.WhenReading))]
    public void CurrentDirectionalJsonIgnoreConditionsArePreserved(
        string propertyName)
    {
        using FileStream stream = File.OpenRead(
            typeof(JsonIgnoreConditionProbe).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType probe = Assert.Single(
            surface.Types,
            type => type.Name == nameof(JsonIgnoreConditionProbe));
        ApiMember property = Assert.Single(
            probe.Members,
            member => member.Name == propertyName);

        Assert.True(property.HasJsonIgnore);
        Assert.False(property.HasJsonIgnoreNever);
    }

    /// <summary>
    /// Pins <see cref="JsonWireIgnoreCondition"/> to System.Text.Json's own
    /// <c>JsonIgnoreCondition</c>. The decoded value is that enum's underlying
    /// constant, so a renumbering there would silently rename every condition
    /// this repository reports.
    /// </summary>
    [Fact]
    public void JsonIgnoreConditionValuesMatchSystemTextJson()
    {
        Assert.Equal(
            (int)JsonIgnoreCondition.Never,
            (int)JsonWireIgnoreCondition.Never);
        Assert.Equal(
            (int)JsonIgnoreCondition.Always,
            (int)JsonWireIgnoreCondition.Always);
        Assert.Equal(
            (int)JsonIgnoreCondition.WhenWritingDefault,
            (int)JsonWireIgnoreCondition.WhenWritingDefault);
        Assert.Equal(
            (int)JsonIgnoreCondition.WhenWritingNull,
            (int)JsonWireIgnoreCondition.WhenWritingNull);
        Assert.Equal(
            (int)JsonIgnoreCondition.WhenWriting,
            (int)JsonWireIgnoreCondition.WhenWriting);
        Assert.Equal(
            (int)JsonIgnoreCondition.WhenReading,
            (int)JsonWireIgnoreCondition.WhenReading);
        Assert.Equal(
            Enum.GetValues<JsonIgnoreCondition>().Length,
            Enum.GetValues<JsonWireIgnoreCondition>().Length);
    }

    [Theory]
    [InlineData(
        nameof(JsonIgnoreConditionProbe.WhenWriting),
        JsonWireIgnoreCondition.WhenWriting)]
    [InlineData(
        nameof(JsonIgnoreConditionProbe.WhenReading),
        JsonWireIgnoreCondition.WhenReading)]
    [InlineData(
        nameof(JsonIgnoreConditionProbe.Bare),
        JsonWireIgnoreCondition.Always)]
    [InlineData(
        nameof(JsonIgnoreConditionProbe.Kept),
        JsonWireIgnoreCondition.Never)]
    [InlineData(
        nameof(JsonIgnoreConditionProbe.WhenWritingDefault),
        JsonWireIgnoreCondition.WhenWritingDefault)]
    [InlineData(
        nameof(JsonIgnoreConditionProbe.WhenWritingNull),
        JsonWireIgnoreCondition.WhenWritingNull)]
    public void DirectionalJsonIgnoreConditionsAreDecodedFromCompiledMetadata(
        string propertyName,
        JsonWireIgnoreCondition expected)
    {
        using FileStream stream = File.OpenRead(
            typeof(JsonIgnoreConditionProbe).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType probe = Assert.Single(
            surface.Types,
            type => type.Name == nameof(JsonIgnoreConditionProbe));
        ApiMember property = Assert.Single(
            probe.Members,
            member => member.Name == propertyName);

        Assert.Equal([expected], property.JsonIgnoreConditions);
        Assert.True(property.HasJsonIgnore);
        Assert.Equal(
            expected == JsonWireIgnoreCondition.Never,
            property.HasJsonIgnoreNever);
    }

    [Fact]
    public void UnattributedMemberReportsNoJsonIgnoreEvidence()
    {
        using FileStream stream = File.OpenRead(
            typeof(JsonIgnoreConditionProbe).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType probe = Assert.Single(
            surface.Types,
            type => type.Name == nameof(JsonIgnoreConditionProbe));
        ApiMember property = Assert.Single(
            probe.Members,
            member => member.Name == nameof(JsonIgnoreConditionProbe.Plain));

        Assert.Empty(property.JsonIgnoreConditions);
        Assert.False(property.HasJsonIgnore);
        Assert.False(property.HasJsonIgnoreNever);
        Assert.False(property.HasMalformedJsonInclude);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedAuthenticJsonIgnoreIsUnsupportedEvidence(
        bool outOfRangeCondition)
    {
        using var stream = new MemoryStream(
            BuildImage(
                "JsonIgnoreAttribute",
                markerConstructor: outOfRangeCondition,
                ignoreCondition: outOfRangeCondition ? 9 : null),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<JsonWireIgnoreCondition?> conditions =
            AttributeReader.ReadJsonIgnoreConditions(
                reader,
                type.GetCustomAttributes());

        Assert.Equal([null], conditions);
        Assert.True(
            AttributeReader.HasJsonIgnoreAttribute(
                reader,
                type.GetCustomAttributes()));
        Assert.False(
            AttributeReader.HasJsonIgnoreNeverAttribute(
                reader,
                type.GetCustomAttributes()));
    }

    [Fact]
    public void AuthenticJsonIgnoreConditionIsDecodedFromSyntheticMetadata()
    {
        using var stream = new MemoryStream(
            BuildImage(
                "JsonIgnoreAttribute",
                markerConstructor: true,
                ignoreCondition: (int)JsonWireIgnoreCondition.WhenReading),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        Assert.Equal(
            [JsonWireIgnoreCondition.WhenReading],
            AttributeReader.ReadJsonIgnoreConditions(
                reader,
                type.GetCustomAttributes()));
    }

    [Fact]
    public void JsonIgnoreConditionFromUntrustedEnumAssemblyIsMalformed()
    {
        using var stream = new MemoryStream(
            BuildImage(
                "JsonIgnoreAttribute",
                markerConstructor: true,
                ignoreCondition:
                    (int)JsonWireIgnoreCondition.WhenReading,
                ignoreConditionTypeName:
                    "System.Text.Json.Serialization.JsonIgnoreCondition, "
                    + "Bogus, Version=10.0.0.0, Culture=neutral, "
                    + "PublicKeyToken=cc7b13ffcd2ddd51"),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        Assert.Equal(
            [null],
            AttributeReader.ReadJsonIgnoreConditions(
                reader,
                type.GetCustomAttributes()));
    }

    [Fact]
    public void UntrustedJsonIgnoreAttributeIsIgnoredRatherThanMalformed()
    {
        using var stream = new MemoryStream(
            BuildImage("JsonIgnoreAttribute", trustedAssembly: false),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        Assert.Empty(
            AttributeReader.ReadJsonIgnoreConditions(
                reader,
                type.GetCustomAttributes()));
    }

    [Fact]
    public void MalformedAuthenticJsonIncludeIsUnsupportedEvidence()
    {
        using var stream = new MemoryStream(
            BuildImage("JsonIncludeAttribute"),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        JsonIncludeAttributeEvidence evidence =
            AttributeReader.ReadJsonIncludeAttributes(
                reader,
                type.GetCustomAttributes());

        Assert.Equal(
            new JsonIncludeAttributeEvidence(0, HasMalformedRow: true),
            evidence);
        Assert.False(
            AttributeReader.HasJsonIncludeAttribute(
                reader,
                type.GetCustomAttributes()));
    }

    [Fact]
    public void WellFormedJsonIncludeIsSupportedEvidence()
    {
        using var stream = new MemoryStream(
            BuildImage("JsonIncludeAttribute", markerConstructor: true),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        Assert.Equal(
            new JsonIncludeAttributeEvidence(1, HasMalformedRow: false),
            AttributeReader.ReadJsonIncludeAttributes(
                reader,
                type.GetCustomAttributes()));
    }

    [Fact]
    public void UntrustedJsonIncludeAttributeIsIgnoredRatherThanMalformed()
    {
        using var stream = new MemoryStream(
            BuildImage("JsonIncludeAttribute", trustedAssembly: false),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        Assert.Equal(
            new JsonIncludeAttributeEvidence(0, HasMalformedRow: false),
            AttributeReader.ReadJsonIncludeAttributes(
                reader,
                type.GetCustomAttributes()));
    }

    /// <summary>
    /// A nested TypeRef chain whose flattened spelling equals a genuine
    /// framework attribute, resolving through the authentic signed AssemblyRef,
    /// must not authenticate: identity is compared structurally, so the
    /// namespace and root-to-leaf segments differ even though the display text
    /// does not.
    /// </summary>
    [Theory]
    [InlineData("JsonIgnoreAttribute")]
    [InlineData("JsonIncludeAttribute")]
    [InlineData("JsonPropertyNameAttribute")]
    public void NestedAttributeIdentityCannotAliasTopLevelFrameworkAttribute(
        string attributeTypeName)
    {
        using var nested = new MemoryStream(
            BuildImage(
                attributeTypeName,
                markerConstructor:
                    attributeTypeName != "JsonPropertyNameAttribute",
                nestedAttributeType: true),
            writable: false);
        using var nestedReader = new PEReader(nested);
        MetadataReader reader = nestedReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        // Non-vacuity: the flattened display spelling really does alias the
        // genuine framework attribute, so only structured identity separates
        // them. If this stops being true the impostor is no longer a threat and
        // the assertions below would pass for the wrong reason.
        Assert.True(
            AttributeReader.HasAttribute(
                reader,
                type.GetCustomAttributes(),
                $"System.Text.Json.Serialization.{attributeTypeName}"));

        Assert.Empty(
            AttributeReader.ReadJsonIgnoreConditions(
                reader,
                type.GetCustomAttributes()));
        Assert.Equal(
            new JsonIncludeAttributeEvidence(0, HasMalformedRow: false),
            AttributeReader.ReadJsonIncludeAttributes(
                reader,
                type.GetCustomAttributes()));
        Assert.Empty(
            AttributeReader.ReadJsonPropertyNames(
                reader,
                type.GetCustomAttributes()));
    }

    [Theory]
    [InlineData("JsonIgnoreAttribute")]
    [InlineData("JsonIncludeAttribute")]
    [InlineData("JsonPropertyNameAttribute")]
    public void TopLevelAttributeIdentityStillAuthenticates(
        string attributeTypeName)
    {
        bool marker = attributeTypeName != "JsonPropertyNameAttribute";
        using var stream = new MemoryStream(
            BuildImage(attributeTypeName, markerConstructor: marker),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        switch (attributeTypeName)
        {
            case "JsonIgnoreAttribute":
                Assert.Equal(
                    [JsonWireIgnoreCondition.Always],
                    AttributeReader.ReadJsonIgnoreConditions(
                        reader,
                        type.GetCustomAttributes()));
                break;
            case "JsonIncludeAttribute":
                Assert.Equal(
                    new JsonIncludeAttributeEvidence(
                        1,
                        HasMalformedRow: false),
                    AttributeReader.ReadJsonIncludeAttributes(
                        reader,
                        type.GetCustomAttributes()));
                break;
            default:
                Assert.Equal(
                    [null],
                    AttributeReader.ReadJsonPropertyNames(
                        reader,
                        type.GetCustomAttributes()));
                break;
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NestedIdentityCannotAliasTopLevelJsExportAttribute(
        bool nestedAttributeType,
        bool expected)
    {
        const string jsNamespace =
            "System.Runtime.InteropServices.JavaScript";
        using var stream = new MemoryStream(
            BuildImage(
                "JSExportAttribute",
                markerConstructor: true,
                attributeNamespace: jsNamespace,
                assemblyName: jsNamespace,
                nestedAttributeType: nestedAttributeType),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        Assert.True(
            AttributeReader.HasAttribute(
                reader,
                type.GetCustomAttributes(),
                $"{jsNamespace}.JSExportAttribute"));
        Assert.Equal(
            expected,
            AttributeReader.HasRuntimeJsExportAttribute(
                reader,
                type.GetCustomAttributes()));
    }

    [Fact]
    public void LocallyDefinedFrameworkNamedAttributeInModuleIsUnauthenticated()
    {
        using var stream = new MemoryStream(
            BuildModuleWithLocalFlagsAttribute(),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition target = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(3));

        Assert.False(reader.IsAssembly);
        Assert.False(
            AttributeReader.HasFlagsAttribute(
                reader,
                target.GetCustomAttributes()));
    }

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
    [InlineData("JsonPropertyNameAttribute")]
    [InlineData("JsonStringEnumMemberNameAttribute")]
    public void AuthenticAttributeWithMalformedConstructorProducesRowMarker(
        string attributeTypeName)
    {
        using var stream = new MemoryStream(
            BuildImage(
                attributeTypeName,
                malformedStringConstructor: true),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        List<string?> values =
            attributeTypeName == "JsonPropertyNameAttribute"
                ? AttributeReader.ReadJsonPropertyNames(
                    reader,
                    type.GetCustomAttributes())
                : AttributeReader.ReadJsonStringEnumMemberNames(
                    reader,
                    type.GetCustomAttributes());

        Assert.Equal([null], values);
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

    [Fact]
    public void MalformedAuthenticFlagsIsUnsupportedEvidence()
    {
        using var stream = new MemoryStream(
            BuildImage(
                "FlagsAttribute",
                markerConstructor: true,
                malformedMarkerValue: true,
                attributeNamespace: "System",
                assemblyName: "System.Runtime"),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        FlagsAttributeEvidence evidence = AttributeReader.ReadFlagsAttributes(
            reader,
            type.GetCustomAttributes());

        Assert.Equal(0, evidence.Count);
        Assert.True(evidence.HasMalformedRow);
    }

    [Fact]
    public void DuplicateAuthenticFlagsRowsAreCounted()
    {
        using var stream = new MemoryStream(
            BuildImage(
                "FlagsAttribute",
                duplicateValidRows: true,
                markerConstructor: true,
                attributeNamespace: "System",
                assemblyName: "System.Runtime"),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        FlagsAttributeEvidence evidence = AttributeReader.ReadFlagsAttributes(
            reader,
            type.GetCustomAttributes());

        Assert.Equal(2, evidence.Count);
        Assert.False(evidence.HasMalformedRow);
    }

    [Fact]
    public void UntrustedFlagsAttributeIsIgnoredRatherThanMalformed()
    {
        using var stream = new MemoryStream(
            BuildImage(
                "FlagsAttribute",
                markerConstructor: true,
                malformedMarkerValue: true,
                trustedAssembly: false,
                attributeNamespace: "System",
                assemblyName: "System.Runtime"),
            writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition type = reader.GetTypeDefinition(
            MetadataTokens.TypeDefinitionHandle(2));

        FlagsAttributeEvidence evidence = AttributeReader.ReadFlagsAttributes(
            reader,
            type.GetCustomAttributes());

        Assert.Equal(0, evidence.Count);
        Assert.False(evidence.HasMalformedRow);
    }

    internal static byte[] BuildImage(
        string attributeTypeName = "JsonPropertyNameAttribute",
        bool duplicateValidRows = false,
        bool trustedAssembly = true,
        bool markerConstructor = false,
        bool malformedMarkerValue = false,
        bool malformedStringConstructor = false,
        string attributeNamespace =
            "System.Text.Json.Serialization",
        string assemblyName = "System.Text.Json",
        bool nestedAttributeType = false,
        int? ignoreCondition = null,
        string? ignoreConditionTypeName = null)
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
        TypeReferenceHandle attributeType;
        if (nestedAttributeType)
        {
            // A nested chain whose flattened spelling is indistinguishable from
            // the genuine top-level attribute, and whose resolution scope still
            // terminates at the authentic framework AssemblyRef.
            int lastDot = attributeNamespace.LastIndexOf('.');
            TypeReferenceHandle outer = metadata.AddTypeReference(
                systemTextJson,
                metadata.GetOrAddString(
                    lastDot < 0 ? "" : attributeNamespace[..lastDot]),
                metadata.GetOrAddString(
                    lastDot < 0
                        ? attributeNamespace
                        : attributeNamespace[(lastDot + 1)..]));
            attributeType = metadata.AddTypeReference(
                outer,
                default,
                metadata.GetOrAddString(attributeTypeName));
        }
        else
        {
            attributeType = metadata.AddTypeReference(
                systemTextJson,
                metadata.GetOrAddString(attributeNamespace),
                metadata.GetOrAddString(attributeTypeName));
        }
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            markerConstructor || malformedStringConstructor ? 0 : 1,
            returnType => returnType.Void(),
            parameters =>
            {
                if (!markerConstructor && !malformedStringConstructor)
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
        if (malformedMarkerValue)
        {
            value.WriteByte(0);
        }
        else
        {
            value.WriteUInt16(1);
            if (markerConstructor)
            {
                if (ignoreCondition is { } condition)
                {
                    value.WriteUInt16(1);
                    value.WriteByte(0x54);
                    value.WriteByte(0x55);
                    value.WriteSerializedString(
                        ignoreConditionTypeName
                        ?? "System.Text.Json.Serialization.JsonIgnoreCondition, "
                            + "System.Text.Json, Version=10.0.0.0, "
                            + "Culture=neutral, "
                            + "PublicKeyToken=cc7b13ffcd2ddd51");
                    value.WriteSerializedString("Condition");
                    value.WriteInt32(condition);
                }
                else
                {
                    value.WriteUInt16(0);
                }
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

    static byte[] BuildModuleWithLocalFlagsAttribute()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        MethodDefinitionHandle constructor =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(constructorSignature),
                bodyOffset: -1,
                parameterList:
                    MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            constructor);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("FlagsAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            constructor);
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddCustomAttribute(
            target,
            constructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 }));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}

public sealed class JsonIgnoreConditionProbe
{
    public string Plain { get; set; } = "";

    [JsonIgnore]
    public string Bare { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Kept { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string WhenWritingDefault { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WhenWritingNull { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string WhenWriting { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public string WhenReading { get; set; } = "";
}
