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
}
