using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Renders the bracketed dimension list of an <c>ArrayShape</c> for signature providers that
/// carry no materialization budget of their own.
/// </summary>
/// <remarks>
/// Rank can drive work without consuming blob bytes in proportion to it:
/// <c>ELEMENT_TYPE_ARRAY</c> followed by four bytes of compressed rank encodes 536,870,911
/// dimensions, and the natural spelling materializes a separator string that long. Sizes and
/// lower bounds are materialized arrays by the time a provider receives them, but a caller can
/// also construct an <see cref="ArrayShape"/> directly, so their counts are bounded here too.
/// Every byte-oriented structural check passes a hostile rank, and <see cref="SignatureBlobGuard"/>
/// deliberately does not charge rank, because <c>TypeNodeProvider.GetArrayType</c> charges it
/// against a real budget and reports the overrun as a visible decode failure rather than silently
/// dropping the member.
/// <para>
/// <see cref="SignatureDecoder"/> also calls <c>ObserveMaterialization</c> with the rank, but that
/// only bounds callers that supply a <c>beforeMaterialize</c> budget. The shared decoder instance
/// — which <c>GuardedSignatureDecoder</c> selects for the default, no-callback path — leaves that
/// callback null, so the charge is a no-op there. It renders through this type for that reason; do
/// not read the <c>ObserveMaterialization</c> call as proof that the allocation is bounded.
/// </para>
/// <para>
/// Providers that render straight to a string have nowhere to report an overrun, so they bound
/// the rendering instead. ECMA-335 II.23.2.13 permits any positive rank, but the CLI cannot load
/// an array of more than <see cref="MaxRenderableRank"/> dimensions, so a larger rank is invalid
/// metadata rather than an exotic-but-real shape. Rendering it as a marker keeps the failure
/// visible in the output — the alternative spellings either lie about the shape or allocate.
/// </para>
/// <c>ArrayShapeTextTests</c> gates the spellings and the bound;
/// the Analysis and Decompiler <c>TypeRefDecoderRecursionTests</c> suites gate their respective
/// decode boundaries that keep a hostile rank out of a <c>TypeRef</c> in the first place.
/// </remarks>
public static class ArrayShapeText
{
    const int MaxEncodedSize = 0x1fffffff;
    const int MinEncodedLowerBound = -0x10000000;
    const int MaxEncodedLowerBound = 0x0fffffff;

    /// <summary>
    /// The largest array rank the CLI can load, and so the largest this renders faithfully.
    /// </summary>
    public const int MaxRenderableRank = 32;

    /// <summary>
    /// Whether <paramref name="rank"/> could belong to an array the CLI can load. ECMA-335
    /// II.23.2.13 encodes rank as an unsigned compressed integer and requires it to be positive,
    /// so zero and negative ranks are malformed rather than merely exotic.
    /// </summary>
    public static bool IsLoadableRank(int rank) => rank >= 1 && rank <= MaxRenderableRank;

    /// <summary>
    /// The text between the brackets of an <c>ELEMENT_TYPE_ARRAY</c> spelling, bounded by
    /// <see cref="MaxRenderableRank"/>: the comma-separated dimension list for a loadable rank,
    /// and a marker otherwise.
    /// </summary>
    /// <remarks>
    /// This renders a rank-1 multi-dimensional array as the empty string, which spells it exactly
    /// like a vector. Callers that must keep the two apart — IL-assembler syntax does — use
    /// <see cref="Format"/> instead. Callers that produce identity strings or display text keep
    /// this spelling, because it is what they already produced for rank 1 and changing it would
    /// move identities rather than bound an allocation.
    /// </remarks>
    public static string FormatDimensions(int rank)
        => IsLoadableRank(rank)
            ? new string(',', rank - 1)
            : $"/* invalid rank {Invariant(rank)} */";

    /// <summary>
    /// <paramref name="elementType"/> followed by rank-only dimensions, preserving the
    /// rank-1 multi-dimensional array distinction without exposing explicit ILAsm bounds.
    /// </summary>
    public static string FormatRankOnly(string elementType, int rank)
        => rank == 1
            ? $"{elementType}[...]"
            : $"{elementType}[{FormatDimensions(rank)}]";

    /// <summary>
    /// <paramref name="elementType"/> followed by <paramref name="shape"/> in IL-assembler
    /// syntax, or a visibly non-assembling marker when the shape is malformed or cannot be
    /// represented faithfully in ILAsm syntax.
    /// </summary>
    /// <remarks>
    /// ILAsm spells a rank-1 multi-dimensional array <c>int32[...]</c>, distinct from the vector
    /// <c>int32[]</c> that <c>GetSZArrayType</c> renders; <c>ildasm</c> is the oracle for that
    /// spelling. Collapsing the two would emit IL that round-trips to a different signature.
    /// <c>ILDisassemblerComparisonTests.CanonicalIL_ArraySpellings_ReassembleToTheSameSignature</c>
    /// gates the rank, size, and lower-bound spellings by reassembling them and requiring
    /// byte-identical signature blobs.
    /// <c>ILDisassemblerComparisonTests.CanonicalIL_UnrepresentableArrayShape_IsRejectedByILAsm</c>
    /// gates the failure marker by requiring native ILAsm to reject it.
    /// </remarks>
    public static string Format(string elementType, ArrayShape shape)
    {
        _ = TryFormat(elementType, shape, out string text);
        return text;
    }

