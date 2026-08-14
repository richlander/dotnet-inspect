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
    public void NestedTypeDefinition_EnclosingTypeIsImmediateParentOnly()
    {
        // A three-level nesting Root -> Mid -> Leaf. The decoded leaf carries its
        // immediately-enclosing type (Mid) as provenance, and that enclosing
        // TypeRef must not itself chain further: only one level is materialized,
        // so deep nesting on untrusted metadata cannot amplify allocation.
        TypeDefinitionHandle leafHandle = default;
        var reader = BuildMetadata(metadata =>
        {
            var root = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Root"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            var mid = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Mid"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            leafHandle = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Leaf"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(mid, root);
            metadata.AddNestedType(leafHandle, mid);
            return default(EntityHandle);
        });

        var result = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, leafHandle, 0);

        Assert.Equal(TypeRefKind.Definition, result.Kind);
        Assert.Equal("Root+Mid+Leaf", result.Name);

        var enclosing = result.EnclosingType;
        Assert.NotNull(enclosing);
        Assert.Equal("Root+Mid", enclosing!.Name);
        Assert.Equal("N", enclosing.Namespace);
        // Immediate-only: the enclosing provenance does not recurse further.
        Assert.Null(enclosing.EnclosingType);
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
    public void PlatformTrust_SelfReferentialTypeReference_DoesNotStackOverflow()
    {
        var reader = BuildMetadata(metadata =>
            // A TypeReference whose ResolutionScope points back at its own row (a
            // nested-type scope cycle). The platform-trust walk must bound the
            // chain and fail closed rather than recurse to StackOverflow.
            metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop")));

        Assert.False(IrImporter.IsTrustedPlatformMemberReference(
            reader,
            MetadataTokens.TypeReferenceHandle(1)));
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

    // Depth is bounded by the node budget; size is not. 64 segments of 512 characters is a legal
    // chain by every count-based rule and still a 32 KB name, so the decoder refuses it — and
    // refuses it visibly, as an unsupported type rather than a truncated name.
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
            "metadata name is incomplete",
            result.UnsupportedReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OverBudgetAggregateNestedDefinitionName_IsRejectedVisibly()
    {
        const int levels = 64;
        TypeDefinitionHandle leafHandle = default;
        var reader = BuildMetadata(metadata =>
        {
            TypeDefinitionHandle previous = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(new string('x', 512)),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            var nestings = new List<(TypeDefinitionHandle Nested, TypeDefinitionHandle Enclosing)>();
            for (int level = 1; level < levels; level++)
            {
                TypeDefinitionHandle current = metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic,
                    default,
                    metadata.GetOrAddString(new string('x', 512)),
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: MetadataTokens.MethodDefinitionHandle(1));
                nestings.Add((current, previous));
                previous = current;
            }

            foreach ((TypeDefinitionHandle nested, TypeDefinitionHandle enclosing) in nestings)
                metadata.AddNestedType(nested, enclosing);
            leafHandle = previous;
            return default(EntityHandle);
        });

        var result = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, leafHandle, 0);

        Assert.Equal(TypeRefKind.Unsupported, result.Kind);
        Assert.Contains(
            "type-definition metadata name is incomplete",
            result.UnsupportedReason,
            StringComparison.Ordinal);
    }

    // The close negative: the same shape well inside the budget still decodes, and its flattened
    // name, structured segments, and escaped identity are exact.
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
        Assert.Equal(rootToLeaf, result.DefinitionName!.Segments);
        Assert.Equal(
            $"N.{string.Join('+', rootToLeaf)}",
            result.DefinitionName.ToEscapedFullName());
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
