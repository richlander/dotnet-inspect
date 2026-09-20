using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Fixtures;

public static class FunctionPointerConventionReturnOverloadFixture
{
    public const string TypeName =
        "FunctionPointerConventionReturnSample";

    public static byte[] Build(bool returnOne)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(
                "FunctionPointerConventionReturnOverloadFixture.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "FunctionPointerConventionReturnOverloadFixture"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);

        AssemblyName coreLibrary = typeof(object).Assembly.GetName();
        AssemblyReferenceHandle coreReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(coreLibrary.Name!),
                coreLibrary.Version!,
                culture: default,
                publicKeyOrToken: metadata.GetOrAddBlob(
                    coreLibrary.GetPublicKeyToken() ?? []),
                flags: default,
                hashValue: default);
        metadata.AddTypeReference(
            coreReference,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CallConvCdecl"));
        metadata.AddTypeReference(
            coreReference,
            metadata.GetOrAddString(
                "System.Runtime.CompilerServices"),
            metadata.GetOrAddString(
                "CallConvSuppressGCTransition"));
        TypeReferenceHandle objectType =
            metadata.AddTypeReference(
                coreReference,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int changedBody = AddFunctionPointerBody(
            bodyEncoder,
            returnOne ? 1 : 0);
        MethodDefinitionHandle firstChanged =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Changed"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        0x00, 0x01, 0x1B, 0x09, 0x00,
                        0x20, 0x05, 0x08, 0x02,
                    }),
                changedBody,
                MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Changed"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x00, 0x01, 0x1B, 0x09, 0x00,
                    0x20, 0x05, 0x20, 0x09, 0x08, 0x02,
                }),
            changedBody,
            MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: firstChanged);
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString(TypeName),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            firstChanged);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static int AddFunctionPointerBody(
        MethodBodyStreamEncoder bodies,
        int value)
    {
        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(code);
        instructions.LoadConstantI4(value);
        instructions.OpCode(ILOpCode.Conv_u);
        instructions.OpCode(ILOpCode.Ret);
        return bodies.AddMethodBody(instructions, maxStack: 1);
    }
}
