using System.Buffers.Binary;

namespace ZipFetch;

/// <summary>The CRC-32 the ZIP format declares for entry content.</summary>
internal static class Crc32
{
    private const int TableWidth = 256;
    private static readonly uint[] Tables = CreateTables();

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        int offset = 0;

        while (bytes.Length - offset >= sizeof(ulong))
        {
            ulong block = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, sizeof(ulong)));
            uint first = (uint)block ^ crc;
            uint second = (uint)(block >> 32);
            crc =
                Tables[(7 * TableWidth) + (byte)first] ^
                Tables[(6 * TableWidth) + (byte)(first >> 8)] ^
                Tables[(5 * TableWidth) + (byte)(first >> 16)] ^
                Tables[(4 * TableWidth) + (byte)(first >> 24)] ^
                Tables[(3 * TableWidth) + (byte)second] ^
                Tables[(2 * TableWidth) + (byte)(second >> 8)] ^
                Tables[TableWidth + (byte)(second >> 16)] ^
                Tables[(byte)(second >> 24)];
            offset += sizeof(ulong);
        }

        for (; offset < bytes.Length; offset++)
            crc = Tables[(byte)(crc ^ bytes[offset])] ^ (crc >> 8);

        return ~crc;
    }

    private static uint[] CreateTables()
    {
        var tables = new uint[8 * TableWidth];
        for (uint index = 0; index < TableWidth; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320;
            tables[index] = value;
        }

        for (int slice = 1; slice < 8; slice++)
        {
            int previousOffset = (slice - 1) * TableWidth;
            int currentOffset = slice * TableWidth;
            for (int index = 0; index < TableWidth; index++)
            {
                uint previous = tables[previousOffset + index];
                tables[currentOffset + index] =
                    tables[(byte)previous] ^ (previous >> 8);
            }
        }

        return tables;
    }
}
