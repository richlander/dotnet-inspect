namespace ILInspector.Metadata;

/// <summary>
/// Renders the bracketed dimension list of an <c>ArrayShape</c> for signature providers that
/// carry no materialization budget of their own.
/// </summary>
/// <remarks>
/// Rank is the one ArrayShape field that drives work without consuming blob bytes in proportion
/// to it: <c>ELEMENT_TYPE_ARRAY</c> followed by four bytes of compressed rank encodes 536,870,911
/// dimensions, and the natural spelling materializes a separator string that long. Every
/// byte-oriented structural check passes such a blob, and <see cref="SignatureBlobGuard"/>
/// deliberately does not charge rank, because the budgeted providers
/// (<c>TypeNodeProvider.GetArrayType</c> and <see cref="SignatureDecoder"/>) charge it themselves
/// and report the overrun as a visible decode failure rather than silently dropping the member.
/// <para>
/// Providers that render straight to a string have nowhere to report an overrun, so they bound
/// the rendering instead. ECMA-335 II.23.2.13 permits any positive rank, but the CLI cannot load
/// an array of more than <see cref="MaxRenderableRank"/> dimensions, so a larger rank is invalid
/// metadata rather than an exotic-but-real shape. Rendering it as a marker keeps the failure
/// visible in the output — the alternative spellings either lie about the shape or allocate.
/// </para>
/// <c>ArrayShapeTextTests</c> gates it.
/// </remarks>
public static class ArrayShapeText
{
    /// <summary>
    /// The largest array rank the CLI can load, and so the largest this renders faithfully.
    /// </summary>
    public const int MaxRenderableRank = 32;

    /// <summary>
    /// <paramref name="elementType"/> followed by <paramref name="rank"/> bracketed dimensions,
    /// or a marker when the rank could not belong to a loadable array.
    /// </summary>
    public static string Format(string elementType, int rank)
        => rank < 0 || rank > MaxRenderableRank
            ? $"{elementType}[/* invalid rank {rank} */]"
            : $"{elementType}[{new string(',', Math.Max(rank - 1, 0))}]";
}
