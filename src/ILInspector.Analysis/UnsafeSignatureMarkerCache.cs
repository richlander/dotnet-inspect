using System.Collections.Concurrent;
using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

[Flags]
internal enum UnsafeSignatureMarkers : byte
{
    None = 0,
    Pointer = 1,
    FunctionPointer = 2,
    Pinned = 4,
}

/// <summary>
/// Caches no-copy unsafe-marker scans by metadata blob and bounds their
/// assembly-wide work. Gated by
/// <c>UnsafeSignatureMarkerCache_RepeatedHandleScansOnce</c> and
/// <c>UnsafeSignatureMarkerCache_RejectsCumulativeWorkAboveAssemblyBudget</c>.
/// </summary>
internal sealed class UnsafeSignatureMarkerCache
{
    readonly MetadataReader _reader;
    readonly Action<BlobHandle>? _signatureScanned;
    readonly ConcurrentDictionary<
        BlobHandle,
        Lazy<UnsafeSignatureMarkers>> _markers = new();
    long _signatureWork;

    internal UnsafeSignatureMarkerCache(
        MetadataReader reader,
        Action<BlobHandle>? signatureScanned = null)
    {
        _reader = reader;
        _signatureScanned = signatureScanned;
    }

    internal UnsafeSignatureMarkers GetMarkers(
        BlobHandle signature)
    {
        if (signature.IsNil)
            return UnsafeSignatureMarkers.None;

        return _markers.GetOrAdd(
            signature,
            handle => new Lazy<UnsafeSignatureMarkers>(
                () => Scan(handle),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    UnsafeSignatureMarkers Scan(
        BlobHandle signature)
    {
        BlobReader blob = _reader.GetBlobReader(signature);
        ReserveWork(Math.Max(blob.Length, 1));
        _signatureScanned?.Invoke(signature);

        UnsafeSignatureMarkers markers =
            UnsafeSignatureMarkers.None;
        while (blob.RemainingBytes > 0)
        {
            markers |= blob.ReadByte() switch
            {
                0x0F => UnsafeSignatureMarkers.Pointer,
                0x1B => UnsafeSignatureMarkers.FunctionPointer,
                0x45 => UnsafeSignatureMarkers.Pinned,
                _ => UnsafeSignatureMarkers.None,
            };
        }
        return markers;
    }

    void ReserveWork(int charge)
    {
        while (true)
        {
            long current = Volatile.Read(
                ref _signatureWork);
            if (current < 0
                || charge
                    > MetadataSafetyPolicy
                        .MaxStructuralSignatureWorkChars
                        - current)
            {
                Interlocked.Exchange(
                    ref _signatureWork,
                    -1);
                throw new BadImageFormatException(
                    "Unsafe signature marker scanning exceeds the assembly budget.");
            }
            if (Interlocked.CompareExchange(
                    ref _signatureWork,
                    current + charge,
                    current)
                == current)
            {
                return;
            }
        }
    }
}
