using System;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Shared re-entry guard for <see cref="ISignatureTypeProvider{TType, TGenericContext}"/>
/// <c>GetTypeFromSpecification</c> overrides. SRM invokes that override for every *nested* TypeSpec
/// reached by handle (e.g. a <c>CMOD</c> custom modifier referencing another TypeSpec). Malformed
/// metadata can form a cross-handle TypeSpec *cycle*, so each re-entry re-decodes a fresh blob and
/// re-enters SRM's native-recursive <c>DecodeType</c> — an uncatchable
/// <see cref="StackOverflowException"/> that a caller-side prescan on the *outer* blob cannot see,
/// and a count-driven pre-allocation OOM. Every provider that decodes a nested TypeSpec must gate
/// the re-entry through <see cref="TryEnter"/> / <see cref="Exit"/>.
///
/// The caps are <c>[ThreadStatic]</c> so the shared provider instances stay thread-safe, and mirror
/// the merged <c>TypeRefDecoder</c> hardening: per-blob length, cumulative cross-blob bytes, and
/// re-entry depth, plus the structural <see cref="SignatureBlobGuard"/> prescan.
/// </summary>
public static class TypeSpecGuard
{
    [ThreadStatic]
    static int s_depth;
    const int MaxDepth = 256;

    [ThreadStatic]
    static int s_cumulativeBytes;
    const int MaxCumulativeBytes = 4096;

    const int MaxBlobLength = 1024;

    /// <summary>
    /// Returns <see langword="true"/> and enters the guarded region — the caller MUST call
    /// <see cref="Exit"/> in a <c>finally</c> with the returned <paramref name="blobLength"/> — if it
    /// is safe to decode <paramref name="handle"/> now. Returns <see langword="false"/> (nothing
    /// entered, caller should return its unresolved placeholder) when a depth / length / cumulative
    /// cap or the <see cref="SignatureBlobGuard"/> prescan would be exceeded. Apply the cheap length
    /// / cumulative caps before the structural prescan so an over-long blob is rejected in O(1).
    /// </summary>
    public static bool TryEnter(MetadataReader reader, TypeSpecificationHandle handle, out int blobLength)
    {
        blobLength = 0;
        if (s_depth >= MaxDepth)
            return false;
        var signature = reader.GetTypeSpecification(handle).Signature;
        int length = reader.GetBlobReader(signature).Length;
        if (length > MaxBlobLength)
            return false;
        if (s_cumulativeBytes + length > MaxCumulativeBytes)
            return false;
        if (!SignatureBlobGuard.IsSafeToDecode(reader, signature, SignatureBlobGuard.Kind.TypeSpecification))
            return false;
        s_cumulativeBytes += length;
        s_depth++;
        blobLength = length;
        return true;
    }

    /// <summary>Leaves a region entered by <see cref="TryEnter"/>; pass the <c>blobLength</c> it
    /// returned.</summary>
    public static void Exit(int blobLength)
    {
        s_depth--;
        s_cumulativeBytes -= blobLength;
    }
}
