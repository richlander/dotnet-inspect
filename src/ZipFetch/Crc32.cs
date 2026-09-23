namespace ZipFetch;

/// <summary>The CRC-32 the ZIP format declares for entry content.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
            crc = Table[(byte)(crc ^ value)] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320;
            table[index] = value;
        }

        return table;
    }
}
