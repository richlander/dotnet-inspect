using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class TypeReferenceOriginTests
{
    [Fact]
    public void AssemblyReference_RetainsFullIdentityAndStructuredName()
    {
        using MetadataReaderProvider provider = BuildMetadata(
            metadata =>
            {
                AssemblyReferenceHandle assembly =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("Target"),
                        new Version(2, 3, 4, 5),
                        metadata.GetOrAddString("neutral"),
                        metadata.GetOrAddBlob(
                            ImmutableArray.Create<byte>(1, 2, 3, 4)),
                        AssemblyFlags.PublicKey,
                        hashValue: default);
                return metadata.AddTypeReference(
                    assembly,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Outer`1+Inner"));
            });
        MetadataReader reader = provider.GetMetadataReader();
        TypeReferenceHandle handle =
            MetadataTokens.TypeReferenceHandle(1);

        TypeRef decoded = TypeRefDecoder.Instance.GetTypeFromReference(
            reader,
            handle,
            0);

        Assert.NotNull(decoded.Resolution);
        var origin = Assert.IsType<
            TypeReferenceOrigin.AssemblyReference>(
                decoded.Resolution.Origin);
        Assert.Equal(
            AssemblyReferenceIdentity.From(
                reader,
                MetadataTokens.AssemblyReferenceHandle(1)),
            origin.Assembly);
        Assert.Equal(
            ["Outer`1", "Inner"],
            decoded.Resolution.Type.Segments);
    }

    [Fact]
    public void Provenance_DoesNotChangeShapeEqualityHashOrDisplay()
    {
        using MetadataReaderProvider provider = BuildMetadata(
            metadata =>
            {
                AssemblyReferenceHandle assembly =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("Owner"),
                        new Version(1, 0, 0, 0),
                        culture: default,
                        publicKeyOrToken: default,
                        flags: default,
                        hashValue: default);
                metadata.AddTypeReference(
                    assembly,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Type"));
                return metadata.AddTypeReference(
                    default,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Type"));
            });
        MetadataReader reader = provider.GetMetadataReader();

        TypeRef assemblyScoped =
            TypeRefDecoder.Instance.GetTypeFromReference(
                reader,
                MetadataTokens.TypeReferenceHandle(1),
                0);
        TypeRef currentAssembly =
            TypeRefDecoder.Instance.GetTypeFromReference(
                reader,
                MetadataTokens.TypeReferenceHandle(2),
                0);

        Assert.NotNull(assemblyScoped.Resolution);
        Assert.NotNull(currentAssembly.Resolution);
        Assert.IsType<TypeReferenceOrigin.AssemblyReference>(
            assemblyScoped.Resolution.Origin);
        Assert.IsType<TypeReferenceOrigin.CurrentAssembly>(
            currentAssembly.Resolution.Origin);
        Assert.Equal(assemblyScoped, currentAssembly);
        Assert.Equal(
            assemblyScoped.GetHashCode(),
            currentAssembly.GetHashCode());
        Assert.Equal(
            assemblyScoped.ToDisplayString(),
            currentAssembly.ToDisplayString());
    }

    [Fact]
    public void PrimitiveAndModuleScopes_RemainDistinct()
    {
        TypeRef primitive =
            TypeRefDecoder.Instance.GetPrimitiveType(
                PrimitiveTypeCode.Int32);
        Assert.NotNull(primitive.Resolution);
        Assert.IsType<TypeReferenceOrigin.IntrinsicCoreLibrary>(
            primitive.Resolution.Origin);

        using MetadataReaderProvider provider = BuildMetadata(
            metadata =>
            {
                ModuleReferenceHandle module = metadata.AddModuleReference(
                    metadata.GetOrAddString("Part.netmodule"));
                return metadata.AddTypeReference(
                    module,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Type"));
            });
        TypeRef moduleScoped =
            TypeRefDecoder.Instance.GetTypeFromReference(
                provider.GetMetadataReader(),
                MetadataTokens.TypeReferenceHandle(1),
                0);

        Assert.NotNull(moduleScoped.Resolution);
        Assert.Equal(
            "Part.netmodule",
            Assert.IsType<TypeReferenceOrigin.ModuleReference>(
                moduleScoped.Resolution.Origin).ModuleName);
    }

    static MetadataReaderProvider BuildMetadata(
        Func<MetadataBuilder, TypeReferenceHandle> addTypeReference)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("Owner.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Owner"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        addTypeReference(metadata);

        var root = new MetadataRootBuilder(
            metadata,
            suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(
            image,
            methodBodyStreamRva: 0,
            mappedFieldDataStreamRva: 0);
        return MetadataReaderProvider.FromMetadataImage(
            ImmutableArray.Create(image.ToArray()));
    }
}
