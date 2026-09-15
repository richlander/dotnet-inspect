namespace ILInspector.Metadata.Tests;

public static unsafe class FunctionPointerModifierFlowSamples
{
    public static void Ref(delegate*<int, void> value) { }
    public static void Ref(delegate*<ref int, void> reference) { }

    public static void Out(delegate*<int, void> value) { }
    public static void Out(delegate*<out int, void> reference) { }

    public static void In(delegate*<int, void> value) { }
    public static void In(delegate*<in int, void> reference) { }

    public static void ReadOnly(delegate*<int, void> value) { }
    public static void ReadOnly(delegate*<ref readonly int, void> reference) { }
}
