using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Bounds cross-handle TypeSpec re-entry performed by signature providers.
/// A top-level blob prescan cannot see a custom modifier that resolves another
/// TypeSpec, including a cycle back to the current row.
/// </summary>
public static class TypeSpecGuard
{
    const int MaxBlobLength = 1024;
    const int MaxCumulativeBytes = 4096;
    const int MaxDepth = 256;

    [ThreadStatic]
    static int s_cumulativeBytes;

    [ThreadStatic]
    static int s_depth;

    public static bool TryEnter(MetadataReader reader, TypeSpecificationHandle handle, out int blobLength)
    {
        blobLength = 0;
        if (s_depth >= MaxDepth)
            return false;

        var signature = reader.GetTypeSpecification(handle).Signature;
        int length = reader.GetBlobReader(signature).Length;
        if (length > MaxBlobLength
            || s_cumulativeBytes + length > MaxCumulativeBytes
            || !SignatureBlobGuard.IsSafeToDecode(reader, signature, SignatureBlobGuard.Kind.TypeSpecification))
        {
            return false;
        }

        s_depth++;
        s_cumulativeBytes += length;
        blobLength = length;
        return true;
    }

    public static void Exit(int blobLength)
    {
        s_depth--;
        s_cumulativeBytes -= blobLength;
    }
}
