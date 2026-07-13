using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Bounds cross-handle TypeSpec re-entry performed by signature providers.
/// A top-level blob prescan cannot see a custom modifier that resolves another
/// TypeSpec, including a cycle back to the current row.
/// </summary>
public static class TypeSpecGuard
{
    /// <summary>
    /// Maximum bytes across the active TypeSpec re-entry closure. A single wide,
    /// shallow TypeSpec may use the entire budget.
    /// </summary>
    public const int MaxCumulativeBytes = 4096;

    /// <summary>Maximum cross-handle TypeSpec re-entry depth.</summary>
    public const int MaxDepth = 256;

    [ThreadStatic]
    static int s_cumulativeBytes;

    [ThreadStatic]
    static int s_depth;

    public static bool TryEnter(MetadataReader reader, TypeSpecificationHandle handle, out int blobLength)
        => TryEnter(reader, handle, out blobLength, out _);

    internal static bool TryEnter(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        out int blobLength,
        out SignatureDecodeRejectionKind rejectionKind)
    {
        blobLength = 0;
        if (s_depth >= MaxDepth)
        {
            rejectionKind = SignatureDecodeRejectionKind.TypeSpecificationBudget;
            return false;
        }

        var signature = reader.GetTypeSpecification(handle).Signature;
        int length = reader.GetBlobReader(signature).Length;
        if ((long)s_cumulativeBytes + length > MaxCumulativeBytes)
        {
            rejectionKind = SignatureDecodeRejectionKind.TypeSpecificationBudget;
            return false;
        }
        if (!SignatureBlobGuard.IsSafeToDecode(
            reader,
            signature,
            SignatureBlobGuard.Kind.TypeSpecification))
        {
            rejectionKind = SignatureDecodeRejectionKind.UnsafeStructure;
            return false;
        }

        s_depth++;
        s_cumulativeBytes += length;
        blobLength = length;
        rejectionKind = default;
        return true;
    }

    public static void Exit(int blobLength)
    {
        s_depth--;
        s_cumulativeBytes -= blobLength;
    }
}
