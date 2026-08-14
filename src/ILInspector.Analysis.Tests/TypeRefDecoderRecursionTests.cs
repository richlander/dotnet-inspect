using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis.Tests;

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

    [Fact]
    public void HugeArrayShapeCount_IsRejectedBeforeDecode()
    {
        var reader = BuildMetadata(metadata =>
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x14); // ARRAY
            signature.WriteByte(0x08); // I4
            signature.WriteByte(0x01); // rank 1
            signature.WriteByte(0xdf); // compressed ~536M sizes count
            signature.WriteByte(0xff);
            signature.WriteByte(0xff);
            signature.WriteByte(0xff);
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

    // The node budget bounds nesting depth, not size: 64 segments of 512 characters is a legal
    // chain by every count-based rule and still a 32 KB name. Rejecting it keeps a decoder from
    // materializing an artifact-sized string, and the rejection stays visible rather than
    // degrading into a shortened name.
    [Fact]
    public void OverBudgetAggregateTypeName_IsRejectedVisibly()
    {
        const int levels = 64;
        var reader = BuildMetadata(metadata =>
        {
            for (int row = 1; row <= levels; row++)
            {
                metadata.AddTypeReference(
                    row == levels ? default : MetadataTokens.TypeReferenceHandle(row + 1),
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString(new string('x', 512)));
            }

            return default(EntityHandle);
        });

        var result = TypeRefDecoder.Instance.GetTypeFromReference(
            reader,
            MetadataTokens.TypeReferenceHandle(1),
            0);

        Assert.Equal(TypeRefKind.Unsupported, result.Kind);
        Assert.Contains(
            "metadata name is invalid (SegmentsTooLong)",
            result.UnsupportedReason,
            StringComparison.Ordinal);
    }

    // The close negative: the same shape one level shy of the budget still decodes, and its
    // flattened name and structured identity are exact.
    [Fact]
    public void DeepNestedTypeNameWithinBudget_PreservesTheExactIdentity()
    {
        const int levels = 32;
        var reader = BuildMetadata(metadata =>
        {
            for (int row = 1; row <= levels; row++)
            {
                metadata.AddTypeReference(
                    row == levels ? default : MetadataTokens.TypeReferenceHandle(row + 1),
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString($"Level{row}`1"));
            }

            return default(EntityHandle);
        });

        var result = TypeRefDecoder.Instance.GetTypeFromReference(
            reader,
            MetadataTokens.TypeReferenceHandle(1),
            0);

        Assert.Equal(TypeRefKind.Definition, result.Kind);
        string[] rootToLeaf =
        [
            .. Enumerable.Range(1, levels).Reverse().Select(row => $"Level{row}`1"),
        ];
        Assert.Equal(string.Join('+', rootToLeaf), result.Name);
        Assert.Equal(rootToLeaf, result.Resolution!.Type.Segments);
        Assert.Equal(
            $"N.{string.Join('+', rootToLeaf)}",
            result.Resolution.Type.ToEscapedFullName());
    }

    // A segment whose own metadata name contains the nesting delimiter keeps its escaped
    // identity distinct from a genuinely nested chain with the same flattened spelling.
    [Fact]
    public void LiteralDelimiterInASegment_StaysDistinctFromNesting()
    {
        var literal = BuildMetadata(metadata =>
        {
            metadata.AddTypeReference(
                default,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer+Inner"));
            return default(EntityHandle);
        });
        var nested = BuildMetadata(metadata =>
        {
            metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(2),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Inner"));
            metadata.AddTypeReference(
                default,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"));
            return default(EntityHandle);
        });

        var literalResult = TypeRefDecoder.Instance.GetTypeFromReference(
            literal,
            MetadataTokens.TypeReferenceHandle(1),
            0);
        var nestedResult = TypeRefDecoder.Instance.GetTypeFromReference(
            nested,
            MetadataTokens.TypeReferenceHandle(1),
            0);

        Assert.Equal("Outer+Inner", literalResult.Name);
        Assert.Equal("Outer+Inner", nestedResult.Name);
        Assert.NotEqual(
            literalResult.Resolution!.Type.ToEscapedFullName(),
            nestedResult.Resolution!.Type.ToEscapedFullName());
        Assert.Equal(@"N.Outer\+Inner", literalResult.Resolution.Type.ToEscapedFullName());
        Assert.Equal("N.Outer+Inner", nestedResult.Resolution.Type.ToEscapedFullName());
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
