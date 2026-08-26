namespace ILInspector.Decompiler.Tests;

public static class MethodSpecArityFixture
{
    public static int Helper<T, U>(T value) => 1;

    public static int Helper<T>(T value) => 2;

    public static int Invoke() => Helper<int, string>(1);
}
