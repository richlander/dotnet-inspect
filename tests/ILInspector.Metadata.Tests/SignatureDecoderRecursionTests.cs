using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

// The string-producing SignatureDecoder (used by the declaration-query API, the compile-back /
// type-source composers, and the metadata scanners) re-decodes nested TypeSpecs reached by handle
// (e.g. a custom modifier referencing another TypeSpec). Malformed metadata can make that
// cross-handle chain cycle or nest arbitrarily deep, which without a guard recurses on the native
// stack inside SRM until it hits an *uncatchable* StackOverflowException and terminates the process.
// These tests craft those shapes and assert the decode fails closed to a placeholder instead of
// crashing.
//
// WARNING: if the guard in SignatureDecoder.GetTypeFromSpecification / TypeResolver is removed,
// these tests do not fail — they StackOverflow and take down the whole test runner.
public class SignatureDecoderRecursionTests
{
    [Fact]
    public void SelfReferentialTypeSpec_ThroughStringDecoder_DoesNotStackOverflow()
    {
        // A shallow (guard-passing) TypeSpec whose first element is a required custom modifier
        // (CMOD_REQD, 0x1f) referencing this very TypeSpec row via its TypeDefOrRefOrSpec coded
        // token ((row 1 << 2) | tag 2 for TypeSpec = 0x06), followed by I4 (0x08). Custom modifiers
        // decode with allowTypeSpecifications: true, so resolving the modifier re-enters
        // GetTypeFromSpecification on this row -> an unbounded cycle. The internal re-entry depth
        // cap must stop it.
        var reader = BuildTypeSpec(sig =>
        {
            sig.WriteByte(0x1f);
            sig.WriteByte(0x06);
            sig.WriteByte(0x08);
        });

        var text = TypeResolver.GetTypeNameFromSpecification(reader, MetadataTokens.TypeSpecificationHandle(1));

        // The string decoder drops custom modifiers (GetModifiedType returns the unmodified type),
        // so this modreq cycle degrades to the underlying "int". Reaching this assertion at all
        // proves the decode terminated instead of overflowing the stack.
        Assert.Equal("int", text);
    }

    [Fact]
    public void OverlongTypeSpec_ThroughStringDecoder_DegradesToPlaceholder()
    {
        // 100000 nested SZARRAY (0x1d) prefixes then I4 (0x08). SRM's SignatureDecoder.DecodeType
        // recurses on the native stack once per prefix before any provider callback, so only
        // refusing the over-deep blob up front (the top-level SignatureBlobGuard prescan) prevents
        // the StackOverflow.
        var reader = BuildTypeSpec(sig =>
        {
            for (int i = 0; i < 100_000; i++)
                sig.WriteByte(0x1d);
            sig.WriteByte(0x08);
        });

        var text = TypeResolver.GetTypeNameFromSpecification(reader, MetadataTokens.TypeSpecificationHandle(1));

        Assert.Equal("object", text);
    }

    [Fact]
    public void MalformedZeroRankArray_ThroughStringDecoder_DegradesToPlaceholder()
    {
        // ELEMENT_TYPE_ARRAY (0x14) I4 (0x08) rank=0 numSizes=0 numLoBounds=0. Rank 0 is malformed
        // (ECMA-335 requires rank >= 1); the string decoder renders `rank - 1` commas, so an
        // unguarded rank 0 throws ArgumentOutOfRangeException (new string(',', -1)). The guard must
        // reject it so the decode fails closed to "object".
        var reader = BuildTypeSpec(sig =>
        {
            sig.WriteByte(0x14);
            sig.WriteByte(0x08);
            sig.WriteCompressedInteger(0); // rank
            sig.WriteCompressedInteger(0); // numSizes
            sig.WriteCompressedInteger(0); // numLoBounds
        });

        var text = TypeResolver.GetTypeNameFromSpecification(reader, MetadataTokens.TypeSpecificationHandle(1));

        Assert.Equal("object", text);
    }

    [Fact]
    public void HugeRankArray_ThroughStringDecoder_DegradesToPlaceholder()
    {
        // ELEMENT_TYPE_ARRAY (0x14) I4 (0x08) rank=1_000_000 numSizes=0 numLoBounds=0. Rank consumes
        // no bytes proportional to its value, so this tiny blob would force the string decoder to
        // allocate a ~1M-comma string. The guard must reject the absurd rank -> "object".
        var reader = BuildTypeSpec(sig =>
        {
            sig.WriteByte(0x14);
            sig.WriteByte(0x08);
            sig.WriteCompressedInteger(1_000_000); // rank
            sig.WriteCompressedInteger(0);         // numSizes
            sig.WriteCompressedInteger(0);         // numLoBounds
        });

        var text = TypeResolver.GetTypeNameFromSpecification(reader, MetadataTokens.TypeSpecificationHandle(1));

        Assert.Equal("object", text);
    }

