using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

// Malformed metadata can encode a TypeReference resolution scope or a nested-type chain that
// points back at itself. The canonical-IL name climbs would then recurse until an *uncatchable*
// StackOverflowException takes down the process. These tests craft those cycles and assert the
// climb terminates (degrading to the leaf name) instead of overflowing.
//
// WARNING: if the climb-depth guard in CanonicalIL is removed, these tests do not fail — they
// StackOverflow and take down the whole test runner.
public class CanonicalILRecursionTests
{
    [Fact]
    public void SelfReferentialResolutionScope_DoesNotStackOverflow()
    {
        var reader = BuildMetadata(metadata =>
            metadata.AddTypeReference(
                // ResolutionScope points at this very TypeReference row (token 1) -> a cycle.
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop")));

        string name = CanonicalIL.QualifiedName(reader, MetadataTokens.TypeReferenceHandle(1));

        // Reaching this assertion at all proves the climb terminated instead of overflowing.
        Assert.EndsWith("N.Loop", name, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfNestedTypeDefinition_DoesNotStackOverflow()
    {
        TypeDefinitionHandle typeHandle = default;
        var reader = BuildMetadata(metadata =>
        {
            typeHandle = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            // Declares the type as nested inside itself -> GetDeclaringType() returns it -> a cycle.
            metadata.AddNestedType(typeHandle, typeHandle);
            return default(EntityHandle);
        });

        string name = CanonicalIL.QualifiedName(reader, typeHandle);

        Assert.EndsWith("Loop", name, StringComparison.Ordinal);
    }

    static MetadataReader BuildMetadata(Func<MetadataBuilder, EntityHandle> addMalformedRow)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        // The module (<Module>) pseudo-type must be row 1; the malformed rows come after it.
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        addMalformedRow(metadata);

        var rootBuilder = new MetadataRootBuilder(metadata, suppressValidation: true);
        var image = new BlobBuilder();
        rootBuilder.Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        var provider = MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray()));
        return provider.GetMetadataReader();
    }
}
