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

    static MetadataReader BuildTypeSpec(Action<BlobBuilder> writeSignature)
    {
        var md = new MetadataBuilder();
        md.AddModule(0, md.GetOrAddString("Synthetic.dll"), md.GetOrAddGuid(Guid.NewGuid()), default, default);
        md.AddAssembly(md.GetOrAddString("Synthetic"), new Version(1, 0, 0, 0), default, default, default, default);
        // The module (<Module>) pseudo-type must be row 1; the TypeSpec comes after it.
        md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        writeSignature(signature);
        md.AddTypeSpecification(md.GetOrAddBlob(signature));

        var root = new MetadataRootBuilder(md, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray())).GetMetadataReader();
    }
}
