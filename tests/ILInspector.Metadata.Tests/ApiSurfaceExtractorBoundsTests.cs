using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates the bounded API-surface extraction: the bound is a hard retention budget the walk
/// enforces on itself, not a total a caller checks after the fact.
/// </summary>
/// <remarks>
/// The two claims that matter are that a bound is reachable — an image over budget is reported as
/// <see cref="ApiSurfaceExtractionResult.Exceeded"/> and yields no surface at all — and that a
/// retained count is exact for an ordinary surface: a budget equal to the unbounded walk's own
/// total extracts the whole surface, and one less stops it. Hostile-shape tests separately gate
/// the conservative pre-materialization work bound used to stop allocation amplification.
/// </remarks>
public sealed class ApiSurfaceExtractorBoundsTests
{
    static readonly string SelfPath = typeof(ApiSurfaceExtractorBoundsTests).Assembly.Location;

    [Fact]
    public void GenerousBounds_ExtractTheSameSurfaceAsTheUnboundedWalk()
    {
        ApiSurface unbounded = Unbounded();
        ApiSurface bounded = Extracted(
            new ApiSurfaceExtractionBounds(
                int.MaxValue,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue));

        Assert.Equal(
            unbounded.Types.Select(type => (type.FullName, type.Members.Count)),
            bounded.Types.Select(type => (type.FullName, type.Members.Count)));
        Assert.Equal(unbounded.TypeForwarders.Count, bounded.TypeForwarders.Count);
        Assert.Equal(
            unbounded.InspectionFailures.Count,
            bounded.InspectionFailures.Count);
    }

    [Fact]
    public void BoundsEqualToTheSurfaceSize_ExtractItWhole()
    {
        ApiSurface unbounded = Unbounded();
        int types = unbounded.Types.Count;
        int members = unbounded.Types.Sum(type => type.Members.Count);
        int inspectionFailures = unbounded.InspectionFailures.Count;
        int typeForwarders = unbounded.TypeForwarders.Count;
        Assert.True(types > 0);
        Assert.True(members > 0);

        ApiSurface exact = Extracted(
            new ApiSurfaceExtractionBounds(
                types,
                members,
                inspectionFailures,
                typeForwarders,
                int.MaxValue));

        Assert.Equal(types, exact.Types.Count);
        Assert.Equal(members, exact.Types.Sum(type => type.Members.Count));
    }

