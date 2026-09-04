using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

internal static class ArrayKindSignatureFixture
{
    public const string TypeName = "ArrayKinds";
    public const int MethodCount = 16;

    public static byte[] BuildImage()
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(8, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemCollections =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Collections"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle list = metadata.AddTypeReference(
            systemCollections,
            metadata.GetOrAddString("System.Collections.Generic"),
            metadata.GetOrAddString("List`1"));
        TypeReferenceHandle valueTuple = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ValueTuple`2"));
        TypeReferenceHandle isVolatile = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("IsVolatile"));

        byte[] md1 = MdArray(Int32, rank: 1);
        byte[] sz = Sz(Int32);
        byte[] md2 = MdArray(Int32, rank: 2);
        byte[] modifiedVector = Sz(RequiredModifier(isVolatile, Int32));
        byte[] nested = GenericInstance(
            isValueType: false,
            list,
            md1);
        byte[] tuple = GenericInstance(
            isValueType: true,
            valueTuple,
            md1,
            sz);

        return BuildImage(
            [
                new("Vector", sz, Void, IsGeneric: false),
                new("Md1", md1, Void, IsGeneric: false),
                new("Md1Twin", md1, Void, IsGeneric: false),
                new("Md2", md2, Void, IsGeneric: false),
                new("Nested", nested, Void, IsGeneric: false),
                new("Pointer", Pointer(md1), Void, IsGeneric: false),
                new("ByRef", ByRef(md1), Void, IsGeneric: false),
                new("Tuple", tuple, Void, IsGeneric: false),
                new("Generic", MdArray(MethodGeneric0, rank: 1), Void, IsGeneric: true),
                new("ModifiedVector", Sz(modifiedVector), Void, IsGeneric: false),
                new("ModifiedMd1", MdArray(modifiedVector, rank: 1), Void, IsGeneric: false),
                new("ReturnVector", null, sz, IsGeneric: false),
                new("ReturnVectorTwin", null, sz, IsGeneric: false),
                new("ReturnMd1", null, md1, IsGeneric: false),
                new("ReturnMd1Twin", null, md1, IsGeneric: false),
                new("ReturnMd2", null, md2, IsGeneric: false),
            ],
            metadata);
    }

    public static byte[] BuildSingleVectorParameterImage() =>
        BuildImage(
            [
                new("M", Sz(Int32), Void, IsGeneric: false),
            ]);

    public static byte[] BuildSingleRankOneNonSzParameterImage() =>
        BuildImage(
            [
                new("M", MdArray(Int32, rank: 1), Void, IsGeneric: false),
            ]);

    public static byte[] BuildProjectionFlowImage(string specimen)
    {
        var metadata = CreateMetadata();
        byte[] signatureType = specimen switch
        {
            "Vector" => Sz(Int32),
            "RankOneNonSz" => MdArray(Int32, rank: 1),
            "RankTwo" => MdArray(Int32, rank: 2),
            "NestedRankOneNonSz" => GenericInstance(
                isValueType: false,
                AddListReference(metadata),
                MdArray(Int32, rank: 1)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(specimen),
                specimen,
                "Unknown array-kind projection specimen."),
        };

        return BuildImage(
            [
                new("M", signatureType, signatureType, IsGeneric: false),
            ],
            metadata);
    }

    static TypeReferenceHandle AddListReference(MetadataBuilder metadata)
    {
        AssemblyReferenceHandle systemCollections =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Collections"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        return metadata.AddTypeReference(
            systemCollections,
            metadata.GetOrAddString("System.Collections.Generic"),
            metadata.GetOrAddString("List`1"));
    }

    static byte[] BuildImage(
        IReadOnlyList<MethodSpec> methods,
        MetadataBuilder? metadata = null)
    {
        metadata ??= CreateMetadata();
        var methodHandles = new List<MethodDefinitionHandle>(methods.Count);
        int parameterRow = 1;
        foreach (MethodSpec method in methods)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(method.IsGeneric ? (byte)0x30 : (byte)0x20);
            if (method.IsGeneric)
                signature.WriteCompressedInteger(1);
            signature.WriteCompressedInteger(
                method.ParameterType is null ? 0 : 1);
            signature.WriteBytes(method.ReturnType);
            if (method.ParameterType is not null)
                signature.WriteBytes(method.ParameterType);

            MethodDefinitionHandle methodHandle =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual
                        | MethodAttributes.HideBySig
                        | MethodAttributes.NewSlot,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(method.Name),
                    metadata.GetOrAddBlob(signature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(parameterRow));
            methodHandles.Add(methodHandle);
            if (method.ParameterType is not null)
            {
                metadata.AddParameter(
                    ParameterAttributes.None,
                    metadata.GetOrAddString("value"),
                    sequenceNumber: 1);
                parameterRow++;
            }
            if (method.IsGeneric)
            {
                metadata.AddGenericParameter(
                    methodHandle,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
            }
        }

        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(TypeName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            methodHandles[0]);

        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    static MetadataBuilder CreateMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddAssembly(
            metadata.GetOrAddString("ArrayKinds"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("ArrayKinds.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] GenericInstance(
        bool isValueType,
        TypeReferenceHandle definition,
        params byte[][] arguments)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(isValueType ? (byte)0x11 : (byte)0x12);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(definition) << 2) | 1);
        signature.WriteCompressedInteger(arguments.Length);
        foreach (byte[] argument in arguments)
            signature.WriteBytes(argument);
        return signature.ToArray();
    }

    static byte[] RequiredModifier(
        TypeReferenceHandle modifier,
        byte[] inner)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x1f);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(modifier) << 2) | 1);
        signature.WriteBytes(inner);
        return signature.ToArray();
    }

    static byte[] MdArray(byte[] elementType, int rank)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x14);
        signature.WriteBytes(elementType);
        signature.WriteCompressedInteger(rank);
        signature.WriteCompressedInteger(0);
        signature.WriteCompressedInteger(0);
        return signature.ToArray();
    }

    static byte[] Sz(byte[] elementType) => [0x1d, .. elementType];

    static byte[] Pointer(byte[] elementType) => [0x0f, .. elementType];

    static byte[] ByRef(byte[] elementType) => [0x10, .. elementType];

    static readonly byte[] Void = [0x01];
    static readonly byte[] Int32 = [0x08];
    static readonly byte[] MethodGeneric0 = [0x1e, 0x00];

    sealed record MethodSpec(
        string Name,
        byte[]? ParameterType,
        byte[] ReturnType,
        bool IsGeneric);
}
