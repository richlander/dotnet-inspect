using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Queries.Tests;

internal static class StructuralCloneMalformedMetadataFixtures
{
    internal static byte[] BuildMalformedTypeNameAssembly(
        int malformedTypes)
    {
        MetadataBuilder metadata = CreateMetadata(
            "MalformedTypeNames",
            new Guid("7A0B1C2D-3E4F-5061-7283-94A5B6C7D8E9"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < malformedTypes; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Broken"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddSyntheticMethod(metadata, encoder, "Seed");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        byte[] bytes = image.ToArray();
        CorruptTypeDefinitionNames(bytes, malformedTypes);
        return bytes;
    }

    static void CorruptTypeDefinitionNames(
        byte[] image,
        int malformedTypes)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        int tableOffset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.TypeDef);
        int rowSize = reader.GetTableRowSize(TableIndex.TypeDef);
        int stringIndexSize =
            reader.GetHeapSize(HeapIndex.String)
                <= ushort.MaxValue
                ? sizeof(ushort)
                : sizeof(uint);

        for (int index = 0; index < malformedTypes; index++)
        {
            int nameOffset =
                tableOffset
                + ((index + 1) * rowSize)
                + sizeof(uint);
            if (stringIndexSize == sizeof(ushort))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    image.AsSpan(nameOffset, sizeof(ushort)),
                    ushort.MaxValue);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    image.AsSpan(nameOffset, sizeof(uint)),
                    uint.MaxValue);
            }
        }
    }

    static MetadataBuilder CreateMetadata(
        string assemblyName,
        Guid moduleVersionId)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(moduleVersionId),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        return metadata;
    }

    static void AddSyntheticMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodies,
        string name)
    {
        var code = new BlobBuilder();
        code.WriteByte(0x2A);
        int body = bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 0);
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            body,
            MetadataTokens.ParameterHandle(1));
    }
}
