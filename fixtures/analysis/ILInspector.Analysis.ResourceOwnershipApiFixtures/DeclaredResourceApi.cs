using System.Buffers;

namespace DeclaredOwnership;

public static class DeclaredResourceApi
{
    public static byte[] Acquire()
    {
        byte[] value;
        try
        {
            value = ArrayPool<byte>.Shared.Rent(16);
        }
        finally
        {
            s_probe++;
        }
        return value;
    }

    public static void Release(byte[] value) =>
        ArrayPool<byte>.Shared.Return(value);

    static int s_probe;
}

public sealed class ValueResourcePool
{
    public static ValueResourcePool Create() => new();

    public byte[] Acquire(int length) => new byte[length];

    public void Release(byte[] value)
    {
    }
}
