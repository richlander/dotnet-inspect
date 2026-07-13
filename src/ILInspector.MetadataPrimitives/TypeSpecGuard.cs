using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Bounds cross-handle TypeSpec re-entry performed by signature providers.
/// A top-level blob prescan cannot see a custom modifier that resolves another
/// TypeSpec, including a cycle back to the current row. Successful entries
/// return a disposable scope so depth and byte accounting cannot be exited
/// independently.
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

    [ThreadStatic]
    static ulong s_currentToken;

    [ThreadStatic]
    static ulong s_nextToken;

    public static bool TryEnter(MetadataReader reader, TypeSpecificationHandle handle, out Scope scope)
    {
        scope = default;
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
        ulong parentToken = s_currentToken;
        ulong token;
        do
        {
            token = unchecked(++s_nextToken);
        }
        while (token == 0 || token == parentToken);
        s_currentToken = token;
        scope = new Scope(length, token, parentToken);
        return true;
    }

    static void Exit(int blobLength)
    {
        s_depth--;
        s_cumulativeBytes -= blobLength;
    }

    public readonly ref struct Scope
    {
        readonly int _blobLength;
        readonly ulong _token;
        readonly ulong _parentToken;

        internal Scope(int blobLength, ulong token, ulong parentToken)
        {
            _blobLength = blobLength;
            _token = token;
            _parentToken = parentToken;
        }

        public void Dispose()
        {
            if (_token == 0 || s_currentToken != _token)
                return;

            Exit(_blobLength);
            s_currentToken = _parentToken;
        }
    }
}