    [Fact]
    public void HugeGenericArityTypeName_FormatDisplayName_DegradesWithoutOom()
    {
        // A type name whose `N arity marker encodes ~2e9. FormatDisplayName renders arity placeholder
        // parameters, so an unbounded arity from this 15-char untrusted #Strings-heap name would drive
        // a multi-billion-iteration allocation and OOM. It must instead degrade to the raw name.
        var text = TypeResolver.FormatDisplayName("Foo`2000000000");

        Assert.Equal("Foo`2000000000", text);
    }

    [Theory]
    [InlineData("List`1", "List<T>")]
    [InlineData("Dictionary`2", "Dictionary<T1, T2>")]
    [InlineData("Plain", "Plain")]
    public void FormatDisplayName_RealArities_Unchanged(string input, string expected)
    {
        Assert.Equal(expected, TypeResolver.FormatDisplayName(input));
    }

    [Fact]
    public void ManyBoundedArityMarkers_FormatDisplayName_DoesNotAmplifyToOom()
    {
        // Each marker's arity is under the per-marker cap (1024), but a name carrying thousands of
        // them would still expand ~1000x per marker into an OOM-scale string. The cumulative output
        // cap must bound the result to roughly the ceiling plus the raw remainder.
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < 5000; i++)
            builder.Append("N`1024.");
        var name = builder.ToString();

        var text = TypeResolver.FormatDisplayName(name);

