using System.Text;

namespace ZipFetch.Tests;

public sealed class Crc32Tests
{
    [Fact]
    public void Compute_MatchesTheStandardCheckValue()
    {
        Assert.Equal(0xcbf43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void Compute_MatchesAnIndependentBitwiseOracleAcrossLengthsAndOffsets()
    {
        byte[] bytes = Enumerable.Range(0, 4_104)
            .Select(index => (byte)((index * 131) ^ (index >> 3)))
            .ToArray();

        for (int offset = 0; offset < 8; offset++)
        {
            for (int length = 0; length <= 4_096; length++)
            {
                ReadOnlySpan<byte> content = bytes.AsSpan(offset, length);
                Assert.Equal(ComputeBitwise(content), Crc32.Compute(content));
            }
        }
    }

    private static uint ComputeBitwise(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }

        return ~crc;
    }
}
