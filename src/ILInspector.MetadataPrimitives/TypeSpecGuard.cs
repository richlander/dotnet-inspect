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

    public static bool TryEnter(MetadataReader reader, TypeSpecificationHandle handle, out Scope scope)
        => TryEnter(reader, handle, out scope, out _);

    internal static bool TryEnter(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        out Scope scope,
        out SignatureDecodeRejectionKind rejectionKind)
    {
        scope = default;
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
        scope = new Scope(length, s_depth);
        rejectionKind = default;
        return true;
    }

    static void Exit(int blobLength, int depth)
    {
        if (s_depth != depth)
            throw new InvalidOperationException("TypeSpecGuard scopes must be disposed in entry order.");

        s_depth--;
        s_cumulativeBytes -= blobLength;
    }

    /// <summary>
    /// Restores the calling thread's TypeSpec decode budget when the guarded
    /// re-entry completes. The stack-only token cannot escape to another thread.
    /// </summary>
    public ref struct Scope
    {
        int _blobLength;
        readonly int _depth;
        bool _active;

        internal Scope(int blobLength, int depth)
        {
            _blobLength = blobLength;
            _depth = depth;
            _active = true;
        }

        public void Dispose()
        {
            if (!_active)
                return;

            Exit(_blobLength, _depth);
            _active = false;
        }
    }
}
