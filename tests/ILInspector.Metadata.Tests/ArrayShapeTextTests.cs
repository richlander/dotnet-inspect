using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates <see cref="ArrayShapeText"/>: the signature providers that render straight to a string
/// carry no materialization budget, so a hostile rank has to be bounded where it is spelled.
/// </summary>
public class ArrayShapeTextTests
{
    [Theory]
    [InlineData(0, "int32[]")]
    [InlineData(1, "int32[]")]
    [InlineData(2, "int32[,]")]
    [InlineData(4, "int32[,,,]")]
    public void RendersLoadableRanksFaithfully(int rank, string expected)
    {
        string rendered = ArrayShapeText.Format("int32", rank);

        Assert.Equal(expected, rendered);
        Assert.DoesNotContain("invalid rank", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersTheLargestLoadableRankFaithfully()
    {
        string rendered = ArrayShapeText.Format(
            "int32",
            ArrayShapeText.MaxRenderableRank);

        Assert.Equal(
            $"int32[{new string(',', ArrayShapeText.MaxRenderableRank - 1)}]",
            rendered);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(1_000)]
    [InlineData(536_870_911)]
    [InlineData(-1)]
    public void MarksRanksNoArrayCouldHave(int rank)
    {
        string rendered = ArrayShapeText.Format("int32", rank);

        Assert.Equal($"int32[/* invalid rank {rank} */]", rendered);

        // The point of the marker: the spelling stays proportional to the text, not to the
        // declared rank. Faithfully rendering 536,870,911 dimensions is a ~536MB string.
        Assert.True(
            rendered.Length < 64,
            $"expected a bounded rendering, got {rendered.Length} chars");
    }

    /// <summary>
    /// The end-to-end canary: an eight-byte TypeSpec declaring ~536M dimensions consumes no blob
    /// bytes in proportion to its rank, so it passes every byte-oriented structural check and
    /// reaches the provider. Decoding it must stay cheap.
    /// </summary>
    [Fact]
    public void CanonicalIL_DoesNotMaterializeAHostileRank()
    {
        // ELEMENT_TYPE_ARRAY, I4, rank 0xDFFFFFFF (~536M), 0 sizes, 0 lower bounds.
        var (reader, handle) = BuildTypeSpec(
            [0x14, 0x08, 0xdf, 0xff, 0xff, 0xff, 0x00, 0x00]);
        BlobHandle signature = reader.GetTypeSpecification(handle).Signature;

        Assert.True(
            SignatureBlobGuard.IsSafeToDecode(
                reader,
                signature,
                SignatureBlobGuard.Kind.TypeSpecification),
            "the structural guard admits this blob, which is why the provider must bound it");

        string rendered = reader
            .GetTypeSpecification(handle)
            .DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null);

        Assert.Contains("invalid rank", rendered, StringComparison.Ordinal);
        Assert.True(
            rendered.Length < 64,
            $"expected a bounded rendering, got {rendered.Length} chars");
    }

    static (MetadataReader Reader, TypeSpecificationHandle Handle) BuildTypeSpec(
        byte[] typeBlob)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("m.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("m"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var blob = new BlobBuilder();
        blob.WriteBytes(typeBlob);
        TypeSpecificationHandle handle =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(blob));

        var root = new MetadataRootBuilder(metadata, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return (
            MetadataReaderProvider
                .FromMetadataImage(ImmutableArray.Create(image.ToArray()))
                .GetMetadataReader(),
            handle);
    }
}
