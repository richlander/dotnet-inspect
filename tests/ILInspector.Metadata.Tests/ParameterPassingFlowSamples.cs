namespace ILInspector.Metadata.Tests;

public static class ParameterPassingFlowSamples
{
    public static void Ref(int value) { }
    public static void Ref(ref int value) { }
    public static void Out(int value) { }
    public static void Out(out int value) => value = 0;
    public static void In(int value) { }
    public static void In(in int value) { }
    public static void ReadOnly(int value) { }
    public static void ReadOnly(ref readonly int value) { }
    public static void Array(int[] values) { }
    public static void Array(ref int[] values) { }
    public static void Array(ref long[] values) { }
    public static void Position(int first, int second) { }
    public static void Position(ref int first, int second) { }
    public static void Position(int first, ref int second) { }
    public static void Position(ref int first, ref int second) { }
}
