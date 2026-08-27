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
    [InlineData(2, "int32[,]")]
    [InlineData(4, "int32[,,,]")]
    public void RendersLoadableRanksFaithfully(int rank, string expected)
    {
        string rendered = ArrayShapeText.Format("int32", rank);

        Assert.Equal(expected, rendered);
        Assert.DoesNotContain("invalid rank", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// ILAsm spells a rank-1 multi-dimensional array <c>int32[...]</c>, which is a different
    /// signature from the vector <c>int32[]</c>. <c>ildasm</c> is the oracle: assembling
    /// <c>M(int32[...], int32[])</c> and disassembling it round-trips to those two spellings.
    /// Collapsing them would emit IL that reassembles to the wrong type.
    /// </summary>
    [Fact]
    public void DistinguishesARankOneMdArrayFromAVector()
    {
        Assert.Equal("int32[...]", ArrayShapeText.Format("int32", 1));
    }

    /// <summary>
    /// Rank is an unsigned compressed integer that ECMA-335 II.23.2.13 requires to be positive,
    /// so a zero rank is malformed metadata and gets the marker rather than a vector's spelling.
    /// </summary>
    [Fact]
    public void MarksARankNoArrayCouldDeclare()
    {
        Assert.Equal("int32[/* invalid rank 0 */]", ArrayShapeText.Format("int32", 0));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(32, true)]
    [InlineData(33, false)]
    [InlineData(536_870_911, false)]
    public void IsLoadableRank_TracksTheCliLimit(int rank, bool expected)
    {
        Assert.Equal(expected, ArrayShapeText.IsLoadableRank(rank));
    }

    /// <summary>
    /// <see cref="ArrayShapeText.FormatDimensions"/> is what the identity and display renderers
    /// use, so its rank-1 spelling stays the empty string they already produced. The bound is the
    /// part that matters there.
    /// </summary>
    [Theory]
    [InlineData(1, "")]
    [InlineData(2, ",")]
    [InlineData(3, ",,")]
    public void FormatDimensions_RendersLoadableRanks(int rank, string expected)
    {
        Assert.Equal(expected, ArrayShapeText.FormatDimensions(rank));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(33)]
    [InlineData(536_870_911)]
    public void FormatDimensions_BoundsRanksNoArrayCouldHave(int rank)
    {
        string rendered = ArrayShapeText.FormatDimensions(rank);

        Assert.Equal($"/* invalid rank {rank} */", rendered);
        Assert.True(
            rendered.Length < 64,
            $"expected a bounded rendering, got {rendered.Length} chars");
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

    /// <summary>
    /// The same canary for the guarded decoder. <c>GuardedSignatureDecoder.DecodeTypeSpecification</c>
    /// selects the shared <see cref="SignatureDecoder.Instance"/> on its default, no-callback path,
    /// and that instance carries no materialization budget — its <c>ObserveMaterialization</c> call
    /// invokes a null delegate. Before this was bounded, the guarded entry point returned a
    /// successfully-decoded 536,870,915-character string and allocated about 2 GB.
    /// </summary>
    [Fact]
    public void GuardedSignatureDecoder_DoesNotMaterializeAHostileRank()
    {
        // ELEMENT_TYPE_ARRAY, I4, rank 0xDFFFFFFF (~536M), 0 sizes, 0 lower bounds.
        var (reader, handle) = BuildTypeSpec(
            [0x14, 0x08, 0xdf, 0xff, 0xff, 0xff, 0x00, 0x00]);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = GuardedSignatureDecoder.DecodeTypeSpecification(reader, handle);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        string rendered = Assert.IsType<SignatureDecodeResult<string>.Decoded>(result).Value;
        Assert.Contains("invalid rank", rendered, StringComparison.Ordinal);
        Assert.True(
            rendered.Length < 64,
            $"expected a bounded rendering, got {rendered.Length} chars");

        // The allocation is the actual defect; the rendering length alone would not catch a
        // provider that built the separator and then truncated it.
        Assert.True(
            allocated < 1024 * 1024,
            $"expected a bounded allocation, got {allocated / 1024 / 1024} MB");
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
