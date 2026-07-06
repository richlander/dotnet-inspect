using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis.Tests;

// Malformed metadata can encode a type-reference resolution scope or a nested-type chain
// that points back at itself. Without a recursion guard the decoder recurses until it hits
// an *uncatchable* StackOverflowException and terminates the process. These tests craft
// those cycles and assert the decoder fails closed to Unsupported instead of crashing.
//
// WARNING: if the recursion guard in TypeRefDecoder is removed, these tests do not fail —
// they StackOverflow and take down the whole test runner.
public class TypeRefDecoderRecursionTests
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

        var result = TypeRefDecoder.Instance.GetTypeFromReference(
            reader,
            MetadataTokens.TypeReferenceHandle(1),
            0);

        Assert.Equal(TypeRefKind.Unsupported, result.Kind);
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

        var result = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, typeHandle, 0);

        Assert.Equal(TypeRefKind.Unsupported, result.Kind);
    }

    [Fact]
    public void SelfReferentialTypeSpecification_DoesNotStackOverflow()
    {
        var reader = BuildMetadata(metadata =>
        {
            // A signature whose first element is a required custom modifier (CMOD_REQD,
            // 0x1f) referencing this very TypeSpec row via its TypeDefOrRefOrSpec coded token
            // ((row 1 << 2) | tag 2 for TypeSpec = 0x06), followed by I4 (0x08) as the
            // modified type. Custom modifiers decode with allowTypeSpecifications: true, so
            // resolving the modifier re-enters GetTypeFromSpecification on this row -> a cycle.
            var signature = new BlobBuilder();
            signature.WriteByte(0x1f);
            signature.WriteByte(0x06);
            signature.WriteByte(0x08);
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
            return default(EntityHandle);
        });

        var result = TypeRefDecoder.Instance.GetTypeFromSpecification(
            reader,
            GenericScope.Empty,
            MetadataTokens.TypeSpecificationHandle(1),
            0);

        Assert.Equal(TypeRefKind.Unsupported, result.Kind);
    }

    [Fact]
    public void OverlongTypeSpecificationBlob_DoesNotStackOverflow()
    {
        var reader = BuildMetadata(metadata =>
        {
            // A signature of 100000 nested SZARRAY (0x1d) prefixes then I4 (0x08). SRM's
            // SignatureDecoder.DecodeType recurses on the native stack once per prefix, before
            // any provider callback, so the depth counter cannot catch it — only refusing the
            // over-long blob up front prevents the StackOverflow.
            var signature = new BlobBuilder();
            for (int i = 0; i < 100_000; i++)
                signature.WriteByte(0x1d);
            signature.WriteByte(0x08);
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
            return default(EntityHandle);
        });

        var result = TypeRefDecoder.Instance.GetTypeFromSpecification(
            reader,
            GenericScope.Empty,
            MetadataTokens.TypeSpecificationHandle(1),
            0);

        Assert.Equal(TypeRefKind.Unsupported, result.Kind);
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
