namespace ILInspector.Metadata.Tests;

public static unsafe class FunctionPointerReturnModifierFlowSamples
{
    public static void Ref(delegate*<int> value) { }
    public static void Ref(delegate*<ref int> reference) { }

    public static void ReadOnly(delegate*<int> value) { }
    public static void ReadOnly(delegate*<ref readonly int> reference) { }
}
