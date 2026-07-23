namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

public static class DynamicStackallocFixtures
{
    public static int ByteCount(int n)
    {
        Span<byte> values = stackalloc byte[n];
        return values.Length;
    }

    public static int GuidCount(int n)
    {
        Span<Guid> values = stackalloc Guid[n];
        return values.Length;
    }

    public static int ByteExpression(int n)
    {
        Span<byte> values = stackalloc byte[n + 1];
        return values.Length;
    }

    public static int ByteEffectful(int n)
    {
        Span<byte> values = stackalloc byte[Math.Abs(n)];
        return values.Length;
    }

    public static int ByteExplicitLocal(int n)
    {
        int count = n + 1;
        Span<byte> values = stackalloc byte[count];
        return values.Length + count;
    }

    public static int ByteTwoCounts(int n, int m)
    {
        Span<byte> first = stackalloc byte[n];
        Span<byte> second = stackalloc byte[m];
        return first.Length + second.Length;
    }

    public static int ByteTwoEffectfulCounts(int n)
    {
        Span<byte> first = stackalloc byte[Math.Abs(n)];
        Span<byte> second = stackalloc byte[Math.Abs(n + 1)];
        return first.Length + second.Length;
    }
}