        // Unbounded expansion would be ~5000 * ~6 KB = tens of MB; the cap keeps output O(input + cap)
        // rather than O(input * arity). The overshoot allows for one in-flight marker expansion.
        Assert.True(text.Length < name.Length + 10 * NestedTypeName.MaxLength,
            $"expected bounded output, got {text.Length} chars");
    }

    [Fact]
    public void CyclicLongNameTypeRef_ThroughResolver_DoesNotAmplifyToOom()
    {
        // A self-scoped TypeReference whose name is long. The depth cap prevents the stack overflow,
        // but without a cumulative length cap the climb would repeat the long name up to 256 times
        // (a small blob -> hundreds of MB). The length cap must bound the output near one name.
        var longName = new string('x', 50_000);
        var reader = BuildAssembly(md =>
            md.AddTypeReference(MetadataTokens.TypeReferenceHandle(1), default, md.GetOrAddString(longName)));

        var text = TypeResolver.GetTypeNameFromReference(reader, MetadataTokens.TypeReferenceHandle(1));

        Assert.True(text.Length <= longName.Length + NestedTypeName.MaxLength + 16,
            $"expected bounded output, got {text.Length} chars");
    }

    [Fact]
    public void ManyLongTypeArguments_ApplyGenericArguments_DoesNotAmplifyToOom()
    {
        // A self-nested generic type produces a declaring chain that repeats the same (untrusted,
        // long) generic parameter name for every marker; ApplyGenericArguments would expand them all
        // into an OOM-scale string. The cumulative output cap must bound it.
        var longArg = new string('x', 50_000);
        var nameBuilder = new System.Text.StringBuilder();
        var arguments = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 257; i++)
        {
            nameBuilder.Append("Loop`1.");
            arguments.Add(longArg);
        }

        var text = TypeResolver.ApplyGenericArguments(nameBuilder.ToString(), arguments);

        // Unbounded expansion would be ~257 * 50 KB = ~12.8 MB; the cap keeps it O(input + cap).
        Assert.True(text.Length < longArg.Length + 10 * NestedTypeName.MaxLength,
            $"expected bounded output, got {text.Length} chars");
    }

    [Fact]
    public void CyclicTypeReferenceResolutionScope_ThroughResolver_DoesNotStackOverflow()
    {
        // A nested TypeReference whose resolution scope points at itself (row 1). SignatureDecoder
        // delegates every TypeRef token to TypeResolver.GetTypeNameFromReference, which climbs the
        // resolution scope to qualify nested types (Outer.Inner) -> an unbounded cycle without the
        // climb-depth guard. Reaching this assertion at all proves the climb terminated instead of
        // overflowing the native stack.
        //
        // WARNING: if the TypeResolver climb guard is removed, this test does not fail — it
        // StackOverflows and takes down the whole test runner.
        var reader = BuildAssembly(md =>
            md.AddTypeReference(MetadataTokens.TypeReferenceHandle(1), md.GetOrAddString("N"), md.GetOrAddString("Loop")));

        var text = TypeResolver.GetTypeNameFromReference(reader, MetadataTokens.TypeReferenceHandle(1));

        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void SelfNestedTypeDefinition_ThroughResolver_DoesNotStackOverflow()
    {
        // A TypeDefinition declared as nested inside itself. TypeResolver.GetFullName climbs the
        // declaring-type chain to qualify nested types -> an unbounded cycle without the guard.
        TypeDefinitionHandle handle = default;
        var reader = BuildAssembly(md =>
        {
            handle = md.AddTypeDefinition(System.Reflection.TypeAttributes.NestedPublic,
                md.GetOrAddString("N"), md.GetOrAddString("Loop"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            md.AddNestedType(handle, handle);
        });

        var text = TypeResolver.GetFullName(reader, reader.GetTypeDefinition(handle));

        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void CyclicTypeReference_ThroughGetFullTypeNameExtension_DoesNotStackOverflow()
    {
        // MetadataReaderExtensions.GetFullTypeName(TypeReference) climbs the resolution scope to
        // qualify a nested TypeRef (Outer.Inner); a self-scoped TypeRef must not recurse forever.
        // The overload delegates the climb to the guarded TypeResolver.GetTypeNameFromReference.
        var reader = BuildAssembly(md =>
            md.AddTypeReference(MetadataTokens.TypeReferenceHandle(1), md.GetOrAddString("N"), md.GetOrAddString("Loop")));

        var text = reader.GetFullTypeName(reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(1)));

        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void TypeSpecGuard_RejectsOverlongBlob()
    {
        // A 100000-deep SZARRAY TypeSpec is far past the per-blob length cap; TryEnter must refuse
        // it so no provider re-decodes it (which would overflow SRM's native decoder).
        var reader = BuildTypeSpec(sig =>
        {
            for (int i = 0; i < 100_000; i++)
                sig.WriteByte(0x1d);
            sig.WriteByte(0x08);
        });

        bool entered = TypeSpecGuard.TryEnter(reader, MetadataTokens.TypeSpecificationHandle(1), out _);

        Assert.False(entered);
    }

    [Fact]
    public void ManyLongGenericParameters_GenericContext_BoundedMaterialization()
    {
        // A type carrying thousands of generic parameters that all point at one long #Strings name
        // would materialize gigabytes; the accumulated-name cap must bound the context's parameter
        // list well below the parameter count.
        var longName = new string('p', 1_000);
        TypeDefinitionHandle typeHandle = default;
        var reader = BuildAssembly(md =>
        {
            typeHandle = md.AddTypeDefinition(System.Reflection.TypeAttributes.Public,
                md.GetOrAddString("N"), md.GetOrAddString("Wide`10000"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            for (int i = 0; i < 10_000; i++)
                md.AddGenericParameter(typeHandle, System.Reflection.GenericParameterAttributes.None,
                    md.GetOrAddString(longName), i);
        });

        var context = GenericContext.ForType(reader, reader.GetTypeDefinition(typeHandle));

        long totalLength = 0;
        foreach (var name in context.TypeParameters)
            totalLength += name.Length;
        Assert.True(context.TypeParameters.Count < 10_000, $"expected capped count, got {context.TypeParameters.Count}");
        Assert.True(totalLength < NestedTypeName.MaxLength + longName.Length,
            $"expected bounded materialization, got {totalLength} chars");
    }

    static MetadataReader BuildTypeSpec(Action<BlobBuilder> writeSignature)
    {
        var signature = new BlobBuilder();
        writeSignature(signature);
        return BuildAssembly(md => md.AddTypeSpecification(md.GetOrAddBlob(signature)));
    }

    static MetadataReader BuildAssembly(Action<MetadataBuilder> addMalformedRows)
    {
        var md = new MetadataBuilder();
        md.AddModule(0, md.GetOrAddString("Synthetic.dll"), md.GetOrAddGuid(Guid.NewGuid()), default, default);
        md.AddAssembly(md.GetOrAddString("Synthetic"), new Version(1, 0, 0, 0), default, default, default, default);
        // The module (<Module>) pseudo-type must be row 1; the malformed rows come after it.
        md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        addMalformedRows(md);

        var root = new MetadataRootBuilder(md, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray())).GetMetadataReader();
    }
}
