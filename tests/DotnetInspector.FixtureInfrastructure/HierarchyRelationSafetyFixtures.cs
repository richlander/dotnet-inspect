using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Fixtures;

public static class HierarchyRelationSafetyFixtures
{
    public static byte[] BuildCyclicNestedTypeVisibility(
        int nestedTypeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            nestedTypeCount);

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("CyclicHierarchy.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("CyclicHierarchy"),
            new Version(1, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("ITarget"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < nestedTypeCount; index++)
        {
            TypeDefinitionHandle cyclic =
                metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic,
                    metadata.GetOrAddString("Sample"),
                    metadata.GetOrAddString($"Cyclic{index}"),
                    target,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(cyclic, cyclic);
        }

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }

    public static byte[] BuildMalformedGenericTypeSpecification()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MalformedHierarchy.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedHierarchy"),
            new Version(1, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle source = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Source"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeReferenceHandle unrelated = metadata.AddTypeReference(
            MetadataTokens.EntityHandle(0x00000001),
            metadata.GetOrAddString("Sample"),
            metadata.GetOrAddString("Unrelated`1"));
        var signature = new BlobBuilder();
        signature.WriteByte(
            (byte)SignatureTypeCode.GenericTypeInstance);
        signature.WriteByte((byte)SignatureTypeCode.Int32);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(unrelated) << 2) | 1);
        TypeSpecificationHandle malformed =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        metadata.AddInterfaceImplementation(source, malformed);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }
}