    [Fact]
    public void OneTypeShortOfTheSurfaceSize_IsAbandonedAtTheTypeBound()
    {
        ApiSurface unbounded = Unbounded();
        int members = unbounded.Types.Sum(type => type.Members.Count);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    unbounded.Types.Count - 1,
                    members,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.Types, exceeded.Bound);
    }

    [Fact]
    public void OneMemberShortOfTheSurfaceSize_IsAbandonedAtTheMemberBound()
    {
        ApiSurface unbounded = Unbounded();
        int members = unbounded.Types.Sum(type => type.Members.Count);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    unbounded.Types.Count,
                    members - 1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.Members, exceeded.Bound);
    }

    // An exhausted budget is a legal input: a caller spending one budget across several images
    // hands the next image nothing, and must get a refusal rather than an argument failure.
    [Fact]
    public void AnExhaustedTypeBudget_RefusesBeforeWalkingMembers()
    {
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    0,
                    0,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.Types, exceeded.Bound);
    }

    [Fact]
    public void NegativeBounds_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, 0, 0, -1));
    }

    [Fact]
    public void TypesOnlyExtraction_SpendsNoMemberBudget()
    {
        ApiSurfaceExtractionResult result = Extract(
            new ApiSurfaceExtractionBounds(
                int.MaxValue,
                0,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue),
            typesOnly: true);

        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
    }

    [Fact]
    public void OneTypeForwarderShortOfTheSurfaceSize_IsAbandoned()
    {
        ApiSurface unbounded = Unbounded();
        Assert.True(unbounded.TypeForwarders.Count > 0);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    unbounded.TypeForwarders.Count - 1,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.TypeForwarders, exceeded.Bound);
    }

    [Fact]
    public void MetadataRowBudget_IsExactAndStopsBeforeExtraction()
    {
        var generous = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        Assert.True(generous.MetadataRows > 0);

        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.MetadataRows)));
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.MetadataRows - 1)));

        Assert.Equal(ApiSurfaceExtractionBound.MetadataRows, exceeded.Bound);
    }

    [Fact]
    public void RetainedTextBudget_IsExact()
    {
        var generous = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        Assert.True(generous.RetainedTextCharacters > 0);

        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.RetainedTextCharacters)));
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.RetainedTextCharacters - 1)));

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
    }

    [Fact]
    public void RepeatedLongMemberName_StopsBeforeLargeAllocationAmplification()
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 10_000,
            nameLength: 4_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 10,
                maxMembers: 10_000,
                maxInspectionFailures: 10,
                maxTypeForwarders: 10,
                maxMetadataRows: 50_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void OneWideSignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideSignatureImage(parameterCount: 10_000, nameLength: 4_000));
    }

    [Fact]
    public void OneInterfaceHeavyType_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildInterfaceFloodImage(interfaceCount: 10_000, nameLength: 4_000));
    }

    [Fact]
    public void OneWideFieldSignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideTypeSpecImage(WideTypeSpecUse.Field, argumentCount: 1_000, nameLength: 10_000));
    }

    [Theory]
    [InlineData(WideTypeSpecUse.BaseType)]
    [InlineData(WideTypeSpecUse.Event)]
    [InlineData(WideTypeSpecUse.Interface)]
    [InlineData(WideTypeSpecUse.GenericConstraint)]
    public void OneWideTypeSpec_StopsBeforeLargeAllocationAmplification(
        WideTypeSpecUse use)
    {
        AssertTextAmplificationIsBounded(
            BuildWideTypeSpecImage(use, argumentCount: 1_000, nameLength: 10_000));
    }

    [Fact]
    public void OneLargeCustomAttribute_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildLargeAttributeImage(valueLength: 4_000_000));
    }

    static ApiSurface Unbounded()
    {
        using var stream = File.OpenRead(SelfPath);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, ApiSurfaceExtractionScope.Public);
    }

    static ApiSurface Extracted(ApiSurfaceExtractionBounds bounds)
        => Assert.IsType<ApiSurfaceExtractionResult.Extracted>(Extract(bounds)).Surface;

    static ApiSurfaceExtractionResult Extract(
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly = false)
    {
        using var stream = File.OpenRead(SelfPath);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            bounds,
            typesOnly);
    }

    static void AssertTextAmplificationIsBounded(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    static byte[] BuildRepeatedLongMethodNameImage(
        int methodCount,
        int nameLength)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Amplification.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Amplification"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Amplifier"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        StringHandle name =
            metadata.GetOrAddString(new string('M', nameLength));
        for (int index = 0; index < methodCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                name,
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildWideSignatureImage(int parameterCount, int nameLength)
    {
        var metadata = Metadata("Wide");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle parameterType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('P', nameLength)));
        AddModuleAndPublicType(metadata, "Wide");
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            parameterCount,
            returnType => returnType.Void(),
            parameters =>
            {
                for (int index = 0; index < parameterCount; index++)
                    parameters.AddParameter().Type().Type(parameterType, isValueType: false);
            });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Wide"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildInterfaceFloodImage(int interfaceCount, int nameLength)
    {
        var metadata = Metadata("Interfaces");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle interfaceType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('I', nameLength)));
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            "Interfaces",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        for (int index = 0; index < interfaceCount; index++)
            metadata.AddInterfaceImplementation(type, interfaceType);
        return Serialize(metadata);
    }

    static byte[] BuildWideTypeSpecImage(
        WideTypeSpecUse use,
        int argumentCount,
        int nameLength)
    {
        var metadata = Metadata($"Wide{use}");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle genericType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"Generic`{argumentCount}"));
        TypeReferenceHandle argumentType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('A', nameLength)));
        var typeSpecSignature = new BlobBuilder();
        WriteWideGenericType(
            typeSpecSignature,
            genericType,
            argumentType,
            argumentCount);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Wide"),
            use == WideTypeSpecUse.BaseType ? typeSpec : default(EntityHandle),
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        switch (use)
        {
            case WideTypeSpecUse.Field:
                var fieldSignature = new BlobBuilder();
                fieldSignature.WriteByte(0x06);
                WriteWideGenericType(
                    fieldSignature,
                    genericType,
                    argumentType,
                    argumentCount);
                metadata.AddFieldDefinition(
                    FieldAttributes.Public | FieldAttributes.Static,
                    metadata.GetOrAddString("Value"),
                    metadata.GetOrAddBlob(fieldSignature));
                break;
            case WideTypeSpecUse.Interface:
                metadata.AddInterfaceImplementation(type, typeSpec);
                break;
            case WideTypeSpecUse.Event:
                var accessorSignature = new BlobBuilder();
                new BlobEncoder(accessorSignature).MethodSignature().Parameters(
                    0,
                    returnType => returnType.Void(),
                    _ => { });
                MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("add_Changed"),
                    metadata.GetOrAddBlob(accessorSignature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(1));
                EventDefinitionHandle @event = metadata.AddEvent(
                    EventAttributes.None,
                    metadata.GetOrAddString("Changed"),
                    typeSpec);
                metadata.AddEventMap(type, @event);
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Adder,
                    accessor);
                break;
            case WideTypeSpecUse.GenericConstraint:
                GenericParameterHandle parameter = metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
                metadata.AddGenericParameterConstraint(parameter, typeSpec);
                break;
        }

        return Serialize(metadata);
    }

    static void WriteWideGenericType(
        BlobBuilder signature,
        TypeReferenceHandle genericType,
        TypeReferenceHandle argumentType,
        int argumentCount)
    {
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericType) << 2 | 1);
        signature.WriteCompressedInteger(argumentCount);
        int argumentCode = MetadataTokens.GetRowNumber(argumentType) << 2 | 1;
        for (int index = 0; index < argumentCount; index++)
        {
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(argumentCode);
        }
    }

    static byte[] BuildLargeAttributeImage(int valueLength)
    {
        var metadata = Metadata("LargeAttribute");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Attributed");
        var value = new BlobBuilder(valueLength + 16);
        value.WriteUInt16(1);
        value.WriteCompressedInteger(valueLength);
        for (int index = 0; index < valueLength; index++)
            value.WriteByte((byte)'"');
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    public enum WideTypeSpecUse
    {
        Field,
        BaseType,
        Event,
        Interface,
        GenericConstraint,
    }

    static MetadataBuilder Metadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        return metadata;
    }

    static TypeDefinitionHandle AddModuleAndPublicType(
        MetadataBuilder metadata,
        string name,
        TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Abstract)
    {
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata.AddTypeDefinition(
            attributes,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(name),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
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
