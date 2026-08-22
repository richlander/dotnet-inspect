using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public sealed class ReturnToSenderTypePlannerTests
{
    static readonly MethodInfo s_findAccessibleInstanceConstructor =
        typeof(CompileBackSourceComposer)
            .GetMethod(
                "FindAccessibleInstanceConstructor",
                BindingFlags.NonPublic
                | BindingFlags.Static)!
        ?? throw new InvalidOperationException(
            "Could not find FindAccessibleInstanceConstructor.");

    public enum ConstructorShape
    {
        Valid,
        MissingSpecialName,
        MissingRtSpecialName,
        VarArgCallingConvention,
        ExplicitThis,
        GenericArity,
        NonVoidReturn,
        Static,
    }

    [Theory]
    [InlineData(ConstructorShape.MissingSpecialName)]
    [InlineData(ConstructorShape.MissingRtSpecialName)]
    [InlineData(ConstructorShape.VarArgCallingConvention)]
    [InlineData(ConstructorShape.ExplicitThis)]
    [InlineData(ConstructorShape.GenericArity)]
    [InlineData(ConstructorShape.NonVoidReturn)]
    [InlineData(ConstructorShape.Static)]
    public void FindAccessibleInstanceConstructor_RejectsMalformedConstructorShapes(
        ConstructorShape shape)
    {
        using var stream = new MemoryStream(
            BuildConstructorLookupImage(shape));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var baseHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name)
            == "Base");
        var derivedHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name)
            == "Derived");

        Assert.Null(
            FindAccessibleInstanceConstructor(
                reader,
                baseHandle,
                derivedHandle,
                []));
    }

    [Fact]
    public void FindAccessibleInstanceConstructor_ReturnsValidConstructor()
    {
        using var stream = new MemoryStream(
            BuildConstructorLookupImage(
                ConstructorShape.Valid));
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var baseHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name)
            == "Base");
        var derivedHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name)
            == "Derived");

        object? result = FindAccessibleInstanceConstructor(
            reader,
            baseHandle,
            derivedHandle,
            []);

        var handle = Assert.IsType<MethodDefinitionHandle>(result);
        Assert.Equal(
            ".ctor",
            reader.GetString(
                reader.GetMethodDefinition(handle).Name));
    }

    static object? FindAccessibleInstanceConstructor(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        TypeDefinitionHandle derivedTypeHandle,
        IReadOnlyList<string> parameterTypes)
        => s_findAccessibleInstanceConstructor.Invoke(
            null,
            [reader, declaringTypeHandle, derivedTypeHandle, parameterTypes]);

    static byte[] BuildConstructorLookupImage(
        ConstructorShape shape)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("ConstructorLookup.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ConstructorLookup"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString("Base"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Derived"),
            baseType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.HideBySig;
        if (shape != ConstructorShape.MissingSpecialName)
            attributes |= MethodAttributes.SpecialName;
        if (shape != ConstructorShape.MissingRtSpecialName)
            attributes |= MethodAttributes.RTSpecialName;
        if (shape == ConstructorShape.Static)
            attributes |= MethodAttributes.Static;

        MethodDefinitionHandle constructor =
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(".ctor"),
                BuildConstructorSignature(
                    metadata,
                    shape),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
        if (shape == ConstructorShape.GenericArity)
        {
            metadata.AddGenericParameter(
                constructor,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        }

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

    static BlobHandle BuildConstructorSignature(
        MetadataBuilder metadata,
        ConstructorShape shape)
    {
        var signature = new BlobBuilder();
        switch (shape)
        {
            case ConstructorShape.VarArgCallingConvention:
                signature.WriteByte(0x25);
                signature.WriteCompressedInteger(0);
                signature.WriteByte(0x01);
                break;
            case ConstructorShape.ExplicitThis:
                signature.WriteByte(0x60);
                signature.WriteCompressedInteger(0);
                signature.WriteByte(0x01);
                break;
            case ConstructorShape.GenericArity:
                signature.WriteByte(0x30);
                signature.WriteCompressedInteger(1);
                signature.WriteCompressedInteger(0);
                signature.WriteByte(0x01);
                break;
            case ConstructorShape.NonVoidReturn:
                signature.WriteByte(0x20);
                signature.WriteCompressedInteger(0);
                signature.WriteByte(0x08);
                break;
            default:
                signature.WriteByte(0x20);
                signature.WriteCompressedInteger(0);
                signature.WriteByte(0x01);
                break;
        }

        return metadata.GetOrAddBlob(signature);
    }
}
