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
