using System.Buffers;

namespace InertText.Encoding;

/// <summary>
/// The explicit exceptional capability for exact opaque-byte transfer.
/// </summary>
public static class OpaqueByteRangeEncoder
{
    /// <summary>Copies exact bytes into destination-owned storage.</summary>
    public static void Encode(
        OpaqueByteRange value,
        IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (value.IsEmpty)
            return;

        Span<byte> output = destination.GetSpan(value.Length);
        value.Bytes.CopyTo(output);
        destination.Advance(value.Length);
    }
}
