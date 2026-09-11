namespace ILInspector.Decompiler.Tests;

internal static class AwaitOrderingHelpers
{
    public static int Combine(int x, int y) => x - y;
    public static void Sink(int value) { }
}
