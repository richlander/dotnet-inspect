using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates <see cref="CustomAttributeValueGuard"/> independently of surface
/// extraction: SRM would allocate builders from declared counts before any
/// provider callback, so the guard must refuse those blobs and still accept
/// legal constructor and named-argument layouts.
/// </summary>
public sealed class CustomAttributeValueGuardTests
{
    [Fact]
    public void HugeArrayCount_IsUnsafe()
    {
        using var image = Open(
            BuildArrayCountImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
    }

    [Fact]
    public void HugeNamedArgumentCount_IsUnsafe()
    {
        using var image = Open(
            BuildNamedArgumentCountImage(namedArgumentCount: 65_535));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
    }

    [Fact]
    public void LegalStringArgument_IsSafe()
    {
        using var image = Open(BuildStringArgumentImage("ok"));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void LegalInt32Array_IsSafe()
    {
        using var image = Open(BuildInt32ArrayImage([1, 2, 3]));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        Assert.Single(decoded.Value.FixedArguments);
        var values = Assert.IsAssignableFrom<ImmutableArray<CustomAttributeTypedArgument<string>>>(
            decoded.Value.FixedArguments[0].Value);
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void BoxedNestingAtLimit_IsSafe()
    {
        using var image = Open(
            BuildBoxedNestingImage(CustomAttributeValueGuard.MaxSerializedDepth - 1));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
    }

    [Fact]
    public void BoxedNestingJustOverLimit_IsUnsafe()
    {
        using var image = Open(
            BuildBoxedNestingImage(CustomAttributeValueGuard.MaxSerializedDepth));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
    }

    [Fact]
    public void DeclaredArrayCount_IsChargedBeforeRefusal()
    {
        using var image = Open(
            BuildArrayCountImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            100_000_000 * CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
    }

    [Fact]
    public void HugeNamedArgumentArrayCount_IsUnsafe()
    {
        using var image = Open(
            BuildNamedArrayCountImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            100_000_000 * CustomAttributeValueGuard.DeclaredSlotCharge
                + CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void LegalNamedInt32Array_IsSafe()
    {
        using var image = Open(BuildNamedInt32ArrayImage([1, 2, 3]));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void NamedArrayNestingAtLimit_IsSafe()
    {
        using var image = Open(
            BuildNamedNestedArrayImage(NamedArrayNestingAtLimit));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
    }

    [Fact]
    public void NamedArrayNestingJustOverLimit_IsUnsafe()
    {
        using var image = Open(
            BuildNamedNestedArrayImage(NamedArrayNestingAtLimit + 1));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
    }

    [Fact]
    public void TypeRefEnumMatchingLocalInt64_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildTypeRefEnumDesyncImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            100_000_000 * CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
    }

    [Fact]
    public void OverDeepEnumFieldModifiers_UseInt32WidthAndSeeFollowingArrayCount()
    {
        using var image = Open(
            BuildEnumCmodDesyncImage(
                modifierCount: SignatureBlobGuard.DefaultMaxDepth + 1,
                elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            100_000_000 * CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
    }

    [Fact]
    public void TypeRefInt32EnumWithoutLocalMatch_IsSafe()
    {
        using var image = Open(BuildTypeRefInt32EnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void LocalInt64EnumFixedArgument_IsSafe()
    {
        using var image = Open(BuildLocalInt64EnumImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
        Assert.NotNull(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void AssemblyQualifiedNamedEnum_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildAssemblyQualifiedNamedEnumImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            (2 + 100_000_000) * CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void ClassSystemStringFixedArgument_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildClassSystemStringImage(elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            100_000_000 * CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
    }

    [Fact]
    public void GenericAttributeTypeParameterInt32_IsSafe()
    {
        using var image = Open(BuildGenericAttributeInt32Image());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
        var decoded = AttributeDecoder.TryDecode(image.Reader, attribute);
        Assert.NotNull(decoded);
        Assert.Single(decoded.Value.FixedArguments);
        Assert.Equal(5, decoded.Value.FixedArguments[0].Value);
    }

    [Fact]
    public void FnPtrEarlierGenericArgumentThenArray_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildGenericEarlierThenArrayImage(
                earlier: EarlierGenericArg.FnPtr,
                elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            100_000_000 * CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void PtrFnPtrEarlierGenericArgumentThenArray_SeesFollowingArrayCount()
    {
        using var image = Open(
            BuildGenericEarlierThenArrayImage(
                earlier: EarlierGenericArg.PtrFnPtr,
                elementCount: 100_000_000));
        CustomAttribute attribute = FirstAttribute(image.Reader);
        int charged = 0;
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                image.Reader,
                attribute,
                count => charged = checked(charged + count)));
        Assert.Equal(
            100_000_000 * CustomAttributeValueGuard.DeclaredSlotCharge,
            charged);
    }

    [Fact]
    public void SelfReferentialGenericVar_IsUnsafe()
    {
        using var image = Open(BuildSelfReferentialGenericVarImage());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(image.Reader, attribute));
        Assert.Null(AttributeDecoder.TryDecode(image.Reader, attribute));
    }

    [Fact]
    public void ObserverFailureDuringNamedEnumLookup_EscapesTryDecode()
    {
        using var image = Open(BuildNamedEnumInt32Image());
        CustomAttribute attribute = FirstAttribute(image.Reader);
        var thrown = Assert.Throws<InvalidOperationException>(
            () => AttributeDecoder.TryDecode(
                image.Reader,
                attribute,
                _ => throw new InvalidOperationException("budget")));
        Assert.Equal("budget", thrown.Message);
    }

    // Named SZARRAY-of-boxed starts the first serialized nest at depth 2;
    // each 0x1d/0x51 pair then advances depth by 2.
    const int NamedArrayNestingAtLimit =
        (CustomAttributeValueGuard.MaxSerializedDepth - 2) / 2;

    static LoadedImage Open(byte[] image) => new(image);

    static CustomAttribute FirstAttribute(MetadataReader reader)
    {
        foreach (var handle in reader.CustomAttributes)
            return reader.GetCustomAttribute(handle);
        throw new InvalidOperationException("The image has no custom attributes.");
    }

    static byte[] BuildArrayCountImage(int elementCount)
    {
        var metadata = CreateMetadata("ArrayCount");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().SZArray().Int32(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedArgumentCountImage(int namedArgumentCount)
    {
        var metadata = CreateMetadata("NamedCount");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16((ushort)namedArgumentCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildStringArgumentImage(string text)
    {
        var metadata = CreateMetadata("StringArg");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().String(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(text);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildBoxedNestingImage(int depth)
    {
        var metadata = CreateMetadata("BoxedNest");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Object(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        for (int index = 0; index < depth; index++)
            value.WriteByte(0x51);
        value.WriteByte(0x08);
        value.WriteInt32(1);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildInt32ArrayImage(int[] elements)
    {
        var metadata = CreateMetadata("IntArray");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().SZArray().Int32(),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elements.Length);
        foreach (int element in elements)
            value.WriteInt32(element);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedArrayCountImage(int elementCount)
    {
        var metadata = CreateMetadata("NamedArrayCount");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedInt32ArrayImage(int[] elements)
    {
        var metadata = CreateMetadata("NamedIntArray");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x54);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("Values");
        value.WriteInt32(elements.Length);
        foreach (int element in elements)
            value.WriteInt32(element);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildNamedNestedArrayImage(int depth)
    {
        var metadata = CreateMetadata("NamedNest");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x51);
        value.WriteSerializedString("V");
        value.WriteInt32(1);
        for (int index = 0; index < depth; index++)
        {
            value.WriteByte(0x1d);
            value.WriteByte(0x51);
            value.WriteInt32(1);
        }

        value.WriteByte(0x08);
        value.WriteInt32(7);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildTypeRefEnumDesyncImage(int elementCount)
    {
        var metadata = CreateMetadata("EnumDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumRef = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"));
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumRef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt64(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildEnumCmodDesyncImage(int modifierCount, int elementCount)
    {
        var metadata = CreateMetadata("EnumCmod");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        int coded = (MetadataTokens.GetRowNumber(systemEnum) << 2) | 0x01;
        for (int index = 0; index < modifierCount; index++)
        {
            fieldSignature.WriteByte(0x20);
            fieldSignature.WriteCompressedInteger(coded);
        }

        fieldSignature.WriteByte(0x0a);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildTypeRefInt32EnumImage()
    {
        var metadata = CreateMetadata("TypeRefEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumRef = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("AttributeTargets"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters => parameters.AddParameter().Type().Type(enumRef, isValueType: true),
            parameterCount: 1);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(1);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildAssemblyQualifiedNamedEnumImage(int elementCount)
    {
        var metadata = CreateMetadata("EnumSuffix");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.E, Other");
        value.WriteSerializedString("F");
        value.WriteInt64(0);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildClassSystemStringImage(int elementCount)
    {
        var metadata = CreateMetadata("ClassString");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemString = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("String"));
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            parameters =>
            {
                parameters.AddParameter().Type().Type(systemString, isValueType: false);
                parameters.AddParameter().Type().SZArray().Int32();
            },
            parameterCount: 2);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    enum EarlierGenericArg
    {
        FnPtr,
        PtrFnPtr,
    }

    static byte[] BuildGenericEarlierThenArrayImage(
        EarlierGenericArg earlier,
        int elementCount)
    {
        var metadata = CreateMetadata("FnPtrDesync");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(2);
        WriteEarlierGenericArg(typeSpecSignature, earlier);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static void WriteEarlierGenericArg(
        BlobBuilder signature,
        EarlierGenericArg earlier)
    {
        if (earlier == EarlierGenericArg.PtrFnPtr)
            signature.WriteByte(0x0f);
        signature.WriteByte(0x1b);
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
    }

    static byte[] BuildSelfReferentialGenericVarImage()
    {
        var metadata = CreateMetadata("VarSo");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(1);
        typeSpecSignature.WriteByte(0x13);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(0);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildGenericAttributeInt32Image()
    {
        var metadata = CreateMetadata("GenericAttr");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(1);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(0);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(5);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static void WriteTypeDefOrRef(BlobBuilder signature, EntityHandle handle)
    {
        int tag = handle.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(handle)),
        };
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(handle) << 2 | tag);
    }

    static byte[] BuildNamedEnumInt32Image()
    {
        var metadata = CreateMetadata("NamedEnum");
        MemberReferenceHandle constructor = AddConstructor(
            metadata,
            _ => { },
            parameterCount: 0);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.Missing");
        value.WriteSerializedString("F");
        value.WriteInt32(0);
        AddAttributedType(metadata, constructor, value);
        return Serialize(metadata);
    }

    static byte[] BuildLocalInt64EnumImage()
    {
        var metadata = CreateMetadata("LocalEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(enumDef, isValueType: true));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt64(7);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static MemberReferenceHandle AddConstructor(
        MetadataBuilder metadata,
        Action<ParametersEncoder> parameters,
        int parameterCount)
    {
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
                parameterCount,
                returnType => returnType.Void(),
                parameters);
        return metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
    }

    static void AddAttributedType(
        MetadataBuilder metadata,
        MemberReferenceHandle constructor,
        BlobBuilder value)
    {
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
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
    }

    sealed class LoadedImage(byte[] image) : IDisposable
    {
        readonly PEReader _peReader = new(
            new MemoryStream(image, writable: false));

        public MetadataReader Reader => _peReader.GetMetadataReader();

        public void Dispose() => _peReader.Dispose();
    }

    static MetadataBuilder CreateMetadata(string assemblyName)
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
