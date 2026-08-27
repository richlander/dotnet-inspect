using System.Collections.Immutable;
using System.Globalization;
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
        string rendered = ArrayShapeText.Format("int32", new ArrayShape(rank, [], []));

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
        Assert.Equal("int32[...]", ArrayShapeText.Format("int32", new ArrayShape(1, [], [])));
    }

    /// <summary>
    /// Rank is an unsigned compressed integer that ECMA-335 II.23.2.13 requires to be positive,
    /// so a zero rank is malformed metadata and gets the marker rather than a vector's spelling.
    /// </summary>
    [Fact]
    public void MarksARankNoArrayCouldDeclare()
    {
        Assert.Equal(
            "int32[/* invalid rank 0 */ invalid]",
            ArrayShapeText.Format("int32", new ArrayShape(0, [], [])));
    }

    [Fact]
    public void RendersSizesAndLowerBoundsInILAsmSyntax()
    {
        Assert.Equal(
            "int32[0...,0...]",
            ArrayShapeText.Format("int32", new ArrayShape(2, [], [0, 0])));
        Assert.Equal(
            "int32[6,-2...3]",
            ArrayShapeText.Format("int32", new ArrayShape(2, [6, 6], [0, -2])));
        Assert.Equal(
            "int32[6,,]",
            ArrayShapeText.Format("int32", new ArrayShape(3, [6], [0])));
        Assert.Equal(
            "int32[0]",
            ArrayShapeText.Format("int32", new ArrayShape(1, [0], [0])));
        Assert.Equal(
            "int32[-2...,,]",
            ArrayShapeText.Format("int32", new ArrayShape(3, [], [-2])));
        Assert.Equal(
            "int32[0...,]",
            ArrayShapeText.Format("int32", new ArrayShape(2, [], [0])));
    }

    [Fact]
    public void SignatureDecoderKeepsCSharpArraySpellingRankOnly()
    {
        Assert.Equal(
            "int[,]",
            new SignatureDecoder().GetArrayType(
                "int",
                new ArrayShape(2, [], [0, 0])));
    }

    [Fact]
    public void RendersAZeroSizePrefixAsAnUnboundedDimension()
    {
        Assert.Equal(
            "int32[-2...,6]",
            ArrayShapeText.Format("int32", new ArrayShape(2, [0, 6], [-2, 0])));
    }

    [Fact]
    public void MarksShapesWhoseSizesHaveNoCorrespondingLowerBounds()
    {
        string sizeSix = ArrayShapeText.Format("int32", new ArrayShape(1, [6], []));
        string sizeSeven = ArrayShapeText.Format("int32", new ArrayShape(1, [7], []));

        Assert.Equal(
            "int32[/* unrepresentable shape: 1 sizes, 0 lower bounds; "
                + "sizes=[6], lower bounds=[] */ invalid]",
            sizeSix);
        Assert.Equal(
            "int32[/* unrepresentable shape: 1 sizes, 0 lower bounds; "
                + "sizes=[7], lower bounds=[] */ invalid]",
            sizeSeven);
        Assert.NotEqual(sizeSix, sizeSeven);
    }

    [Fact]
    public void MarksATerminalZeroSizeWithANonzeroLowerBound()
    {
        Assert.Equal(
            "int32[/* unrepresentable zero size with lower bound 1 at dimension 0; "
                + "sizes=[0], lower bounds=[1] */ invalid]",
            ArrayShapeText.Format("int32", new ArrayShape(1, [0], [1])));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x20000000)]
    public void MarksSizesOutsideTheCompressedIntegerRange(int size)
    {
        Assert.Contains(
            $"invalid size {size} at dimension 0",
            ArrayShapeText.Format("int32", new ArrayShape(1, [size], [0])),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0x10000001)]
    [InlineData(0x10000000)]
    public void MarksLowerBoundsOutsideTheCompressedSignedIntegerRange(int lowerBound)
    {
        Assert.Contains(
            $"invalid lower bound {lowerBound} at dimension 0",
            ArrayShapeText.Format("int32", new ArrayShape(1, [], [lowerBound])),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RendersCompressedIntegerBoundaryValues()
    {
        Assert.Equal(
            "int32[-268435456...268435454]",
            ArrayShapeText.Format(
                "int32",
                new ArrayShape(1, [0x1fffffff], [-0x10000000])));
        Assert.Equal(
            "int32[268435455...]",
            ArrayShapeText.Format("int32", new ArrayShape(1, [], [0x0fffffff])));
    }

    [Fact]
    public void RendersNumericTokensWithInvariantCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");

            Assert.Equal(
                "int32[6,-2...3]",
                ArrayShapeText.Format("int32", new ArrayShape(2, [6, 6], [0, -2])));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void RendersTheLargestLoadableShapeFaithfully()
    {
        var sizes = ImmutableArray.CreateRange(Enumerable.Repeat(1, ArrayShapeText.MaxRenderableRank));
        var lowerBounds = ImmutableArray.CreateRange(Enumerable.Repeat(0, ArrayShapeText.MaxRenderableRank));
        string rendered = ArrayShapeText.Format(
            "int32",
            new ArrayShape(ArrayShapeText.MaxRenderableRank, sizes, lowerBounds));

        Assert.Equal($"int32[{string.Join(',', sizes)}]", rendered);
    }

    [Fact]
    public void MarksDimensionCountsNoLoadableShapeCouldHave()
    {
        var excess = ImmutableArray.CreateRange(
            Enumerable.Repeat(0, ArrayShapeText.MaxRenderableRank + 1));

        string sizes = ArrayShapeText.Format(
            "int32",
            new ArrayShape(ArrayShapeText.MaxRenderableRank, excess, []));
        string lowerBounds = ArrayShapeText.Format(
            "int32",
            new ArrayShape(ArrayShapeText.MaxRenderableRank, [], excess));

        Assert.Equal(
            "int32[/* invalid size count 33 for rank 32 */ invalid]",
            sizes);
        Assert.Equal(
            "int32[/* invalid lower-bound count 33 for rank 32 */ invalid]",
            lowerBounds);
        Assert.True(sizes.Length < 128);
        Assert.True(lowerBounds.Length < 128);
    }

    [Fact]
    public void MarksANegativeSize()
    {
        Assert.Equal(
            "int32[/* invalid size -1 at dimension 0; sizes=[-1], "
                + "lower bounds=[0] */ invalid]",
            ArrayShapeText.Format("int32", new ArrayShape(1, [-1], [0])));
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
            new ArrayShape(ArrayShapeText.MaxRenderableRank, [], []));

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
        string rendered = ArrayShapeText.Format("int32", new ArrayShape(rank, [], []));

        Assert.Equal($"int32[/* invalid rank {rank} */ invalid]", rendered);

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

        long before = GC.GetTotalAllocatedBytes(precise: true);
        var result = GuardedSignatureDecoder.DecodeTypeSpecification(reader, handle);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

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