    /// <summary>
    /// Attempts to render <paramref name="shape"/> faithfully in IL-assembler syntax.
    /// The output remains a visibly non-assembling marker when the shape is rejected.
    /// </summary>
    public static bool TryFormat(string elementType, ArrayShape shape, out string text)
    {
        int rank = shape.Rank;
        if (!IsLoadableRank(rank))
        {
            text = Failure(elementType, $"invalid rank {Invariant(rank)}");
            return false;
        }

        if (shape.Sizes.Length > rank)
        {
            text = Failure(
                elementType,
                $"invalid size count {Invariant(shape.Sizes.Length)} for rank {Invariant(rank)}");
            return false;
        }

        if (shape.LowerBounds.Length > rank)
        {
            text = Failure(
                elementType,
                $"invalid lower-bound count {Invariant(shape.LowerBounds.Length)} for rank {Invariant(rank)}");
            return false;
        }

        for (int dimension = 0; dimension < shape.Sizes.Length; dimension++)
        {
            int size = shape.Sizes[dimension];
            if (size < 0 || size > MaxEncodedSize)
            {
                text = ShapeFailure(
                    elementType,
                    $"invalid size {Invariant(size)} at dimension {Invariant(dimension)}",
                    shape);
                return false;
            }
        }

        for (int dimension = 0; dimension < shape.LowerBounds.Length; dimension++)
        {
            int lowerBound = shape.LowerBounds[dimension];
            if (lowerBound < MinEncodedLowerBound || lowerBound > MaxEncodedLowerBound)
            {
                text = ShapeFailure(
                    elementType,
                    $"invalid lower bound {Invariant(lowerBound)} at dimension {Invariant(dimension)}",
                    shape);
                return false;
            }
        }

        if (shape.Sizes.Length > shape.LowerBounds.Length)
        {
            text = ShapeFailure(
                elementType,
                $"unrepresentable shape: {Invariant(shape.Sizes.Length)} sizes, "
                    + $"{Invariant(shape.LowerBounds.Length)} lower bounds",
                shape);
            return false;
        }

        var builder = new StringBuilder(elementType);
        builder.Append('[');
        for (int dimension = 0; dimension < rank; dimension++)
        {
            if (dimension > 0)
                builder.Append(',');

            bool hasSize = dimension < shape.Sizes.Length;
            bool hasLowerBound = dimension < shape.LowerBounds.Length;
            if (hasSize)
            {
                int size = shape.Sizes[dimension];
                int lowerBound = hasLowerBound ? shape.LowerBounds[dimension] : 0;
                if (size == 0 && lowerBound != 0)
                {
                    if (dimension == shape.Sizes.Length - 1)
                    {
                        text = ShapeFailure(
                            elementType,
                            $"unrepresentable zero size with lower bound {Invariant(lowerBound)} "
                                + $"at dimension {Invariant(dimension)}",
                            shape);
                        return false;
                    }

                    builder.Append(Invariant(lowerBound));
                    builder.Append("...");
                }
                else if (lowerBound == 0)
                {
                    builder.Append(Invariant(size));
                }
                else
                {
                    builder.Append(Invariant(lowerBound));
                    builder.Append("...");
                    builder.Append(Invariant((long)lowerBound + size - 1L));
                }
            }
            else if (hasLowerBound)
            {
                builder.Append(Invariant(shape.LowerBounds[dimension]));
                builder.Append("...");
            }
            else if (rank == 1)
            {
                builder.Append("...");
            }
        }

        builder.Append(']');
        text = builder.ToString();
        return true;
    }

    static string Failure(string elementType, string detail)
        => $"{elementType}[/* {detail} */ invalid]";

    static string ShapeFailure(string elementType, string detail, ArrayShape shape)
    {
        var text = new StringBuilder(elementType);
        text.Append("[/* ");
        text.Append(detail);
        text.Append("; rank=");
        text.Append(Invariant(shape.Rank));
        text.Append(", sizes=[");
        AppendValues(text, shape.Sizes);
        text.Append("], lower bounds=[");
        AppendValues(text, shape.LowerBounds);
        text.Append("] */ invalid]");
        return text.ToString();
    }

    static void AppendValues(StringBuilder text, ImmutableArray<int> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                text.Append(',');
            text.Append(Invariant(values[i]));
        }
    }

    static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
    static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
