using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Malformed metadata can encode cyclic or over-budget relationship chains. The decoder
// must preserve the shared relationship rejection rather than recurse or invent a
// plausible partial identity.
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
        Assert.Contains(
            "type-reference resolution-scope relationship rejected (Cycle) at 0x01000001 after 1 nodes",
            result.UnsupportedReason,
            StringComparison.Ordinal);
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
        Assert.Contains(
            "type-definition declaring-type relationship rejected (Cycle) at 0x02000002 after 1 nodes",
            result.UnsupportedReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OverBudgetResolutionScope_PreservesNodeBudgetFailure()
    {
        var reader = BuildMetadata(metadata =>
        {
            for (int row = 1; row <= 257; row++)
            {
                metadata.AddTypeReference(
                    row == 257 ? default : MetadataTokens.TypeReferenceHandle(row + 1),
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString($"Type{row}"));
            }

            return default(EntityHandle);
        });

        var result = TypeRefDecoder.Instance.GetTypeFromReference(
            reader,
            MetadataTokens.TypeReferenceHandle(1),
            0);

        Assert.Equal(TypeRefKind.Unsupported, result.Kind);
        Assert.Contains(
            "type-reference resolution-scope relationship rejected (NodeBudget) at 0x01000101 after 256 nodes",
            result.UnsupportedReason,
            StringComparison.Ordinal);
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

        // The decompiler intentionally drops custom modifiers (GetModifiedType returns the
        // unmodified type), so this modreq cycle degrades to the underlying I4 rather than
        // Unsupported. The guard's contract here is purely crash-avoidance: reaching this
        // assertion at all proves the decode terminated instead of overflowing the stack.
        Assert.NotNull(result);
        Assert.Equal("Int32", result.Name);
    }

    [Fact]
    public void PlatformTrust_SelfReferentialTypeSpecification_DoesNotStackOverflow()
    {
        var reader = BuildMetadata(metadata =>
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x1f);
            signature.WriteByte(0x06);
            signature.WriteByte(0x08);
            return metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
        });

        Assert.False(IrImporter.IsTrustedPlatformMemberReference(
            reader,
            MetadataTokens.TypeSpecificationHandle(1)));
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

    [Fact]
    public void PlatformTrust_OverlongTypeSpecificationBlob_DoesNotStackOverflow()
    {
        var reader = BuildMetadata(metadata =>
        {
            var signature = new BlobBuilder();
            for (int i = 0; i < 100_000; i++)
                signature.WriteByte(0x1d);
            signature.WriteByte(0x08);
            return metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
        });

        Assert.False(IrImporter.IsTrustedPlatformMemberReference(
            reader,
            MetadataTokens.TypeSpecificationHandle(1)));
    }

    [Fact]
    public void SelfReferentialModreqUnderBlobCap_DoesNotStackOverflow()
    {
        var reader = BuildMetadata(metadata =>
        {
            // Each blob is under the per-blob cap (1003 bytes) but cycles: 1000 SZARRAY (0x1d)
            // prefixes, then a required custom modifier (CMOD_REQD, 0x1f) whose coded token
            // (0x06) points back at this TypeSpec row, then I4 (0x08). Decoding it re-enters
            // GetTypeFromSpecification, stacking another ~1000 SRM native frames per re-entry —
            // so the per-blob cap alone would still StackOverflow. The cumulative-bytes cap must
            // stop the chain.
            var signature = new BlobBuilder();
            for (int i = 0; i < 1000; i++)
                signature.WriteByte(0x1d);
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

        // Reaching this assertion at all proves the cumulative cap stopped the cycle before it
        // overflowed the native stack.
        Assert.NotNull(result);
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
