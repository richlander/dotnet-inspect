namespace ZipFetch;

/// <summary>The CRC-32 the ZIP format declares for entry content.</summary>
internal static class Crc32
{
    public static uint Compute(ReadOnlySpan<byte> bytes) =>
        System.IO.Hashing.Crc32.HashToUInt32(bytes);
}
