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
/// <c>TypeRefDecoderRecursionTests</c> gates the Analysis decode boundary that keeps a hostile
/// rank out of a <c>TypeRef</c> in the first place.
/// </remarks>
public static class ArrayShapeText
{
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
            : $"/* invalid rank {rank} */";

    /// <summary>
    /// <paramref name="elementType"/> followed by its bracketed dimensions in IL-assembler
    /// syntax, or a marker when the rank could not belong to a loadable array.
    /// </summary>
    /// <remarks>
    /// ILAsm spells a rank-1 multi-dimensional array <c>int32[...]</c>, distinct from the vector
    /// <c>int32[]</c> that <c>GetSZArrayType</c> renders; <c>ildasm</c> is the oracle for that
    /// spelling. Collapsing the two would emit IL that round-trips to a different signature.
    /// <c>ILDisassemblerComparisonTests.CanonicalIL_ArraySpellings_ReassembleToTheSameSignature</c>
    /// gates that by reassembling what this spells and requiring a byte-identical signature blob.
    /// <para>
    /// Known gap, deliberately not asserted: this renders rank only, so an <c>ArrayShape</c>
    /// carrying explicit sizes or lower bounds loses them — <c>int32[0...,0...]</c> renders as
    /// <c>int32[,]</c>, which ILAsm reassembles to a different signature. That conflation is
    /// pre-existing and unrelated to bounding rank; it is tracked separately rather than
    /// silently implied to be handled here.
    /// </para>
    /// </remarks>
    public static string Format(string elementType, int rank)
        => rank == 1
            ? $"{elementType}[...]"
            : $"{elementType}[{FormatDimensions(rank)}]";
}
